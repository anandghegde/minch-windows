using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Minch.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Minch.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    public SessionViewModel Session => App.Session;
    public LibraryViewModel Library => App.Session.Library;

    public static string SectionGlyph(LibrarySection section) => section.Glyph();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Library.Rebuild();
    }

    public void FocusAddBox()
    {
        if (Library.HasTorrentFile) Library.ClearTorrentFileCommand.Execute(null);
        AddBox.Focus(FocusState.Keyboard);
        AddBox.SelectAll();
    }

    // ------------------------------------------------------------- add bar

    private void OnAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (Library.AddCommand.CanExecute(null)) Library.AddCommand.Execute(null);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Library.ResetAdd();
        }
    }

    private void OnBrowseTorrent(object sender, RoutedEventArgs e) => _ = PickTorrentFileAsync();

    public async Task PickTorrentFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".torrent");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle());
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await Library.LoadTorrentFileAsync(file.Path);
    }

    private static nint WindowHandle() =>
        App.MainWindow is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;

    // --------------------------------------------------------- drag & drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.WebLink))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to TorBox";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.OfType<StorageFile>().FirstOrDefault(f => f.FileType.Equals(".torrent", StringComparison.OrdinalIgnoreCase)) is { } file)
                {
                    await Library.LoadTorrentFileAsync(file.Path);
                    return;
                }
            }
            string? text = null;
            if (e.DataView.Contains(StandardDataFormats.WebLink)) text = (await e.DataView.GetWebLinkAsync())?.ToString();
            else if (e.DataView.Contains(StandardDataFormats.Text)) text = await e.DataView.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                Library.AddText = text.Trim();
                AddBox.Focus(FocusState.Programmatic);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ---------------------------------------------------------- file rows

    private void OnFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FileItemViewModel f && f.CanPlay)
            f.PlayCommand.Execute(null);
    }

    private void OnFileMenuOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        var sub = menu.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault();
        if (sub?.Tag is not FileItemViewModel file) return;
        sub.Items.Clear();
        var players = file.ExternalPlayers;
        sub.Visibility = players.Count > 0 && file.CanPlay ? Visibility.Visible : Visibility.Collapsed;
        foreach (var p in players)
            sub.Items.Add(new MenuFlyoutItem { Text = p.DisplayName, Command = file.PlayExternalCommand, CommandParameter = p });
    }

    // ------------------------------------------------------ transfer menus

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TransferItemViewModel t) return;
        var name = await Dialogs.PromptAsync(XamlRoot, "Rename transfer", "Name", t.Name, "Rename");
        if (name is not null && name.Trim() != t.Name) await t.RenameAsync(name.Trim());
    }

    private async void OnEditTags(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TransferItemViewModel t) return;
        var text = await Dialogs.PromptAsync(XamlRoot, "Edit tags", "Tags (comma separated)", t.TagsText, "Save");
        if (text is not null) await t.SetTagsAsync(TransferItemViewModel.ParseTags(text));
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TransferItemViewModel t) return;
        var ok = await Dialogs.ConfirmAsync(
            XamlRoot,
            "Delete from TorBox?",
            $"“{t.Name}” will be removed from your TorBox account. Files already downloaded to this PC are kept.",
            "Delete");
        if (ok) await t.DeleteAsync();
    }
}
