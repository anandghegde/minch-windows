using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Minch.ViewModels;
using Windows.System;

namespace Minch.App.Views;

public sealed partial class CommandPaletteDialog : ContentDialog
{
    public CommandPaletteDialog(CommandPaletteViewModel palette)
    {
        Palette = palette;
        palette.Open();
        InitializeComponent();
        Opened += (_, _) => QueryBox.Focus(FocusState.Programmatic);
    }

    public CommandPaletteViewModel Palette { get; }

    /// <summary>The item to run after the dialog closes (run outside the dialog so it can open its own dialogs).</summary>
    public PaletteItem? Chosen { get; private set; }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                Palette.MoveSelection(1);
                ScrollToSelection();
                e.Handled = true;
                break;
            case VirtualKey.Up:
                Palette.MoveSelection(-1);
                ScrollToSelection();
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                Choose(Palette.Selected);
                e.Handled = true;
                break;
        }
    }

    private void ScrollToSelection()
    {
        if (Palette.Selected is { } s) ResultsList.ScrollIntoView(s);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e) => Choose(e.ClickedItem as PaletteItem);

    private void Choose(PaletteItem? item)
    {
        if (item is null) return;
        Chosen = item;
        Hide();
    }
}
