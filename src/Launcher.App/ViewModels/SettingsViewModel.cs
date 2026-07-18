using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Settings;

namespace Launcher.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IBypassService _bypassService;

    [ObservableProperty]
    private int _minRamMb = 1024;

    [ObservableProperty]
    private int _maxRamMb = 4096;

    [ObservableProperty]
    private string? _javaPathOverride;

    [ObservableProperty]
    private string _statusText = "";

    // ---- Bypass (zapret) ----

    [ObservableProperty]
    private bool _bypassEnabled;

    [ObservableProperty]
    private string _bypassStatusText = "";

    /// <summary>Guards the toggle's change handler while we set it programmatically (on load / after a
    /// declined UAC prompt), so those don't re-trigger start/stop.</summary>
    private bool _suppressBypassToggle;

    public SettingsViewModel(ISettingsService settingsService, IBypassService bypassService)
    {
        _settingsService = settingsService;
        _bypassService = bypassService;
        LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var settings = await _settingsService.LoadAsync();
        MinRamMb = settings.MinRamMb;
        MaxRamMb = settings.MaxRamMb;
        JavaPathOverride = settings.JavaPathOverride;

        _suppressBypassToggle = true;
        BypassEnabled = settings.BypassEnabled;
        _suppressBypassToggle = false;
        UpdateBypassStatus();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Mutate the on-disk settings rather than building a fresh object — otherwise this page would
        // wipe fields it doesn't edit (nickname, local account, bypass flag).
        var settings = await _settingsService.LoadAsync();
        settings.MinRamMb = MinRamMb;
        settings.MaxRamMb = MaxRamMb;
        settings.JavaPathOverride = string.IsNullOrWhiteSpace(JavaPathOverride) ? null : JavaPathOverride;
        await _settingsService.SaveAsync(settings);
        StatusText = "Настройки сохранены.";
    }

    partial void OnBypassEnabledChanged(bool value)
    {
        if (_suppressBypassToggle)
        {
            return;
        }

        _ = ToggleBypassAsync(value);
    }

    private async Task ToggleBypassAsync(bool enable)
    {
        BypassStatusText = enable
            ? "Запуск обхода (при первом включении подтвердите права администратора)..."
            : "Отключение обхода...";

        var ok = enable ? await _bypassService.StartAsync() : await _bypassService.StopAsync();
        if (!ok)
        {
            // User declined UAC or something failed — revert the toggle without re-triggering this.
            _suppressBypassToggle = true;
            BypassEnabled = !enable;
            _suppressBypassToggle = false;
            BypassStatusText = "Не удалось изменить состояние обхода (нужны права администратора при первом включении).";
            return;
        }

        var settings = await _settingsService.LoadAsync();
        settings.BypassEnabled = enable;
        await _settingsService.SaveAsync(settings);

        // winws.exe takes a moment to appear/disappear; give it a beat before reading status.
        await Task.Delay(1500);
        UpdateBypassStatus();
    }

    private void UpdateBypassStatus()
    {
        BypassStatusText = _bypassService.IsRunning
            ? "Обход работает — Modrinth и облако лаунчера доступны."
            : "Обход выключен.";
    }
}
