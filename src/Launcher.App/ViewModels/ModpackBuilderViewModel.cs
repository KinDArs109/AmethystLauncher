using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views.Pages;
using Launcher.Core.Instances;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;
using Launcher.ModSources;
using Launcher.ModSources.Modrinth;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

/// <summary>One mod in the builder's draft: which Modrinth project, which exact version, on/off.
/// Built either from a search hit (new mod) or from an already-installed mod (edit mode).</summary>
public partial class BuilderModItem : ObservableObject
{
    public string ProjectId { get; }
    public string Title { get; }
    public string Author { get; }
    public string? IconUrl { get; }

    /// <summary>Version id it was installed with — null for mods newly added in this session.</summary>
    public string? OriginalVersionId { get; }

    public bool WasInstalled { get; }

    public bool OriginalEnabled { get; }

    public BuilderModItem(ModrinthSearchHit hit)
    {
        ProjectId = hit.ProjectId;
        Title = hit.Title;
        Author = hit.Author;
        IconUrl = hit.IconUrl;
        OriginalEnabled = true;
    }

    public BuilderModItem(InstalledMod installed)
    {
        ProjectId = installed.ProjectId;
        Title = installed.Title;
        Author = "";
        IconUrl = null;
        OriginalVersionId = installed.VersionId;
        WasInstalled = true;
        OriginalEnabled = installed.IsEnabled;
        _isEnabled = installed.IsEnabled;
    }

    public ObservableCollection<ModrinthVersion> AvailableVersions { get; } = [];

    [ObservableProperty]
    private ModrinthVersion? _selectedVersion;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isLoadingVersions;

    /// <summary>Set when the current game version/loader combo has no build of this mod.</summary>
    [ObservableProperty]
    private bool _isIncompatible;
}

/// <summary>The "Конструктор сборок" page. Two modes: create a brand-new instance, or edit an
/// existing one — same draft list either way (exact per-mod versions, on/off, add/remove).
/// Mod configs (files in /config) are edited on the instance's Файлы tab after the first run.</summary>
public partial class ModpackBuilderViewModel : ObservableObject
{
    private readonly IVersionManifestService _versionManifestService;
    private readonly IReadOnlyList<ILoaderInstaller> _loaderInstallers;
    private readonly IModrinthClient _modrinthClient;
    private readonly IModInstallService _modInstallService;
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly IInstanceVersionResolver _instanceVersionResolver;
    private readonly Launcher.Core.Launch.IInstancePreparer _instancePreparer;
    private readonly IDownloadCenter _downloadCenter;
    private readonly ISelectedInstanceContext _selectedInstanceContext;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ModpackBuilderViewModel> _logger;

    public IReadOnlyList<ModLoaderType> LoaderTypes { get; } =
        [ModLoaderType.Fabric, ModLoaderType.Quilt, ModLoaderType.Forge];

    // ---- Mode ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool _isEditMode;

    /// <summary>Instances the builder can edit — everything except Vanilla (no loader, no mods).</summary>
    public ObservableCollection<LauncherInstance> EditableInstances { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private LauncherInstance? _selectedEditInstance;

    public string SaveButtonText => IsEditMode ? "Сохранить изменения" : "Создать сборку";

    // ---- Create-mode parameters ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = "";

    [ObservableProperty]
    private ObservableCollection<VersionManifestEntry> _gameVersions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private VersionManifestEntry? _selectedGameVersion;

    [ObservableProperty]
    private ModLoaderType _selectedLoaderType = ModLoaderType.Fabric;

    // ---- Search (Modrinth) ----

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isSearching;

    public ObservableCollection<ModrinthSearchHit> SearchResults { get; } = [];

    // ---- Draft ----

    /// <summary>Local filter over the draft list — mirrors the Content tab's "поиск среди установленного".</summary>
    [ObservableProperty]
    private string _draftFilterText = "";

    public ObservableCollection<BuilderModItem> DraftMods { get; } = [];

    /// <summary>What the list actually shows: <see cref="DraftMods"/> narrowed by <see cref="DraftFilterText"/>.</summary>
    public ObservableCollection<BuilderModItem> FilteredDraftMods { get; } = [];

    private readonly List<string> _originalProjectIds = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>The instance created/edited by the last save — enables "Открыть сборку".</summary>
    [ObservableProperty]
    private LauncherInstance? _lastTouchedInstance;

    public bool CanSave => !IsBusy && (IsEditMode
        ? SelectedEditInstance is not null
        : SelectedGameVersion is not null && !string.IsNullOrWhiteSpace(Name));

    public ModpackBuilderViewModel(
        IVersionManifestService versionManifestService,
        IEnumerable<ILoaderInstaller> loaderInstallers,
        IModrinthClient modrinthClient,
        IModInstallService modInstallService,
        IInstanceLibrary instanceLibrary,
        IInstanceVersionResolver instanceVersionResolver,
        Launcher.Core.Launch.IInstancePreparer instancePreparer,
        IDownloadCenter downloadCenter,
        ISelectedInstanceContext selectedInstanceContext,
        INavigationService navigationService,
        ILogger<ModpackBuilderViewModel> logger)
    {
        _versionManifestService = versionManifestService;
        _loaderInstallers = loaderInstallers.ToList();
        _modrinthClient = modrinthClient;
        _modInstallService = modInstallService;
        _instanceLibrary = instanceLibrary;
        _instanceVersionResolver = instanceVersionResolver;
        _instancePreparer = instancePreparer;
        _downloadCenter = downloadCenter;
        _selectedInstanceContext = selectedInstanceContext;
        _navigationService = navigationService;
        _logger = logger;

        LoadGameVersionsCommand.ExecuteAsync(null);
        _ = LoadEditableInstancesAsync();
    }

    private async Task LoadEditableInstancesAsync()
    {
        try
        {
            await _instanceLibrary.RefreshAsync();
            EditableInstances.Clear();
            foreach (var instance in _instanceLibrary.Instances.Where(i => i.LoaderType != "Vanilla"))
            {
                EditableInstances.Add(instance);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load instances for the builder");
        }
    }

    [RelayCommand]
    private void SetCreateMode()
    {
        IsEditMode = false;
        SelectedEditInstance = null;
        ResetDraft();
    }

    [RelayCommand]
    private void SetEditMode() => IsEditMode = true;

    partial void OnSelectedEditInstanceChanged(LauncherInstance? value)
    {
        if (value is not null)
        {
            _ = LoadDraftFromInstanceAsync(value);
        }
    }

    private void ResetDraft()
    {
        DraftMods.Clear();
        _originalProjectIds.Clear();
        RefreshDraftView();
        StatusText = "";
        LastTouchedInstance = null;
    }

    private async Task LoadDraftFromInstanceAsync(LauncherInstance instance)
    {
        try
        {
            IsBusy = true;
            ResetDraft();

            var installed = await _modInstallService.GetInstalledModsAsync(instance.DirectoryPath);
            foreach (var mod in installed.Where(m => m.ProjectType == "mod"))
            {
                var item = new BuilderModItem(mod);
                DraftMods.Add(item);
                _originalProjectIds.Add(mod.ProjectId);
            }

            RefreshDraftView();
            StatusText = DraftMods.Count == 0 ? "В этой сборке пока нет модов — добавь через «Найти проекты»." : "";

            // Version lists load lazily but sequentially — Modrinth rate-limits aggressive parallelism.
            foreach (var item in DraftMods.ToList())
            {
                await LoadVersionsForAsync(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load installed mods for editing {Instance}", instance.Name);
            StatusText = $"Не удалось прочитать моды сборки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadGameVersionsAsync()
    {
        try
        {
            var manifest = await _versionManifestService.GetManifestAsync();
            GameVersions = new ObservableCollection<VersionManifestEntry>(manifest.Versions.Where(v => v.Type == "release"));
            SelectedGameVersion = GameVersions.FirstOrDefault(v => v.Id == manifest.Latest.Release) ?? GameVersions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load version manifest for the builder");
            StatusText = $"Не удалось загрузить список версий: {ex.Message}";
        }
    }

    partial void OnSelectedGameVersionChanged(VersionManifestEntry? value) => _ = ReloadDraftVersionsAsync();

    partial void OnSelectedLoaderTypeChanged(ModLoaderType value) => _ = ReloadDraftVersionsAsync();

    partial void OnDraftFilterTextChanged(string value) => RefreshDraftView();

    private void RefreshDraftView()
    {
        var filter = DraftFilterText.Trim();
        FilteredDraftMods.Clear();
        foreach (var item in DraftMods)
        {
            if (filter.Length == 0 || item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredDraftMods.Add(item);
            }
        }
    }

    /// <summary>Game version + loader the draft is being built against (mode-dependent).</summary>
    private (string? GameVersion, string LoaderFacet) CurrentTarget => IsEditMode && SelectedEditInstance is not null
        ? (SelectedEditInstance.VersionId, SelectedEditInstance.LoaderType.ToLowerInvariant())
        : (SelectedGameVersion?.Id, SelectedLoaderType.ToString().ToLowerInvariant());

    [RelayCommand]
    private void ToggleSearch() => IsSearchOpen = !IsSearchOpen;

    private bool CanSearch => !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        try
        {
            IsSearching = true;
            var (gameVersion, loaderFacet) = CurrentTarget;
            var result = await _modrinthClient.SearchProjectsAsync(
                SearchText.Trim(), "mod", gameVersion, loaderFacet, sortIndex: "relevance", limit: 12);

            SearchResults.Clear();
            foreach (var hit in result.Hits)
            {
                SearchResults.Add(hit);
            }

            StatusText = SearchResults.Count == 0 ? "Ничего не найдено под выбранную версию и загрузчик." : "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Builder mod search failed");
            StatusText = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task AddModAsync(ModrinthSearchHit hit)
    {
        if (DraftMods.Any(m => m.ProjectId == hit.ProjectId))
        {
            StatusText = $"«{hit.Title}» уже в сборке.";
            return;
        }

        var item = new BuilderModItem(hit);
        DraftMods.Add(item);
        RefreshDraftView();
        await LoadVersionsForAsync(item);
    }

    [RelayCommand]
    private void RemoveMod(BuilderModItem item)
    {
        DraftMods.Remove(item);
        RefreshDraftView();
    }

    private async Task LoadVersionsForAsync(BuilderModItem item)
    {
        try
        {
            item.IsLoadingVersions = true;
            var (gameVersion, loaderFacet) = CurrentTarget;
            var versions = await _modrinthClient.GetProjectVersionsAsync(item.ProjectId, gameVersion, loaderFacet);

            item.AvailableVersions.Clear();
            foreach (var version in versions.Take(20))
            {
                item.AvailableVersions.Add(version);
            }

            // Installed mods start pinned to the version actually on disk; new mods take the latest.
            item.SelectedVersion =
                item.AvailableVersions.FirstOrDefault(v => v.Id == item.OriginalVersionId)
                ?? item.AvailableVersions.FirstOrDefault();
            item.IsIncompatible = item.AvailableVersions.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load versions for {Project}", item.Title);
            item.IsIncompatible = true;
        }
        finally
        {
            item.IsLoadingVersions = false;
        }
    }

    /// <summary>Game version / loader changed — every draft mod's version list is now stale.</summary>
    private async Task ReloadDraftVersionsAsync()
    {
        foreach (var item in DraftMods.ToList())
        {
            await LoadVersionsForAsync(item);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsEditMode)
        {
            await ApplyChangesAsync();
        }
        else
        {
            await CreateAsync();
        }
    }

    private async Task CreateAsync()
    {
        if (SelectedGameVersion is null)
        {
            return;
        }

        var incompatible = DraftMods.Where(m => m.IsIncompatible).Select(m => m.Title).ToList();
        if (incompatible.Count > 0)
        {
            StatusText = $"Убери несовместимые моды или смени версию: {string.Join(", ", incompatible)}.";
            return;
        }

        try
        {
            IsBusy = true;
            LastTouchedInstance = null;
            StatusText = "Подбираю версию загрузчика...";

            var installer = _loaderInstallers.FirstOrDefault(l => l.LoaderType == SelectedLoaderType)
                ?? throw new InvalidOperationException($"Загрузчик {SelectedLoaderType} недоступен.");
            var loaderVersions = await installer.GetAvailableVersionsAsync(SelectedGameVersion.Id);
            var loaderVersion = (loaderVersions.FirstOrDefault(v => v.Stable) ?? loaderVersions.FirstOrDefault())
                ?? throw new InvalidOperationException($"Для {SelectedGameVersion.Id} нет сборок {SelectedLoaderType}.");

            var instance = await _instanceLibrary.CreateAsync(
                Name.Trim(), SelectedGameVersion.Id, SelectedLoaderType.ToString(), loaderVersion.Version);

            var gameVersionId = SelectedGameVersion.Id;
            var loaderFacet = SelectedLoaderType.ToString().ToLowerInvariant();
            var mods = DraftMods.Select(m => (m.ProjectId, m.Title, VersionId: m.SelectedVersion?.Id, m.IsEnabled)).ToList();
            var download = _downloadCenter.Start(instance.Name);

            _ = Task.Run(async () =>
            {
                try
                {
                    var effectiveVersion = await _instanceVersionResolver.ResolveAsync(instance, download.Progress.SetStatus);
                    await _instancePreparer.PrepareAsync(effectiveVersion, instance.DirectoryPath, progress: download.Progress);

                    foreach (var mod in mods)
                    {
                        download.Progress.SetStatus($"Установка «{mod.Title}»...");
                        var installed = await _modInstallService.InstallModAsync(
                            mod.ProjectId, mod.Title, gameVersionId, loaderFacet, instance.DirectoryPath,
                            projectType: "mod", versionId: mod.VersionId);

                        if (!mod.IsEnabled)
                        {
                            await _modInstallService.SetEnabledAsync(instance.DirectoryPath, installed.ProjectId, enabled: false);
                        }
                    }

                    _downloadCenter.Complete(download);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Builder failed to prepare instance '{Instance}'", instance.Name);
                    _downloadCenter.Complete(download, $"Ошибка сборки: {ex.Message}");
                }
            });

            LastTouchedInstance = instance;
            StatusText = "Сборка создаётся — прогресс в правом нижнем углу. Конфиги модов появятся после первого запуска (вкладка «Файлы»).";
            await LoadEditableInstancesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Builder failed to create instance");
            StatusText = $"Не удалось создать сборку: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Edit mode: applies the draft's diff to the selected instance — uninstall removed mods,
    /// install added ones, re-install version changes, flip enabled toggles.</summary>
    private async Task ApplyChangesAsync()
    {
        var instance = SelectedEditInstance;
        if (instance is null)
        {
            return;
        }

        var incompatible = DraftMods.Where(m => m.IsIncompatible && !m.WasInstalled).Select(m => m.Title).ToList();
        if (incompatible.Count > 0)
        {
            StatusText = $"Убери несовместимые моды: {string.Join(", ", incompatible)}.";
            return;
        }

        var gameVersionId = instance.VersionId;
        var loaderFacet = instance.LoaderType.ToLowerInvariant();
        var removedIds = _originalProjectIds.Except(DraftMods.Select(m => m.ProjectId)).ToList();
        var items = DraftMods
            .Select(m => (m.ProjectId, m.Title, m.WasInstalled, m.OriginalVersionId, m.OriginalEnabled,
                          TargetVersionId: m.SelectedVersion?.Id, m.IsEnabled))
            .ToList();

        var download = _downloadCenter.Start(instance.Name);
        StatusText = "Применяю изменения — прогресс в правом нижнем углу.";
        LastTouchedInstance = instance;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var projectId in removedIds)
                {
                    download.Progress.SetStatus("Удаление модов...");
                    await _modInstallService.UninstallModAsync(instance.DirectoryPath, projectId);
                }

                foreach (var item in items)
                {
                    if (!item.WasInstalled)
                    {
                        download.Progress.SetStatus($"Установка «{item.Title}»...");
                        var installed = await _modInstallService.InstallModAsync(
                            item.ProjectId, item.Title, gameVersionId, loaderFacet, instance.DirectoryPath,
                            projectType: "mod", versionId: item.TargetVersionId);
                        if (!item.IsEnabled)
                        {
                            await _modInstallService.SetEnabledAsync(instance.DirectoryPath, installed.ProjectId, enabled: false);
                        }

                        continue;
                    }

                    if (item.TargetVersionId is not null && item.TargetVersionId != item.OriginalVersionId)
                    {
                        download.Progress.SetStatus($"Смена версии «{item.Title}»...");
                        await _modInstallService.UninstallModAsync(instance.DirectoryPath, item.ProjectId);
                        await _modInstallService.InstallModAsync(
                            item.ProjectId, item.Title, gameVersionId, loaderFacet, instance.DirectoryPath,
                            projectType: "mod", versionId: item.TargetVersionId);
                        if (!item.IsEnabled)
                        {
                            await _modInstallService.SetEnabledAsync(instance.DirectoryPath, item.ProjectId, enabled: false);
                        }
                    }
                    else if (item.IsEnabled != item.OriginalEnabled)
                    {
                        await _modInstallService.SetEnabledAsync(instance.DirectoryPath, item.ProjectId, item.IsEnabled);
                    }
                }

                _downloadCenter.Complete(download);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Builder failed to apply changes to '{Instance}'", instance.Name);
                _downloadCenter.Complete(download, $"Ошибка изменения сборки: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void OpenTouchedInstance()
    {
        if (LastTouchedInstance is null)
        {
            return;
        }

        _selectedInstanceContext.Current = LastTouchedInstance;
        _navigationService.Navigate(typeof(InstanceDetailPage));
    }
}
