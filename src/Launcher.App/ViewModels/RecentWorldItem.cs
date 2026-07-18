using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Instances;

namespace Launcher.App.ViewModels;

/// <summary>One row in Home's "Jump back in" — a single-player world plus the instance it belongs to,
/// so the card can show both and launch straight into that instance.</summary>
public partial class RecentWorldItem(LauncherInstance instance, string worldName, DateTimeOffset modified) : ObservableObject
{
    public LauncherInstance Instance { get; } = instance;

    public string WorldName { get; } = worldName;

    public DateTimeOffset Modified { get; } = modified;

    [ObservableProperty]
    private bool _isLaunching;
}
