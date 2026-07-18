using System.Windows;
using System.Windows.Controls;
using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class AccountsPage : INavigableView<AccountsViewModel>
{
    public AccountsViewModel ViewModel { get; }

    public AccountsPage(AccountsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    // PasswordBox.Password isn't a DependencyProperty (by design, to avoid the plaintext value ending
    // up bound/cached anywhere), so it can't be data-bound like the rest of the form — sync it to the
    // ViewModel by hand instead.
    private void LocalPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        ViewModel.LocalPassword = ((PasswordBox)sender).Password;

    private void LocalConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        ViewModel.LocalConfirmPassword = ((PasswordBox)sender).Password;
}
