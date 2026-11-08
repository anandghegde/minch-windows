using System.Globalization;
using Minch.Core.Models;

namespace Minch.Core.Formatting;

/// <summary>Visual phase of a transfer row (status glyph + meta line).</summary>
public enum StatusPhase
{
    Idle,
    Queued,
    Active,
    Paused,
    Error,
    Done,
}

/// <summary>Port of MinchTransferRow formatting helpers. Pure and culture-invariant.</summary>
public static class TransferFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static StatusPhase Phase(TransferStatus status) => status switch
    {
        TransferStatus.Queued => StatusPhase.Queued,
        TransferStatus.Downloading or TransferStatus.Seeding => StatusPhase.Active,
        TransferStatus.Paused => StatusPhase.Paused,
        TransferStatus.Error => StatusPhase.Error,
        TransferStatus.Done => StatusPhase.Done,
        _ => StatusPhase.Idle,
    };

    public static double Clamp(double p) => Math.Max(0, Math.Min(p, 1));

    public static string Percent(double progress) => (Clamp(progress) * 100).ToString("0", Inv) + "%";

    /// <summary>Decimal units, like ByteCountFormatter's .file style: "12 KB", "1.5 MB", "2.34 GB".</summary>
    public static string Size(long bytes) => Bytes(bytes, 1000);

    /// <summary>Binary units plus "/s"; null when there's nothing to show.</summary>
    public static string? Speed(long bytesPerSecond) => bytesPerSecond > 0 ? Bytes(bytesPerSecond, 1024) + "/s" : null;

    private static string Bytes(long bytes, double unit)
    {
        if (bytes <= 0) return "Zero KB";
        if (bytes < unit) return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        var value = bytes / unit;
        var i = 0;
        while (value >= unit && i < units.Length - 1)
        {
            value /= unit;
            i++;
        }
        var format = i switch { 0 => "0", 1 => "0.#", _ => "0.##" };
        return value.ToString(format, Inv) + " " + units[i];
    }

    /// <summary>"&lt;1m left", "3m left", "2h left", "1h 15m left"; null for non-positive input.</summary>
    public static string? Eta(int seconds)
    {
        if (seconds <= 0) return null;
        if (seconds < 60) return "<1m left";
        var totalMinutes = seconds / 60;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0) return $"{minutes}m left";
        if (minutes == 0) return $"{hours}h left";
        return $"{hours}h {minutes}m left";
    }

    public static string? Swarm(int? seeds, int? peers, StatusPhase phase)
    {
        if (seeds is not { } s || phase != StatusPhase.Active) return null;
        return peers is > 0 ? $"{s} seeders · {peers} peers" : $"{s} seeders";
    }

    /// <summary>"just now", "3m", "3h", "2d".</summary>
    public static string RelativeShort(DateTimeOffset date, DateTimeOffset now)
    {
        var elapsed = Math.Max(0, (long)(now - date).TotalSeconds);
        if (elapsed < 60) return "just now";
        var minutes = elapsed / 60;
        if (minutes < 60) return $"{minutes}m";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h";
        return $"{hours / 24}d";
    }

    public static string MetaLine(Transfer t, int? queuePosition = null, DateTimeOffset? now = null) =>
        MetaLine(Phase(t.Status), t.SizeBytes, t.DownloadSpeed, t.Progress, t.EtaSeconds, queuePosition, t.Error,
            t.AddedAt, t.Seeds, t.Peers, now ?? DateTimeOffset.UtcNow);

    public static string MetaLine(
        StatusPhase phase, long sizeBytes, long downloadSpeed, double progress, int? etaSeconds,
        int? queuePosition, string? error, DateTimeOffset? addedAt, int? seeds, int? peers, DateTimeOffset now)
    {
        switch (phase)
        {
            case StatusPhase.Idle:
                return Size(sizeBytes);
            case StatusPhase.Queued:
            {
                var parts = new List<string> { "Queued" };
                if (progress > 0) parts.Add(Percent(progress));
                if (queuePosition is { } q) parts.Add($"#{q}");
                return string.Join(" · ", parts);
            }
            case StatusPhase.Active:
            {
                var parts = new List<string> { Size(sizeBytes), Percent(progress) };
                if (Speed(downloadSpeed) is { } sp) parts.Add(sp);
                if (etaSeconds is { } e && Eta(e) is { } eta) parts.Add(eta);
                if (Swarm(seeds, peers, phase) is { } sw) parts.Add(sw);
                return string.Join(" · ", parts);
            }
            case StatusPhase.Paused:
                return $"Paused · {Size(sizeBytes)} · {Percent(progress)}";
            case StatusPhase.Error:
                return string.IsNullOrWhiteSpace(error) ? "Error" : error;
            case StatusPhase.Done:
                return addedAt is { } a ? $"{Size(sizeBytes)} · added {RelativeShort(a, now)} ago" : Size(sizeBytes);
            default:
                return Size(sizeBytes);
        }
    }

    public static string StatusLabel(TransferStatus status) => status switch
    {
        TransferStatus.Queued => "Queued",
        TransferStatus.Downloading => "Downloading",
        TransferStatus.Seeding => "Seeding",
        TransferStatus.Paused => "Paused",
        TransferStatus.Error => "Error",
        TransferStatus.Done => "Ready",
        _ => status.ToString(),
    };

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", Inv) : t.ToString(@"m\:ss", Inv);
}
