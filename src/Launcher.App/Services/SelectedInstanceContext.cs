using Launcher.App.ViewModels;
using Launcher.Core.Instances;

namespace Launcher.App.Services;

/// <summary>
/// Carries the instance a library card was clicked for across navigation to the detail page.
/// WPF-UI's <c>INavigationService.Navigate(Type)</c> takes no parameter, so the library sets this
/// immediately before navigating and the detail page's ViewModel reads it in its constructor.
/// </summary>
public interface ISelectedInstanceContext
{
    LauncherInstance? Current { get; set; }

    /// <summary>Same hand-off pattern as <see cref="Current"/>, but for "Найти проекты" on the instance
    /// Content tab jumping into Поиск проектов scoped to that instance's loader/version.</summary>
    LauncherInstance? ProjectSearchScope { get; set; }

    /// <summary>Optional tab the detail page should open on (consumed once). Used by the builder's
    /// "Изменение конфигов" node to jump straight to the Файлы tab of the built instance.</summary>
    InstanceDetailTab? InitialTab { get; set; }
}

public sealed class SelectedInstanceContext : ISelectedInstanceContext
{
    public LauncherInstance? Current { get; set; }

    public LauncherInstance? ProjectSearchScope { get; set; }

    public InstanceDetailTab? InitialTab { get; set; }
}
