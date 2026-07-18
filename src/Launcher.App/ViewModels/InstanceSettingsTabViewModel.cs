using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Instances;
using Launcher.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Launcher.App.ViewModels;

/// <summary>Backs the instance detail page's "Настройки" tab — per-instance overrides for RAM, Java,
/// JVM args, env vars, window size/fullscreen and the Library grouping tag. Every override is optional;
/// leaving a toggle off means "use the global default from Настройки лаунчера".</summary>
public partial class InstanceSettingsTabViewModel : ObservableObject
{
    private readonly IInstanceLibrary _instanceLibrary;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;

    private LauncherInstance _instance;

    [ObservableProperty]
    private bool _useCustomRam;

    [ObservableProperty]
    private int _minRamMb = 1024;

    [ObservableProperty]
    private int _maxRamMb = 4096;

    [ObservableProperty]
    private int _globalMinRamMb;

    [ObservableProperty]
    private int _globalMaxRamMb;

    [ObservableProperty]
    private bool _useCustomJava;

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private string _jvmArgs = "";

    [ObservableProperty]
    private bool _useCustomWindow;

    [ObservableProperty]
    private int _windowWidth = 1280;

    [ObservableProperty]
    private int _windowHeight = 720;

    [ObservableProperty]
    private bool _fullscreen;

    [ObservableProperty]
    private string _groupTag = "";

    [ObservableProperty]
    private string _envVarsText = "";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Raised after a successful save so the parent view model can swap in the updated instance.</summary>
    public event Action<LauncherInstance>? InstanceUpdated;

    public InstanceSettingsTabViewModel(IInstanceLibrary instanceLibrary, ISettingsService settingsService, LauncherInstance instance, ILogger logger)
    {
        _instanceLibrary = instanceLibrary;
        _settingsService = settingsService;
        _logger = logger;
        _instance = instance;
        LoadFromInstance(instance);
        _ = LoadGlobalDefaultsAsync();
    }

    public void UpdateInstance(LauncherInstance instance)
    {
        _instance = instance;
        LoadFromInstance(instance);
    }

    private void LoadFromInstance(LauncherInstance instance)
    {
        UseCustomRam = instance.MinRamMb.HasValue || instance.MaxRamMb.HasValue;
        MinRamMb = instance.MinRamMb ?? (GlobalMinRamMb > 0 ? GlobalMinRamMb : 1024);
        MaxRamMb = instance.MaxRamMb ?? (GlobalMaxRamMb > 0 ? GlobalMaxRamMb : 4096);
        UseCustomJava = !string.IsNullOrWhiteSpace(instance.JavaPathOverride);
        JavaPath = instance.JavaPathOverride ?? "";
        JvmArgs = instance.JvmArgs ?? "";
        UseCustomWindow = instance.WindowWidth.HasValue || instance.WindowHeight.HasValue;
        WindowWidth = instance.WindowWidth ?? 1280;
        WindowHeight = instance.WindowHeight ?? 720;
        Fullscreen = instance.Fullscreen;
        GroupTag = instance.GroupTag ?? "";
        EnvVarsText = instance.EnvVars is null
            ? ""
            : string.Join(Environment.NewLine, instance.EnvVars.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private async Task LoadGlobalDefaultsAsync()
    {
        var settings = await _settingsService.LoadAsync();
        GlobalMinRamMb = settings.MinRamMb;
        GlobalMaxRamMb = settings.MaxRamMb;

        if (!UseCustomRam)
        {
            MinRamMb = GlobalMinRamMb;
            MaxRamMb = GlobalMaxRamMb;
        }
    }

    [RelayCommand]
    private void BrowseJava()
    {
        var dialog = new OpenFileDialog { Title = "Выберите java.exe", Filter = "Java (java.exe)|java.exe|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            JavaPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _instance.GroupTag = string.IsNullOrWhiteSpace(GroupTag) ? null : GroupTag.Trim();
            _instance.MinRamMb = UseCustomRam ? MinRamMb : null;
            _instance.MaxRamMb = UseCustomRam ? MaxRamMb : null;
            _instance.JavaPathOverride = UseCustomJava && !string.IsNullOrWhiteSpace(JavaPath) ? JavaPath.Trim() : null;
            _instance.JvmArgs = string.IsNullOrWhiteSpace(JvmArgs) ? null : JvmArgs.Trim();
            _instance.WindowWidth = UseCustomWindow ? WindowWidth : null;
            _instance.WindowHeight = UseCustomWindow ? WindowHeight : null;
            _instance.Fullscreen = Fullscreen;
            _instance.EnvVars = ParseEnvVars(EnvVarsText);

            _instance = await _instanceLibrary.UpdateAsync(_instance);
            InstanceUpdated?.Invoke(_instance);
            StatusText = "Настройки сохранены.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save instance settings for '{Instance}'", _instance.Name);
            StatusText = $"Не удалось сохранить: {ex.Message}";
        }
    }

    private static Dictionary<string, string>? ParseEnvVars(string text)
    {
        var result = new Dictionary<string, string>();
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex > 0)
            {
                result[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
            }
        }

        return result.Count == 0 ? null : result;
    }
}
