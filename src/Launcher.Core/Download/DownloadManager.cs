using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Launcher.Core.Download;

public interface IDownloadManager
{
    Task DownloadFileAsync(
        string url,
        string destinationPath,
        string? expectedSha1 = null,
        InstallProgress? progress = null,
        CancellationToken ct = default);
}

public sealed class DownloadManager(HttpClient httpClient, ILogger<DownloadManager> logger) : IDownloadManager
{
    private const int BufferSize = 81920;
    private const int MaxAttempts = 3;

    // A stalled connection (no timeout, no bytes) would otherwise hang Parallel.ForEachAsync forever,
    // since it waits on every item — observed live with Mojang's asset CDN on a slow/unstable link.
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(30);

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        string? expectedSha1 = null,
        InstallProgress? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(destinationPath) &&
            (expectedSha1 is null || await MatchesHashAsync(destinationPath, expectedSha1, ct)))
        {
            // Already have it — still counts toward "downloaded" so a re-run of an already-complete
            // install reports 100% instead of getting stuck under whatever this file's total share is.
            progress?.AddDownloaded(new FileInfo(destinationPath).Length);
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = destinationPath + ".tmp";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(PerAttemptTimeout);

            try
            {
                await DownloadOnceAsync(url, tempPath, progress, attemptCts.Token);
                break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt == MaxAttempts)
                {
                    throw new IOException($"Загрузка '{url}' не завершилась за {PerAttemptTimeout.TotalSeconds:F0} c после {MaxAttempts} попыток.");
                }

                logger.LogWarning("Download of {Url} timed out after {Timeout}s (attempt {Attempt}/{Max}), retrying...",
                    url, PerAttemptTimeout.TotalSeconds, attempt, MaxAttempts);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Download of {Url} failed (attempt {Attempt}/{Max}), retrying...", url, attempt, MaxAttempts);
            }
        }

        if (expectedSha1 is not null && !await MatchesHashAsync(tempPath, expectedSha1, ct))
        {
            File.Delete(tempPath);
            throw new IOException($"SHA1 mismatch after downloading '{url}'.");
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        logger.LogDebug("Downloaded {Url} -> {Path}", url, destinationPath);
    }

    private async Task DownloadOnceAsync(string url, string tempPath, InstallProgress? progress, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(tempPath);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.AddDownloaded(read);
        }
    }

    private static async Task<bool> MatchesHashAsync(string path, string expectedSha1, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}
