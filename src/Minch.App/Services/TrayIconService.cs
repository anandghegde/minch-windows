using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Minch.ViewModels;

namespace Minch.App.Services;

/// <summary>Notification-area icon: left click shows the window; right click opens a native menu.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly App _app;
    private readonly SessionViewModel _session;
    private TaskbarIcon? _icon;

    public TrayIconService(App app, SessionViewModel session)
    {
        _app = app;
        _session = session;
    }

    public void Create()
    {
        var show = new RelayCommand(() => App.MainWindow?.ShowAndActivate());
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open Minch", Command = show });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Add from clipboard",
            Command = new AsyncRelayCommand(AddFromClipboardAsync),
        });
        menu.Items.Add(new MenuFlyoutItem { Text = "Refresh", Command = new AsyncRelayCommand(() => _session.RefreshAsync()) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Quit Minch", Command = new RelayCommand(_app.Quit) });

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "minch.ico");
        _icon = new TaskbarIcon
        {
            ToolTipText = "Minch",
            ContextFlyout = menu,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            LeftClickCommand = show,
            NoLeftClickDelay = true,
        };
        if (File.Exists(iconPath)) _icon.Icon = new System.Drawing.Icon(iconPath);
        _icon.ForceCreate();
    }

    private async Task AddFromClipboardAsync()
    {
        var text = await (_session.Services.Clipboard?.GetTextAsync() ?? Task.FromResult<string?>(null));
        App.MainWindow?.ShowAndActivate();
        App.MainWindow?.ShowLibrary();
        if (string.IsNullOrWhiteSpace(text)) return;
        _session.Library.AddText = text.Trim();
        if (_session.Library.AddKind is Minch.Core.Input.AddInputKind.Magnet or Minch.Core.Input.AddInputKind.WebLink)
            await _session.Library.AddAsync();
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
