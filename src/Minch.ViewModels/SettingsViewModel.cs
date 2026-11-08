using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Downloads;
using Minch.Core.Models;
using Minch.Core.Services;
using Minch.Core.Settings;

namespace Minch.ViewModels;

public sealed record SeedChoice(int Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record PlayerChoice(string? Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Settings page: TorBox account preferences (diffed + saved remotely),
/// streaming preferences, and local app preferences (saved immediately).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string SeedKey = "seed_torrents";
    public const string ZippedKey = "allow_zipped";
    public const string SpeedInTabKey = "download_speed_in_tab";
    public const string TrackerKey = "show_tracker_in_torrents";

    public static readonly IReadOnlyList<SeedChoice> SeedChoices =
    [
        new(1, "Auto"),
        new(2, "Always"),
        new(3, "Never"),
    ];

    public static readonly IReadOnlyList<ThemePreference> Themes = [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];

    private readonly SessionViewModel _session;
    private IReadOnlyDictionary<string, SettingValue> _original = new Dictionary<string, SettingValue>();
    private bool _loadingLocal;

    internal SettingsViewModel(SessionViewModel session)
    {
        _session = session;
        LoadLocal(session.Services.Settings.Current);
        session.Services.Settings.Changed += (_, s) => session.OnUi(() => LoadLocal(s));
    }

    // ------------------------------------------------------------ remote

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemote))]
    private bool _remoteLoaded;

    [ObservableProperty]
    private bool _isLoadingRemote;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRemoteCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoteError))]
    private string? _remoteError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveRemoteCommand))]
    private SeedChoice? _seedTorrents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveRemoteCommand))]
    private bool _allowZipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveRemoteCommand))]
    private bool _downloadSpeedInTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveRemoteCommand))]
    private bool _showTrackerInTorrents;

    public bool HasRemote => RemoteLoaded;
    public bool HasRemoteError => !string.IsNullOrEmpty(RemoteError);
    public bool IsDirty => RemoteLoaded && Diff().Count > 0;

    public static string SeedTooltip => "Whether finished torrents keep seeding.";
    public static string ZippedTooltip => "Bundle multi-file downloads into a single .zip.";
    public static string SpeedInTabTooltip => "Show download speed in the browser title.";
    public static string TrackerTooltip => "Display tracker URLs alongside details.";

    [RelayCommand]
    public async Task LoadRemoteAsync()
    {
        if (!_session.IsSignedIn || IsLoadingRemote) return;
        IsLoadingRemote = true;
        RemoteError = null;
        try
        {
            var values = await _session.Client.LoadUserSettingsAsync();
            _session.OnUi(() => ApplyRemote(values));
        }
        catch (Exception ex)
        {
            RemoteError = ApiException.Friendly(ex, "Couldn't load your TorBox settings.");
        }
        finally
        {
            IsLoadingRemote = false;
        }
    }

    internal void ApplyRemote(IReadOnlyDictionary<string, SettingValue> values)
    {
        _original = values;
        var seed = values.TryGetValue(SeedKey, out var s) && s.Kind == SettingKind.Number ? (int)s.Number : 1;
        SeedTorrents = SeedChoices.FirstOrDefault(c => c.Value == seed) ?? SeedChoices[0];
        AllowZipped = BoolOf(values, ZippedKey);
        DownloadSpeedInTab = BoolOf(values, SpeedInTabKey);
        ShowTrackerInTorrents = BoolOf(values, TrackerKey);
        RemoteLoaded = true;
        OnPropertyChanged(nameof(IsDirty));
        SaveRemoteCommand.NotifyCanExecuteChanged();
    }

    private static bool BoolOf(IReadOnlyDictionary<string, SettingValue> values, string key) =>
        values.TryGetValue(key, out var v) && v.Kind switch
        {
            SettingKind.Bool => v.Bool,
            SettingKind.Number => v.Number != 0,
            SettingKind.String => v.Text is "true" or "1",
            _ => false,
        };

    internal Dictionary<string, SettingValue> Diff()
    {
        var diff = new Dictionary<string, SettingValue>(StringComparer.Ordinal);
        if (!RemoteLoaded) return diff;
        var seed = SeedTorrents?.Value ?? 1;
        var origSeed = _original.TryGetValue(SeedKey, out var s) && s.Kind == SettingKind.Number ? (int)s.Number : 1;
        if (seed != origSeed) diff[SeedKey] = SettingValue.FromNumber(seed);
        void Bool(string key, bool value)
        {
            if (value != BoolOf(_original, key)) diff[key] = SettingValue.FromBool(value);
        }
        Bool(ZippedKey, AllowZipped);
        Bool(SpeedInTabKey, DownloadSpeedInTab);
        Bool(TrackerKey, ShowTrackerInTorrents);
        return diff;
    }

    private bool CanSaveRemote() => !IsSaving && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSaveRemote))]
    private async Task SaveRemoteAsync()
    {
        var diff = Diff();
        if (diff.Count == 0) return;
        IsSaving = true;
        RemoteError = null;
        try
        {
            await _session.Client.UpdateSettingsAsync(diff);
            var merged = new Dictionary<string, SettingValue>(_original, StringComparer.Ordinal);
            foreach (var (k, v) in diff) merged[k] = v;
            ApplyRemote(merged);
            _session.ShowInfo("TorBox settings saved.");
        }
        catch (Exception ex)
        {
            RemoteError = ApiException.Friendly(ex, "Couldn't save your TorBox settings.");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void RevertRemote() => ApplyRemote(_original);

    internal void ResetRemote()
    {
        _original = new Dictionary<string, SettingValue>();
        RemoteLoaded = false;
        RemoteError = null;
        ApiKeyMask = null;
        NewApiKey = "";
        ApiKeyError = null;
    }

    // ------------------------------------------------------------- local

    [ObservableProperty] private string _downloadFolder = DownloadManager.DefaultRoot;
    [ObservableProperty] private ThemePreference _theme;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _notifyOnDownloadComplete;
    [ObservableProperty] private bool _notifyOnTransferFinished;
    [ObservableProperty] private bool _handleMagnetLinks;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _preferExternalPlayer;
    [ObservableProperty] private PlayerChoice? _preferredPlayer;
    [ObservableProperty] private string _preferredSubtitleLanguage = "";
    [ObservableProperty] private string _preferredAudioLanguage = "";
    [ObservableProperty] private bool _scrobblingEnabled;

    [ObservableProperty]
    private IReadOnlyList<PlayerChoice> _playerChoices = [];

    public bool CanHandleMagnetLinks => _session.Services.Protocol is not null;
    public bool HasExternalPlayers => PlayerChoices.Count > 1;
    public string AppVersion => _session.Services.AppVersion.ToString(3);

    private void LoadLocal(AppSettings s)
    {
        _loadingLocal = true;
        try
        {
            DownloadFolder = s.DownloadFolder ?? DownloadManager.DefaultRoot;
            Theme = s.Theme;
            CloseToTray = s.CloseToTray;
            StartMinimized = s.StartMinimized;
            NotifyOnDownloadComplete = s.NotifyOnDownloadComplete;
            NotifyOnTransferFinished = s.NotifyOnTransferFinished;
            HandleMagnetLinks = s.HandleMagnetLinks;
            CheckForUpdates = s.CheckForUpdates;
            PreferExternalPlayer = s.PreferExternalPlayer;
            PreferredSubtitleLanguage = s.PreferredSubtitleLanguage ?? "";
            PreferredAudioLanguage = s.PreferredAudioLanguage ?? "";
            ScrobblingEnabled = s.ScrobblingEnabled;
            RefreshPlayers(s.PreferredExternalPlayer);
        }
        finally
        {
            _loadingLocal = false;
        }
    }

    public void RefreshPlayers(string? preferred = null)
    {
        preferred ??= _session.Services.Settings.Current.PreferredExternalPlayer;
        var choices = new List<PlayerChoice> { new(null, "Ask / first installed") };
        choices.AddRange(_session.Services.Players.Installed().Select(p => new PlayerChoice(p.Id.ToString(), p.DisplayName)));
        var was = _loadingLocal;
        _loadingLocal = true;
        PlayerChoices = choices;
        PreferredPlayer = choices.FirstOrDefault(c => string.Equals(c.Key, preferred, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        _loadingLocal = was;
        OnPropertyChanged(nameof(HasExternalPlayers));
    }

    private void Save(Func<AppSettings, AppSettings> mutate)
    {
        if (_loadingLocal) return;
        _session.Services.Settings.Update(mutate);
    }

    partial void OnDownloadFolderChanged(string value) =>
        Save(s => s with { DownloadFolder = string.Equals(value, DownloadManager.DefaultRoot, StringComparison.OrdinalIgnoreCase) ? null : value });
    partial void OnThemeChanged(ThemePreference value) => Save(s => s with { Theme = value });
    partial void OnCloseToTrayChanged(bool value) => Save(s => s with { CloseToTray = value });
    partial void OnStartMinimizedChanged(bool value) => Save(s => s with { StartMinimized = value });
    partial void OnNotifyOnDownloadCompleteChanged(bool value) => Save(s => s with { NotifyOnDownloadComplete = value });
    partial void OnNotifyOnTransferFinishedChanged(bool value) => Save(s => s with { NotifyOnTransferFinished = value });
    partial void OnCheckForUpdatesChanged(bool value) => Save(s => s with { CheckForUpdates = value });
    partial void OnPreferExternalPlayerChanged(bool value) => Save(s => s with { PreferExternalPlayer = value });
    partial void OnPreferredPlayerChanged(PlayerChoice? value) => Save(s => s with { PreferredExternalPlayer = value?.Key });
    partial void OnPreferredSubtitleLanguageChanged(string value) => Save(s => s with { PreferredSubtitleLanguage = NullIfBlank(value) });
    partial void OnPreferredAudioLanguageChanged(string value) => Save(s => s with { PreferredAudioLanguage = NullIfBlank(value) });
    partial void OnScrobblingEnabledChanged(bool value) => Save(s => s with { ScrobblingEnabled = value });

    partial void OnHandleMagnetLinksChanged(bool value)
    {
        if (_loadingLocal) return;
        var registrar = _session.Services.Protocol;
        try
        {
            if (value) registrar?.Register();
            else registrar?.Unregister();
            Save(s => s with { HandleMagnetLinks = value });
        }
        catch (Exception ex)
        {
            _session.ShowError($"Couldn't update the magnet link handler: {ex.Message}");
            _loadingLocal = true;
            HandleMagnetLinks = !value;
            _loadingLocal = false;
        }
    }

    private static string? NullIfBlank(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    [RelayCommand]
    private void ResetDownloadFolder() => DownloadFolder = DownloadManager.DefaultRoot;

    [RelayCommand]
    private void OpenDownloadFolder()
    {
        try { Directory.CreateDirectory(DownloadFolder); } catch { }
        _session.Services.Shell?.OpenFolder(DownloadFolder);
    }

    // ----------------------------------------------------------- API key

    [ObservableProperty]
    private string? _apiKeyMask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceApiKeyCommand))]
    private string _newApiKey = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasApiKeyError))]
    private string? _apiKeyError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceApiKeyCommand))]
    private bool _isReplacingKey;

    public bool HasApiKeyError => !string.IsNullOrEmpty(ApiKeyError);

    public void RefreshApiKeyMask()
    {
        var last4 = _session.CurrentApiKeyLast4();
        ApiKeyMask = last4 is null ? "Not stored" : $"•••• •••• {last4}";
    }

    private bool CanReplaceKey() => !IsReplacingKey && !string.IsNullOrWhiteSpace(NewApiKey);

    [RelayCommand(CanExecute = nameof(CanReplaceKey))]
    private async Task ReplaceApiKeyAsync()
    {
        IsReplacingKey = true;
        ApiKeyError = null;
        try
        {
            await _session.ReplaceApiKeyAsync(NewApiKey);
            NewApiKey = "";
            RefreshApiKeyMask();
            _session.ShowInfo("API key updated.");
            await LoadRemoteAsync();
        }
        catch (Exception ex)
        {
            ApiKeyError = ApiException.Friendly(ex, "That key didn't work.");
        }
        finally
        {
            IsReplacingKey = false;
        }
    }

    /// <summary>Called when the page is shown.</summary>
    public async Task OnShownAsync()
    {
        RefreshApiKeyMask();
        RefreshPlayers();
        if (!RemoteLoaded) await LoadRemoteAsync();
    }
}
