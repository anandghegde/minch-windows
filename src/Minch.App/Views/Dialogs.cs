using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Minch.App.Views;

/// <summary>Small ContentDialog helpers for prompts and confirmations.</summary>
internal static class Dialogs
{
    public static async Task<string?> PromptAsync(XamlRoot root, string title, string header, string initial, string primary)
    {
        var box = new TextBox { Header = header, Text = initial, AcceptsReturn = false, MinWidth = 360 };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = box,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                dialog.Tag = "ok";
                dialog.Hide();
            }
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary || Equals(dialog.Tag, "ok") ? box.Text : null;
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string primary, bool destructive = true)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
