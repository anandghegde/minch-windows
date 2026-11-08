using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Minch.ViewModels;

namespace Minch.App.Views;

public sealed partial class AccountPage : Page
{
    public AccountPage()
    {
        InitializeComponent();
    }

    public AccountViewModel Account => App.Session.Account;

    public static Visibility NoSubscriptions(bool has) => has ? Visibility.Collapsed : Visibility.Visible;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Account.NotifyUser();
        await Account.LoadAsync();
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        var ok = await Dialogs.ConfirmAsync(XamlRoot, "Sign out of TorBox?",
            "Your API key will be removed from Windows Credential Manager. Downloaded files stay where they are.", "Sign out");
        if (ok) await App.Session.SignOutAsync();
    }
}
