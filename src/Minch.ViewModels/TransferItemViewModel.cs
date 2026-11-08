using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Downloads;
using Minch.Core.Formatting;
using Minch.Core.Media;
using Minch.Core.Models;

namespace Minch.ViewModels;

public sealed partial class TransferItemViewModel : ObservableObject
{
    private readonly SessionViewModel _session;

    internal TransferItemViewModel(SessionViewModel session, Transfer transfer)
    {
        _session = session;
        _transfer = transfer;
        SyncFiles();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(MetaLine), nameof(Progress), nameof(ProgressPercent), nameof(Phase),
        nameof(IsDone), nameof(IsActive), nameof(IsError), nameof(IsPaused), nameof(IsQueued), nameof(StatusLabel), nameof(StatusGlyph),
        nameof(IsFavorite), nameof(Tags), nameof(HasTags), nameof(TagsText), nameof(FileCountText), nameof(KindLabel),
        nameof(CanResume), nameof(CanStopSeeding), nameof(CanReannounce), nameof(IsWebDownload), nameof(ShowProgress))]
    private Transfer _transfer;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isDeleting;

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    public string Id => Transfer.Id;
    public string Name => Transfer.Name;
    public StatusPhase Phase => TransferFormat.Phase(Transfer.Status);
    public string MetaLine => TransferFormat.MetaLine(Transfer);
    public double Progress => TransferFormat.Clamp(Transfer.Progress);
    public double ProgressPercent => Progress * 100;
    public bool IsDone => Transfer.Status == TransferStatus.Done;
    public bool IsActive => Phase == StatusPhase.Active;
    public bool IsQueued => Phase == StatusPhase.Queued;
    public bool IsPaused => Phase == StatusPhase.Paused;
    public bool IsError => Phase == StatusPhase.Error;
    public bool ShowProgress => !IsDone && !IsError;
    public bool IsWebDownload => Transfer.Kind == TransferKind.WebDl;
    public string StatusLabel => TransferFormat.StatusLabel(Transfer.Status);
    public bool IsFavorite => Transfer.IsFavorite;
    public IReadOnlyList<string> Tags => Transfer.TagNames;
    public bool HasTags => Transfer.TagNames.Count > 0;
    public string TagsText => string.Join(", ", Transfer.TagNames);
    public string KindLabel => Transfer.Kind == TransferKind.WebDl ? "Web download" : "Torrent";
    public string FileCountText => Transfer.Files.Count == 1 ? "1 file" : $"{Transfer.Files.Count} files";
    public bool CanResume => IsPaused || IsError;
    public bool CanStopSeeding => Transfer.Status == TransferStatus.Seeding;
    public bool CanReannounce => !IsWebDownload && (IsActive || IsQueued);

    /// <summary>Segoe Fluent Icons glyph for the status.</summary>
    public string StatusGlyph => Phase switch
    {
        StatusPhase.Queued => "",   // Stopwatch
        StatusPhase.Active => Transfer.Status == TransferStatus.Seeding ? "" : "", // Upload / Download
        StatusPhase.Paused => "",   // Pause
        StatusPhase.Error => "",    // Error
        StatusPhase.Done => "",     // CheckMark
        _ => "",
    };

    public bool HasMedia(MediaKind kind) => Transfer.Files.Any(f => MediaKinds.Detect(f.Name, f.Mime) == kind);

    public FileItemViewModel? PrimaryPlayable =>
        Files.Where(f => f.CanPlay && f.Kind != MediaKind.Other).MaxBy(f => f.File.SizeBytes);

    internal void Update(Transfer t)
    {
        if (t != Transfer) Transfer = t;
        SyncFiles();
    }

    internal void ApplyDownload(DownloadUpdate u)
    {
        Files.FirstOrDefault(f => f.Id == u.FileId)?.ApplyDownload(u);
    }

    private void SyncFiles()
    {
        var incoming = Transfer.Files;
        var byId = Files.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var wanted = new HashSet<string>(incoming.Select(f => f.Id), StringComparer.Ordinal);
        for (var i = Files.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Files[i].Id)) Files.RemoveAt(i);

        for (var i = 0; i < incoming.Count; i++)
        {
            var f = incoming[i];
            if (byId.TryGetValue(f.Id, out var vm))
            {
                vm.Update(this, f);
                var at = Files.IndexOf(vm);
                if (at != i) Files.Move(at, i);
            }
            else
            {
                vm = new FileItemViewModel(_session, this, f) { IsDownloading = _session.Downloads.IsDownloading(f.Id) };
                Files.Insert(i, vm);
            }
        }
        OnPropertyChanged(nameof(PrimaryPlayable));
        OnPropertyChanged(nameof(HasLocalFiles));
    }

    // ------------------------------------------------------------- commands

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void ToggleFavorite() => _session.Store.SetFavorite(Id, !IsFavorite);

    [RelayCommand]
    private Task ResumeAsync() => ControlAsync(ControlOp.Resume, "Resumed.");

    [RelayCommand]
    private Task ReannounceAsync() => ControlAsync(ControlOp.Reannounce, "Reannounced to trackers.");

    [RelayCommand]
    private Task StopSeedingAsync() => ControlAsync(ControlOp.StopSeeding, "Stopped seeding.");

    private async Task ControlAsync(ControlOp op, string done)
    {
        try
        {
            await _session.Client.ControlTransferAsync(Id, op);
            _session.ShowInfo(done);
            await _session.QuietRefreshAsync();
        }
        catch (Exception ex)
        {
            _session.ShowError(ApiException.Friendly(ex, $"Couldn't {op.ToWire().Replace('_', ' ')} that transfer."));
        }
    }

    /// <summary>Deletes from TorBox (the view confirms first). Local files are kept.</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (IsDeleting) return;
        IsDeleting = true;
        _session.Sync.NoteDeleted(Id);
        try
        {
            await _session.Client.ControlTransferAsync(Id, ControlOp.Delete);
            foreach (var f in Files) _session.Downloads.Cancel(f.Id);
            _session.Store.Remove(Id);
            _session.ShowInfo("Removed from TorBox.");
        }
        catch (Exception ex)
        {
            _session.ShowError(ApiException.Friendly(ex, "Couldn't delete that transfer."));
        }
        finally
        {
            IsDeleting = false;
        }
    }

    public async Task RenameAsync(string newName)
    {
        var clean = newName.Trim();
        if (clean.Length == 0 || clean == Name) return;
        try
        {
            await _session.Client.EditTransferAsync(Id, name: clean);
            _session.Store.UpdateRemote(Id, t => t with { Name = clean });
            _session.ShowInfo("Renamed on TorBox.");
        }
        catch (Exception ex)
        {
            _session.ShowError(ApiException.Friendly(ex, "Couldn't rename that transfer."));
        }
    }

    /// <summary>Saves tags locally, then best-effort pushes them to TorBox (torrents only).</summary>
    public async Task SetTagsAsync(IEnumerable<string> tags)
    {
        var list = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _session.Store.SetTags(Id, list);
        if (IsWebDownload) return;
        try
        {
            await _session.Client.EditTransferAsync(Id, tags: list);
        }
        catch
        {
            _session.ShowError("Couldn't sync tags to TorBox.");
        }
    }

    public static IReadOnlyList<string> ParseTags(string text) =>
        text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [RelayCommand]
    private void CopyName()
    {
        _session.Services.Clipboard?.SetText(Name);
        _session.ShowInfo("Name copied.");
    }

    [RelayCommand]
    private void CopyHash()
    {
        if (Transfer.InfoHash is not { Length: > 0 } h) return;
        _session.Services.Clipboard?.SetText(h);
        _session.ShowInfo("Info hash copied.");
    }

    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        foreach (var f in Files.Where(f => !f.IsDownloaded && f.CanDownload).ToList())
            await f.DownloadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Files.Select(f => f.LocalPath).FirstOrDefault(p => p is not null) is { } p
            ? Path.GetDirectoryName(p)
            : null;
        if (folder is not null) _session.Services.Shell?.OpenFolder(folder);
    }

    public bool HasLocalFiles => Files.Any(f => f.IsDownloaded);
}
