using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace Minch.App;

/// <summary>
/// Custom entry point so a second launch (e.g. clicking a magnet: link while
/// Minch is running) is redirected to the existing instance instead of opening
/// another window.
/// </summary>
public static class Program
{
    private const string InstanceKey = "Minch.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        if (RedirectIfAlreadyRunning())
            return 0;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool RedirectIfAlreadyRunning()
    {
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            main.Activated += (_, e) => App.Instance?.OnRedirectedActivation(e);
            return false;
        }

        // Redirect synchronously: RedirectActivationToAsync must complete before we exit,
        // and blocking on it from the STA thread can deadlock, so run it on the pool.
        var done = new ManualResetEventSlim();
        Task.Run(async () =>
        {
            try { await main.RedirectActivationToAsync(args); }
            finally { done.Set(); }
        });
        done.Wait(TimeSpan.FromSeconds(5));
        return true;
    }
}
