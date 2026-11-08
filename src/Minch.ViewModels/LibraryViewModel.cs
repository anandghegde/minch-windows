using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Downloads;
using Minch.Core.Input;
using Minch.Core.Media;
using Minch.Core.Models;

namespace Minch.ViewModels;

public sealed partial class SectionItem : ObservableObject
{
    public SectionItem(LibrarySection section) => Section = section;
    public LibrarySection Section { get; }
    public string Title => Section.Title();
    public string Glyph => Section.Glyph();
    public string Key => Section.Key();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _count;

    public string CountText => Count > 0 ? Count.ToString() : "";
}

public sealed record TorrentFileDraft(string FileName, byte[] Data);

public sealed partial class LibraryViewModel : ObservableObject
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan PreflightDebounce = TimeSpan.FromMilliseconds(350);

    private readonly SessionViewModel _session;
    private readonly Dictionary<string, TransferItemViewModel> _vms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _finishedNotified = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TransferStatus> _lastStatus = new(StringComparer.Ordinal);
    private CancellationTokenSource? _preflight;
    private bool _rebuildQueued;

    internal LibraryViewModel(SessionViewModel session)
    {
        _session = session;
        Sections = new ObservableCollection<SectionItem>(LibrarySections.All.Select(s => new SectionItem(s)));
        var last = LibrarySections.FromKey(session.Services.Settings.Current.LastSection);
        _selectedSection = last ?? LibrarySection.Active;
        session.Store.Changed += (_, _) => QueueRebuild();
        session.Downloads.Updated += (_, u) => session.OnUi(() => ApplyDownload(u));
    }

    public ObservableCollection<SectionItem> Sections { get; }
    public ObservableCollection<TransferItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle), nameof(EmptyTitle), nameof(EmptyMessage))]
    private LibrarySection _selectedSection;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _visibleCount;

    public string CountText => VisibleCount switch
    {
        0 => "",
        1 => "1 item",
        var n => $"{n} items",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDownloads))]
    private int _activeDownloadCount;

    public string SectionTitle => SelectedSection.Title();
    public string EmptyTitle => !string.IsNullOrWhiteSpace(SearchText) ? "No matches" : SelectedSection.EmptyState().Title;
    public string EmptyMessage => !string.IsNullOrWhiteSpace(SearchText)
        ? $"Nothing in {SelectedSection.Title()} matches “{SearchText.Trim()}”."
        : SelectedSection.EmptyState().Message;
    public bool ShowEmptyState => IsEmpty;
    public bool HasActiveDownloads => ActiveDownloadCount > 0;

    partial void OnSelectedSectionChanged(LibrarySection value)
    {
        _session.Services.Settings.Update(s => s with { LastSection = value.Key() });
        Rebuild();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        Rebuild();
    }

    // ------------------------------------------------------------- rebuild

    private void QueueRebuild()
    {
        // Coalesce bursts of store changes into one UI pass.
        lock (_vms)
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
        }
        _session.Services.Dispatcher.Post(() =>
        {
            lock (_vms) _rebuildQueued = false;
            Rebuild();
        });
    }

    public void Rebuild()
    {
        var all = _session.Store.All().Where(t => !t.IsHidden).ToList();
        NotifyFinished(all);

        var now = DateTimeOffset.UtcNow;
        foreach (var item in Sections)
            item.Count = all.Count(t => Matches(t, item.Section, now));

        var query = SearchText.Trim();
        var visible = all
            .Where(t => Matches(t, SelectedSection, now))
            .Where(t => MatchesSearch(t, query))
            .OrderByDescending(t => t.IsFavorite)
            .ThenByDescending(t => t.AddedAt)
            .ToList();

        // Update or create VMs; keep instances stable so expander state survives polling.
        var liveIds = new HashSet<string>(all.Select(t => t.Id), StringComparer.Ordinal);
        foreach (var id in _vms.Keys.Where(k => !liveIds.Contains(k)).ToList()) _vms.Remove(id);
        foreach (var t in all)
        {
            if (_vms.TryGetValue(t.Id, out var vm)) vm.Update(t);
            else _vms[t.Id] = new TransferItemViewModel(_session, t);
        }

        var wanted = visible.Select(t => _vms[t.Id]).ToList();
        var wantedSet = new HashSet<TransferItemViewModel>(wanted);
        for (var i = Items.Count - 1; i >= 0; i--)
            if (!wantedSet.Contains(Items[i])) Items.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var vm = wanted[i];
            var at = Items.IndexOf(vm);
            if (at < 0) Items.Insert(i, vm);
            else if (at != i) Items.Move(at, i);
        }

        IsEmpty = Items.Count == 0;
        VisibleCount = Items.Count;
        ActiveDownloadCount = _session.Downloads.ActiveFileIds.Count;
    }

    public static bool Matches(Transfer t, LibrarySection section, DateTimeOffset now) => section switch
    {
        LibrarySection.Active => t.Status != TransferStatus.Done,
        LibrarySection.Downloaded => t.Status == TransferStatus.Done,
        LibrarySection.Videos => t.Files.Any(f => MediaKinds.Detect(f.Name, f.Mime) == MediaKind.Video),
        LibrarySection.Audio => t.Files.Any(f => MediaKinds.Detect(f.Name, f.Mime) == MediaKind.Audio),
        LibrarySection.Recent => now - t.AddedAt <= RecentWindow,
        _ => true,
    };

    public static bool MatchesSearch(Transfer t, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return terms.All(term =>
            t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            t.TagNames.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            t.Files.Any(f => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private void NotifyFinished(IReadOnlyList<Transfer> all)
    {
        foreach (var t in all)
        {
            var had = _lastStatus.TryGetValue(t.Id, out var prev);
            _lastStatus[t.Id] = t.Status;
            // Only toast transitions observed during this session, never on first load.
            if (had && prev != TransferStatus.Done && t.Status == TransferStatus.Done && _finishedNotified.Add(t.Id)
                && _session.Services.Settings.Current.NotifyOnTransferFinished)
                _session.Services.Notifier.TransferFinished(t.Name);
        }
    }

    private void ApplyDownload(DownloadUpdate u)
    {
        var id = u.FileId;
        var transferId = id[..Math.Max(0, id.LastIndexOf(':'))];
        if (_vms.TryGetValue(transferId, out var vm)) vm.ApplyDownload(u);
        ActiveDownloadCount = _session.Downloads.ActiveFileIds.Count;
        if (u.State == DownloadState.Failed && u.Error is { } err)
            _session.ShowError($"Download failed: {err}");
    }

    public TransferItemViewModel? Find(string transferId) => _vms.GetValueOrDefault(transferId);

    public IEnumerable<TransferItemViewModel> AllItems => _vms.Values;

    // --------------------------------------------------------------- add bar

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddKind), nameof(AddHint), nameof(IsMagnetInput), nameof(CanSubmitAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _addText = "";

    [ObservableProperty]
    private string _pendingName = "";

    [ObservableProperty]
    private bool _cacheOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTorrentFile), nameof(TorrentFileName), nameof(CanSubmitAdd), nameof(AddHint))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private TorrentFileDraft? _pendingTorrentFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddHint))]
    private bool _isCached;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private bool _isAdding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddError))]
    private string? _addError;

    [ObservableProperty]
    private IReadOnlyList<Hoster> _hosters = [];

    public AddInputKind AddKind => AddInput.Classify(AddText);
    public bool IsMagnetInput => AddKind == AddInputKind.Magnet || HasTorrentFile;
    public bool HasTorrentFile => PendingTorrentFile is not null;
    public string? TorrentFileName => PendingTorrentFile?.FileName;
    public bool HasAddError => !string.IsNullOrEmpty(AddError);
    public bool CanSubmitAdd => !IsAdding && (HasTorrentFile || AddKind is AddInputKind.Magnet or AddInputKind.WebLink);

    public string AddHint => HasTorrentFile
        ? $"Ready to upload {TorrentFileName}"
        : AddKind switch
        {
            AddInputKind.Magnet when IsCached => "Already cached on TorBox — this will be instant.",
            AddInputKind.Magnet => "Magnet link",
            AddInputKind.WebLink => "Web download link",
            AddInputKind.Invalid => "Paste a magnet, .torrent file, or http(s) download link.",
            _ => "Paste a magnet link, a download URL, or drop a .torrent file",
        };

    partial void OnAddTextChanged(string value)
    {
        AddError = null;
        if (!string.IsNullOrWhiteSpace(value) && PendingTorrentFile is not null) PendingTorrentFile = null;
        _ = PreflightAsync(value);
    }

    private async Task PreflightAsync(string text)
    {
        _preflight?.Cancel();
        IsCached = false;
        if (!_session.IsSignedIn || AddInput.BtihHash(text) is not { } hash) return;
        var cts = _preflight = new CancellationTokenSource();
        try
        {
            await Task.Delay(PreflightDebounce, cts.Token);
            var hits = await _session.Client.CheckCachedAsync([hash], cts.Token);
            if (!cts.IsCancellationRequested)
            {
                var cached = hits.Contains(hash.ToLowerInvariant());
                _session.OnUi(() => IsCached = cached);
            }
        }
        catch
        {
            // advisory only
        }
    }

    public void LoadTorrentFile(string fileName, byte[] data)
    {
        if (data.Length == 0 || data.Length >= AddInput.MaxTorrentFileBytes)
        {
            AddError = "That file looks empty or unreasonably large.";
            return;
        }
        PendingTorrentFile = new TorrentFileDraft(fileName, data);
        AddText = "";
        IsCached = false;
        AddError = null;
    }

    public async Task LoadTorrentFileAsync(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length >= AddInput.MaxTorrentFileBytes)
            {
                AddError = "That file looks empty or unreasonably large.";
                return;
            }
            LoadTorrentFile(info.Name, await File.ReadAllBytesAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddError = "Couldn't read that .torrent file.";
        }
    }

    [RelayCommand]
    private void ClearTorrentFile() => PendingTorrentFile = null;

    [RelayCommand(CanExecute = nameof(CanSubmitAdd))]
    public async Task AddAsync()
    {
        if (!_session.IsSignedIn) return;
        var name = PendingName.Trim();
        var nameArg = name.Length == 0 ? null : name;
        string failure;
        IsAdding = true;
        AddError = null;
        try
        {
            AddResult result;
            if (PendingTorrentFile is { } draft)
            {
                failure = "Couldn't add that .torrent file.";
                result = await _session.Client.AddTorrentFileAsync(draft.Data, draft.FileName, nameArg, CacheOnly);
            }
            else
            {
                var text = AddText.Trim();
                switch (AddInput.Classify(text))
                {
                    case AddInputKind.Magnet:
                        failure = "Couldn't add magnet.";
                        result = await _session.Client.AddMagnetAsync(text, nameArg, CacheOnly);
                        break;
                    case AddInputKind.WebLink:
                        failure = "Couldn't add that download link.";
                        result = await _session.Client.AddWebDownloadAsync(text, nameArg);
                        break;
                    default:
                        AddError = "Paste a magnet, .torrent file, or http(s) download link.";
                        return;
                }
            }

            if (result.IsEmpty)
            {
                AddError = result.Detail ?? failure;
                return;
            }
            if (!string.IsNullOrWhiteSpace(result.Detail)) _session.ShowInfo(result.Detail);
            ResetAdd();
            await _session.QuietRefreshAsync();
            if (SelectedSection != LibrarySection.Active && SelectedSection != LibrarySection.Recent)
                SelectedSection = LibrarySection.Active;
        }
        catch (Exception ex)
        {
            AddError = ApiException.Friendly(ex, "Couldn't add that.");
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>Magnet from the protocol handler, command line, or paste.</summary>
    public async Task IngestExternalAsync(string raw)
    {
        if (AddInput.ExternalMagnet(raw) is not { } magnet)
        {
            _session.ShowError("Could not parse an external magnet link.");
            return;
        }
        PendingTorrentFile = null;
        AddText = magnet;
        await AddAsync();
    }

    public void ResetAdd()
    {
        _preflight?.Cancel();
        AddText = "";
        PendingTorrentFile = null;
        PendingName = "";
        CacheOnly = false;
        IsCached = false;
        AddError = null;
    }

    internal async Task LoadHostersAsync()
    {
        if (Hosters.Count > 0) return;
        try
        {
            var list = await _session.Client.ListHostersAsync();
            _session.OnUi(() => Hosters = list);
        }
        catch
        {
            // advisory
        }
    }

    // -------------------------------------------------------------- streams

    /// <summary>Opens a TorBox stream, honouring scrobbling and preferred languages on first open.</summary>
    public async Task<StreamSession?> OpenStreamAsync(string transferId, string fileId, StreamOptions? options)
    {
        var settings = _session.Services.Settings.Current;
        var resolved = (options ?? StreamOptions.Default) with
        {
            ScrobblingEnabled = options?.ScrobblingEnabled ?? settings.ScrobblingEnabled,
        };
        try
        {
            var session = await _session.Client.CreateStreamAsync(transferId, fileId, resolved);
            if (options is not null) return session;

            var refined = resolved;
            var changed = false;
            if (AddInput.MatchTrack(session.Subtitles, settings.PreferredSubtitleLanguage) is { } sub)
            {
                refined = refined with { SubtitleIndex = sub };
                changed = true;
            }
            if (session.AudioTracks.Count > 1 && AddInput.MatchTrack(session.AudioTracks, settings.PreferredAudioLanguage) is { } audio
                && audio != resolved.AudioIndex)
            {
                refined = refined with { AudioIndex = audio };
                changed = true;
            }
            if (!changed) return session;
            try { return await _session.Client.CreateStreamAsync(transferId, fileId, refined); }
            catch { return session; }
        }
        catch (Exception ex)
        {
            _session.ShowError(ApiException.Friendly(ex, "Couldn't open the stream."));
            return null;
        }
    }
}
