using System.Net;
using System.Net.Http.Headers;
using Microsoft.Win32.SafeHandles;

namespace Minch.Core.Downloads;

public sealed record RangeDownloadOptions
{
    public long MinimumSizeBytes { get; init; } = 8L * 1024 * 1024;
    public long ChunkSizeBytes { get; init; } = 8L * 1024 * 1024;
    public int MaxConcurrentChunks { get; init; } = 4;
    public int ChunkRetries { get; init; } = 3;
}

/// <summary>
/// Downloads a file as parallel HTTP range requests written straight into a
/// preallocated file. Returns false (without touching the destination) when
/// the server doesn't support ranges or the file is too small to bother;
/// callers then fall back to a single stream.
/// </summary>
public sealed class RangeDownloadAccelerator(HttpClient http, RangeDownloadOptions? options = null)
{
    private readonly RangeDownloadOptions _opt = options ?? new RangeDownloadOptions();

    public async Task<long?> ProbeAsync(Uri url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(0, 0);
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode != HttpStatusCode.PartialContent) return null;
        var range = ParseContentRange(resp);
        if (range is not { Start: 0, End: 0 } r) return null;
        var body = await resp.Content.ReadAsByteArrayAsync(ct);
        return body.Length == 1 ? r.TotalBytes : null;
    }

    /// <param name="partialPath">Temp file the chunks are written into; moved to <paramref name="destination"/> on success.</param>
    public async Task<bool> DownloadAsync(Uri url, string destination, string partialPath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long total;
        try
        {
            if (await ProbeAsync(url, ct) is not { } t || t < _opt.MinimumSizeBytes) return false;
            total = t;
        }
        catch (HttpRequestException)
        {
            return false;
        }

        var chunks = RangePlanner.Plan(total, _opt.ChunkSizeBytes);
        if (chunks.Count <= 1) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        long done = 0;
        var started = DateTimeOffset.UtcNow;
        progress?.Report(new DownloadProgress(0, total, 0));

        try
        {
            using (var handle = File.OpenHandle(partialPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                       FileOptions.Asynchronous, preallocationSize: total))
            {
                await Parallel.ForEachAsync(chunks,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _opt.MaxConcurrentChunks), CancellationToken = ct },
                    async (chunk, token) =>
                    {
                        await DownloadChunkWithRetryAsync(url, chunk, total, handle, bytes =>
                        {
                            var now = Interlocked.Add(ref done, bytes);
                            var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
                            progress?.Report(new DownloadProgress(now, total, elapsed > 0.25 ? (long)(now / elapsed) : 0));
                        }, token);
                    });
                RandomAccess.SetLength(handle, total);
            }
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(partialPath, destination, overwrite: true);
            progress?.Report(new DownloadProgress(total, total, 0));
            return true;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private async Task DownloadChunkWithRetryAsync(Uri url, RangeChunk chunk, long total, SafeFileHandle handle, Action<long> onBytes, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            long written = 0;
            try
            {
                await DownloadChunkAsync(url, chunk, total, handle, b => { written += b; onBytes(b); }, ct);
                return;
            }
            catch (Exception ex) when ((ex is HttpRequestException or IOException or InvalidDataException) && attempt < _opt.ChunkRetries && !ct.IsCancellationRequested)
            {
                onBytes(-written); // roll back progress for the failed attempt
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)), ct);
            }
        }
    }

    private async Task DownloadChunkAsync(Uri url, RangeChunk chunk, long total, SafeFileHandle handle, Action<long> onBytes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(chunk.Start, chunk.End);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode != HttpStatusCode.PartialContent)
            throw new HttpRequestException($"range request returned HTTP {(int)resp.StatusCode}");
        var range = ParseContentRange(resp);
        if (range is not { } r || r.Start != chunk.Start || r.End != chunk.End || r.TotalBytes != total)
            throw new InvalidDataException("server returned an unexpected Content-Range");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[256 * 1024];
        var offset = chunk.Start;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (offset + read > chunk.End + 1) throw new InvalidDataException("chunk overflow");
            await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), offset, ct);
            offset += read;
            onBytes(read);
        }
        if (offset != chunk.End + 1) throw new IOException("chunk ended early");
    }

    private static ContentRange? ParseContentRange(HttpResponseMessage resp)
    {
        if (resp.Content.Headers.ContentRange is { HasRange: true, From: { } from, To: { } to, Length: { } len })
            return from >= 0 && to >= from && to < len ? new ContentRange(from, to, len) : null;
        return resp.Content.Headers.TryGetValues("Content-Range", out var values) ? ContentRange.Parse(values.FirstOrDefault()) : null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public readonly record struct DownloadProgress(long BytesDone, long BytesTotal, long BytesPerSecond)
{
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1) : 0;
}

/// <summary>Synchronous <see cref="IProgress{T}"/> — invokes the callback inline, serialised.</summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Lock _gate = new();
    public void Report(T value)
    {
        lock (_gate) handler(value);
    }
}
