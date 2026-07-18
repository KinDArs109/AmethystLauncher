using Launcher.App.Views.Pages;
using Launcher.Core.Settings;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace Launcher.App.Services;

/// <summary>Shows the main window and navigates to the start page once the host has finished building services.</summary>
public sealed class ApplicationHostService(
    INavigationWindow navigationWindow,
    INavigationViewPageProvider pageProvider,
    ISettingsService settingsService,
    IBypassService bypassService) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        navigationWindow.SetPageService(pageProvider);
        navigationWindow.ShowWindow();
        navigationWindow.Navigate(typeof(HomePage));

        // Re-arm the DPI bypass the user turned on previously, so Modrinth/Supabase work from the first
        // page load. Skipped if it's already running (e.g. installed as a service by hand).
        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken);
            if (settings.BypassEnabled && !bypassService.IsRunning)
            {
                await bypassService.StartAsync();
            }
        }
        catch
        {
            // Best-effort — a failed auto-start must never block the window from showing.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
