using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Minch.Core.Services;
using Minch.Core.Settings;

namespace Minch.App.Services;

/// <summary>Windows toasts via the App SDK notification manager (works unpackaged).</summary>
public sealed class ToastNotifier : INotifier, IDisposable
{
    public const string ActionArg = "action";
    public const string PathArg = "path";

    private readonly ISettingsStore _settings;
    private bool _registered;

    public ToastNotifier(ISettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>Raised when the user clicks a toast or one of its buttons.</summary>
    public event EventHandler<IDictionary<string, string>>? Invoked;

    public void Register()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, e) => Invoked?.Invoke(this, e.Arguments);
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception)
        {
            // Notifications are best-effort (e.g. disabled by policy).
        }
    }

    public void DownloadCompleted(string displayName, string path)
    {
        if (!_settings.Current.NotifyOnDownloadComplete) return;
        Show(new AppNotificationBuilder()
            .AddArgument(ActionArg, "open")
            .AddArgument(PathArg, path)
            .AddText("Download complete")
            .AddText(displayName)
            .AddButton(new AppNotificationButton("Open").AddArgument(ActionArg, "open").AddArgument(PathArg, path))
            .AddButton(new AppNotificationButton("Show in folder").AddArgument(ActionArg, "reveal").AddArgument(PathArg, path)));
    }

    public void DownloadFailed(string displayName, string reason)
    {
        if (!_settings.Current.NotifyOnDownloadComplete) return;
        Show(new AppNotificationBuilder()
            .AddArgument(ActionArg, "show")
            .AddText("Download failed")
            .AddText(displayName)
            .AddText(reason));
    }

    public void TransferFinished(string name)
    {
        if (!_settings.Current.NotifyOnTransferFinished) return;
        Show(new AppNotificationBuilder()
            .AddArgument(ActionArg, "show")
            .AddText("Ready on TorBox")
            .AddText(name));
    }

    private void Show(AppNotificationBuilder builder)
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Show(builder.BuildNotification()); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
        _registered = false;
    }
}
