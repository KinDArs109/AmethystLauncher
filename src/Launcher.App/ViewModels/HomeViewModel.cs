using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views.Pages;
using Launcher.ModSources;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly IInstanceLaunchService _instanceLaunchService;
    private readonly IModInstallService _modInstallService;
    private readonly ILogger<HomeViewModel> _logger;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _instanceCount;

    [ObservableProperty]
    private int _installedContentCount;

    [ObservableProperty]
    private int _worldCount;

    [ObservableProperty]
    private string _lastPlayedSummary = "Ещё не запускалась";

    public ObservableCollection<RecentWorldItem> RecentWorlds { get; } = [];

    public HomeViewModel(
        INavigationService navigationService,
        IInstanceLibrary instanceLibrary,
        IInstanceLaunchService instanceLaunchService,
        IModInstallService modInstallService,
        ILogger<HomeViewModel> logger)
    {
        _navigationService = navigationService;
        _instanceLibrary = instanceLibrary;
        _instanceLaunchService = instanceLaunchService;
        _modInstallService = modInstallService;
        _logger = logger;

        _ = LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            await _instanceLibrary.RefreshAsync();
            var instances = _instanceLibrary.Instances;

            InstanceCount = instances.Count;

            var worldItems = instances
                .SelectMany(instance => WorldScanner.ScanSaves(instance.DirectoryPath)
                    .Select(w => new RecentWorldItem(instance, w.Name, w.Modified)))
                .OrderByDescending(w => w.Modified)
                .ToList();

            WorldCount = worldItems.Count;

            RecentWorlds.Clear();
            foreach (var item in worldItems.Take(6))
            {
                RecentWorlds.Add(item);
            }

            var lastPlayed = instances
                .Where(i => i.LastPlayedAt.HasValue)
                .OrderByDescending(i => i.LastPlayedAt)
                .FirstOrDefault();
            LastPlayedSummary = lastPlayed is null ? "Ещё не запускалась" : lastPlayed.Name;

            var contentCounts = await Task.WhenAll(instances.Select(async i =>
            {
                try
                {
                    return (await _modInstallService.GetInstalledModsAsync(i.DirectoryPath)).Count;
                }
                catch
                {
                    return 0;
                }
            }));
            InstalledContentCount = contentCounts.Sum();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Home dashboard stats");
        }
    }

    [RelayCommand]
    private void GoToPlay() => _navigationService.Navigate(typeof(LibraryPage));

    [RelayCommand]
    private void GoToNews() => _navigationService.Navigate(typeof(NewsPage));

    [RelayCommand]
    private void GoToMods() => _navigationService.Navigate(typeof(ModsPage));

    /// <summary>Launches straight into the world's instance — Minecraft has no deep-link into a specific
    /// world, so like Modrinth App's own "Jump back in", this only gets the player to the world-select
    /// screen, not into the world itself.</summary>
    [RelayCommand]
    private async Task PlayWorldAsync(RecentWorldItem item)
    {
        if (item.IsLaunching)
        {
            return;
        }

        try
        {
            item.IsLaunching = true;
            StatusText = $"Запуск «{item.Instance.Name}»...";

            await _instanceLaunchService.LaunchAsync(item.Instance, onStatus: s => StatusText = s);

            StatusText = "Игра запущена.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch from Jump back in for {Instance}", item.Instance.Name);
            StatusText = $"Не удалось запустить: {ex.Message}";
        }
        finally
        {
            item.IsLaunching = false;
        }
    }
}
