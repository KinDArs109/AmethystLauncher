using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

public enum LoaderVersionMode
{
    Stable,
    Latest,
    Other,
}

public partial class CreateInstanceViewModel : ObservableObject
{
    private readonly IVersionManifestService _versionManifestService;
    private readonly IReadOnlyList<ILoaderInstaller> _loaderInstallers;
    private readonly ILogger<CreateInstanceViewModel> _logger;

    private bool _isAutoFillingName;
    private bool _nameManuallyEdited;

    public IReadOnlyList<ModLoaderType> LoaderTypes { get; } =
        [ModLoaderType.Vanilla, ModLoaderType.Fabric, ModLoaderType.Quilt, ModLoaderType.Forge];

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private ModLoaderType _selectedLoaderType = ModLoaderType.Vanilla;

    [ObservableProperty]
    private ObservableCollection<VersionManifestEntry> _gameVersions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private VersionManifestEntry? _selectedGameVersion;

    [ObservableProperty]
    private ObservableCollection<LoaderVersionInfo> _loaderVersions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private LoaderVersionInfo? _selectedLoaderVersion;

    [ObservableProperty]
    private LoaderVersionMode _loaderVersionMode = LoaderVersionMode.Stable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private bool _isLoadingLoaderVersions;

    [ObservableProperty]
    private string _statusText = "";

    public bool CanCreate =>
        !IsLoadingVersions && SelectedGameVersion is not null &&
        (SelectedLoaderType == ModLoaderType.Vanilla || SelectedLoaderVersion is not null);

    public CreateInstanceViewModel(
        IVersionManifestService versionManifestService,
        IEnumerable<ILoaderInstaller> loaderInstallers,
        ILogger<CreateInstanceViewModel> logger)
    {
        _versionManifestService = versionManifestService;
        _loaderInstallers = loaderInstallers.ToList();
        _logger = logger;

        LoadGameVersionsCommand.ExecuteAsync(null);
    }

    partial void OnNameChanged(string value)
    {
        if (!_isAutoFillingName)
        {
            _nameManuallyEdited = true;
        }
    }

    partial void OnSelectedLoaderTypeChanged(ModLoaderType value)
    {
        UpdateNameSuggestion();
        LoadLoaderVersionsCommand.ExecuteAsync(null);
    }

    partial void OnSelectedGameVersionChanged(VersionManifestEntry? value)
    {
        UpdateNameSuggestion();
        LoadLoaderVersionsCommand.ExecuteAsync(null);
    }

    partial void OnLoaderVersionModeChanged(LoaderVersionMode value) => ApplyLoaderVersionMode();

    [RelayCommand]
    private async Task LoadGameVersionsAsync()
    {
        try
        {
            IsLoadingVersions = true;
            var manifest = await _versionManifestService.GetManifestAsync();
            var releases = manifest.Versions.Where(v => v.Type == "release");
            GameVersions = new ObservableCollection<VersionManifestEntry>(releases);
            SelectedGameVersion = GameVersions.FirstOrDefault(v => v.Id == manifest.Latest.Release) ?? GameVersions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load version list for instance creation");
            StatusText = $"Не удалось загрузить список версий: {ex.Message}";
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    private async Task LoadLoaderVersionsAsync()
    {
        LoaderVersions = [];
        SelectedLoaderVersion = null;

        if (SelectedLoaderType == ModLoaderType.Vanilla || SelectedGameVersion is null)
        {
            return;
        }

        var installer = _loaderInstallers.FirstOrDefault(l => l.LoaderType == SelectedLoaderType);
        if (installer is null)
        {
            return;
        }

        try
        {
            IsLoadingLoaderVersions = true;
            var versions = await installer.GetAvailableVersionsAsync(SelectedGameVersion.Id);
            LoaderVersions = new ObservableCollection<LoaderVersionInfo>(versions);
            ApplyLoaderVersionMode();

            if (LoaderVersions.Count == 0)
            {
                StatusText = $"Для {SelectedGameVersion.Id} нет сборок {SelectedLoaderType}.";
            }
            else
            {
                StatusText = "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {LoaderType} versions for {McVersion}", SelectedLoaderType, SelectedGameVersion.Id);
            StatusText = $"Не удалось получить список версий {SelectedLoaderType}: {ex.Message}";
        }
        finally
        {
            IsLoadingLoaderVersions = false;
        }
    }

    private void ApplyLoaderVersionMode()
    {
        if (LoaderVersions.Count == 0)
        {
            return;
        }

        SelectedLoaderVersion = LoaderVersionMode switch
        {
            LoaderVersionMode.Stable => LoaderVersions.FirstOrDefault(v => v.Stable) ?? LoaderVersions.FirstOrDefault(),
            LoaderVersionMode.Latest => LoaderVersions.FirstOrDefault(),
            _ => SelectedLoaderVersion ?? LoaderVersions.FirstOrDefault(),
        };

        UpdateNameSuggestion();
    }

    private void UpdateNameSuggestion()
    {
        if (_nameManuallyEdited || SelectedGameVersion is null)
        {
            return;
        }

        _isAutoFillingName = true;
        Name = SelectedLoaderType == ModLoaderType.Vanilla
            ? SelectedGameVersion.Id
            : $"{SelectedLoaderType} {SelectedGameVersion.Id}";
        _isAutoFillingName = false;
    }
}
