using Launcher.Core.Download;
using Launcher.Core.Instances;
using Launcher.Core.Java;
using Launcher.Core.Launch;
using Launcher.Core.Settings;
using Launcher.Core.Versions;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherCore(this IServiceCollection services)
    {
        services.AddHttpClient<IVersionManifestService, VersionManifestService>();
        services.AddHttpClient<IDownloadManager, DownloadManager>();
        services.AddHttpClient<IAssetDownloader, AssetDownloader>();
        services.AddHttpClient<IJreProvisioner, JreProvisioner>();

        services.AddSingleton<ILibraryDownloader, LibraryDownloader>();
        services.AddSingleton<IJavaRuntimeResolver, JavaRuntimeResolver>();
        services.AddSingleton<INativesExtractor, NativesExtractor>();
        services.AddSingleton<IArgumentBuilder, ArgumentBuilder>();
        services.AddSingleton<IInstancePreparer, InstancePreparer>();
        services.AddSingleton<IGameLauncher, GameLauncher>();

        services.AddSingleton<IInstanceManager, InstanceManager>();
        services.AddSingleton<ISettingsService, SettingsService>();

        return services;
    }
}
