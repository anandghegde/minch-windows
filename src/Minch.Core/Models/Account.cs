namespace Minch.Core.Models;

public sealed record UserAccount(string? Email, int? Plan, bool? IsSubscribed)
{
    public string PlanName => Plan switch
    {
        0 => "Free",
        1 => "Essential",
        2 => "Pro",
        3 => "Standard",
        _ => "Member",
    };
}

public sealed record Subscription(
    string SubscriptionCode,
    string? PlanName,
    string? PlanCode,
    string? Status,
    string? Gateway,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UserStats(long? TotalDownloaded, long? TotalUploaded, double? Ratio, int? TotalItemsDownloaded);

public sealed record Hoster(int Id, string Name, IReadOnlyList<string> Domains, string? Url, string? Icon, bool? IsDegraded, string? Type);

/// <summary>Outcome of an add call; <see cref="Detail"/> is TorBox's human message ("Torrent queued", …).</summary>
public sealed record AddResult(string? Detail, int? QueuedId, int? TorrentId)
{
    public bool WasQueued => QueuedId is not null && TorrentId is null;
    public bool IsEmpty => QueuedId is null && TorrentId is null;
}

public enum ControlOp
{
    Resume,
    Reannounce,
    Delete,
    StopSeeding,
}

public static class ControlOpExtensions
{
    public static string ToWire(this ControlOp op) => op switch
    {
        ControlOp.Resume => "resume",
        ControlOp.Reannounce => "reannounce",
        ControlOp.Delete => "delete",
        ControlOp.StopSeeding => "stop_seeding",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };
}

public enum SettingKind
{
    Bool,
    String,
    Number,
    Null,
}

/// <summary>Scalar value of a TorBox account setting. Arrays/objects are skipped.</summary>
public readonly record struct SettingValue(SettingKind Kind, bool Bool = false, string? Text = null, double Number = 0)
{
    public static SettingValue FromBool(bool v) => new(SettingKind.Bool, Bool: v);
    public static SettingValue FromString(string v) => new(SettingKind.String, Text: v);
    public static SettingValue FromNumber(double v) => new(SettingKind.Number, Number: v);
    public static readonly SettingValue Null = new(SettingKind.Null);

    public string NumberText => Kind == SettingKind.Number
        ? (Math.Round(Number) == Number && Math.Abs(Number) < 1e15
            ? ((long)Number).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : Number.ToString(System.Globalization.CultureInfo.InvariantCulture))
        : "";
}
