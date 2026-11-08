using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Downloads;
using Minch.Core.Models;
using Minch.Core.Persistence;
using Minch.Core.Services;
using Minch.Core.Sync;

namespace Minch.ViewModels;

public enum AuthState
{
    Unknown,
    SignedOut,
    Validating,
    SignedIn,
}

/// <summary>
/// Root view model — owns the TorBox client, local store, sync loop and
/// downloads. Port of AppModel's session half; the library half lives in
/// <see cref="LibraryViewModel"/>.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly MutableApiKeyProvider _keys = new();
    private Task? _bannerClear;

    public SessionViewModel(AppServices services)
    {
        Services = services;
        Store = new TransferStore(services.DatabasePath);
        Client = new TorBoxClient(services.Http, _keys, services.ApiBaseUrl);
        Sync = SyncEngine.ForClient(Client, Store);
        Polling = new PollingScheduler(Sync, () => Store.All().Any(t => t.Status != TransferStatus.Done));
        Downloads = new DownloadManager(services.DownloadHttp ?? services.Http, Store, services.Notifier, services.Settings.Current.DownloadFolder);

        Library = new LibraryViewModel(this);
        Account = new AccountViewModel(this);
        Settings = new SettingsViewModel(this);
        Palette = new CommandPaletteViewModel(this);

        services.Settings.Changed += (_, s) => Downloads.Root = s.DownloadFolder ?? DownloadManager.DefaultRoot;
    }

    public AppServices Services { get; }
    public TorBoxClient Client { get; }
    public TransferStore Store { get; }
    public SyncEngine Sync { get; }
    public PollingScheduler Polling { get; }
    public DownloadManager Downloads { get; }

    public LibraryViewModel Library { get; }
    public AccountViewModel Account { get; }
    public SettingsViewModel Settings { get; }
    public CommandPaletteViewModel Palette { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(IsSignedOut), nameof(IsBusy))]
    private AuthState _state = AuthState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanName), nameof(DisplayName))]
    private UserAccount? _user;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _pendingKey = "";

    [ObservableProperty]
    private string? _signInMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    private string? _infoMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isRefreshing;

    public bool IsSignedIn => State == AuthState.SignedIn;
    public bool IsSignedOut => State == AuthState.SignedOut;
    public bool IsBusy => State is AuthState.Unknown or AuthState.Validating;
    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string PlanName => User?.PlanName ?? "";
    public string DisplayName => User?.Email ?? "TorBox account";

    /// <summary>Raised when the UI should open the in-app player.</summary>
    public event EventHandler<PlayerViewModel>? PlayerRequested;

    /// <summary>Raised when a sign-in completes (used by the shell to navigate).</summary>
    public event EventHandler? SignedIn;

    internal void RequestPlayer(PlayerViewModel vm) => PlayerRequested?.Invoke(this, vm);

    // ------------------------------------------------------------------ auth

    public async Task BootstrapAsync()
    {
        if (State != AuthState.Unknown) return;
        State = AuthState.Validating;
        string? key;
        try { key = Services.Secrets.Read(SecretKeys.TorBoxApiKey); }
        catch { key = null; }
        if (string.IsNullOrWhiteSpace(key))
        {
            State = AuthState.SignedOut;
            return;
        }
        try
        {
            await ValidateAsync(key, persist: false);
        }
        catch (ApiException ex) when (ex.Kind is ApiErrorKind.Network or ApiErrorKind.Transient)
        {
            // Offline at launch: keep the cached library visible and let polling recover.
            _keys.Key = key;
            Activate(null);
            ShowError(ex.FriendlyMessage);
        }
        catch (Exception)
        {
            State = AuthState.SignedOut;
        }
    }

    private bool CanConnect() => !string.IsNullOrWhiteSpace(PendingKey);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var key = PendingKey.Trim();
        if (key.Length == 0)
        {
            SignInMessage = "Enter your TorBox API key to continue.";
            State = AuthState.SignedOut;
            return;
        }
        try
        {
            await ValidateAsync(key, persist: true);
            PendingKey = "";
            SignInMessage = null;
        }
        catch (Exception ex)
        {
            SignInMessage = ApiException.Friendly(ex, "Couldn't reach TorBox. Check your connection and try again.");
            State = AuthState.SignedOut;
        }
    }

    [RelayCommand]
    public async Task SignOutAsync()
    {
        await Polling.StopAsync();
        Downloads.CancelAll();
        try { Services.Secrets.Delete(SecretKeys.TorBoxApiKey); } catch { }
        _keys.Key = null;
        Store.Clear();
        User = null;
        Account.Reset();
        Settings.ResetRemote();
        State = AuthState.SignedOut;
    }

    /// <summary>
    /// Validates <paramref name="newKey"/> first; only on success is it stored and swapped in.
    /// The local library is kept.
    /// </summary>
    public async Task ReplaceApiKeyAsync(string newKey)
    {
        var trimmed = newKey.Trim();
        if (trimmed.Length == 0) throw new ApiException(ApiErrorKind.Validation, "API key cannot be empty.");
        var candidate = new TorBoxClient(Services.Http, new StaticApiKeyProvider(trimmed), Services.ApiBaseUrl);
        var account = await candidate.MeAsync();
        Services.Secrets.Write(SecretKeys.TorBoxApiKey, trimmed);
        await Polling.StopAsync();
        _keys.Key = trimmed;
        Activate(account);
    }

    public string? CurrentApiKeyLast4()
    {
        try
        {
            var key = Services.Secrets.Read(SecretKeys.TorBoxApiKey);
            return key is { Length: >= 4 } ? key[^4..] : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task ValidateAsync(string key, bool persist)
    {
        State = AuthState.Validating;
        var candidate = new TorBoxClient(Services.Http, new StaticApiKeyProvider(key), Services.ApiBaseUrl);
        var account = await candidate.MeAsync();
        if (persist) Services.Secrets.Write(SecretKeys.TorBoxApiKey, key);
        _keys.Key = key;
        Activate(account);
    }

    private void Activate(UserAccount? account)
    {
        if (account is not null) User = account;
        State = AuthState.SignedIn;
        Library.Rebuild();
        Polling.Start();
        SignedIn?.Invoke(this, EventArgs.Empty);
        _ = Library.LoadHostersAsync();
        if (account is null) _ = RetryAccountAsync();
    }

    private async Task RetryAccountAsync()
    {
        try { User = await Client.MeAsync(); }
        catch { /* stays "TorBox account" until next launch */ }
    }

    // --------------------------------------------------------------- refresh

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!IsSignedIn) return;
        IsRefreshing = true;
        try
        {
            await Sync.RefreshAsync(bypassCache: true);
        }
        catch (Exception ex)
        {
            ShowError(ApiException.Friendly(ex, "Couldn't refresh transfers."));
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Refresh without surfacing errors (after a mutation).</summary>
    internal async Task QuietRefreshAsync()
    {
        try { await Sync.RefreshAsync(bypassCache: true); }
        catch { Polling.Nudge(); }
    }

    // --------------------------------------------------------------- banners

    public void ShowInfo(string message)
    {
        ErrorMessage = null;
        InfoMessage = message;
        var current = message;
        _bannerClear = ClearInfoLaterAsync(current);
    }

    public void ShowError(string message)
    {
        InfoMessage = null;
        ErrorMessage = message;
    }

    [RelayCommand]
    public void DismissBanners()
    {
        InfoMessage = null;
        ErrorMessage = null;
    }

    private async Task ClearInfoLaterAsync(string message)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        Services.Dispatcher.Post(() =>
        {
            if (InfoMessage == message) InfoMessage = null;
        });
    }

    internal void OnUi(Action action)
    {
        if (Services.Dispatcher.HasThreadAccess) action();
        else Services.Dispatcher.Post(action);
    }

    public async ValueTask DisposeAsync()
    {
        await Polling.DisposeAsync();
        Downloads.CancelAll();
        Store.Dispose();
    }
}
