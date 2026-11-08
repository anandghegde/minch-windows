using System.Globalization;

namespace Minch.Core.Downloads;

/// <summary>Parsed <c>Content-Range: bytes start-end/total</c> header.</summary>
public readonly record struct ContentRange(long Start, long End, long TotalBytes)
{
    public long Length => End - Start + 1;

    public static ContentRange? Parse(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase)) return null;
        var body = trimmed["bytes ".Length..];
        var slash = body.IndexOf('/');
        if (slash < 0) return null;
        if (!long.TryParse(body[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var total) || total <= 0)
            return null;
        var bounds = body[..slash];
        var dash = bounds.IndexOf('-');
        if (dash < 0) return null;
        if (!long.TryParse(bounds[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !long.TryParse(bounds[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end))
            return null;
        if (start < 0 || end < start || end >= total) return null;
        return new ContentRange(start, end, total);
    }
}

public readonly record struct RangeChunk(int Index, long Start, long End)
{
    public long Length => End - Start + 1;
}

public static class RangePlanner
{
    public static IReadOnlyList<RangeChunk> Plan(long totalBytes, long chunkSize)
    {
        chunkSize = Math.Max(1, chunkSize);
        var chunks = new List<RangeChunk>();
        long start = 0;
        var index = 0;
        while (start < totalBytes)
        {
            var end = Math.Min(totalBytes - 1, start + chunkSize - 1);
            chunks.Add(new RangeChunk(index++, start, end));
            start = end + 1;
        }
        return chunks;
    }
}
