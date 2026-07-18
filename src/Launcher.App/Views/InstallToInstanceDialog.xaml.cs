using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class InstallToInstanceDialog
{
    public InstallToInstanceViewModel ViewModel { get; }

    public InstallToInstanceDialog(InstallToInstanceViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
