using System.Net;
using System.Net.Http.Headers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Minch.App.Services;
using Minch.Core.Settings;
using Minch.ViewModels;
using Windows.ApplicationModel.Activation;

namespace Minch.App;

public partial class App : Application
{
    private MainWindow? _window;
    private TrayIconService? _tray;
    private ToastNotifier? _toasts;
    private string? _pendingExternal;

    public App()
    {
        Instance = this;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keep the app alive on stray UI exceptions; surface them as a banner instead.
            e.Handled = true;
            Session?.ShowError(e.Message);
        };
    }

    public static App? Instance { get; private set; }
    public static SessionViewModel Session { get; private set; } = null!;
    public static ISettingsStore Settings { get; private set; } = null!;
    public static MainWindow? MainWindow => Instance?._window;
    public static Version Version { get; } = typeof(App).Assembly.GetName().Version ?? new Version(0, 1, 0);

    /// <summary>True once the user picked Quit (vs. close-to-tray).</summary>
    public bool IsQuitting { get; private set; }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Settings = new JsonSettingsStore();
        _toasts = new ToastNotifier(Settings);
        _toasts.Register();
        _toasts.Invoked += (_, a) => dispatcher.TryEnqueue(() => OnToastInvoked(a));

        var services = new AppServices
        {
            Secrets = new CredentialLockerSecretStore(),
            Settings = Settings,
            Dispatcher = new DispatcherQueueUiDispatcher(dispatcher),
            Notifier = _toasts,
            Clipboard = new ClipboardService(),
            Shell = new ShellService(),
            Players = new ExternalPlayerLauncher(),
            Protocol = new MagnetProtocolRegistrar(),
            Http = CreateHttp(TimeSpan.FromSeconds(30)),
            DownloadHttp = CreateHttp(Timeout.InfiniteTimeSpan),
            AppVersion = Version,
        };
        Session = new SessionViewModel(services);
        Session.SignedIn += (_, _) => FlushPendingExternal();

        _window = new MainWindow(Session);
        _tray = new TrayIconService(this, Session);
        _tray.Create();

        _pendingExternal = ExtractExternal(Environment.GetCommandLineArgs().Skip(1));
        var startHidden = Settings.Current.StartMinimized && Settings.Current.CloseToTray && _pendingExternal is null;
        if (startHidden) _window.HideToTray();
        else _window.Activate();

        _ = Session.BootstrapAsync();
        _ = UpdateCheck.RunAsync(services.Http, Settings, Version);
    }

    private static HttpClient CreateHttp(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
        };
        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Minch-Windows", Version.ToString(3)));
        return http;
    }

    /// <summary>Second launch redirected to us (magnet click, Start menu, etc.). Called off the UI thread.</summary>
    internal void OnRedirectedActivation(AppActivationArguments e)
    {
        string? external = e.Kind switch
        {
            ExtendedActivationKind.Protocol when e.Data is IProtocolActivatedEventArgs p => p.Uri?.ToString(),
            ExtendedActivationKind.Launch when e.Data is ILaunchActivatedEventArgs l => ExtractExternal(SplitArgs(l.Arguments)),
            _ => null,
        };
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            _window.ShowAndActivate();
            if (external is not null)
            {
                _pendingExternal = external;
                FlushPendingExternal();
            }
        });
    }

    private void FlushPendingExternal()
    {
        if (_pendingExternal is not { } raw || !Session.IsSignedIn) return;
        _pendingExternal = null;
        if (raw.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(raw))
        {
            _window?.ShowLibrary();
            _ = Session.Library.LoadTorrentFileAsync(raw);
            return;
        }
        _window?.ShowLibrary();
        _ = Session.Library.IngestExternalAsync(raw);
    }

    internal static string? ExtractExternal(IEnumerable<string> args)
    {
        foreach (var raw in args)
        {
            var a = raw.Trim().Trim('"');
            if (a.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith("minch:", StringComparison.OrdinalIgnoreCase) ||
                (a.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(a)))
                return a;
        }
        return null;
    }

    private static IEnumerable<string> SplitArgs(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) yield break;
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ' ' && !quoted)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private void OnToastInvoked(IDictionary<string, string> args)
    {
        args.TryGetValue(ToastNotifier.PathArg, out var path);
        args.TryGetValue(ToastNotifier.ActionArg, out var action);
        var shell = Session.Services.Shell;
        switch (action)
        {
            case "open" when path is not null:
                shell?.OpenFile(path);
                break;
            case "reveal" when path is not null:
                shell?.RevealInExplorer(path);
                break;
            default:
                _window?.ShowAndActivate();
                break;
        }
    }

    public async void Quit()
    {
        IsQuitting = true;
        _window?.SavePlacement();
        _tray?.Dispose();
        _toasts?.Dispose();
        try { await Session.DisposeAsync(); } catch { }
        _window?.Close();
        Exit();
    }
}
