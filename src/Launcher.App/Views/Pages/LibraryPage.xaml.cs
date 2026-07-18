using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class LibraryPage : INavigableView<LibraryViewModel>
{
    public LibraryViewModel ViewModel { get; }

    public LibraryPage(LibraryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
