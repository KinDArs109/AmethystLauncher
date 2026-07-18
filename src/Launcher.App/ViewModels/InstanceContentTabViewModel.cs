using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Instances;
using Launcher.ModSources;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

public sealed record ContentTypeFilterOption(string Key, string Label);

/// <summary>Content tab of the instance detail page — shows what's already installed, with a local
/// name/type filter and sort. Discovering new content happens on the separate "Поиск проектов" page
/// (opened via <see cref="InstanceDetailViewModel.FindProjectsCommand"/>), not inline here.</summary>
public partial class InstanceContentTabViewModel : ObservableObject
{
    private readonly IModInstallService _modInstallService;
    private readonly LauncherInstance _instance;
    private readonly ILogger _logger;

    private readonly List<InstalledMod> _allInstalledMods = [];

    public bool SupportsMods => _instance.LoaderType != "Vanilla";

    public IReadOnlyList<ContentTypeFilterOption> TypeFilters { get; } =
    [
        new("", "Всё"),
        new("mod", "Моды"),
        new("resourcepack", "Наборы ресурсов"),
        new("shader", "Шейдеры"),
        new("datapack", "Наборы данных"),
    ];

    [ObservableProperty]
    private ContentTypeFilterOption _selectedTypeFilter;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _isLoadingInstalled;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<InstalledMod> InstalledMods { get; } = [];

    public InstanceContentTabViewModel(IModInstallService modInstallService, LauncherInstance instance, ILogger logger)
    {
        _modInstallService = modInstallService;
        _instance = instance;
        _logger = logger;
        _selectedTypeFilter = TypeFilters[0];

        if (SupportsMods)
        {
            LoadInstalledCommand.ExecuteAsync(null);
        }
    }

    partial void OnSelectedTypeFilterChanged(ContentTypeFilterOption value) => ApplyFilterAndSort();

    partial void OnFilterTextChanged(string value) => ApplyFilterAndSort();

    [RelayCommand]
    private void SelectTypeFilter(ContentTypeFilterOption option) => SelectedTypeFilter = option;

    [RelayCommand]
    private void ToggleSort()
    {
        SortDescending = !SortDescending;
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private async Task LoadInstalledAsync()
    {
        _allInstalledMods.Clear();
        try
        {
            IsLoadingInstalled = true;
            var installed = await _modInstallService.GetInstalledModsAsync(_instance.DirectoryPath);
            _allInstalledMods.AddRange(installed);
            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load installed mods for {Instance}", _instance.Name);
            StatusText = $"Не удалось прочитать список установленных модов: {ex.Message}";
        }
        finally
        {
            IsLoadingInstalled = false;
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<InstalledMod> query = _allInstalledMods;

        if (!string.IsNullOrEmpty(SelectedTypeFilter.Key))
        {
            query = query.Where(m => m.ProjectType == SelectedTypeFilter.Key);
        }

        var filterText = FilterText.Trim();
        if (filterText.Length > 0)
        {
            query = query.Where(m => m.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase));
        }

        query = SortDescending
            ? query.OrderByDescending(m => m.Title, StringComparer.OrdinalIgnoreCase)
            : query.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase);

        InstalledMods.Clear();
        foreach (var mod in query)
        {
            InstalledMods.Add(mod);
        }

        StatusText = _allInstalledMods.Count == 0
            ? "Нет контента"
            : InstalledMods.Count == 0 ? "Ничего не найдено по фильтру." : "";
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(InstalledMod mod)
    {
        try
        {
            await _modInstallService.SetEnabledAsync(_instance.DirectoryPath, mod.ProjectId, !mod.IsEnabled);
            await LoadInstalledAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle mod {ProjectId}", mod.ProjectId);
            StatusText = $"Не удалось переключить «{mod.Title}»: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(InstalledMod mod)
    {
        try
        {
            await _modInstallService.UninstallModAsync(_instance.DirectoryPath, mod.ProjectId);
            await LoadInstalledAsync();
            StatusText = $"«{mod.Title}» удалён.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall mod {ProjectId}", mod.ProjectId);
            StatusText = $"Не удалось удалить «{mod.Title}»: {ex.Message}";
        }
    }
}
