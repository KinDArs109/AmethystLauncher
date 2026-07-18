using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.App.Views.Pages;
using Launcher.Core.Instances;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public sealed record LibraryOption(string Key, string Label);

/// <summary>One visual section of the Library grid — either a real group ("Survival", "Fabric") or the
/// catch-all bucket when grouping is off / an instance has no tag.</summary>
public sealed class InstanceGroupViewModel(string name, IEnumerable<LauncherInstance> items)
{
    public string Name { get; } = name;

    public ObservableCollection<LauncherInstance> Items { get; } = new(items);
}

public partial class LibraryViewModel : ObservableObject
{
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly ISelectedInstanceContext _selectedInstanceContext;
    private readonly INavigationService _navigationService;
    private readonly ILogger<LibraryViewModel> _logger;

    public ObservableCollection<LauncherInstance> Instances => _instanceLibrary.Instances;

    public ObservableCollection<InstanceGroupViewModel> Groups { get; } = [];

    public IReadOnlyList<LibraryOption> GroupOptions { get; } =
    [
        new("none", "Без группировки"),
        new("tag", "По группе"),
        new("loader", "По загрузчику"),
    ];

    public IReadOnlyList<LibraryOption> SortOptions { get; } =
    [
        new("name", "По названию"),
        new("lastPlayed", "Последний запуск"),
        new("created", "Дата создания"),
    ];

    [ObservableProperty]
    private LibraryOption _selectedGroupOption;

    [ObservableProperty]
    private LibraryOption _selectedSortOption;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "";

    public LibraryViewModel(
        IInstanceLibrary instanceLibrary,
        ISelectedInstanceContext selectedInstanceContext,
        INavigationService navigationService,
        ILogger<LibraryViewModel> logger)
    {
        _instanceLibrary = instanceLibrary;
        _selectedInstanceContext = selectedInstanceContext;
        _navigationService = navigationService;
        _logger = logger;

        _selectedGroupOption = GroupOptions[0];
        _selectedSortOption = SortOptions[1];

        Instances.CollectionChanged += (_, _) => RebuildGroups();

        LoadCommand.ExecuteAsync(null);
    }

    partial void OnSelectedGroupOptionChanged(LibraryOption value) => RebuildGroups();

    partial void OnSelectedSortOptionChanged(LibraryOption value) => RebuildGroups();

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            await _instanceLibrary.RefreshAsync();
            StatusText = Instances.Count == 0
                ? "Пока нет ни одной сборки — создайте её через «Создать сборку» в меню слева."
                : "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load instances");
            StatusText = $"Не удалось загрузить список сборок: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            RebuildGroups();
        }
    }

    [RelayCommand]
    private void OpenInstance(LauncherInstance instance)
    {
        // Clicking a build opens it in the Конструктор сборок, pre-selected for editing (mods/configs/
        // play all hang off there now). The builder's "Конфиги" node still reaches the full detail page.
        _selectedInstanceContext.BuilderInstance = instance;
        _navigationService.Navigate(typeof(ModpackBuilderPage));
    }

    private void RebuildGroups()
    {
        IEnumerable<LauncherInstance> filtered = Instances;
        var query = SearchText.Trim();
        if (query.Length > 0)
        {
            filtered = filtered.Where(i => i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        IEnumerable<LauncherInstance> sorted = SelectedSortOption.Key switch
        {
            "lastPlayed" => filtered.OrderByDescending(i => i.LastPlayedAt ?? DateTimeOffset.MinValue),
            "created" => filtered.OrderByDescending(i => i.CreatedAt),
            _ => filtered.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Groups.Clear();

        if (SelectedGroupOption.Key == "none")
        {
            Groups.Add(new InstanceGroupViewModel("", sorted));
            return;
        }

        var keyed = SelectedGroupOption.Key == "loader"
            ? sorted.GroupBy(i => i.LoaderType)
            : sorted.GroupBy(i => string.IsNullOrWhiteSpace(i.GroupTag) ? "Без группы" : i.GroupTag!);

        foreach (var group in keyed.OrderBy(g => g.Key == "Без группы" ? 1 : 0).ThenBy(g => g.Key))
        {
            Groups.Add(new InstanceGroupViewModel(group.Key, group));
        }
    }
}
