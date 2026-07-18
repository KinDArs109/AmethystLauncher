using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views;
using Launcher.App.Views.Pages;
using Launcher.Core.Download;
using Launcher.Core.Instances;
using Launcher.Core.Settings;
using Launcher.ModSources;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public enum InstanceDetailTab
{
    Content,
    Files,
    Worlds,
    Logs,
}

public partial class InstanceDetailViewModel : ObservableObject
{
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly IInstanceLaunchService _instanceLaunchService;
    private readonly ISelectedInstanceContext _selectedInstanceContext;
    private readonly IContentDialogService _contentDialogService;
    private readonly INavigationService _navigationService;
    private readonly IInstanceLogHistoryService _logHistoryService;
    private readonly ILogger<InstanceDetailViewModel> _logger;

    [ObservableProperty]
    private LauncherInstance _instance;

    [ObservableProperty]
    private InstanceDetailTab _selectedTab = InstanceDetailTab.Content;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "";

    public InstanceContentTabViewModel ContentTab { get; }

    public InstanceFilesTabViewModel FilesTab { get; }

    public InstanceWorldsTabViewModel WorldsTab { get; }

    public InstanceSettingsTabViewModel SettingsTab { get; }

    public InstanceLogsTabViewModel LogsTab { get; }

    public InstanceDetailViewModel(
        ISelectedInstanceContext selectedInstanceContext,
        IInstanceLibrary instanceLibrary,
        IInstanceLaunchService instanceLaunchService,
        ISettingsService settingsService,
        IContentDialogService contentDialogService,
        INavigationService navigationService,
        IModInstallService modInstallService,
        IInstanceFileService fileService,
        IInstanceLogHistoryService logHistoryService,
        ILoggerFactory loggerFactory)
    {
        _instance = selectedInstanceContext.Current
            ?? throw new InvalidOperationException("Не выбрана сборка — вернитесь в библиотеку и откройте её заново.");

        _instanceLibrary = instanceLibrary;
        _instanceLaunchService = instanceLaunchService;
        _selectedInstanceContext = selectedInstanceContext;
        _contentDialogService = contentDialogService;
        _navigationService = navigationService;
        _logHistoryService = logHistoryService;
        _logger = loggerFactory.CreateLogger<InstanceDetailViewModel>();

        ContentTab = new InstanceContentTabViewModel(modInstallService, Instance, loggerFactory.CreateLogger<InstanceContentTabViewModel>());
        FilesTab = new InstanceFilesTabViewModel(fileService, contentDialogService, Instance.DirectoryPath, loggerFactory.CreateLogger<InstanceFilesTabViewModel>());
        WorldsTab = new InstanceWorldsTabViewModel(Instance.DirectoryPath, contentDialogService, loggerFactory.CreateLogger<InstanceWorldsTabViewModel>());
        SettingsTab = new InstanceSettingsTabViewModel(instanceLibrary, settingsService, Instance, loggerFactory.CreateLogger<InstanceSettingsTabViewModel>());
        SettingsTab.InstanceUpdated += updated => Instance = updated;
        LogsTab = new InstanceLogsTabViewModel(logHistoryService, Instance, loggerFactory.CreateLogger<InstanceLogsTabViewModel>());

        LogsTab.LoadHistoryCommand.ExecuteAsync(null);

        // Honour a requested landing tab (e.g. the builder's "Изменение конфигов" opens Файлы), then
        // clear it so a plain re-open of the page starts on Content again.
        if (selectedInstanceContext.InitialTab is { } initialTab)
        {
            SelectedTab = initialTab;
            if (initialTab == InstanceDetailTab.Files)
            {
                FilesTab.LoadCommand.Execute(null);
            }

            selectedInstanceContext.InitialTab = null;
        }
    }

    [RelayCommand]
    private void ShowContentTab() => SelectedTab = InstanceDetailTab.Content;

    [RelayCommand]
    private void ShowFilesTab()
    {
        SelectedTab = InstanceDetailTab.Files;
        FilesTab.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowWorldsTab()
    {
        SelectedTab = InstanceDetailTab.Worlds;
        WorldsTab.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task OpenSettingsDialogAsync()
    {
        var dialog = new InstanceSettingsDialog(SettingsTab);
        await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
    }

    [RelayCommand]
    private void ShowLogsTab() => SelectedTab = InstanceDetailTab.Logs;

    [RelayCommand]
    private void GoBack() => _navigationService.Navigate(typeof(LibraryPage));

    [RelayCommand]
    private void FindProjects()
    {
        _selectedInstanceContext.ProjectSearchScope = Instance;
        _navigationService.Navigate(typeof(ModsPage));
    }

    [RelayCommand]
    private async Task CloneAsync()
    {
        var newName = await DialogHelpers.PromptAsync(_contentDialogService, "Клонировать сборку", $"{Instance.Name} (копия)");
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Клонирование...";
            await _instanceLibrary.CloneAsync(Instance, newName.Trim());
            StatusText = $"Сборка «{newName.Trim()}» создана в Библиотеке.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone instance '{Instance}'", Instance.Name);
            StatusText = $"Не удалось клонировать: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Instance.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open instance folder {Path}", Instance.DirectoryPath);
            StatusText = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        var newName = await DialogHelpers.PromptAsync(_contentDialogService, "Переименовать сборку", Instance.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == Instance.Name)
        {
            return;
        }

        try
        {
            Instance = await _instanceLibrary.RenameAsync(Instance, newName.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename instance '{Instance}'", Instance.Name);
            StatusText = $"Не удалось переименовать: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteInstanceAsync()
    {
        if (IsBusy || IsRunning)
        {
            return;
        }

        var confirmed = await DialogHelpers.ConfirmAsync(
            _contentDialogService,
            "Удалить сборку?",
            $"«{Instance.Name}» и все её файлы — сохранения, моды, настройки — будут удалены безвозвратно.");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _instanceLibrary.DeleteAsync(Instance);
            _navigationService.Navigate(typeof(LibraryPage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete instance '{Instance}'", Instance.Name);
            StatusText = $"Не удалось удалить: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (IsBusy || IsRunning)
        {
            return;
        }

        IInstanceLogWriter? logWriter = null;

        try
        {
            IsBusy = true;
            StatusText = "Подготовка...";
            LogsTab.StartNewLiveRun();
            logWriter = _logHistoryService.StartRun(Instance.DirectoryPath);

            var writer = logWriter;
            var outcome = await _instanceLaunchService.LaunchAsync(
                Instance,
                onStatus: s => StatusText = s,
                onOutputLine: (line, isError) => LogsTab.AppendLiveLine(line, isError),
                logWriter: writer,
                progress: new InstallProgress());

            IsRunning = true;
            StatusText = "Игра запущена.";
            Instance = outcome.UpdatedInstance;

            outcome.Process.Exited += (_, _) => Application.Current.Dispatcher.Invoke(() =>
            {
                IsRunning = false;
                writer.Dispose();
                LogsTab.LoadHistoryCommand.ExecuteAsync(null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch instance '{Instance}'", Instance.Name);
            StatusText = $"Ошибка запуска: {ex.Message}";
            logWriter?.Dispose();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
