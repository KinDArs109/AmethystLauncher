using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class CreateInstanceDialog
{
    public CreateInstanceViewModel ViewModel { get; }

    public CreateInstanceDialog(CreateInstanceViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
