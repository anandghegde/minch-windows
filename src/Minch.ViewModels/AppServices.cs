using Minch.Core.Services;
using Minch.Core.Settings;

namespace Minch.ViewModels;

/// <summary>Platform services the view models need. The WinUI app supplies real implementations.</summary>
public sealed record AppServices
{
    public required ISecretStore Secrets { get; init; }
    public required ISettingsStore Settings { get; init; }
    public required IUiDispatcher Dispatcher { get; init; }
    public INotifier Notifier { get; init; } = NullNotifier.Instance;
    public IClipboardService? Clipboard { get; init; }
    public IShellService? Shell { get; init; }
    public IExternalPlayerLauncher Players { get; init; } = NoExternalPlayers.Instance;
    public IProtocolRegistrar? Protocol { get; init; }
    public required HttpClient Http { get; init; }
    /// <summary>Separate client for file downloads (no timeout).</summary>
    public HttpClient? DownloadHttp { get; init; }
    public string DatabasePath { get; init; } = Minch.Core.Persistence.TransferStore.DefaultPath;
    public Uri? ApiBaseUrl { get; init; }
    public Version AppVersion { get; init; } = new(0, 1, 0);
}
