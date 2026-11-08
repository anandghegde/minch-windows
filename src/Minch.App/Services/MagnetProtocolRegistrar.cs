using Microsoft.Win32;
using Minch.Core.Services;

namespace Minch.App.Services;

/// <summary>
/// Registers a per-user magnet: handler under HKCU\Software\Classes. No admin needed.
/// Windows 11 still asks the user to confirm the default the first time a link is opened.
/// </summary>
public sealed class MagnetProtocolRegistrar : IProtocolRegistrar
{
    private const string KeyPath = @"Software\Classes\magnet";
    private const string Marker = "MinchHandler";

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Minch.exe");

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(Marker) is not null;
    }

    public void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(null, "URL:Magnet link");
        key.SetValue("URL Protocol", "");
        key.SetValue(Marker, "1");
        using (var icon = key.CreateSubKey("DefaultIcon"))
            icon.SetValue(null, $"\"{Exe}\",0");
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue(null, $"\"{Exe}\" \"%1\"");
    }

    public void Unregister()
    {
        // Only remove the key if we own it, so we don't break another client's handler.
        if (!IsRegistered()) return;
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
    }
}
