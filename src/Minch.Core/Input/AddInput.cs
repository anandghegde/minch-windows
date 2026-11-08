namespace Minch.Core.Input;

public enum AddInputKind
{
    Empty,
    Magnet,
    WebLink,
    Invalid,
}

/// <summary>Classifies what the user typed/pasted into the add box.</summary>
public static class AddInput
{
    public const long MaxTorrentFileBytes = 8L * 1024 * 1024;

    public static AddInputKind Classify(string? text)
    {
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return AddInputKind.Empty;
        if (t.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return AddInputKind.Magnet;
        if (Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            return AddInputKind.WebLink;
        return AddInputKind.Invalid;
    }

    /// <summary>
    /// Pulls the BTIH out of <c>xt=urn:btih:…</c>. Hex (40) and base32 (32)
    /// forms are returned as-is; callers compare lower-cased.
    /// </summary>
    public static string? BtihHash(string? magnet)
    {
        if (string.IsNullOrWhiteSpace(magnet)) return null;
        var m = magnet.Trim();
        var q = m.IndexOf('?');
        if (!m.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || q < 0) return null;
        foreach (var pair in m[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var name = Uri.UnescapeDataString(pair[..eq]);
            if (!name.Equals("xt", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("xt.", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            const string prefix = "urn:btih:";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var raw = value[prefix.Length..];
            return raw.Length == 0 ? null : raw;
        }
        return null;
    }

    /// <summary>
    /// Accepts a raw magnet or <c>minch://addmagnet?url=…</c> / <c>?magnet=…</c>
    /// (from the protocol handler / command line). Returns null when no magnet is found.
    /// </summary>
    public static string? ExternalMagnet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().Trim('"');
        if (text.StartsWith("minch:", StringComparison.OrdinalIgnoreCase))
        {
            var q = text.IndexOf('?');
            if (q < 0) return null;
            string? found = null;
            foreach (var pair in text[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var name = pair[..eq];
                if (name is "url" or "magnet")
                {
                    found = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                    break;
                }
            }
            text = found?.Trim() ?? "";
        }
        return text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    /// <summary>Returns the language-matched track index, or null when nothing matches confidently.</summary>
    public static int? MatchTrack(IEnumerable<Api.StreamTrack> tracks, string? preference)
    {
        var needle = preference?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(needle)) return null;
        foreach (var t in tracks)
        {
            var lang = t.Language?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(lang) && (lang == needle || lang.StartsWith(needle, StringComparison.Ordinal) || needle.StartsWith(lang, StringComparison.Ordinal)))
                return t.Index;
            if (t.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)) return t.Index;
        }
        return null;
    }
}
