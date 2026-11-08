using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Downloads;
using Minch.Core.Formatting;
using Minch.Core.Media;
using Minch.Core.Models;
using Minch.Core.Services;

namespace Minch.ViewModels;

public sealed partial class FileItemViewModel : ObservableObject
{
    private readonly SessionViewModel _session;
    private TransferItemViewModel _parent;

    internal FileItemViewModel(SessionViewModel session, TransferItemViewModel parent, TransferFile file)
    {
        _session = session;
        _parent = parent;
        _file = file;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(SizeText), nameof(IsDownloaded), nameof(LocalPath), nameof(Kind),
        nameof(Capability), nameof(Glyph), nameof(CanStream), nameof(CanPlay), nameof(CanReveal), nameof(CanDownload), nameof(Subtitle))]
    private TransferFile _file;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(Subtitle))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private double _downloadFraction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private long _downloadSpeed;

    [ObservableProperty]
    private bool _isBusy;

    public string Id => File.Id;
    public string Name => File.Name;
    public string SizeText => TransferFormat.Size(File.SizeBytes);
    public bool IsDownloaded => File.IsDownloaded;
    public string? LocalPath => File.LocalPath;
    public MediaKind Kind => MediaKinds.Detect(File.Name, File.Mime);
    public MediaCapability Capability => MediaCapability.Evaluate(File.Name, File.Mime, File.IsDownloaded);
    public bool TransferReady => _parent.IsDone;
    public bool CanStream => TransferReady && Capability.CanStream;
    public bool CanPlay => Capability.CanPlayLocally || CanStream;
    public bool CanReveal => IsDownloaded;
    public bool CanDownload => TransferReady && !IsDownloading;

    public string Glyph => Kind switch
    {
        MediaKind.Video => "",  // Video
        MediaKind.Audio => "",  // Audio
        _ => "",                // Document
    };

    public string Subtitle
    {
        get
        {
            if (IsDownloading)
            {
                var parts = new List<string> { $"Downloading {TransferFormat.Percent(DownloadFraction)}" };
                if (TransferFormat.Speed(DownloadSpeed) is { } s) parts.Add(s);
                return string.Join(" · ", parts);
            }
            return IsDownloaded ? $"{SizeText} · On this PC" : SizeText;
        }
    }

    internal void Update(TransferItemViewModel parent, TransferFile file)
    {
        _parent = parent;
        if (file != File) File = file;
        OnPropertyChanged(nameof(TransferReady));
        OnPropertyChanged(nameof(CanStream));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanDownload));
    }

    internal void ApplyDownload(DownloadUpdate u)
    {
        switch (u.State)
        {
            case DownloadState.Running:
                IsDownloading = true;
                DownloadFraction = u.Progress.Fraction;
                DownloadSpeed = u.Progress.BytesPerSecond;
                break;
            default:
                IsDownloading = false;
                DownloadFraction = 0;
                DownloadSpeed = 0;
                break;
        }
    }

    // ------------------------------------------------------------- commands

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading || _session.Downloads.IsDownloading(Id)) return;
        IsDownloading = true;
        try
        {
            var url = await _session.Client.RequestDownloadUrlAsync(_parent.Id, Id);
            if (!_session.Downloads.Start(url, Id, _parent.Name, Name))
                return;
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            _session.ShowError(ApiException.Friendly(ex, "Couldn't request a download URL."));
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _session.Downloads.Cancel(Id);
        IsDownloading = false;
    }

    [RelayCommand]
    private async Task CopyLinkAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var url = await _session.Client.RequestDownloadUrlAsync(_parent.Id, Id);
            _session.Services.Clipboard?.SetText(url.ToString());
            _session.ShowInfo("Download link copied.");
        }
        catch (Exception ex)
        {
            _session.ShowError(ApiException.Friendly(ex, "Couldn't request a download URL."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyStreamUrlAsync()
    {
        var session = await _session.Library.OpenStreamAsync(_parent.Id, Id, null);
        if (session is null) return;
        _session.Services.Clipboard?.SetText(session.Url.ToString());
        _session.ShowInfo("Stream URL copied.");
    }

    /// <summary>
    /// Default play action: local file if playable in-app, otherwise a
    /// preferred external player (if configured), otherwise the in-app HLS stream.
    /// </summary>
    [RelayCommand]
    private async Task PlayAsync()
    {
        var settings = _session.Services.Settings.Current;
        var preferred = PreferredPlayer();
        var wantExternal = preferred is not null && (settings.PreferExternalPlayer || Capability.PrefersExternal);

        if (IsDownloaded && LocalPath is { } path)
        {
            if (wantExternal && _session.Services.Players.Launch(preferred!, path, Name))
            {
                MarkPlayed();
                return;
            }
            if (Capability.CanPlayLocally)
            {
                MarkPlayed();
                _session.RequestPlayer(PlayerViewModel.ForLocalFile(_session, _parent.Name, File));
                return;
            }
        }

        if (!CanStream)
        {
            if (LocalPath is { } p) _session.Services.Shell?.OpenFile(p);
            return;
        }

        if (wantExternal)
        {
            await PlayExternalAsync(preferred!);
            return;
        }

        MarkPlayed();
        _session.RequestPlayer(PlayerViewModel.ForStream(_session, _parent.Id, _parent.Name, File));
    }

    [RelayCommand]
    private void PlayInApp()
    {
        MarkPlayed();
        _session.RequestPlayer(IsDownloaded && Capability.CanPlayLocally
            ? PlayerViewModel.ForLocalFile(_session, _parent.Name, File)
            : PlayerViewModel.ForStream(_session, _parent.Id, _parent.Name, File));
    }

    [RelayCommand]
    public async Task PlayExternalAsync(ExternalPlayer player)
    {
        string target;
        if (IsDownloaded && LocalPath is { } path)
        {
            target = path;
        }
        else
        {
            var session = await _session.Library.OpenStreamAsync(_parent.Id, Id, null);
            if (session is null) return;
            target = session.Url.ToString();
        }
        if (_session.Services.Players.Launch(player, target, Name))
        {
            MarkPlayed();
            _session.ShowInfo($"Opening in {player.DisplayName}…");
        }
        else
        {
            _session.ShowError($"Couldn't open {player.DisplayName}.");
        }
    }

    [RelayCommand]
    private void Reveal()
    {
        if (LocalPath is { } p) _session.Services.Shell?.RevealInExplorer(p);
    }

    [RelayCommand]
    private void OpenWithDefaultApp()
    {
        if (LocalPath is { } p) _session.Services.Shell?.OpenFile(p);
    }

    public IReadOnlyList<ExternalPlayer> ExternalPlayers => _session.Services.Players.Installed();

    private ExternalPlayer? PreferredPlayer()
    {
        var installed = _session.Services.Players.Installed();
        if (installed.Count == 0) return null;
        var pref = _session.Services.Settings.Current.PreferredExternalPlayer;
        return installed.FirstOrDefault(p => p.Id.ToString().Equals(pref, StringComparison.OrdinalIgnoreCase)) ?? installed[0];
    }

    private void MarkPlayed() => _session.Store.MarkPlayed(Id);
}
