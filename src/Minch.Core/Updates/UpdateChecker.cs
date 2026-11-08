using System.Net.Http.Headers;
using System.Text.Json;

namespace Minch.Core.Updates;

public sealed record UpdateInfo(Version Version, string Tag, Uri PageUrl, string? Notes);

/// <summary>Checks GitHub's "latest release" for a newer tag. Silent on any failure.</summary>
public sealed class UpdateChecker(HttpClient http, string repo = "anandghegde/minch-windows")
{
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Minch-Windows", current.ToString(3)));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (ParseVersion(tag) is not { } latest || latest <= Normalize(current)) return null;
            var page = root.TryGetProperty("html_url", out var u) && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri)
                ? uri
                : new Uri($"https://github.com/{repo}/releases/latest");
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new UpdateInfo(latest, tag, page, notes);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static Version? ParseVersion(string tag)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOfAny(['-', '+']);
        if (dash >= 0) t = t[..dash];
        return Version.TryParse(t.Contains('.') ? t : t + ".0", out var v) ? Normalize(v) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
