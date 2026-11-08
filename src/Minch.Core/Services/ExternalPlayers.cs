namespace Minch.Core.Services;

public enum ExternalPlayerId
{
    Vlc,
    Mpv,
    MpcHc,
    PotPlayer,
}

public sealed record ExternalPlayer(ExternalPlayerId Id, string DisplayName, string ExecutablePath);

/// <summary>Detects and launches desktop media players (VLC, mpv, MPC-HC, PotPlayer).</summary>
public interface IExternalPlayerLauncher
{
    /// <summary>Installed players, re-detected on each call (cheap: a few file/registry probes).</summary>
    IReadOnlyList<ExternalPlayer> Installed();
    bool Launch(ExternalPlayer player, string mediaUrlOrPath, string? title = null);
}

public sealed class NoExternalPlayers : IExternalPlayerLauncher
{
    public static readonly NoExternalPlayers Instance = new();
    public IReadOnlyList<ExternalPlayer> Installed() => [];
    public bool Launch(ExternalPlayer player, string mediaUrlOrPath, string? title = null) => false;
}

public static class ExternalPlayerNames
{
    public static string Display(ExternalPlayerId id) => id switch
    {
        ExternalPlayerId.Vlc => "VLC",
        ExternalPlayerId.Mpv => "mpv",
        ExternalPlayerId.MpcHc => "MPC-HC",
        ExternalPlayerId.PotPlayer => "PotPlayer",
        _ => id.ToString(),
    };
}
