using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views;
using Launcher.Core.Instances;
using Launcher.ModSources;
using Launcher.ModSources.Modrinth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public sealed record ProjectTypeOption(string Key, string Label, bool Enabled);

public sealed record SortOption(string Key, string Label);

/// <summary>Backs the "Поиск проектов" page — a general Modrinth browser (mods, resource packs, data
/// packs, shaders, modpacks) modelled after the category tabs on modrinth.com. Browsing itself has no
/// instance context (matching modrinth.com, which isn't scoped to any one install either); picking a
/// target happens per-item, in the "Установка проекта" dialog (<see cref="InstallToInstanceViewModel"/>).
/// Modpacks are the exception — installing one always creates its own new instance directly. Servers
/// aren't real downloadable content — Modrinth doesn't expose a public server-directory API — so that tab
/// is shown but disabled.</summary>
public partial class ModsViewModel : ObservableObject
{
    // Live search: wait this long after the user stops typing before firing a request, so fast
    // typists don't queue up a request per keystroke against a slow/distant API.
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(10);
    private const int PageSize = 20;

    private readonly IModInstallService _modInstallService;
    private readonly IModpackInstaller _modpackInstaller;
    private readonly IDownloadCenter _downloadCenter;
    private readonly IContentDialogService _contentDialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModsViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScopedInstance))]
    private LauncherInstance? _scopedInstance;

    public bool HasScopedInstance => ScopedInstance is not null;

    // Caches completed searches for this session so retyping/backspacing/paging back is instant.
    private readonly Dictionary<string, ModrinthSearchResult> _searchCache = [];

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _searchCts;

    public IReadOnlyList<ProjectTypeOption> ProjectTypes { get; } =
    [
        new("", "Всё", true),
        new("mod", "Моды", true),
        new("resourcepack", "Наборы ресурсов", true),
        new("shader", "Шейдеры", true),
        new("datapack", "Наборы данных", true),
        new("modpack", "Сборки", true),
        new("server", "Серверы", false),
    ];

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new("relevance", "Актуальность"),
        new("downloads", "Загрузки"),
        new("follows", "Лайки"),
        new("newest", "Новизна"),
        new("updated", "Обновление"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServersTab))]
    [NotifyPropertyChangedFor(nameof(IsModpacksTab))]
    private ProjectTypeOption _selectedProjectType;

    [ObservableProperty]
    private SortOption _selectedSortOption;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _totalHits;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _currentPage = 1;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalHits / (double)PageSize));

    public bool IsServersTab => SelectedProjectType.Key == "server";

    public bool IsModpacksTab => SelectedProjectType.Key == "modpack";

    public ObservableCollection<ModSearchItem> SearchResults { get; } = [];

    public ModsViewModel(
        IModInstallService modInstallService,
        IModpackInstaller modpackInstaller,
        IDownloadCenter downloadCenter,
        IContentDialogService contentDialogService,
        IServiceProvider serviceProvider,
        ISelectedInstanceContext selectedInstanceContext,
        ILogger<ModsViewModel> logger)
    {
        _modInstallService = modInstallService;
        _modpackInstaller = modpackInstaller;
        _downloadCenter = downloadCenter;
        _contentDialogService = contentDialogService;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _selectedProjectType = ProjectTypes[1]; // "Mods" — matches this page's previous, narrower scope
        _selectedSortOption = SortOptions[0];

        // Consumed once — "Найти проекты" on an instance's Content tab sets this right before
        // navigating here; clearing it means a later, unrelated visit to this page isn't still scoped.
        if (selectedInstanceContext.ProjectSearchScope is { } scoped)
        {
            selectedInstanceContext.ProjectSearchScope = null;
            ScopedInstance = scoped;
        }

        RestartSearch();
    }

    [RelayCommand]
    private void ClearScope()
    {
        ScopedInstance = null;
        CurrentPage = 1;
        RestartSearch();
    }

    partial void OnSelectedProjectTypeChanged(ProjectTypeOption value)
    {
        CurrentPage = 1;
        RestartSearch();
    }

    partial void OnSelectedSortOptionChanged(SortOption value)
    {
        CurrentPage = 1;
        RestartSearch();
    }

    partial void OnSearchTextChanged(string value)
    {
        CurrentPage = 1;
        RestartSearch();
    }

    private void RestartSearch()
    {
        _debounceCts?.Cancel();
        _searchCts?.Cancel();

        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        DebounceThenSearchAsync(cts.Token);
    }

    private async void DebounceThenSearchAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, debounceToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!debounceToken.IsCancellationRequested)
        {
            await PerformSearchAsync();
        }
    }

    [RelayCommand]
    private void SelectProjectType(ProjectTypeOption option)
    {
        if (option.Enabled)
        {
            SelectedProjectType = option;
        }
    }

    [RelayCommand]
    private void SelectSort(SortOption option) => SelectedSortOption = option;

    private bool CanGoNext => CurrentPage < TotalPages;

    private bool CanGoPrevious => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage()
    {
        CurrentPage++;
        RestartSearch();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        CurrentPage--;
        RestartSearch();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _debounceCts?.Cancel();
        await PerformSearchAsync();
    }

    private async Task PerformSearchAsync()
    {
        var projectType = SelectedProjectType;

        if (projectType.Key == "server")
        {
            SearchResults.Clear();
            TotalHits = 0;
            StatusText = "Список серверов пока недоступен — раздел в разработке.";
            return;
        }

        var query = SearchText.Trim();
        var offset = (CurrentPage - 1) * PageSize;
        var scopeKey = ScopedInstance is null ? "" : $"{ScopedInstance.VersionId}|{ScopedInstance.LoaderType}";
        var cacheKey = $"{projectType.Key}|{SelectedSortOption.Key}|{query.ToLowerInvariant()}|{offset}|{scopeKey}";

        // A newer search (later keystroke, filter change, or manual trigger) always wins.
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource(SearchTimeout);
        _searchCts = cts;

        if (_searchCache.TryGetValue(cacheKey, out var cachedResult))
        {
            await ApplyResultsAsync(cachedResult);
            return;
        }

        try
        {
            IsSearching = true;
            StatusText = "Поиск...";

            // An empty query means "browse" rather than "search" — sort by downloads by default so it
            // reads as a popular list instead of Modrinth's mostly-arbitrary relevance order.
            var sortIndex = string.IsNullOrWhiteSpace(query) && SelectedSortOption.Key == "relevance"
                ? "downloads"
                : SelectedSortOption.Key;

            // Scoped from "Найти проекты" on an instance — narrow results to what that instance can
            // actually use, same as the loader/version facets Modrinth's own site applies.
            var scopedGameVersion = ScopedInstance?.VersionId;
            var scopedLoader = projectType.Key == "mod" && ScopedInstance is { LoaderType: not "Vanilla" } scoped
                ? scoped.LoaderType.ToLowerInvariant()
                : null;

            var result = await _modInstallService.SearchProjectsAsync(
                query, projectType.Key, scopedGameVersion, scopedLoader, sortIndex, offset, ct: cts.Token);

            _searchCache[cacheKey] = result;
            await ApplyResultsAsync(result);
        }
        catch (OperationCanceledException)
        {
            // Only report a timeout if nothing superseded us — a supersede already replaced _searchCts.
            if (ReferenceEquals(_searchCts, cts))
            {
                StatusText = "Modrinth не ответил вовремя — попробуйте ещё раз.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modrinth search failed for '{Query}'", query);
            StatusText = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
            }
        }
    }

    private async Task ApplyResultsAsync(ModrinthSearchResult result)
    {
        SearchResults.Clear();
        foreach (var hit in result.Hits)
        {
            SearchResults.Add(new ModSearchItem(hit));
        }

        if (ScopedInstance is { } scoped)
        {
            await RefreshScopedInstalledFlagsAsync(scoped);
        }

        TotalHits = result.TotalHits;
        StatusText = result.Hits.Count == 0 ? "Ничего не найдено." : "";
    }

    private async Task RefreshScopedInstalledFlagsAsync(LauncherInstance scoped)
    {
        try
        {
            var installed = await _modInstallService.GetInstalledModsAsync(scoped.DirectoryPath);
            var installedIds = installed.Select(m => m.ProjectId).ToHashSet();
            foreach (var item in SearchResults)
            {
                item.IsInstalled = installedIds.Contains(item.Hit.ProjectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read installed content for scoped instance {Instance}", scoped.Name);
        }
    }

    [RelayCommand]
    private async Task InstallAsync(ModSearchItem item)
    {
        if (item.Hit.ProjectType == "modpack")
        {
            if (item.IsInstalled || item.IsInstalling)
            {
                return;
            }

            InstallModpack(item);
            return;
        }

        // Scoped from "Найти проекты" on an instance's Content tab — install straight into it instead
        // of making the user pick it again from the dialog they just came from.
        if (ScopedInstance is { } scoped)
        {
            await InstallIntoScopedInstanceAsync(item, scoped);
            return;
        }

        var dialogViewModel = _serviceProvider.GetRequiredService<InstallToInstanceViewModel>();
        await dialogViewModel.InitializeAsync(item.Hit);
        var dialog = new InstallToInstanceDialog(dialogViewModel);
        await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
    }

    private async Task InstallIntoScopedInstanceAsync(ModSearchItem item, LauncherInstance instance)
    {
        if (item.IsInstalled || item.IsInstalling)
        {
            return;
        }

        try
        {
            item.IsInstalling = true;
            var loader = instance.LoaderType.ToLowerInvariant();
            await _modInstallService.InstallModAsync(
                item.Hit.ProjectId, item.Hit.Title, instance.VersionId, loader, instance.DirectoryPath,
                progress: null, item.Hit.ProjectType);

            item.IsInstalled = true;
            StatusText = $"«{item.Hit.Title}» добавлен в «{instance.Name}».";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install {ProjectId} into scoped instance {Instance}", item.Hit.ProjectId, instance.Name);
            StatusText = $"Не удалось установить: {ex.Message}";
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>Runs in the background and reports through the same download-toast used when creating an
    /// instance from scratch — a modpack install (vanilla + loader + every mod file) can take minutes,
    /// far longer than the inline spinner the other project types use.</summary>
    private void InstallModpack(ModSearchItem item)
    {
        item.IsInstalling = true;
        var download = _downloadCenter.Start(item.Hit.Title);

        _ = Task.Run(async () =>
        {
            try
            {
                var instance = await _modpackInstaller.InstallAsync(
                    item.Hit.ProjectId, item.Hit.Title, item.Hit.Title, download.Progress);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _downloadCenter.Complete(download);
                    item.IsInstalled = true;
                    item.IsInstalling = false;
                    StatusText = $"«{item.Hit.Title}» установлен как новая сборка «{instance.Name}».";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to install modpack {ProjectId}", item.Hit.ProjectId);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _downloadCenter.Complete(download, $"Ошибка установки: {ex.Message}");
                    item.IsInstalling = false;
                });
            }
        });
    }
}
