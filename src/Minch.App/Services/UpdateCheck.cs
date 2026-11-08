using Minch.Core.Settings;
using Minch.Core.Updates;

namespace Minch.App.Services;

/// <summary>Once-per-launch GitHub release check; surfaces an InfoBar via the session banner.</summary>
internal static class UpdateCheck
{
    public static async Task RunAsync(HttpClient http, ISettingsStore settings, Version current)
    {
        if (!settings.Current.CheckForUpdates) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var info = await new UpdateChecker(http).CheckAsync(current);
            if (info is null || info.Tag == settings.Current.SkippedUpdateVersion) return;
            App.MainWindow?.DispatcherQueue.TryEnqueue(() => App.MainWindow?.ShowUpdate(info));
        }
        catch
        {
            // offline or rate-limited; try again next launch
        }
    }
}
