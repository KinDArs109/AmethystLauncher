using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class HomePage : INavigableView<HomeViewModel>
{
    public HomeViewModel ViewModel { get; }

    public HomePage(HomeViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
