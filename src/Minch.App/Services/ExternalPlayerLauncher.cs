using System.Diagnostics;
using Microsoft.Win32;
using Minch.Core.Services;

namespace Minch.App.Services;

/// <summary>Finds VLC, mpv, MPC-HC and PotPlayer via known install paths, App Paths and PATH.</summary>
public sealed class ExternalPlayerLauncher : IExternalPlayerLauncher
{
    private sealed record Candidate(ExternalPlayerId Id, string[] Exes, string[] RelativePaths);

    private static readonly Candidate[] Candidates =
    [
        new(ExternalPlayerId.Vlc, ["vlc.exe"], [@"VideoLAN\VLC\vlc.exe"]),
        new(ExternalPlayerId.Mpv, ["mpv.exe"], [@"mpv\mpv.exe", @"mpv.net\mpvnet.exe"]),
        new(ExternalPlayerId.MpcHc, ["mpc-hc64.exe", "mpc-hc.exe"], [@"MPC-HC\mpc-hc64.exe", @"MPC-HC\mpc-hc.exe", @"K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe"]),
        new(ExternalPlayerId.PotPlayer, ["PotPlayerMini64.exe", "PotPlayerMini.exe"], [@"DAUM\PotPlayer\PotPlayerMini64.exe", @"PotPlayer\PotPlayerMini64.exe"]),
    ];

    public IReadOnlyList<ExternalPlayer> Installed()
    {
        var found = new List<ExternalPlayer>();
        foreach (var c in Candidates)
        {
            if (Locate(c) is { } path)
                found.Add(new ExternalPlayer(c.Id, ExternalPlayerNames.Display(c.Id), path));
        }
        return found;
    }

    public bool Launch(ExternalPlayer player, string mediaUrlOrPath, string? title = null)
    {
        try
        {
            var psi = new ProcessStartInfo(player.ExecutablePath) { UseShellExecute = false };
            switch (player.Id)
            {
                case ExternalPlayerId.Vlc:
                    if (title is not null) psi.ArgumentList.Add($"--meta-title={title}");
                    psi.ArgumentList.Add(mediaUrlOrPath);
                    break;
                case ExternalPlayerId.Mpv:
                    if (title is not null) psi.ArgumentList.Add($"--force-media-title={title}");
                    psi.ArgumentList.Add(mediaUrlOrPath);
                    break;
                default:
                    psi.ArgumentList.Add(mediaUrlOrPath);
                    break;
            }
            return Process.Start(psi) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? Locate(Candidate c)
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        ];
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            foreach (var rel in c.RelativePaths)
            {
                var p = Path.Combine(root, rel);
                if (File.Exists(p)) return p;
            }

        foreach (var exe in c.Exes)
        {
            if (AppPath(exe) is { } p) return p;
        }

        // scoop / winget shims / manual installs on PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var exe in c.Exes)
            {
                try
                {
                    var p = Path.Combine(dir.Trim('"'), exe);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
        return null;
    }

    private static string? AppPath(string exe)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                if (key?.GetValue(null) is string p && File.Exists(p.Trim('"'))) return p.Trim('"');
            }
            catch (Exception) { }
        }
        return null;
    }
}
