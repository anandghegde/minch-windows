using System.Diagnostics;
using Minch.Core.Services;

namespace Minch.App.Services;

public sealed class ShellService : IShellService
{
    public void RevealInExplorer(string path)
    {
        if (File.Exists(path))
            Start("explorer.exe", $"/select,\"{path}\"");
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            OpenFolder(dir);
    }

    public void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenUrl(Uri url)
    {
        if (url.Scheme is not ("http" or "https")) return;
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
    }

    private static void Start(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
