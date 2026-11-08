using Minch.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Minch.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public async Task<string?> GetTextAsync()
    {
        try
        {
            var view = Clipboard.GetContent();
            return view.Contains(StandardDataFormats.Text) ? await view.GetTextAsync() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
