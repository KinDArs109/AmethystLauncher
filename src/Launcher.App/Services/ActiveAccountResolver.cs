using Launcher.Auth;
using Launcher.Auth.Offline;
using Launcher.Core.Settings;

namespace Launcher.App.Services;

public interface IActiveAccountResolver
{
    /// <summary>
    /// Resolves the identity "Играть" launches as — an offline profile for
    /// <see cref="LauncherSettings.PreferredNickname"/>, set on the "Аккаунты" page.
    /// (Microsoft account support was removed from the launcher.)
    /// </summary>
    Task<IGameAccount> ResolveAsync(CancellationToken ct = default);
}

public sealed class ActiveAccountResolver(
    ISettingsService settingsService,
    IOfflineProfileFactory offlineProfileFactory) : IActiveAccountResolver
{
    public async Task<IGameAccount> ResolveAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.LoadAsync(ct);
        var nickname = string.IsNullOrWhiteSpace(settings.PreferredNickname) ? "Player" : settings.PreferredNickname;
        return offlineProfileFactory.CreateAccount(nickname);
    }
}
