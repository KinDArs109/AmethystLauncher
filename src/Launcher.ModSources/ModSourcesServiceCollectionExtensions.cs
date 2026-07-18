using Launcher.ModSources.Modrinth;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.ModSources;

public static class ModSourcesServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherModSources(this IServiceCollection services)
    {
        services.AddHttpClient<IModrinthClient, ModrinthClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.modrinth.com/v2/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftLauncher/1.0 (private community launcher)");
        });

        services.AddSingleton<IInstalledModsStore, InstalledModsStore>();
        services.AddSingleton<IModInstallService, ModInstallService>();

        return services;
    }
}
