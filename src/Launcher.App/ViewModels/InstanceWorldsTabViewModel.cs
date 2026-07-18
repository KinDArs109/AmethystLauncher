using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public sealed record WorldEntry(string Name, string FullPath, DateTimeOffset Modified);

/// <summary>Lists saved worlds under &lt;instance&gt;/saves. Server list (servers.dat, NBT binary) isn't
/// parsed — out of scope for now, so this only covers local single-player worlds.</summary>
public partial class InstanceWorldsTabViewModel : ObservableObject
{
    private readonly string _instanceDirectory;
    private readonly IContentDialogService _contentDialogService;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<WorldEntry> Worlds { get; } = [];

    public InstanceWorldsTabViewModel(string instanceDirectory, IContentDialogService contentDialogService, ILogger logger)
    {
        _instanceDirectory = instanceDirectory;
        _contentDialogService = contentDialogService;
        _logger = logger;
    }

    [RelayCommand]
    private void Load()
    {
        Worlds.Clear();
        try
        {
            foreach (var (name, fullPath, modified) in WorldScanner.ScanSaves(_instanceDirectory))
            {
                Worlds.Add(new WorldEntry(name, fullPath, modified));
            }

            StatusText = Worlds.Count == 0 ? "Нет серверов и миров" : "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list worlds for {Instance}", _instanceDirectory);
            StatusText = $"Не удалось прочитать список миров: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder(WorldEntry world)
    {
        try
        {
            Process.Start(new ProcessStartInfo(world.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open world folder {Path}", world.FullPath);
            StatusText = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(WorldEntry world)
    {
        var confirmed = await DialogHelpers.ConfirmAsync(
            _contentDialogService, "Удалить мир?", $"Мир «{world.Name}» будет удалён безвозвратно.");
        if (!confirmed)
        {
            return;
        }

        try
        {
            Directory.Delete(world.FullPath, recursive: true);
            Worlds.Remove(world);
            if (Worlds.Count == 0)
            {
                StatusText = "Нет серверов и миров";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete world {Path}", world.FullPath);
            StatusText = $"Не удалось удалить мир: {ex.Message}";
        }
    }
}
