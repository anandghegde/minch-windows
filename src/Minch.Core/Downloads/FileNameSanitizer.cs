using System.Text;

namespace Minch.Core.Downloads;

/// <summary>Makes TorBox names safe as a single Windows path segment.</summary>
public static class FileNameSanitizer
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public const int MaxSegmentLength = 200;

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Untitled";
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(c) ? '_' : c);

        var result = sb.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length == 0 || result.All(c => c == '_' || c == '.')) return "Untitled";

        var dot = result.IndexOf('.');
        var stem = dot >= 0 ? result[..dot] : result;
        if (Reserved.Contains(stem.TrimEnd())) result = "_" + result;

        if (result.Length > MaxSegmentLength)
        {
            var ext = Path.GetExtension(result);
            if (ext.Length > 16) ext = "";
            result = result[..(MaxSegmentLength - ext.Length)].TrimEnd('.', ' ') + ext;
        }
        return result;
    }
}
