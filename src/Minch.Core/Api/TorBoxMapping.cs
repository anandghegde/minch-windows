using System.Text.Json;
using Minch.Core.Models;

namespace Minch.Core.Api;

/// <summary>JSON → domain mapping. Port of TorBoxTransferDTO / WebDownloadDTO / StreamTrackDTO.</summary>
public static class TorBoxMapping
{
    public static TransferStatus MapStatus(string? raw) => raw?.ToLowerInvariant() switch
    {
        "downloading" or "metadl" or "downloadingmetadata" => TransferStatus.Downloading,
        "uploading" or "seeding" => TransferStatus.Seeding,
        "completed" or "cached" or "downloaded" or "finished" => TransferStatus.Done,
        "paused" => TransferStatus.Paused,
        "error" or "failed" or "missingfiles" => TransferStatus.Error,
        _ => TransferStatus.Queued,
    };

    internal static Transfer ToTorrent(JsonElement e) => ToTransfer(e, TransferKind.Torrent);

    internal static Transfer ToWebDownload(JsonElement e) => ToTransfer(e, TransferKind.WebDl);

    private static Transfer ToTransfer(JsonElement e, TransferKind kind)
    {
        var rawId = e.Str("id") ?? throw new ApiException(ApiErrorKind.Decoding, "transfer without id");
        var id = kind == TransferKind.WebDl ? TorBoxIds.WebDlPrefix + rawId : rawId;
        var state = e.Str("download_state");
        var status = MapStatus(state);
        var now = DateTimeOffset.UtcNow;
        return new Transfer
        {
            Id = id,
            InfoHash = e.Str("hash"),
            Name = NonEmpty(e.Str("name")) ?? "Untitled",
            Kind = kind,
            AddedAt = e.Date("created_at") ?? now,
            SizeBytes = e.Long("size") ?? 0,
            Status = status,
            Progress = Math.Clamp(e.Double("progress") ?? 0, 0, 1),
            DownloadSpeed = e.Long("download_speed") ?? 0,
            UploadSpeed = e.Long("upload_speed") ?? 0,
            EtaSeconds = e.Int("eta"),
            Availability = kind == TransferKind.Torrent ? e.Double("availability") : null,
            Error = status == TransferStatus.Error ? (NonEmpty(e.Str("error")) ?? state) : null,
            Files = e.Array("files").Select(f => ToFile(f, id)).ToList(),
            Seeds = kind == TransferKind.Torrent ? e.Int("seeds") : null,
            Peers = kind == TransferKind.Torrent ? e.Int("peers") : null,
            LastSyncedAt = now,
        };
    }

    private static TransferFile ToFile(JsonElement f, string transferId)
    {
        var fid = f.Str("id") ?? "0";
        var name = NonEmpty(f.Str("name"));
        var shortName = NonEmpty(f.Str("short_name"));
        return new TransferFile
        {
            Id = $"{transferId}:{fid}",
            Name = name ?? shortName ?? $"file_{fid}",
            PathInTransfer = shortName ?? name ?? $"file_{fid}",
            SizeBytes = f.Long("size") ?? 0,
            Mime = f.Str("mimetype"),
        };
    }

    internal static UserAccount ToAccount(JsonElement e) =>
        new(e.Str("email"), e.Int("plan"), e.Bool("is_subscribed"));

    internal static Subscription ToSubscription(JsonElement e) => new(
        e.Str("subscription_code") ?? e.Str("id") ?? Guid.NewGuid().ToString("N"),
        e.Str("plan_name"),
        e.Str("plan_code"),
        e.Str("status"),
        e.Str("gateway"),
        e.Date("created_at"),
        e.Date("updated_at"));

    internal static UserStats ToStats(JsonElement e)
    {
        var g = e.TryProp("general", out var general) ? general : e;
        return new UserStats(g.Long("total_downloaded"), g.Long("total_uploaded"), g.Double("ratio"), g.Int("total_items_downloaded"));
    }

    internal static Hoster ToHoster(JsonElement e) => new(
        e.Int("id") ?? 0,
        e.Str("name") ?? "Unknown",
        e.Array("domains").Where(d => d.ValueKind == JsonValueKind.String).Select(d => d.GetString()!).ToList(),
        e.Str("url"),
        e.Str("icon"),
        e.Bool("status"),
        e.Str("type"));

    /// <summary>Tolerant track parsing: index|id, name|title|label, language|lang.</summary>
    internal static IReadOnlyList<StreamTrack> ToTracks(JsonElement payload, string kind, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!payload.TryProp(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            var list = new List<StreamTrack>();
            var offset = 0;
            foreach (var t in arr.EnumerateArray())
            {
                var index = t.Int("index") ?? t.Int("id") ?? offset;
                var name = NonEmpty(t.Str("name")) ?? NonEmpty(t.Str("title")) ?? NonEmpty(t.Str("label"));
                var lang = NonEmpty(t.Str("language")) ?? NonEmpty(t.Str("lang"));
                var label = name ?? lang?.ToUpperInvariant() ?? $"{kind} {index + 1}";
                list.Add(new StreamTrack(index, label, lang));
                offset++;
            }
            return list;
        }
        return [];
    }

    internal static Dictionary<string, SettingValue> ToSettings(JsonElement me)
    {
        var result = new Dictionary<string, SettingValue>(StringComparer.Ordinal);
        if (!me.TryProp("settings", out var settings) || settings.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in settings.EnumerateObject())
        {
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.True: result[p.Name] = SettingValue.FromBool(true); break;
                case JsonValueKind.False: result[p.Name] = SettingValue.FromBool(false); break;
                case JsonValueKind.String: result[p.Name] = SettingValue.FromString(p.Value.GetString() ?? ""); break;
                case JsonValueKind.Number: result[p.Name] = SettingValue.FromNumber(p.Value.GetDouble()); break;
                case JsonValueKind.Null: result[p.Name] = SettingValue.Null; break;
            }
        }
        return result;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
