namespace Minch.Core.Models;

public enum TransferKind
{
    Torrent,
    WebDl,
    Usenet,
}

public enum TransferStatus
{
    Queued,
    Downloading,
    Seeding,
    Paused,
    Error,
    Done,
}

/// <summary>
/// A single file inside a TorBox transfer. <see cref="Id"/> is a composite
/// "&lt;transferId&gt;:&lt;fileId&gt;" so it is unique across transfers; TorBox itself
/// only wants the trailing numeric id (see <see cref="Api.TorBoxIds.RawFileId"/>).
/// </summary>
public sealed record TransferFile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string PathInTransfer { get; init; } = "";
    public long SizeBytes { get; init; }
    public string? Mime { get; init; }

    // Local-only state (never comes from TorBox, preserved across syncs).
    public bool IsDownloaded { get; init; }
    public string? LocalPath { get; init; }
    public DateTimeOffset? PlayedAt { get; init; }
    public double? PlaybackPositionSec { get; init; }
}

/// <summary>
/// Domain model for a torrent / web download. Mirrors MinchKit.Transfer on macOS.
/// Transfer ids for web downloads are namespaced "webdl:&lt;n&gt;" so they never
/// collide with torrent ids in the merged list.
/// </summary>
public sealed record Transfer
{
    public required string Id { get; init; }
    public string? InfoHash { get; init; }
    public required string Name { get; init; }
    public TransferKind Kind { get; init; } = TransferKind.Torrent;
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public long SizeBytes { get; init; }
    public TransferStatus Status { get; init; } = TransferStatus.Queued;
    public double Progress { get; init; }
    public long DownloadSpeed { get; init; }
    public long UploadSpeed { get; init; }
    public int? EtaSeconds { get; init; }
    public double? Availability { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<TransferFile> Files { get; init; } = [];
    public IReadOnlyList<string> TagNames { get; init; } = [];
    public bool IsFavorite { get; init; }
    public bool IsHidden { get; init; }
    public DateTimeOffset LastSyncedAt { get; init; } = DateTimeOffset.UtcNow;
    public int? Seeds { get; init; }
    public int? Peers { get; init; }

    public bool IsActive => Status is TransferStatus.Queued or TransferStatus.Downloading or TransferStatus.Seeding;
}
