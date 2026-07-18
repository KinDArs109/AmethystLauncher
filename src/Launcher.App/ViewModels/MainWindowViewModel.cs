using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Launcher.App.Messages;
using Launcher.App.Services;
using Launcher.Backend.Models;
using Launcher.Backend.News;
using Launcher.App.Views;
using Launcher.App.Views.Pages;
using Launcher.Core.Launch;
using Launcher.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Launcher.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDownloadCenter _downloadCenter;
    private readonly IContentDialogService _contentDialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly IInstanceVersionResolver _instanceVersionResolver;
    private readonly IInstancePreparer _instancePreparer;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private string _applicationTitle = "Amethyst Launcher";

    /// <summary>Whether the docked right column (account/friends/news) is shown. The Конструктор сборок
    /// page hides it so its full-width orbital canvas has room; every other page shows it.</summary>
    [ObservableProperty]
    private bool _isRightPanelVisible = true;

    [ObservableProperty]
    private string _accountDisplayName = "Игрок";

    [ObservableProperty]
    private string _accountInitial = "?";

    public IDownloadCenter DownloadCenter { get; }

    public FriendsPanelViewModel Friends { get; }

    /// <summary>Latest published announcements for the docked "Новости" card (top 3).</summary>
    public ObservableCollection<Announcement> LatestNews { get; } = [];

    private readonly IAnnouncementsService _announcementsService;

    public MainWindowViewModel(
        IDownloadCenter downloadCenter,
        IContentDialogService contentDialogService,
        IServiceProvider serviceProvider,
        IInstanceLibrary instanceLibrary,
        IInstanceVersionResolver instanceVersionResolver,
        IInstancePreparer instancePreparer,
        ISettingsService settingsService,
        INavigationService navigationService,
        FriendsPanelViewModel friendsPanel,
        IAnnouncementsService announcementsService,
        ILogger<MainWindowViewModel> logger)
    {
        Friends = friendsPanel;
        _announcementsService = announcementsService;
        _downloadCenter = downloadCenter;
        _contentDialogService = contentDialogService;
        _serviceProvider = serviceProvider;
        _instanceLibrary = instanceLibrary;
        _instanceVersionResolver = instanceVersionResolver;
        _instancePreparer = instancePreparer;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _logger = logger;
        DownloadCenter = downloadCenter;

        _ = LoadAccountSummaryAsync();
        _ = RunNewsRefreshLoopAsync();

        // The Аккаунты page announces profile changes; refresh the "Играете как" card right away.
        WeakReferenceMessenger.Default.Register<AccountSummaryChangedMessage>(
            this, (_, _) => _ = LoadAccountSummaryAsync());
    }

    /// <summary>The docked news card refreshes itself every few minutes — realtime push is flaky
    /// (see NewsViewModel), so polling is what actually keeps the panel current without a restart.</summary>
    private async Task RunNewsRefreshLoopAsync()
    {
        while (true)
        {
            await LoadLatestNewsAsync();
            await Task.Delay(TimeSpan.FromMinutes(3));
        }
    }

    private async Task LoadLatestNewsAsync()
    {
        try
        {
            var all = await _announcementsService.GetPublishedAsync();
            LatestNews.Clear();
            foreach (var announcement in all.Take(3))
            {
                LatestNews.Add(announcement);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load news for the docked panel");
        }
    }

    /// <summary>Lightweight — reads the locally-saved nickname/mode, no network refresh (unlike
    /// IActiveAccountResolver, which is only meant to be called right before actually launching).</summary>
    private async Task LoadAccountSummaryAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            AccountDisplayName = string.IsNullOrWhiteSpace(settings.PreferredNickname) ? "Игрок" : settings.PreferredNickname;
            AccountInitial = AccountDisplayName.Length > 0 ? AccountDisplayName[..1].ToUpperInvariant() : "?";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load account summary for nav footer");
        }
    }

    [RelayCommand]
    private void DismissDownload(ActiveDownload download) => DownloadCenter.Dismiss(download);

    [RelayCommand]
    private async Task OpenCreateInstanceAsync()
    {
        var formViewModel = _serviceProvider.GetRequiredService<CreateInstanceViewModel>();
        var dialog = new CreateInstanceDialog(formViewModel);

        var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary || !formViewModel.CanCreate)
        {
            return;
        }

        var gameVersion = formViewModel.SelectedGameVersion!;
        var loaderType = formViewModel.SelectedLoaderType;
        var loaderVersion = formViewModel.SelectedLoaderVersion;
        var instanceName = string.IsNullOrWhiteSpace(formViewModel.Name) ? gameVersion.Id : formViewModel.Name.Trim();

        var instance = await _instanceLibrary.CreateAsync(
            instanceName, gameVersion.Id, loaderType.ToString(), loaderVersion?.Version);

        var download = _downloadCenter.Start(instance.Name);

        _ = Task.Run(async () =>
        {
            try
            {
                var effectiveVersion = await _instanceVersionResolver.ResolveAsync(instance, download.Progress.SetStatus);
                await _instancePreparer.PrepareAsync(effectiveVersion, instance.DirectoryPath, progress: download.Progress);
                _downloadCenter.Complete(download);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download instance '{Instance}'", instance.Name);
                _downloadCenter.Complete(download, $"Ошибка загрузки: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void OpenAccount() => _navigationService.Navigate(typeof(AccountsPage));

    /// <summary>The nav-rail "+" now opens the Конструктор сборок (fresh, create mode) instead of the
    /// old create-instance dialog — the builder is the single place to make and manage builds.</summary>
    [RelayCommand]
    private void OpenBuilder() => _navigationService.Navigate(typeof(ModpackBuilderPage));
}
