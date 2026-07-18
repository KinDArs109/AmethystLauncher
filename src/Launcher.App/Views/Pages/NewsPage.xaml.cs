using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class NewsPage : INavigableView<NewsViewModel>
{
    public NewsViewModel ViewModel { get; }

    public NewsPage(NewsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
