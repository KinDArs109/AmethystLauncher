using Launcher.Backend.Accounts;
using Launcher.Backend.Friends;
using Launcher.Backend.News;
using Launcher.Backend.Skins;
using Launcher.Backend.Support;
using Launcher.Backend.Supabase;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Backend;

public static class BackendServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherBackend(this IServiceCollection services)
    {
        services.AddSingleton<SupabaseSessionStore>();
        services.AddSingleton<ISupabaseClientProvider, SupabaseClientProvider>();
        services.AddSingleton<IAnnouncementsService, AnnouncementsService>();
        services.AddSingleton<ISupportService, SupportService>();
        services.AddSingleton<ILocalAccountStore, LocalAccountStore>();
        services.AddSingleton<IFriendsService, FriendsService>();
        services.AddSingleton<ISkinService, SkinService>();

        return services;
    }
}
