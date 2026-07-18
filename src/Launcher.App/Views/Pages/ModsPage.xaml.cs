using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class ModsPage : INavigableView<ModsViewModel>
{
    public ModsViewModel ViewModel { get; }

    public ModsPage(ModsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
