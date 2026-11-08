using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Minch.ViewModels;
using Windows.System;

namespace Minch.App.Views;

public sealed partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
    }

    public SessionViewModel Session { get; private set; } = null!;

    public void Initialize(SessionViewModel session)
    {
        Session = session;
        Bindings.Update();
        Loaded += (_, _) => KeyBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (Session.ConnectCommand.CanExecute(null)) Session.ConnectCommand.Execute(null);
    }
}
