using System.Collections.Concurrent;
using Minch.Core.Api;
using Minch.Core.Models;
using Minch.Core.Persistence;

namespace Minch.Core.Sync;

public delegate Task<IReadOnlyList<Transfer>> TransferFetcher(bool bypassCache, CancellationToken ct);

/// <summary>
/// Pulls torrents + web downloads from TorBox and merges them into the store.
/// Both lists are fetched concurrently; if either fails the merge is aborted
/// (a partial list would delete every row of the missing kind).
/// </summary>
public sealed class SyncEngine
{
    public static readonly TimeSpan DeleteGrace = TimeSpan.FromSeconds(15);

    private readonly TransferStore _store;
    private readonly TransferFetcher[] _fetchers;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentlyDeleted = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncEngine(TransferStore store, TimeProvider? time = null, params TransferFetcher[] fetchers)
    {
        _store = store;
        _fetchers = fetchers;
        _time = time ?? TimeProvider.System;
    }

    public static SyncEngine ForClient(TorBoxClient client, TransferStore store, TimeProvider? time = null) =>
        new(store, time,
            (b, ct) => client.ListTransfersAsync(b, ct),
            (b, ct) => client.ListWebDownloadsAsync(b, ct));

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>
    /// TorBox's list endpoints are cached for a few seconds, so a just-deleted
    /// transfer can reappear on the next poll. Hide it for <see cref="DeleteGrace"/>.
    /// </summary>
    public void NoteDeleted(string transferId) => _recentlyDeleted[transferId] = _time.GetUtcNow();

    /// <summary>Returns true when the store changed.</summary>
    public async Task<bool> RefreshAsync(bool bypassCache = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var results = await Task.WhenAll(_fetchers.Select(f => f(bypassCache, ct)));
            var now = _time.GetUtcNow();
            foreach (var (id, at) in _recentlyDeleted)
                if (now - at > DeleteGrace) _recentlyDeleted.TryRemove(id, out _);

            var merged = results
                .SelectMany(r => r)
                .Where(t => !_recentlyDeleted.ContainsKey(t.Id))
                .ToList();
            var changed = _store.ReplaceRemote(merged);
            LastSyncedAt = now;
            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }
}
