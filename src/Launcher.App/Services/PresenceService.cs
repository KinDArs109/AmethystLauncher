using System.Diagnostics;
using Launcher.Backend.Friends;
using Launcher.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

/// <summary>Keeps the signed-in local account's presence row fresh in Supabase: a heartbeat every
/// minute while the launcher runs (server counts "online" as a heartbeat within 2 minutes), the
/// current instance name while a game process is alive, and an explicit offline on shutdown.</summary>
public sealed class PresenceService(
    IFriendsService friendsService,
    ISettingsService settingsService,
    ILogger<PresenceService> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

    private readonly CancellationTokenSource _stopping = new();
    private string? _playingInstance;
    private Task? _loop;

    /// <summary>Name of the instance currently being played, or null. Read by the friends UI too.</summary>
    public string? PlayingInstance => _playingInstance;

    /// <summary>Called by the launch pipeline right after the game process starts — presence switches
    /// to "playing <paramref name="instanceName"/>" until that process exits.</summary>
    public void TrackGame(string instanceName, Process process)
    {
        _playingInstance = instanceName;
        _ = PokeAsync();

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            // Only clear if another launch hasn't replaced us in the meantime.
            if (_playingInstance == instanceName)
            {
                _playingInstance = null;
                _ = PokeAsync();
            }
        };
    }

    /// <summary>Sends one heartbeat immediately — used after login and on play-state changes so
    /// friends see the update without waiting for the next minute tick.</summary>
    public async Task PokeAsync()
    {
        try
        {
            var settings = await settingsService.LoadAsync();
            if (!string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
            {
                await friendsService.HeartbeatAsync(settings.LocalAccountUsername, _playingInstance);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Presence heartbeat failed");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            await PokeAsync();
            try
            {
                while (await timer.WaitForNextTickAsync(_stopping.Token))
                {
                    await PokeAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
            {
                // Best-effort with a hard cap so a dead network can't hang app exit.
                await friendsService.GoOfflineAsync(settings.LocalAccountUsername)
                    .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Presence offline on shutdown failed");
        }
    }

    public void Dispose() => _stopping.Dispose();
}
