using System.Diagnostics;
using Launcher.Backend.Skins;
using Launcher.Core.Download;
using Launcher.Core.Instances;
using Launcher.Core.Launch;
using Launcher.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

public sealed record LaunchOutcome(Process Process, LauncherInstance UpdatedInstance);

/// <summary>Resolves account/settings/version and starts the game for a given instance — the one place
/// that knows how to turn a <see cref="LauncherInstance"/> into a running process, so both the instance
/// detail page and the Home page's "Продолжить" shortcuts launch identically.</summary>
public interface IInstanceLaunchService
{
    Task<LaunchOutcome> LaunchAsync(
        LauncherInstance instance,
        Action<string>? onStatus = null,
        Action<string, bool>? onOutputLine = null,
        IInstanceLogWriter? logWriter = null,
        InstallProgress? progress = null,
        CancellationToken ct = default);
}

public sealed class InstanceLaunchService(
    IActiveAccountResolver accountResolver,
    IInstanceVersionResolver instanceVersionResolver,
    IGameLauncher gameLauncher,
    ISettingsService settingsService,
    IInstanceLibrary instanceLibrary,
    PresenceService presenceService,
    ISkinService skinService,
    ISkinModInstaller skinModInstaller,
    ILogger<InstanceLaunchService> logger) : IInstanceLaunchService
{
    public async Task<LaunchOutcome> LaunchAsync(
        LauncherInstance instance,
        Action<string>? onStatus = null,
        Action<string, bool>? onOutputLine = null,
        IInstanceLogWriter? logWriter = null,
        InstallProgress? progress = null,
        CancellationToken ct = default)
    {
        var account = await accountResolver.ResolveAsync();
        var settings = await settingsService.LoadAsync();
        var effectiveVersion = await instanceVersionResolver.ResolveAsync(instance, s => onStatus?.Invoke(s));

        var request = new LaunchRequest(
            effectiveVersion,
            instance.DirectoryPath,
            account.Username,
            account.Uuid,
            account.AccessToken,
            account.UserType,
            instance.MinRamMb ?? settings.MinRamMb,
            instance.MaxRamMb ?? settings.MaxRamMb,
            instance.JavaPathOverride ?? settings.JavaPathOverride,
            instance.JvmArgs,
            instance.WindowWidth,
            instance.WindowHeight,
            instance.Fullscreen,
            instance.EnvVars);

        // If the player has a cloud skin, drop the CustomSkinLoader mod + config into the instance so
        // the skin actually renders in-game. Best-effort: a skin problem must never block a launch.
        if (!string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
        {
            try
            {
                onStatus?.Invoke("Проверка скина...");
                var skin = await skinService.GetAsync(settings.LocalAccountUsername, ct);
                if (skin is not null)
                {
                    await skinModInstaller.EnsureInstalledAsync(instance, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skin mod install failed; launching without it");
            }
        }

        logger.LogInformation("Launching instance '{Instance}'", instance.Name);

        var process = await gameLauncher.LaunchAsync(request, progress, (line, isError) =>
        {
            onOutputLine?.Invoke(line, isError);
            logWriter?.WriteLine(line);
        }, ct);

        presenceService.TrackGame(instance.Name, process);

        var updated = await instanceLibrary.MarkPlayedAsync(instance);
        return new LaunchOutcome(process, updated);
    }
}
