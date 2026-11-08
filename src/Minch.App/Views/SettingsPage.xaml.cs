using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Minch.Core.Settings;
using Minch.ViewModels;
using Windows.Storage.Pickers;

namespace Minch.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loadingTheme;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel Settings => App.Session.Settings;
    public IReadOnlyList<SeedChoice> SeedChoices => SettingsViewModel.SeedChoices;
    public string AboutText => $"Version {App.Version.ToString(3)} · A native Windows client for TorBox.";

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loadingTheme = true;
        ThemeBox.SelectedIndex = Settings.Theme switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0,
        };
        _loadingTheme = false;
        await Settings.OnShownAsync();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (Enum.TryParse<ThemePreference>(tag, out var theme)) Settings.Theme = theme;
    }

    private async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        if (App.MainWindow is { } w)
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(w));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) Settings.DownloadFolder = folder.Path;
    }

    private void OnRescanPlayers(object sender, RoutedEventArgs e) => Settings.RefreshPlayers();
}
