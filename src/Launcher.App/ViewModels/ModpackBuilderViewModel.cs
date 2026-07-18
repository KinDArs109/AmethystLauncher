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

/// <summary>The four "orbital" stages of the redesigned builder — each is a node the user clicks to
/// reveal its panel. They flow left-to-right conceptually: pick/create a build, add mods, tweak configs,
/// then test it.</summary>
public enum BuilderNode
{
    Create,
    Mods,
    Configs,
    Test,
}

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
    private readonly IInstanceLaunchService _instanceLaunchService;
    private readonly ILogger<ModpackBuilderViewModel> _logger;

    // ---- Orbital node navigation (redesigned page) ----

    /// <summary>Which orbital node's panel is showing. Starts on "Создать сборку".</summary>
    [ObservableProperty]
    private BuilderNode _activeNode = BuilderNode.Create;

    /// <summary>The build the Mods/Configs/Play nodes operate on: the one just created, or the existing
    /// one picked by clicking the orbital's centre.</summary>
    public LauncherInstance? WorkingInstance => IsEditMode ? SelectedEditInstance : LastTouchedInstance;

    public bool HasWorkingInstance => WorkingInstance is not null;

    /// <summary>The centre-of-orbit build picker (list of existing builds) is open.</summary>
    [ObservableProperty]
    private bool _isBuildPickerOpen;

    /// <summary>Whether the action panel is slid out. Starts closed so the orbit sits centred; clicking a
    /// node opens it.</summary>
    [ObservableProperty]
    private bool _isPanelOpen;

    /// <summary>A build passed in from a library click, applied once the instance list has loaded.</summary>
    private string? _pendingBuilderPath;

    [RelayCommand]
    private void SelectNode(BuilderNode node)
    {
        ActiveNode = node;
        IsPanelOpen = true;
    }

    [RelayCommand]
    private void ClosePanel() => IsPanelOpen = false;

    partial void OnActiveNodeChanged(BuilderNode value)
    {
        // "Создать" is strictly for new builds — entering it drops any existing-build selection so the
        // form starts blank. Existing builds are chosen by clicking the orbit's centre instead.
        if (value == BuilderNode.Create)
        {
            SetCreateMode();
        }
        // Entering "Моды" surfaces mods right away (popular ones for the current version/loader) so the
        // list isn't empty until the user types.
        else if (value == BuilderNode.Mods && SearchResults.Count == 0)
        {
            _ = RunSearchAsync();
        }
    }

    [RelayCommand]
    private void ToggleBuildPicker() => IsBuildPickerOpen = !IsBuildPickerOpen;

    /// <summary>Picks an existing build to work on (from the centre picker) — switches to edit mode and
    /// jumps to the Моды node so the user can start changing it.</summary>
    [RelayCommand]
    private void PickBuild(LauncherInstance instance)
    {
        IsBuildPickerOpen = false;
        IsEditMode = true;
        SelectedEditInstance = instance;
        ActiveNode = BuilderNode.Mods;
        IsPanelOpen = true;
    }

    public IReadOnlyList<ModLoaderType> LoaderTypes { get; } =
        [ModLoaderType.Fabric, ModLoaderType.Quilt, ModLoaderType.Forge, ModLoaderType.NeoForge];

    // ---- Mode ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkingInstance))]
    [NotifyPropertyChangedFor(nameof(HasWorkingInstance))]
    private bool _isEditMode;

    /// <summary>Instances the builder can edit — everything except Vanilla (no loader, no mods).</summary>
    public ObservableCollection<LauncherInstance> EditableInstances { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(WorkingInstance))]
    [NotifyPropertyChangedFor(nameof(HasWorkingInstance))]
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
    private string _searchText = "";

    [ObservableProperty]
    private bool _isSearching;

    public ObservableCollection<ModrinthSearchHit> SearchResults { get; } = [];

    /// <summary>Restarted on every keystroke so the live search debounces instead of firing per character.</summary>
    private CancellationTokenSource? _searchDebounceCts;

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
    [NotifyPropertyChangedFor(nameof(WorkingInstance))]
    [NotifyPropertyChangedFor(nameof(HasWorkingInstance))]
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
        IInstanceLaunchService instanceLaunchService,
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
        _instanceLaunchService = instanceLaunchService;
        _logger = logger;

        // A library click hands off the build to open here (consumed once); applied after the instance
        // list loads so we can select the matching entry.
        if (_selectedInstanceContext.BuilderInstance is { } handoff)
        {
            _pendingBuilderPath = handoff.DirectoryPath;
            _selectedInstanceContext.BuilderInstance = null;
        }

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

            // Honour a build handed off from a library click: open it pre-selected for editing.
            if (_pendingBuilderPath is not null)
            {
                var match = EditableInstances.FirstOrDefault(i => i.DirectoryPath == _pendingBuilderPath);
                _pendingBuilderPath = null;
                if (match is not null)
                {
                    IsEditMode = true;
                    SelectedEditInstance = match;
                    ActiveNode = BuilderNode.Mods;
                    IsPanelOpen = true;
                }
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
    private void ToggleSearch()
    {
        IsSearchOpen = !IsSearchOpen;
        // Opening the panel immediately shows popular mods for the current version/loader (like the
        // "Поиск проектов" tab) — no need to type first.
        if (IsSearchOpen && SearchResults.Count == 0)
        {
            _ = RunSearchAsync();
        }
    }

    partial void OnSearchTextChanged(string value) => DebounceSearch();

    /// <summary>Live search: query Modrinth ~350 ms after the user stops typing.</summary>
    private void DebounceSearch()
    {
        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token);
                await App.Current.Dispatcher.InvokeAsync(async () => await RunSearchAsync(cts.Token));
            }
            catch (TaskCanceledException)
            {
                // superseded by a newer keystroke
            }
        });
    }

    [RelayCommand]
    private Task SearchAsync() => RunSearchAsync();

    private async Task RunSearchAsync(CancellationToken ct = default)
    {
        try
        {
            IsSearching = true;
            var query = SearchText.Trim();
            var (gameVersion, loaderFacet) = CurrentTarget;
            // Empty query = browse the most popular mods; a typed query ranks by relevance.
            var sort = query.Length == 0 ? "downloads" : "relevance";
            var result = await _modrinthClient.SearchProjectsAsync(
                query, "mod", gameVersion, loaderFacet, sortIndex: sort, limit: 20);

            if (ct.IsCancellationRequested)
            {
                return;
            }

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

                    var failed = await InstallModsParallelAsync(mods, gameVersionId, loaderFacet, instance.DirectoryPath, download.Progress.SetStatus);

                    _downloadCenter.Complete(download, failed.Count == 0
                        ? null
                        : $"Готово, но не установились: {string.Join(", ", failed)}");
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

    /// <summary>Installs a batch of mods concurrently (up to 4 at once) instead of one-by-one, so a big
    /// draft finishes far faster. Each mod is independent: a single failure is collected and reported at
    /// the end rather than aborting the whole batch. Returns the titles that failed.</summary>
    private async Task<IReadOnlyList<string>> InstallModsParallelAsync(
        IReadOnlyList<(string ProjectId, string Title, string? VersionId, bool IsEnabled)> mods,
        string gameVersionId,
        string loaderFacet,
        string instanceDir,
        Action<string> setStatus)
    {
        using var gate = new SemaphoreSlim(4);
        var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
        var done = 0;

        var tasks = mods.Select(async mod =>
        {
            await gate.WaitAsync();
            try
            {
                setStatus($"Установка модов ({Interlocked.Increment(ref done)}/{mods.Count})...");
                var installed = await _modInstallService.InstallModAsync(
                    mod.ProjectId, mod.Title, gameVersionId, loaderFacet, instanceDir,
                    projectType: "mod", versionId: mod.VersionId);

                if (!mod.IsEnabled)
                {
                    await _modInstallService.SetEnabledAsync(instanceDir, installed.ProjectId, enabled: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Builder failed to install mod {Mod}", mod.Title);
                failed.Add(mod.Title);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return failed.ToArray();
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

                // Mods that need a (re)install — brand-new ones and version changes — go through the
                // parallel installer. A version change is uninstall-then-install, so do the uninstall
                // first (cheap, sequential) and let the install run in the concurrent batch.
                var toInstall = new List<(string ProjectId, string Title, string? VersionId, bool IsEnabled)>();
                foreach (var item in items)
                {
                    if (!item.WasInstalled)
                    {
                        toInstall.Add((item.ProjectId, item.Title, item.TargetVersionId, item.IsEnabled));
                    }
                    else if (item.TargetVersionId is not null && item.TargetVersionId != item.OriginalVersionId)
                    {
                        await _modInstallService.UninstallModAsync(instance.DirectoryPath, item.ProjectId);
                        toInstall.Add((item.ProjectId, item.Title, item.TargetVersionId, item.IsEnabled));
                    }
                    else if (item.IsEnabled != item.OriginalEnabled)
                    {
                        await _modInstallService.SetEnabledAsync(instance.DirectoryPath, item.ProjectId, item.IsEnabled);
                    }
                }

                var failed = await InstallModsParallelAsync(
                    toInstall, gameVersionId, loaderFacet, instance.DirectoryPath, download.Progress.SetStatus);

                _downloadCenter.Complete(download, failed.Count == 0
                    ? null
                    : $"Готово, но не установились: {string.Join(", ", failed)}");
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

    /// <summary>"Изменение конфигов" node — opens the working build straight on its Файлы tab, where the
    /// /config folder lives. Configs only exist after the first launch, so we hint that if empty.</summary>
    [RelayCommand]
    private void OpenConfigs()
    {
        if (WorkingInstance is null)
        {
            StatusText = "Сначала создай или выбери сборку в узле «Создать сборку».";
            ActiveNode = BuilderNode.Create;
            return;
        }

        _selectedInstanceContext.Current = WorkingInstance;
        _selectedInstanceContext.InitialTab = InstanceDetailTab.Files;
        _navigationService.Navigate(typeof(InstanceDetailPage));
    }

    /// <summary>"Тестирование сборки" node — launches the working build so the user can verify it in game.</summary>
    [RelayCommand]
    private async Task LaunchTestAsync()
    {
        if (WorkingInstance is null)
        {
            StatusText = "Сначала создай или выбери сборку в узле «Создать сборку».";
            ActiveNode = BuilderNode.Create;
            return;
        }

        try
        {
            StatusText = "Запуск сборки для теста...";
            await _instanceLaunchService.LaunchAsync(WorkingInstance, onStatus: s => StatusText = s);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Builder failed to launch test for '{Instance}'", WorkingInstance.Name);
            StatusText = $"Не удалось запустить сборку: {ex.Message}";
        }
    }
}
