using Launcher.Auth.AccountStore;
using Launcher.Auth.MinecraftServices;
using Launcher.Auth.Microsoft;
using Launcher.Auth.Offline;
using Launcher.Auth.Xbox;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherAuth(this IServiceCollection services)
    {
        services.AddSingleton<IOfflineProfileFactory, OfflineProfileFactory>();
        services.AddSingleton<IAccountStore, AccountStore.AccountStore>();

        // Minecraft Services sits behind Cloudflare and returns a bare 403 for requests with no
        // User-Agent (HttpClient sends none by default) — every client in the auth chain needs one.
        static void ConfigureClient(HttpClient client) =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftLauncher/1.0");

        services.AddHttpClient<IMsaTokenClient, MsaTokenClient>(ConfigureClient);
        services.AddHttpClient<IXblAuthClient, XblAuthClient>(ConfigureClient);
        services.AddHttpClient<IXstsAuthClient, XstsAuthClient>(ConfigureClient);
        services.AddHttpClient<IMinecraftAuthClient, MinecraftAuthClient>(ConfigureClient);
        services.AddHttpClient<IEntitlementChecker, EntitlementChecker>(ConfigureClient);
        services.AddHttpClient<IProfileClient, ProfileClient>(ConfigureClient);

        services.AddSingleton<IMicrosoftAuthenticator, MicrosoftAuthenticator>();

        return services;
    }
}
