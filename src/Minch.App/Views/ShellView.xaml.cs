using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Minch.Core.Updates;
using Minch.ViewModels;

namespace Minch.App.Views;

public sealed partial class ShellView : UserControl
{
    private readonly Dictionary<LibrarySection, NavigationViewItem> _sectionItems = [];
    private UpdateInfo? _update;
    private MainWindow? _window;
    private bool _syncingSelection;

    public ShellView()
    {
        InitializeComponent();
    }

    public SessionViewModel Session { get; private set; } = null!;

    /// <summary>NavigationView keeps its own toggle; the TitleBar's stays hidden.</summary>
    public bool IsPaneToggleNeeded => false;

    public void Initialize(SessionViewModel session, MainWindow window)
    {
        Session = session;
        _window = window;
        Bindings.Update();

        foreach (var s in session.Library.Sections)
        {
            var item = new NavigationViewItem
            {
                Content = s.Title,
                Tag = s.Key,
                Icon = new FontIcon { Glyph = s.Glyph },
            };
            var badge = new InfoBadge { Value = s.Count, Visibility = s.Count > 0 ? Visibility.Visible : Visibility.Collapsed };
            item.InfoBadge = badge;
            s.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SectionItem.Count)) return;
                badge.Value = s.Count;
                badge.Visibility = s.Count > 0 && s.Section == LibrarySection.Active ? Visibility.Visible : Visibility.Collapsed;
            };
            // Only the Active section shows a count badge, like Mail's unread count.
            badge.Visibility = s.Section == LibrarySection.Active && s.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _sectionItems[s.Section] = item;
            Nav.MenuItems.Add(item);
        }

        session.Library.PropertyChanged += OnLibraryChanged;
        session.SignedIn += (_, _) => DispatcherQueue.TryEnqueue(() => Navigate("library"));
        Loaded += (_, _) =>
        {
            if (ContentFrame.Content is null) Navigate("library");
        };
    }

    private void OnLibraryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedSection) && ContentFrame.Content is LibraryPage)
            SelectNavItem(_sectionItems[Session.Library.SelectedSection]);
    }

    private void SelectNavItem(object item)
    {
        _syncingSelection = true;
        Nav.SelectedItem = item;
        _syncingSelection = false;
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection) return;
        if (args.IsSettingsSelected)
        {
            NavigateTo(typeof(SettingsPage), args.RecommendedNavigationTransitionInfo);
            return;
        }
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        if (tag == "account")
        {
            NavigateTo(typeof(AccountPage), args.RecommendedNavigationTransitionInfo);
            return;
        }
        if (LibrarySections.FromKey(tag) is { } section)
        {
            Session.Library.SelectedSection = section;
            if (ContentFrame.Content is not LibraryPage)
                NavigateTo(typeof(LibraryPage), args.RecommendedNavigationTransitionInfo);
        }
    }

    private void NavigateTo(Type page, NavigationTransitionInfo? transition)
    {
        if (ContentFrame.Content?.GetType() == page) return;
        ContentFrame.Navigate(page, null, transition ?? new EntranceNavigationTransitionInfo());
    }

    /// <summary>"library", "settings" or "account".</summary>
    public void Navigate(string page)
    {
        switch (page)
        {
            case "settings":
                SelectNavItem(Nav.SettingsItem);
                NavigateTo(typeof(SettingsPage), null);
                break;
            case "account":
                SelectNavItem(AccountItem);
                NavigateTo(typeof(AccountPage), null);
                break;
            default:
                SelectNavItem(_sectionItems[Session.Library.SelectedSection]);
                NavigateTo(typeof(LibraryPage), null);
                break;
        }
    }

    public void ShowLibraryIfNeeded()
    {
        if (ContentFrame.Content is not LibraryPage) Navigate("library");
    }

    public void TogglePane() => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    public void FocusAddBox()
    {
        Navigate("library");
        DispatcherQueue.TryEnqueue(() => (ContentFrame.Content as LibraryPage)?.FocusAddBox());
    }

    public async Task PickTorrentFileAsync()
    {
        Navigate("library");
        await Task.Yield();
        if (ContentFrame.Content is LibraryPage page) await page.PickTorrentFileAsync();
    }

    public void ShowUpdate(UpdateInfo info)
    {
        _update = info;
        UpdateBar.Message = $"Minch {info.Version.ToString(3)} is available.";
        UpdateBar.IsOpen = true;
        UpdateBar.Closed += (_, _) => App.Settings.Update(s => s with { SkippedUpdateVersion = info.Tag });
    }

    private void OnUpdateDownload(object sender, RoutedEventArgs e)
    {
        if (_update is not null) Session.Services.Shell?.OpenUrl(_update.PageUrl);
        UpdateBar.IsOpen = false;
    }
}
