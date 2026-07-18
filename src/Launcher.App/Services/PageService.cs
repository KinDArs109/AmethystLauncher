using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions;

namespace Launcher.App.Services;

/// <summary>Resolves navigation pages from the DI container instead of via reflection/Activator.</summary>
public sealed class PageService(IServiceProvider serviceProvider) : INavigationViewPageProvider
{
    public object GetPage(Type pageType) =>
        serviceProvider.GetService(pageType)
        ?? throw new InvalidOperationException($"Page '{pageType}' is not registered in the DI container.");
}
