namespace Launcher.Core.Download;

/// <summary>
/// Shared, thread-safe byte counter for a single instance install/download session. Callers deep in the
/// download pipeline (library/asset/java downloaders) add known file sizes to the total as they discover
/// them and report bytes as they stream them; the UI polls <see cref="TotalBytes"/>/<see cref="DownloadedBytes"/>
/// on a timer rather than subscribing to an event, since chunks land far too often (every ~80KB) to push
/// each one across to the UI thread.
/// </summary>
public sealed class InstallProgress
{
    private long _totalBytes;
    private long _downloadedBytes;
    private volatile string _status = "";

    public string Status => _status;

    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    public long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);

    public void AddToTotal(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _totalBytes, bytes);
        }
    }

    public void AddDownloaded(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _downloadedBytes, bytes);
        }
    }

    public void SetStatus(string status) => _status = status;
}
