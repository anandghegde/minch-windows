using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Models;
using Minch.Core.Services;

namespace Minch.ViewModels;

/// <summary>Option row for the subtitle/audio/quality menus. Index null = "Off".</summary>
public sealed record TrackChoice(int? Index, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// State for the in-app player window. The view owns the MediaPlayer; this VM
/// resolves the source URI (local file or TorBox HLS) and recreates the stream
/// when the user picks a different track.
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly SessionViewModel _session;
    private readonly string? _transferId;
    private readonly TransferFile _file;
    private readonly bool _isLocal;
    private bool _suppressReload;

    private PlayerViewModel(SessionViewModel session, string? transferId, string transferName, TransferFile file, bool isLocal)
    {
        _session = session;
        _transferId = transferId;
        _file = file;
        _isLocal = isLocal;
        TransferName = transferName;
        Title = file.Name;
        StartPositionSec = file.PlaybackPositionSec;
    }

    public static PlayerViewModel ForLocalFile(SessionViewModel session, string transferName, TransferFile file) =>
        new(session, null, transferName, file, isLocal: true);

    public static PlayerViewModel ForStream(SessionViewModel session, string transferId, string transferName, TransferFile file) =>
        new(session, transferId, transferName, file, isLocal: false);

    public string Title { get; }
    public string TransferName { get; }
    public string FileId => _file.Id;
    public bool IsLocal => _isLocal;
    public double? StartPositionSec { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    private Uri? _mediaUri;

    /// <summary>True when <see cref="MediaUri"/> is an HLS manifest (use AdaptiveMediaSource).</summary>
    [ObservableProperty]
    private bool _isAdaptive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackMenus))]
    private IReadOnlyList<TrackChoice> _subtitleChoices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackMenus))]
    private IReadOnlyList<TrackChoice> _audioChoices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackMenus))]
    private IReadOnlyList<TrackChoice> _resolutionChoices = [];

    [ObservableProperty]
    private TrackChoice? _selectedSubtitle;

    [ObservableProperty]
    private TrackChoice? _selectedAudio;

    [ObservableProperty]
    private TrackChoice? _selectedResolution;

    public bool HasMedia => MediaUri is not null;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool HasTrackMenus => SubtitleChoices.Count > 0 || AudioChoices.Count > 1 || ResolutionChoices.Count > 1;
    public bool HasSubtitleMenu => SubtitleChoices.Count > 0;
    public bool HasAudioMenu => AudioChoices.Count > 1;
    public bool HasResolutionMenu => ResolutionChoices.Count > 1;

    /// <summary>Raised when MediaUri changes because of a track switch; the view should resume at the current position.</summary>
    public event EventHandler? SourceReplaced;

    public IReadOnlyList<ExternalPlayer> ExternalPlayers => _session.Services.Players.Installed();

    public async Task LoadAsync()
    {
        if (_isLocal)
        {
            if (_file.LocalPath is { } path && File.Exists(path))
            {
                MediaUri = new Uri(path);
                IsAdaptive = false;
            }
            else
            {
                Error = "The downloaded file is missing.";
            }
            return;
        }
        await OpenStreamAsync(null);
    }

    private async Task OpenStreamAsync(StreamOptions? options)
    {
        if (_transferId is null) return;
        IsLoading = true;
        Error = null;
        try
        {
            var stream = await _session.Library.OpenStreamAsync(_transferId, _file.Id, options);
            if (stream is null)
            {
                Error = "Couldn't open the stream.";
                return;
            }
            Apply(stream);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(StreamSession s)
    {
        _suppressReload = true;
        try
        {
            SubtitleChoices = s.Subtitles.Count == 0
                ? []
                : [new TrackChoice(null, "Off"), .. s.Subtitles.Select(t => new TrackChoice(t.Index, t.Label))];
            AudioChoices = s.AudioTracks.Select(t => new TrackChoice(t.Index, t.Label)).ToList();
            ResolutionChoices = s.Resolutions.Select(t => new TrackChoice(t.Index, t.Label)).ToList();
            SelectedSubtitle = SubtitleChoices.FirstOrDefault(c => c.Index == s.SelectedSubtitleIndex) ?? SubtitleChoices.FirstOrDefault();
            SelectedAudio = AudioChoices.FirstOrDefault(c => c.Index == s.SelectedAudioIndex) ?? AudioChoices.FirstOrDefault();
            SelectedResolution = ResolutionChoices.FirstOrDefault(c => c.Index == s.SelectedResolutionIndex) ?? ResolutionChoices.FirstOrDefault();
            OnPropertyChanged(nameof(HasSubtitleMenu));
            OnPropertyChanged(nameof(HasAudioMenu));
            OnPropertyChanged(nameof(HasResolutionMenu));
        }
        finally
        {
            _suppressReload = false;
        }
        var replacing = MediaUri is not null;
        IsAdaptive = s.Url.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || !s.Url.IsFile;
        MediaUri = s.Url;
        if (replacing) SourceReplaced?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedSubtitleChanged(TrackChoice? value) => Reload();
    partial void OnSelectedAudioChanged(TrackChoice? value) => Reload();
    partial void OnSelectedResolutionChanged(TrackChoice? value) => Reload();

    private void Reload()
    {
        if (_suppressReload || _isLocal || MediaUri is null) return;
        var options = new StreamOptions(
            SubtitleIndex: SelectedSubtitle?.Index,
            AudioIndex: SelectedAudio?.Index ?? 0,
            ResolutionIndex: SelectedResolution?.Index,
            ScrobblingEnabled: _session.Services.Settings.Current.ScrobblingEnabled);
        _ = OpenStreamAsync(options);
    }

    /// <summary>Called by the view periodically and on close so playback can resume later.</summary>
    public void SavePosition(TimeSpan position, TimeSpan duration)
    {
        if (position <= TimeSpan.Zero) return;
        // Forget the position near the end so finished items restart from the top.
        double? sec = duration > TimeSpan.Zero && duration - position < TimeSpan.FromSeconds(30) ? 0 : position.TotalSeconds;
        try { _session.Store.MarkPlayed(_file.Id, sec); } catch { }
    }

    [RelayCommand]
    private void OpenExternal(ExternalPlayer player)
    {
        if (MediaUri is null) return;
        var target = MediaUri.IsFile ? MediaUri.LocalPath : MediaUri.ToString();
        if (!_session.Services.Players.Launch(player, target, Title))
            Error = $"Couldn't open {player.DisplayName}.";
    }

    [RelayCommand]
    private void CopyStreamUrl()
    {
        if (MediaUri is null || MediaUri.IsFile) return;
        _session.Services.Clipboard?.SetText(MediaUri.ToString());
    }
}
