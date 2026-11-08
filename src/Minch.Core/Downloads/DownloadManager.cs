using System.Collections.Concurrent;
using Minch.Core.Persistence;
using Minch.Core.Services;

namespace Minch.Core.Downloads;

public enum DownloadState
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record DownloadUpdate(string FileId, DownloadState State, DownloadProgress Progress, string? Path = null, string? Error = null);

/// <summary>
/// Downloads TorBox files to disk: tries the range accelerator first, falls
/// back to a single stream. Writes to &lt;root&gt;/.minch-downloads/&lt;fileId&gt;
/// and moves into &lt;root&gt;/&lt;transfer&gt;/&lt;file&gt; on success.
/// </summary>
public sealed class DownloadManager
{
    private readonly HttpClient _http;
    private readonly TransferStore _store;
    private readonly INotifier _notifier;
    private readonly RangeDownloadAccelerator _accelerator;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new(StringComparer.Ordinal);
    private volatile string _root;

    public DownloadManager(HttpClient http, TransferStore store, INotifier notifier, string? root = null, RangeDownloadOptions? options = null)
    {
        _http = http;
        _store = store;
        _notifier = notifier;
        _accelerator = new RangeDownloadAccelerator(http, options);
        _root = root ?? DefaultRoot;
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Minch");

    /// <summary>Raised from a thread-pool thread.</summary>
    public event EventHandler<DownloadUpdate>? Updated;

    public string Root
    {
        get => _root;
        set => _root = string.IsNullOrWhiteSpace(value) ? DefaultRoot : value;
    }

    public bool IsDownloading(string fileId) => _inflight.ContainsKey(fileId);

    public IReadOnlyCollection<string> ActiveFileIds => _inflight.Keys.ToList();

    public string DestinationFor(string transferName, string fileName) =>
        Path.Combine(_root, FileNameSanitizer.Sanitize(transferName), FileNameSanitizer.Sanitize(fileName));

    /// <summary>Starts a download; returns false if this file is already downloading.</summary>
    public bool Start(Uri url, string fileId, string transferName, string fileName)
    {
        var cts = new CancellationTokenSource();
        if (!_inflight.TryAdd(fileId, cts))
        {
            cts.Dispose();
            return false;
        }
        var root = _root;
        var destination = DestinationFor(transferName, fileName);
        var tempDir = Path.Combine(root, ".minch-downloads", FileNameSanitizer.Sanitize(fileId.Replace(':', '_')));
        _ = Task.Run(() => RunAsync(url, fileId, fileName, destination, tempDir, cts));
        return true;
    }

    public void Cancel(string fileId)
    {
        if (_inflight.TryGetValue(fileId, out var cts)) cts.Cancel();
    }

    public void CancelAll()
    {
        foreach (var cts in _inflight.Values) cts.Cancel();
    }

    private async Task RunAsync(Uri url, string fileId, string displayName, string destination, string tempDir, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var lastReport = DateTimeOffset.MinValue;
        DownloadProgress last = default;
        var progress = new InlineProgress<DownloadProgress>(p =>
        {
            last = p;
            var now = DateTimeOffset.UtcNow;
            if (now - lastReport < TimeSpan.FromMilliseconds(250) && p.BytesDone < p.BytesTotal) return;
            lastReport = now;
            Updated?.Invoke(this, new DownloadUpdate(fileId, DownloadState.Running, p));
        });
        var partial = Path.Combine(tempDir, "partial");

        try
        {
            Directory.CreateDirectory(tempDir);
            Updated?.Invoke(this, new DownloadUpdate(fileId, DownloadState.Running, default));

            bool accelerated;
            try
            {
                accelerated = await _accelerator.DownloadAsync(url, destination, partial, progress, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                accelerated = false; // fall back to a single stream
            }

            if (!accelerated)
                await SingleStreamAsync(url, destination, partial, progress, ct);

            _store.MarkDownloaded(fileId, destination);
            var size = new FileInfo(destination).Length;
            Updated?.Invoke(this, new DownloadUpdate(fileId, DownloadState.Completed, new DownloadProgress(size, size, 0), destination));
            _notifier.DownloadCompleted(displayName, destination);
        }
        catch (OperationCanceledException)
        {
            Updated?.Invoke(this, new DownloadUpdate(fileId, DownloadState.Cancelled, last));
        }
        catch (Exception ex)
        {
            Updated?.Invoke(this, new DownloadUpdate(fileId, DownloadState.Failed, last, Error: ex.Message));
            _notifier.DownloadFailed(displayName, ex.Message);
        }
        finally
        {
            _inflight.TryRemove(fileId, out _);
            cts.Dispose();
            TryDeleteDirectory(tempDir);
            TryDeleteIfEmpty(Path.GetDirectoryName(tempDir)!);
        }
    }

    private async Task SingleStreamAsync(Uri url, string destination, string partial, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        var started = DateTimeOffset.UtcNow;
        await using (var input = await resp.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        {
            var buffer = new byte[256 * 1024];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
                progress.Report(new DownloadProgress(done, Math.Max(total, done), elapsed > 0.25 ? (long)(done / elapsed) : 0));
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(partial, destination, overwrite: true);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteIfEmpty(string dir)
    {
        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
