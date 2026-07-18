using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Instances;

namespace Launcher.App.ViewModels;

/// <summary>One row in the "Установка проекта" dialog's existing-instance list.</summary>
public partial class InstanceInstallOption(LauncherInstance instance) : ObservableObject
{
    public LauncherInstance Instance { get; } = instance;

    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>False shows the row with a warning-styled "Установить" button instead of the normal one —
    /// installable, but the project likely doesn't support this instance's loader.</summary>
    [ObservableProperty]
    private bool _isCompatible = true;

    [ObservableProperty]
    private bool _isInstalling;
}
