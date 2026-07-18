using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class SupportPage : INavigableView<SupportViewModel>
{
    public SupportViewModel ViewModel { get; }

    public SupportPage(SupportViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
