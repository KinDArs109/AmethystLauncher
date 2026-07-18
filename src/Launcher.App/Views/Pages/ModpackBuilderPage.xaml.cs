using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class ModpackBuilderPage : INavigableView<ModpackBuilderViewModel>
{
    public ModpackBuilderViewModel ViewModel { get; }

    public ModpackBuilderPage(ModpackBuilderViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
