namespace Minch.Core.Services;

/// <summary>Secure storage for the TorBox API key (Credential Locker on Windows).</summary>
public interface ISecretStore
{
    string? Read(string key);
    void Write(string key, string value);
    void Delete(string key);
}

public static class SecretKeys
{
    public const string TorBoxApiKey = "torbox.api_key";
}

/// <summary>OS notifications (toasts).</summary>
public interface INotifier
{
    void DownloadCompleted(string displayName, string path);
    void DownloadFailed(string displayName, string reason);
    void TransferFinished(string name);
}

public interface IClipboardService
{
    void SetText(string text);
    Task<string?> GetTextAsync();
}

public interface IShellService
{
    /// <summary>Opens Explorer with the file selected.</summary>
    void RevealInExplorer(string path);
    /// <summary>Opens a file with its default handler.</summary>
    void OpenFile(string path);
    void OpenFolder(string path);
    void OpenUrl(Uri url);
}

/// <summary>Runs work on the UI thread.</summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    void Post(Action action);
}

/// <summary>Dispatcher that runs inline — for tests and non-UI hosts.</summary>
public sealed class InlineDispatcher : IUiDispatcher
{
    public static readonly InlineDispatcher Instance = new();
    public bool HasThreadAccess => true;
    public void Post(Action action) => action();
}

public sealed class NullNotifier : INotifier
{
    public static readonly NullNotifier Instance = new();
    public void DownloadCompleted(string displayName, string path) { }
    public void DownloadFailed(string displayName, string reason) { }
    public void TransferFinished(string name) { }
}

/// <summary>Registers Minch as the per-user handler for magnet: links.</summary>
public interface IProtocolRegistrar
{
    bool IsRegistered();
    void Register();
    void Unregister();
}
