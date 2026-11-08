using System.Globalization;
using System.Text.Json;

namespace Minch.Core.Api;

/// <summary>
/// Tolerant accessors over <see cref="JsonElement"/>. TorBox's payloads drift
/// between API versions (numbers as strings, missing fields, null vs absent),
/// so the client reads fields defensively instead of binding strict DTOs.
/// </summary>
internal static class Json
{
    public static bool TryProp(this JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            return true;
        value = default;
        return false;
    }

    public static string? Str(this JsonElement e, string name) =>
        e.TryProp(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            }
            : null;

    public static long? Long(this JsonElement e, string name)
    {
        if (!e.TryProp(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt64(out var l)) return l;
            if (v.TryGetDouble(out var d)) return (long)d;
        }
        if (v.ValueKind == JsonValueKind.String &&
            long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            return s;
        return null;
    }

    public static int? Int(this JsonElement e, string name) => e.Long(name) is { } l ? (int)Math.Clamp(l, int.MinValue, int.MaxValue) : null;

    public static double? Double(this JsonElement e, string name)
    {
        if (!e.TryProp(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return s;
        return null;
    }

    public static bool? Bool(this JsonElement e, string name)
    {
        if (!e.TryProp(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n != 0 : null,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null,
        };
    }

    public static DateTimeOffset? Date(this JsonElement e, string name) =>
        e.Str(name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;

    public static IEnumerable<JsonElement> Array(this JsonElement e, string name) =>
        e.TryProp(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    public static IEnumerable<JsonElement> Items(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];
}
