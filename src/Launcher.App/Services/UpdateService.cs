using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Launcher.App.Services;

/// <summary>Wraps Velopack's <see cref="UpdateManager"/> against the project's GitHub Releases, so the
/// splash screen can check for, download, and apply launcher updates. When the app is running from a
/// plain build (not a Velopack install) <see cref="IsInstalled"/> is false and every operation no-ops —
/// updates only make sense for the installed copy.</summary>
public interface IUpdateService
{
    /// <summary>True only when running as a Velopack-installed app (i.e. updates are possible).</summary>
    bool IsInstalled { get; }

    /// <summary>Returns update info when a newer release is available, otherwise null.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the pending update (reporting 0–100% progress) and relaunches into it. Does
    /// not return on success — the process is replaced.</summary>
    Task DownloadAndApplyAsync(UpdateInfo update, Action<int> onProgress, CancellationToken ct = default);
}

public sealed class UpdateService : IUpdateService
{
    // The public GitHub repository that hosts the Velopack release feed. Filled in once the repo is
    // created; until then IsInstalled is false in dev builds so this is never actually contacted.
    public const string RepositoryUrl = "https://github.com/AMETHYST_OWNER/AmethystLauncher";

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _manager;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        try
        {
            return await _manager.CheckForUpdatesAsync().WaitAsync(ct);
        }
        catch (Exception ex)
        {
            // Offline, blocked, or no releases yet — treat as "no update" so startup is never blocked.
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    public async Task DownloadAndApplyAsync(UpdateInfo update, Action<int> onProgress, CancellationToken ct = default)
    {
        await _manager.DownloadUpdatesAsync(update, p => onProgress(p)).WaitAsync(ct);
        _manager.ApplyUpdatesAndRestart(update);
    }
}
