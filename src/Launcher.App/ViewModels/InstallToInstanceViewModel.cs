using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Launch;
using Launcher.Loaders.Abstractions;
using Launcher.ModSources;
using Launcher.ModSources.Modrinth;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

/// <summary>Backs the "Установка проекта" dialog opened from "Поиск проектов" — lets the user add a mod/
/// resource pack/data pack/shader to one of their existing instances, or spin up a brand-new instance and
/// install straight into it. Modpacks don't use this (they always create their own instance directly).</summary>
public partial class InstallToInstanceViewModel : ObservableObject
{
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly IModInstallService _modInstallService;
    private readonly IInstanceVersionResolver _instanceVersionResolver;
    private readonly IInstancePreparer _instancePreparer;
    private readonly IDownloadCenter _downloadCenter;
    private readonly ILogger<InstallToInstanceViewModel> _logger;

    private ModrinthSearchHit _hit = null!;

    [ObservableProperty]
    private bool _isNewInstanceMode;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _projectTitle = "";

    public ObservableCollection<InstanceInstallOption> Options { get; } = [];

    public ObservableCollection<InstanceInstallOption> FilteredOptions { get; } = [];

    public int CompatibleCount => Options.Count(o => o.IsCompatible);

    public string CompatibleCountLabel =>
        $"{CompatibleCount} {RussianPlural.Of(CompatibleCount, "совместимая сборка", "совместимые сборки", "совместимых сборок")}";

    public CreateInstanceViewModel NewInstance { get; }

    public InstallToInstanceViewModel(
        IInstanceLibrary instanceLibrary,
        IModInstallService modInstallService,
        IInstanceVersionResolver instanceVersionResolver,
        IInstancePreparer instancePreparer,
        IDownloadCenter downloadCenter,
        CreateInstanceViewModel newInstance,
        ILogger<InstallToInstanceViewModel> logger)
    {
        _instanceLibrary = instanceLibrary;
        _modInstallService = modInstallService;
        _instanceVersionResolver = instanceVersionResolver;
        _instancePreparer = instancePreparer;
        _downloadCenter = downloadCenter;
        NewInstance = newInstance;
        _logger = logger;

        // NewInstance is CreateInstanceViewModel, whose CanCreate alone doesn't require a loader —
        // a plain vanilla instance is a valid *create*, but useless as an *install target* for a mod/
        // resource pack here. Re-check our stricter CanExecute whenever anything relevant changes.
        NewInstance.PropertyChanged += (_, _) => CreateAndInstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreateAndInstall =>
        NewInstance.CanCreate && NewInstance.SelectedLoaderType != ModLoaderType.Vanilla;

    public async Task InitializeAsync(ModrinthSearchHit hit)
    {
        _hit = hit;
        ProjectTitle = hit.Title;
        IsNewInstanceMode = false;
        Options.Clear();

        // IInstanceLibrary only loads once something asks it to — if the user opens this dialog before
        // ever visiting Библиотека, Instances would otherwise still be empty.
        await _instanceLibrary.RefreshAsync();

        foreach (var instance in _instanceLibrary.Instances)
        {
            var compatible = hit.ProjectType != "mod" ||
                (instance.LoaderType != "Vanilla" && hit.Categories.Contains(instance.LoaderType.ToLowerInvariant()));
            Options.Add(new InstanceInstallOption(instance) { IsCompatible = compatible });
        }

        ApplyFilter();
        OnPropertyChanged(nameof(CompatibleCount));
        OnPropertyChanged(nameof(CompatibleCountLabel));
        await RefreshInstalledAsync();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredOptions.Clear();
        var query = FilterText.Trim();
        foreach (var option in Options)
        {
            if (query.Length == 0 || option.Instance.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredOptions.Add(option);
            }
        }
    }

    private async Task RefreshInstalledAsync()
    {
        foreach (var option in Options)
        {
            try
            {
                var installed = await _modInstallService.GetInstalledModsAsync(option.Instance.DirectoryPath);
                option.IsInstalled = installed.Any(m => m.ProjectId == _hit.ProjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read installed content for {Instance}", option.Instance.Name);
            }
        }
    }

    [RelayCommand]
    private void SelectExisting() => IsNewInstanceMode = false;

    [RelayCommand]
    private void SelectNew() => IsNewInstanceMode = true;

    [RelayCommand]
    private async Task InstallIntoAsync(InstanceInstallOption option)
    {
        if (option.IsInstalled || option.IsInstalling)
        {
            return;
        }

        try
        {
            option.IsInstalling = true;
            var loader = option.Instance.LoaderType.ToLowerInvariant();
            await _modInstallService.InstallModAsync(
                _hit.ProjectId, _hit.Title, option.Instance.VersionId, loader, option.Instance.DirectoryPath,
                progress: null, _hit.ProjectType);

            option.IsInstalled = true;
            StatusText = $"«{_hit.Title}» добавлен в «{option.Instance.Name}».";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install {ProjectId} into {Instance}", _hit.ProjectId, option.Instance.Name);
            StatusText = $"Не удалось установить в «{option.Instance.Name}»: {ex.Message}";
        }
        finally
        {
            option.IsInstalling = false;
        }
    }

    /// <summary>Creates the instance, then installs vanilla+loader and this project's file into it — all
    /// in the background via the same download toast a plain "Создать сборку" uses, since it can take
    /// minutes. The dialog stays open; closing it doesn't cancel the install.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateAndInstall))]
    private async Task CreateAndInstallAsync()
    {
        var gameVersion = NewInstance.SelectedGameVersion!;
        var loaderType = NewInstance.SelectedLoaderType;
        var loaderVersion = NewInstance.SelectedLoaderVersion;
        var instanceName = string.IsNullOrWhiteSpace(NewInstance.Name) ? gameVersion.Id : NewInstance.Name.Trim();

        var instance = await _instanceLibrary.CreateAsync(instanceName, gameVersion.Id, loaderType.ToString(), loaderVersion?.Version);
        StatusText = $"Сборка «{instance.Name}» создаётся — следите за прогрессом в углу экрана.";
        IsNewInstanceMode = false;

        var download = _downloadCenter.Start(instance.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                var effectiveVersion = await _instanceVersionResolver.ResolveAsync(instance, download.Progress.SetStatus);
                await _instancePreparer.PrepareAsync(effectiveVersion, instance.DirectoryPath, progress: download.Progress);

                var loader = instance.LoaderType.ToLowerInvariant();
                IProgress<string> statusAdapter = new Progress<string>(download.Progress.SetStatus);
                await _modInstallService.InstallModAsync(
                    _hit.ProjectId, _hit.Title, instance.VersionId, loader, instance.DirectoryPath, statusAdapter, _hit.ProjectType);

                Application.Current.Dispatcher.Invoke(() => _downloadCenter.Complete(download));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create instance and install {ProjectId}", _hit.ProjectId);
                Application.Current.Dispatcher.Invoke(
                    () => _downloadCenter.Complete(download, $"Ошибка установки: {ex.Message}"));
            }
        });
    }
}
