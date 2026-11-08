using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Minch.App.Views;
using Minch.Core.Settings;
using Minch.Core.Updates;
using Minch.ViewModels;
using Windows.Graphics;

namespace Minch.App;

public sealed partial class MainWindow : Window
{
    private const int MinWidth = 720;
    private const int MinHeight = 520;

    private readonly SessionViewModel _session;
    private readonly List<PlayerWindow> _players = [];

    public MainWindow(SessionViewModel session)
    {
        _session = session;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "minch.ico"));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }
        RestorePlacement();

        AppWindow.Closing += OnClosing;
        Root.Loaded += (_, _) => ApplyTheme(App.Settings.Current.Theme);
        App.Settings.Changed += (_, s) => DispatcherQueue.TryEnqueue(() => ApplyTheme(s.Theme));

        Onboarding.Initialize(session);
        Shell.Initialize(session, this);

        session.PropertyChanged += OnSessionChanged;
        session.PlayerRequested += (_, vm) => OpenPlayer(vm);
        session.Palette.FocusAddRequested += (_, _) => Shell.FocusAddBox();
        session.Palette.NavigateRequested += (_, page) => Shell.Navigate(page);
        UpdateAuthState();
    }

    // ------------------------------------------------------------ auth/state

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.State)) UpdateAuthState();
    }

    private bool _onboardingShown;

    private void UpdateAuthState()
    {
        var state = _session.State;
        var signedIn = state == AuthState.SignedIn;
        if (state == AuthState.SignedOut) _onboardingShown = true;
        if (signedIn) _onboardingShown = false;

        // While validating a stored key at launch, keep the spinner; once the user
        // has seen onboarding, keep it on screen (it shows its own progress).
        var showOnboarding = !signedIn && _onboardingShown;
        var booting = !signedIn && !showOnboarding;
        BootRing.IsActive = booting;
        BootRing.Visibility = booting ? Visibility.Visible : Visibility.Collapsed;
        Shell.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        Onboarding.Visibility = showOnboarding ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        PaletteButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        AppTitleBar.IsPaneToggleButtonVisible = signedIn && Shell.IsPaneToggleNeeded;
        if (!signedIn) SearchBox.Text = "";
    }

    internal void SetPaneToggleVisible(bool visible) =>
        AppTitleBar.IsPaneToggleButtonVisible = visible && _session.IsSignedIn;

    private void OnPaneToggleRequested(TitleBar sender, object args) => Shell.TogglePane();

    // ----------------------------------------------------------------- search

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        Shell.ShowLibraryIfNeeded();
        _session.Library.SearchText = sender.Text;
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        Shell.ShowLibraryIfNeeded();
        _session.Library.SearchText = args.QueryText;
    }

    private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_session.IsSignedIn) return;
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    // ------------------------------------------------------------ accelerators

    private void OnPaletteClick(object sender, RoutedEventArgs e) => _ = ShowPaletteAsync();

    private void OnPaletteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowPaletteAsync();
    }

    private bool _paletteOpen;

    private async Task ShowPaletteAsync()
    {
        if (!_session.IsSignedIn || _paletteOpen) return;
        _paletteOpen = true;
        try
        {
            var dialog = new CommandPaletteDialog(_session.Palette) { XamlRoot = Root.XamlRoot };
            await dialog.ShowAsync();
            if (dialog.Chosen is { } item) await _session.Palette.RunAsync(item);
        }
        finally
        {
            _paletteOpen = false;
        }
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _session.RefreshAsync();
    }

    private void OnAddAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_session.IsSignedIn) return;
        args.Handled = true;
        Shell.FocusAddBox();
    }

    private void OnOpenTorrentAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_session.IsSignedIn) return;
        args.Handled = true;
        _ = Shell.PickTorrentFileAsync();
    }

    // ---------------------------------------------------------------- public

    public void ShowLibrary() => Shell.Navigate("library");

    public void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
    }

    public void HideToTray()
    {
        SavePlacement();
        AppWindow.Hide();
    }

    public void ShowUpdate(UpdateInfo info) => Shell.ShowUpdate(info);

    private void OpenPlayer(PlayerViewModel vm)
    {
        var window = new PlayerWindow(vm);
        _players.Add(window);
        window.Closed += (_, _) => _players.Remove(window);
        window.Activate();
    }

    // ------------------------------------------------------------- lifecycle

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.Instance is { IsQuitting: false } app && App.Settings.Current.CloseToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }
        SavePlacement();
        foreach (var p in _players.ToList()) p.Close();
        if (App.Instance is { IsQuitting: false } a) a.Quit();
    }

    public void SavePlacement()
    {
        if (AppWindow.Presenter is not OverlappedPresenter p) return;
        var maximized = p.State == OverlappedPresenterState.Maximized;
        if (p.State == OverlappedPresenterState.Minimized) return;
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        App.Settings.Update(s => maximized
            ? s with { WindowMaximized = true }
            : s with { WindowMaximized = false, WindowX = pos.X, WindowY = pos.Y, WindowWidth = size.Width, WindowHeight = size.Height });
    }

    private void RestorePlacement()
    {
        var s = App.Settings.Current;
        if (s is { WindowX: { } x, WindowY: { } y, WindowWidth: { } w, WindowHeight: { } h } && w >= MinWidth && h >= MinHeight)
        {
            var rect = new RectInt32(x, y, w, h);
            // Only restore if the window would still land on a connected display.
            var area = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
            if (area is not null) AppWindow.MoveAndResize(rect);
            else AppWindow.Resize(new SizeInt32(w, h));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1180, 780));
            CenterOnScreen();
        }
        if (s.WindowMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2));
    }

    private void ApplyTheme(ThemePreference theme)
    {
        if (Content is not FrameworkElement root) return;
        root.RequestedTheme = theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        var dark = root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        foreach (var p in _players) p.ApplyTheme(root.RequestedTheme);
    }
}
