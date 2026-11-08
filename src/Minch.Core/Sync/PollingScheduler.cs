using Minch.Core.Api;

namespace Minch.Core.Sync;

/// <summary>
/// Background refresh loop: 4 s while anything is in flight, 15 s when idle,
/// 60 s back-off after a quota error. Polling errors are swallowed — only a
/// manual refresh surfaces them to the user.
/// </summary>
public sealed class PollingScheduler : IAsyncDisposable
{
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan QuotaBackoff = TimeSpan.FromSeconds(60);

    private readonly SyncEngine _sync;
    private readonly Func<bool> _hasActive;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly SemaphoreSlim _wake = new(0, 1);

    public PollingScheduler(SyncEngine sync, Func<bool> hasActive)
    {
        _sync = sync;
        _hasActive = hasActive;
    }

    public event EventHandler<Exception>? PollFailed;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Skip the current wait and poll now (e.g. right after adding a magnet).</summary>
    public void Nudge()
    {
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); } catch (SemaphoreFullException) { }
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        try { if (_loop is not null) await _loop; }
        catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var active = _hasActive();
            var delay = active ? ActiveInterval : IdleInterval;
            try
            {
                await _sync.RefreshAsync(bypassCache: active, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (ApiException ex) when (ex.Kind == ApiErrorKind.Quota)
            {
                delay = QuotaBackoff;
                PollFailed?.Invoke(this, ex);
            }
            catch (Exception ex)
            {
                PollFailed?.Invoke(this, ex);
            }

            // Recompute after the refresh: a completed sync may have changed activity.
            if (delay != QuotaBackoff) delay = _hasActive() ? ActiveInterval : IdleInterval;
            try
            {
                await _wake.WaitAsync(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
