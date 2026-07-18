using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public partial class InstanceFilesTabViewModel : ObservableObject
{
    private readonly IInstanceFileService _fileService;
    private readonly IContentDialogService _contentDialogService;
    private readonly string _rootPath;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _currentPath;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<FileEntry> Entries { get; } = [];

    /// <summary>Breadcrumb segments from the instance root down to <see cref="CurrentPath"/>, each clickable.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumb { get; } = [];

    public bool CanNavigateUp => !string.Equals(CurrentPath, _rootPath, StringComparison.OrdinalIgnoreCase);

    public InstanceFilesTabViewModel(
        IInstanceFileService fileService, IContentDialogService contentDialogService, string rootPath, ILogger logger)
    {
        _fileService = fileService;
        _contentDialogService = contentDialogService;
        _rootPath = rootPath;
        _currentPath = rootPath;
        _logger = logger;
    }

    [RelayCommand]
    private void Load() => RefreshEntries();

    partial void OnSearchTextChanged(string value) => RefreshEntries();

    private void RefreshEntries()
    {
        Entries.Clear();
        try
        {
            var all = _fileService.ListDirectory(CurrentPath);
            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? all
                : all.Where(e => e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in filtered)
            {
                Entries.Add(entry);
            }

            StatusText = "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list directory {Path}", CurrentPath);
            StatusText = $"Не удалось прочитать папку: {ex.Message}";
        }

        UpdateBreadcrumb();
        OnPropertyChanged(nameof(CanNavigateUp));
    }

    private void UpdateBreadcrumb()
    {
        Breadcrumb.Clear();
        var relative = Path.GetRelativePath(_rootPath, CurrentPath);
        Breadcrumb.Add(new BreadcrumbSegment("Сборка", _rootPath));

        if (relative == ".")
        {
            return;
        }

        var accumulated = _rootPath;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            accumulated = Path.Combine(accumulated, part);
            Breadcrumb.Add(new BreadcrumbSegment(part, accumulated));
        }
    }

    [RelayCommand]
    private void NavigateTo(BreadcrumbSegment segment)
    {
        CurrentPath = segment.Path;
        RefreshEntries();
    }

    [RelayCommand]
    private void NavigateUp()
    {
        var parent = Path.GetDirectoryName(CurrentPath);
        if (parent is not null && CanNavigateUp)
        {
            CurrentPath = parent;
            RefreshEntries();
        }
    }

    /// <summary>Extensions the in-app editor handles — mod configs and other plain-text formats.
    /// Everything else (jars, images, NBT worlds) still opens through the shell.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".json5", ".jsonc", ".toml", ".properties", ".cfg", ".conf", ".config",
        ".yml", ".yaml", ".ini", ".log", ".md", ".mcmeta", ".snbt", ".lang", ".xml", ".csv",
    };

    private const long MaxEditableFileBytes = 2 * 1024 * 1024;

    [RelayCommand]
    private async Task OpenAsync(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            CurrentPath = entry.FullPath;
            RefreshEntries();
            return;
        }

        try
        {
            var isText = TextExtensions.Contains(Path.GetExtension(entry.Name));
            if (isText && new FileInfo(entry.FullPath).Length <= MaxEditableFileBytes)
            {
                var content = await File.ReadAllTextAsync(entry.FullPath);
                var edited = await DialogHelpers.EditTextAsync(_contentDialogService, entry.Name, content);
                if (edited is not null && edited != content)
                {
                    await File.WriteAllTextAsync(entry.FullPath, edited);
                    StatusText = $"«{entry.Name}» сохранён.";
                }

                return;
            }

            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file {Path}", entry.FullPath);
            StatusText = $"Не удалось открыть файл: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(FileEntry entry)
    {
        var confirmed = await DialogHelpers.ConfirmAsync(
            _contentDialogService, "Удалить?", $"«{entry.Name}» будет удалён безвозвратно.");
        if (!confirmed)
        {
            return;
        }

        try
        {
            _fileService.Delete(entry);
            Entries.Remove(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Path}", entry.FullPath);
            StatusText = $"Не удалось удалить: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameAsync(FileEntry entry)
    {
        var newName = await DialogHelpers.PromptAsync(_contentDialogService, "Переименовать", entry.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name)
        {
            return;
        }

        try
        {
            _fileService.Rename(entry, newName);
            RefreshEntries();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename {Path}", entry.FullPath);
            StatusText = $"Не удалось переименовать: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Добавить файлы" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _fileService.CopyInto(CurrentPath, dialog.FileNames);
            RefreshEntries();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy files into {Path}", CurrentPath);
            StatusText = $"Не удалось добавить файлы: {ex.Message}";
        }
    }
}

public sealed record BreadcrumbSegment(string Label, string Path);
