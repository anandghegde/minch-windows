using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minch.Core.Models;

namespace Minch.Core.Api;

/// <summary>
/// TorBox REST client. Port of MinchAPI.TorBoxClient on macOS.
/// All responses use the envelope <c>{success, detail, error, data}</c>.
/// </summary>
public sealed class TorBoxClient
{
    public static readonly Uri DefaultBaseUrl = new("https://api.torbox.app/v1/api/");

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly IApiKeyProvider _keys;

    public TorBoxClient(HttpClient http, IApiKeyProvider keys, Uri? baseUrl = null)
    {
        _http = http;
        _keys = keys;
        var b = (baseUrl ?? DefaultBaseUrl).ToString();
        _baseUrl = new Uri(b.EndsWith('/') ? b : b + "/");
    }

    // ---------------------------------------------------------------- account

    public async Task<UserAccount> MeAsync(CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "user/me", ct: ct);
        return TorBoxMapping.ToAccount(data.Data);
    }

    public async Task<IReadOnlyDictionary<string, SettingValue>> LoadUserSettingsAsync(CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "user/me", Q(("settings", "true")), ct: ct);
        return TorBoxMapping.ToSettings(data.Data);
    }

    /// <summary>PUT /user/settings/editsettings — submit only the changed keys.</summary>
    public async Task UpdateSettingsAsync(IReadOnlyDictionary<string, SettingValue> values, CancellationToken ct = default)
    {
        var body = new JsonObject();
        foreach (var (key, value) in values)
        {
            body[key] = value.Kind switch
            {
                SettingKind.Bool => JsonValue.Create(value.Bool),
                SettingKind.String => JsonValue.Create(value.Text ?? ""),
                SettingKind.Number => JsonValue.Create(value.Number),
                _ => null,
            };
        }
        await SendAsync(HttpMethod.Put, "user/settings/editsettings", json: body, requireData: false, ct: ct);
    }

    public async Task<IReadOnlyList<Subscription>> SubscriptionsAsync(CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "user/subscriptions", requireData: false, ct: ct);
        return data.Data.Items().Select(TorBoxMapping.ToSubscription).ToList();
    }

    public async Task<UserStats> StatsAsync(CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "user/stats", Q(("general", "true"), ("bandwidth", "false")), ct: ct);
        return TorBoxMapping.ToStats(data.Data);
    }

    // -------------------------------------------------------------- transfers

    public async Task<IReadOnlyList<Transfer>> ListTransfersAsync(bool bypassCache = false, CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "torrents/mylist", bypassCache ? Q(("bypass_cache", "true")) : null, requireData: false, ct: ct);
        return data.Data.Items().Select(TorBoxMapping.ToTorrent).ToList();
    }

    public async Task<IReadOnlyList<Transfer>> ListWebDownloadsAsync(bool bypassCache = false, CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "webdl/mylist", bypassCache ? Q(("bypass_cache", "true")) : null, requireData: false, ct: ct);
        return data.Data.Items().Select(TorBoxMapping.ToWebDownload).ToList();
    }

    public async Task<IReadOnlyList<Hoster>> ListHostersAsync(CancellationToken ct = default)
    {
        var data = await SendAsync(HttpMethod.Get, "webdl/hosters", requireData: false, ct: ct);
        return data.Data.Items().Select(TorBoxMapping.ToHoster).ToList();
    }

    public Task<AddResult> AddMagnetAsync(string magnet, string? name = null, bool cacheOnly = false, CancellationToken ct = default)
    {
        var fields = new List<(string, string)> { ("magnet", magnet) };
        if (!string.IsNullOrEmpty(name)) fields.Add(("name", name));
        if (cacheOnly) fields.Add(("as_queued", "false"));
        return AddAsync("torrents/createtorrent", fields, null, ct);
    }

    public Task<AddResult> AddTorrentFileAsync(byte[] data, string filename, string? name = null, bool cacheOnly = false, CancellationToken ct = default)
    {
        var fields = new List<(string, string)>();
        if (!string.IsNullOrEmpty(name)) fields.Add(("name", name));
        if (cacheOnly) fields.Add(("as_queued", "false"));
        return AddAsync("torrents/createtorrent", fields, ("file", filename, data), ct);
    }

    public Task<AddResult> AddWebDownloadAsync(string link, string? name = null, CancellationToken ct = default)
    {
        var fields = new List<(string, string)> { ("link", link) };
        if (!string.IsNullOrEmpty(name)) fields.Add(("name", name));
        return AddAsync("webdl/createwebdownload", fields, null, ct);
    }

    private async Task<AddResult> AddAsync(string path, List<(string Name, string Value)> fields, (string Field, string FileName, byte[] Data)? file, CancellationToken ct)
    {
        var form = new MultipartFormDataContent("minch-" + Guid.NewGuid().ToString("N"));
        foreach (var (n, v) in fields) form.Add(new StringContent(v, Encoding.UTF8), n);
        if (file is { } f)
        {
            var part = new ByteArrayContent(f.Data);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(part, f.Field, f.FileName);
        }
        var env = await SendAsync(HttpMethod.Post, path, content: form, ct: ct);
        var d = env.Data;
        return new AddResult(env.Detail, d.Int("queued_id"), d.Int("torrent_id") ?? d.Int("webdownload_id"));
    }

    /// <summary>Returns the lower-cased subset of <paramref name="hashes"/> TorBox reports as cached.</summary>
    public async Task<IReadOnlySet<string>> CheckCachedAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default)
    {
        var hits = new HashSet<string>(StringComparer.Ordinal);
        if (hashes.Count == 0) return hits;
        var env = await SendAsync(HttpMethod.Get, "torrents/checkcached",
            Q(("hash", string.Join(',', hashes)), ("format", "object"), ("list_files", "false")),
            requireData: false, ct: ct);
        if (env.Data.ValueKind != JsonValueKind.Object) return hits;
        var wanted = hashes.Select(h => h.ToLowerInvariant()).ToHashSet();
        foreach (var p in env.Data.EnumerateObject())
        {
            var key = p.Name.ToLowerInvariant();
            if (!wanted.Contains(key)) continue;
            if (p.Value.ValueKind is JsonValueKind.False or JsonValueKind.Null) continue;
            hits.Add(key);
        }
        return hits;
    }

    public async Task<Uri> RequestDownloadUrlAsync(string transferId, string fileId, CancellationToken ct = default)
    {
        var raw = TorBoxIds.RawFileId(fileId);
        var key = await _keys.CurrentKeyAsync(ct) ?? "";
        Envelope env;
        if (TorBoxIds.SplitWebDl(transferId) is { } web)
            env = await SendAsync(HttpMethod.Get, "webdl/requestdl",
                Q(("web_id", web), ("file_id", raw), ("append_name", "true"), ("token", key)), ct: ct);
        else
            env = await SendAsync(HttpMethod.Get, "torrents/requestdl",
                Q(("torrent_id", transferId), ("file_id", raw), ("append_name", "true"), ("token", key)), ct: ct);

        var s = env.Data.ValueKind == JsonValueKind.String ? env.Data.GetString() : null;
        if (s is null || !Uri.TryCreate(s, UriKind.Absolute, out var uri))
            throw new ApiException(ApiErrorKind.Decoding, $"requestdl returned non-URL: {s}");
        return uri;
    }

    public async Task<StreamSession> CreateStreamAsync(string transferId, string fileId, StreamOptions? options = null, CancellationToken ct = default)
    {
        options ??= StreamOptions.Default;
        var (id, type) = TorBoxIds.StreamRoute(transferId);
        var q = new List<(string, string)> { ("id", id), ("file_id", TorBoxIds.RawFileId(fileId)), ("type", type) };
        if (options.AudioIndex is { } a) q.Add(("chosen_audio_index", a.ToString(CultureInfo.InvariantCulture)));
        if (options.SubtitleIndex is { } s) q.Add(("chosen_subtitle_index", s.ToString(CultureInfo.InvariantCulture)));
        if (options.ResolutionIndex is { } r) q.Add(("chosen_resolution_index", r.ToString(CultureInfo.InvariantCulture)));
        if (options.ScrobblingEnabled is { } sc) q.Add(("scrobbling_enabled", sc ? "true" : "false"));

        var env = await SendAsync(HttpMethod.Get, "stream/createstream", q, ct: ct);
        var p = env.Data;
        var hls = p.Str("hls_url");
        if (hls is null || !Uri.TryCreate(hls, UriKind.Absolute, out var url))
            throw new ApiException(ApiErrorKind.Decoding, $"createstream returned non-URL: {hls}");
        return new StreamSession
        {
            Url = url,
            Token = p.Str("token"),
            Subtitles = TorBoxMapping.ToTracks(p, "Subtitle", "subtitles"),
            AudioTracks = TorBoxMapping.ToTracks(p, "Audio", "audio_tracks", "audio"),
            Resolutions = TorBoxMapping.ToTracks(p, "Quality", "resolutions", "qualities"),
            SelectedSubtitleIndex = options.SubtitleIndex,
            SelectedAudioIndex = options.AudioIndex,
            SelectedResolutionIndex = options.ResolutionIndex,
        };
    }

    public async Task<StreamSession> GetStreamDataAsync(string transferId, string fileId, CancellationToken ct = default)
    {
        var (id, type) = TorBoxIds.StreamRoute(transferId);
        var env = await SendAsync(HttpMethod.Get, "stream/getstreamdata",
            Q(("id", id), ("file_id", TorBoxIds.RawFileId(fileId)), ("type", type)), requireData: false, ct: ct);
        var p = env.Data;
        return new StreamSession
        {
            Url = new Uri("about:blank"),
            Subtitles = TorBoxMapping.ToTracks(p, "Subtitle", "subtitles"),
            AudioTracks = TorBoxMapping.ToTracks(p, "Audio", "audio_tracks", "audio"),
            Resolutions = TorBoxMapping.ToTracks(p, "Quality", "resolutions", "qualities"),
        };
    }

    public async Task ControlTransferAsync(string id, ControlOp op, CancellationToken ct = default)
    {
        var body = new JsonObject { ["operation"] = op.ToWire() };
        if (TorBoxIds.SplitWebDl(id) is { } web)
        {
            body["webdl_id"] = IdNode(web);
            await SendAsync(HttpMethod.Post, "webdl/controlwebdownload", json: body, requireData: false, ct: ct);
        }
        else
        {
            body["torrent_id"] = IdNode(id);
            await SendAsync(HttpMethod.Post, "torrents/controltorrent", json: body, requireData: false, ct: ct);
        }
    }

    /// <summary>Web downloads accept a name only; tags are silently dropped for them.</summary>
    public async Task EditTransferAsync(string id, string? name = null, IReadOnlyList<string>? tags = null, CancellationToken ct = default)
    {
        var body = new JsonObject();
        if (name is not null) body["name"] = name;
        if (TorBoxIds.SplitWebDl(id) is { } web)
        {
            body["webdl_id"] = IdNode(web);
            await SendAsync(HttpMethod.Put, "webdl/editwebdownload", json: body, requireData: false, ct: ct);
        }
        else
        {
            body["torrent_id"] = IdNode(id);
            if (tags is not null) body["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
            await SendAsync(HttpMethod.Put, "torrents/edittorrent", json: body, requireData: false, ct: ct);
        }
    }

    // ---------------------------------------------------------------- plumbing

    private readonly record struct Envelope(JsonElement Data, string? Detail);

    private static List<(string, string)> Q(params (string, string)[] items) => [.. items];

    private static JsonNode IdNode(string id) =>
        long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : JsonValue.Create(id)!;

    internal Uri BuildUri(string path, IReadOnlyList<(string Name, string Value)>? query)
    {
        var sb = new StringBuilder(path.TrimStart('/'));
        if (query is { Count: > 0 })
        {
            sb.Append('?');
            sb.AppendJoin('&', query.Select(q => Uri.EscapeDataString(q.Name) + "=" + Uri.EscapeDataString(q.Value)));
        }
        return new Uri(_baseUrl, sb.ToString());
    }

    private async Task<Envelope> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<(string, string)>? query = null,
        JsonNode? json = null,
        HttpContent? content = null,
        bool requireData = true,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(method, BuildUri(path, query));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var key = await _keys.CurrentKeyAsync(ct);
        if (!string.IsNullOrEmpty(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (content is not null)
            req.Content = content;
        else if (json is not null)
            req.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(ApiErrorKind.Network, ex.Message, null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(ApiErrorKind.Network, "request timed out", null, ex);
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            var code = (int)resp.StatusCode;
            if (code is < 200 or >= 300)
                throw MapStatus(code, body, resp);

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException ex)
            {
                throw new ApiException(ApiErrorKind.Decoding, ex.Message, code, ex);
            }

            using (doc)
            {
                var root = doc.RootElement;
                var success = root.Bool("success") ?? false;
                var detail = root.Str("detail");
                if (!success)
                    throw new ApiException(ApiErrorKind.Validation, detail ?? root.Str("error") ?? "envelope reported failure", code);
                var hasData = root.TryProp("data", out var data);
                if (requireData && !hasData)
                    throw new ApiException(ApiErrorKind.Validation, detail ?? root.Str("error") ?? "envelope missing data", code);
                return new Envelope(hasData ? data.Clone() : default, detail);
            }
        }
    }

    internal static ApiException MapStatus(int code, string body, HttpResponseMessage? resp = null)
    {
        var server = EnvelopeMessage(body);
        switch (code)
        {
            case 401 or 403:
                return new ApiException(ApiErrorKind.Auth, server ?? $"HTTP {code}", code);
            case 402 or 429:
                var retry = Header(resp, "Retry-After") ?? "?";
                var remaining = Header(resp, "X-RateLimit-Remaining") ?? "?";
                var rate = $"retry-after={retry}s remaining={remaining}";
                return new ApiException(ApiErrorKind.Quota, server is null ? $"HTTP {code} {rate}" : $"{server} ({rate})", code);
            case 400 or 422:
                return new ApiException(ApiErrorKind.Validation, server ?? body, code);
            case >= 500 and <= 599:
                return new ApiException(ApiErrorKind.Transient, server ?? $"HTTP {code}", code);
            default:
                return new ApiException(ApiErrorKind.Unknown, server ?? body, code);
        }
    }

    private static string? Header(HttpResponseMessage? resp, string name)
    {
        if (resp is null) return null;
        if (resp.Headers.TryGetValues(name, out var v)) return v.FirstOrDefault();
        if (name == "Retry-After" && resp.Headers.RetryAfter is { } ra)
            return ra.Delta is { } d ? ((int)d.TotalSeconds).ToString(CultureInfo.InvariantCulture) : ra.Date?.ToString("R");
        return null;
    }

    /// <summary>Best-effort extraction of envelope detail/error from an error body.</summary>
    internal static string? EnvelopeMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var d = doc.RootElement.Str("detail")?.Trim();
            if (!string.IsNullOrEmpty(d)) return d;
            var e = doc.RootElement.Str("error")?.Trim();
            return string.IsNullOrEmpty(e) ? null : e;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
