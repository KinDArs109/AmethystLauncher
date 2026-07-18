using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class InstanceSettingsDialog
{
    public InstanceSettingsDialog(InstanceSettingsTabViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
