using Launcher.Loaders.Abstractions;
using Launcher.Loaders.Fabric;
using Launcher.Loaders.Forge;
using Launcher.Loaders.NeoForge;
using Launcher.Loaders.Quilt;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Loaders;

public static class LoadersServiceCollectionExtensions
{
    /// <summary>
    /// Registers all loader installers against <see cref="ILoaderInstaller"/> — resolve them as
    /// <c>IEnumerable&lt;ILoaderInstaller&gt;</c> and pick by <see cref="ILoaderInstaller.LoaderType"/>.
    /// </summary>
    public static IServiceCollection AddLauncherLoaders(this IServiceCollection services)
    {
        services.AddHttpClient<ILoaderInstaller, FabricLoaderInstaller>();
        services.AddHttpClient<ILoaderInstaller, QuiltLoaderInstaller>();
        services.AddHttpClient<ILoaderInstaller, ForgeLoaderInstaller>();
        services.AddHttpClient<ILoaderInstaller, NeoForgeLoaderInstaller>();

        return services;
    }
}
