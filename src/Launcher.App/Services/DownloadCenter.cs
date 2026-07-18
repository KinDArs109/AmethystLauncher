using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Launcher.App.Services;

/// <summary>
/// Tracks background instance downloads app-wide so a floating toast (hosted in MainWindow, see its XAML)
/// can show live progress no matter which page the user is currently on — matching the corner download
/// notification in Modrinth App. A single <see cref="DispatcherTimer"/> polls each active download's
/// <see cref="Launcher.Core.Download.InstallProgress"/> rather than reacting to its per-chunk updates,
/// since those land on background threads far too often to push to the UI individually.
/// </summary>
public interface IDownloadCenter
{
    ObservableCollection<ActiveDownload> Downloads { get; }

    ActiveDownload Start(string title);

    void Complete(ActiveDownload download, string? errorMessage = null);

    void Dismiss(ActiveDownload download);
}

public sealed class DownloadCenter : IDownloadCenter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(4);

    public ObservableCollection<ActiveDownload> Downloads { get; } = [];

    public DownloadCenter()
    {
        var timer = new DispatcherTimer { Interval = PollInterval };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    public ActiveDownload Start(string title)
    {
        var download = new ActiveDownload
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Progress = new Core.Download.InstallProgress(),
        };
        Downloads.Add(download);
        return download;
    }

    public void Complete(ActiveDownload download, string? errorMessage = null)
    {
        // Sync one last time before Tick() starts skipping this download — otherwise whatever the last
        // poll happened to catch (up to PollInterval stale) is what's shown next to "Готово" forever.
        download.DownloadedBytes = download.Progress.DownloadedBytes;
        download.TotalBytes = download.Progress.TotalBytes;

        download.ErrorMessage = errorMessage;
        download.IsCompleted = errorMessage is null;
        download.Status = errorMessage ?? "Готово";

        if (errorMessage is null)
        {
            var dismissTimer = new DispatcherTimer { Interval = AutoDismissDelay };
            dismissTimer.Tick += (_, _) =>
            {
                dismissTimer.Stop();
                Dismiss(download);
            };
            dismissTimer.Start();
        }
    }

    public void Dismiss(ActiveDownload download) => Downloads.Remove(download);

    private void Tick()
    {
        foreach (var download in Downloads)
        {
            if (download.IsCompleted)
            {
                continue;
            }

            download.DownloadedBytes = download.Progress.DownloadedBytes;
            download.TotalBytes = download.Progress.TotalBytes;
            download.Status = download.Progress.Status;
        }
    }
}
