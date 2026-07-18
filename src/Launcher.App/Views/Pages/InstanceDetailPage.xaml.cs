using System.Windows;
using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class InstanceDetailPage : INavigableView<InstanceDetailViewModel>
{
    public InstanceDetailViewModel ViewModel { get; }

    public InstanceDetailPage(InstanceDetailViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    // ui:Button has no built-in dropdown-on-click affordance — ContextMenu only opens on right-click by
    // default, so the "⋯" overflow button opens it explicitly on left-click too.
    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }
}
