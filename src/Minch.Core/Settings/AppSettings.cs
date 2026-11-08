using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minch.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Local, per-machine preferences. Persisted as JSON in %LOCALAPPDATA%\Minch\settings.json.</summary>
public sealed record AppSettings
{
    public string? DownloadFolder { get; init; }
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public bool CloseToTray { get; init; } = true;
    public bool StartMinimized { get; init; }
    public bool NotifyOnDownloadComplete { get; init; } = true;
    public bool NotifyOnTransferFinished { get; init; } = true;
    public bool HandleMagnetLinks { get; init; }
    public bool CheckForUpdates { get; init; } = true;
    public string? PreferredExternalPlayer { get; init; }
    public bool PreferExternalPlayer { get; init; }

    // Streaming preferences (sent with /stream/createstream).
    public string? PreferredSubtitleLanguage { get; init; }
    public string? PreferredAudioLanguage { get; init; }
    public bool ScrobblingEnabled { get; init; }

    // Window placement.
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public bool WindowMaximized { get; init; }
    public string? LastSection { get; init; }
    public string? SkippedUpdateVersion { get; init; }
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? Changed;
    void Update(Func<AppSettings, AppSettings> mutate);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private AppSettings _current;

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _current = Load(_path);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minch", "settings.json");

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        AppSettings next;
        lock (_gate)
        {
            next = mutate(_current);
            if (next == _current) return;
            _current = next;
            Save(_path, next);
        }
        Changed?.Invoke(this, next);
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Opts) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable: fall back to defaults; the next save rewrites it.
        }
        return new AppSettings();
    }

    private static void Save(string path, AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opts));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort.
        }
    }
}

/// <summary>In-memory settings for tests.</summary>
public sealed class MemorySettingsStore(AppSettings? initial = null) : ISettingsStore
{
    public AppSettings Current { get; private set; } = initial ?? new AppSettings();
    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        var next = mutate(Current);
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(this, next);
    }
}
