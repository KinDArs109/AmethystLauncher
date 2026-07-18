using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Instances;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

/// <summary>One dropdown entry: "Живой лог" (Entry == null) or a past run read from disk.</summary>
public sealed record LogRunOption(string Label, LogRunEntry? Entry);

public partial class InstanceLogsTabViewModel : ObservableObject
{
    private readonly IInstanceLogHistoryService _logHistoryService;
    private readonly LauncherInstance _instance;
    private readonly ILogger _logger;

    private readonly List<GameLogEntry> _liveLines = [];
    private List<GameLogEntry> _historyLines = [];

    [ObservableProperty]
    private string _logSearchText = "";

    [ObservableProperty]
    private GameLogLevel? _logFilter;

    [ObservableProperty]
    private LogRunOption? _selectedRun;

    public ObservableCollection<LogRunOption> RunOptions { get; } = [];

    public ObservableCollection<GameLogEntry> VisibleLogLines { get; } = [];

    public InstanceLogsTabViewModel(IInstanceLogHistoryService logHistoryService, LauncherInstance instance, ILogger logger)
    {
        _logHistoryService = logHistoryService;
        _instance = instance;
        _logger = logger;

        RunOptions.Add(new LogRunOption("Живой лог", null));
        _selectedRun = RunOptions[0];
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _logHistoryService.GetHistoryAsync(_instance.DirectoryPath);
            var previouslySelected = SelectedRun;

            while (RunOptions.Count > 1)
            {
                RunOptions.RemoveAt(1);
            }

            foreach (var entry in history)
            {
                RunOptions.Add(new LogRunOption(entry.StartedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm"), entry));
            }

            // Keep whatever was selected pointed at the same run if it still exists, else fall back to live.
            SelectedRun = previouslySelected is { Entry: not null }
                ? RunOptions.FirstOrDefault(o => o.Entry?.FilePath == previouslySelected.Entry.FilePath) ?? RunOptions[0]
                : RunOptions[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load log history for {Instance}", _instance.Name);
        }
    }

    partial void OnSelectedRunChanged(LogRunOption? value) => _ = ApplySelectedRunAsync();

    partial void OnLogFilterChanged(GameLogLevel? value) => RefreshVisible();

    partial void OnLogSearchTextChanged(string value) => RefreshVisible();

    private async Task ApplySelectedRunAsync()
    {
        if (SelectedRun?.Entry is null)
        {
            RefreshVisible();
            return;
        }

        try
        {
            var lines = await _logHistoryService.ReadLogAsync(SelectedRun.Entry.FilePath);
            _historyLines = lines.Select(l => new GameLogEntry(l, GameLogEntry.ClassifyLevel(l, false))).ToList();
            RefreshVisible();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read log file {Path}", SelectedRun.Entry.FilePath);
        }
    }

    /// <summary>Called from the launch pipeline for every Minecraft output line while a run is in progress.</summary>
    public void AppendLiveLine(string text, bool isErrorStream)
    {
        var entry = new GameLogEntry(text, GameLogEntry.ClassifyLevel(text, isErrorStream));

        Application.Current.Dispatcher.Invoke(() =>
        {
            _liveLines.Add(entry);
            if (SelectedRun?.Entry is null && MatchesFilter(entry))
            {
                VisibleLogLines.Add(entry);
            }
        });
    }

    public void StartNewLiveRun()
    {
        _liveLines.Clear();
        SelectedRun = RunOptions[0];
        RefreshVisible();
    }

    private void RefreshVisible()
    {
        var source = SelectedRun?.Entry is null ? _liveLines : _historyLines;
        VisibleLogLines.Clear();
        foreach (var entry in source.Where(MatchesFilter))
        {
            VisibleLogLines.Add(entry);
        }
    }

    private bool MatchesFilter(GameLogEntry entry) =>
        (LogFilter is null || entry.Level == LogFilter) &&
        (string.IsNullOrWhiteSpace(LogSearchText) || entry.Text.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase));
}
