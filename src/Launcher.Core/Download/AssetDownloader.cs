using System.Net.Http.Json;
using Launcher.Core.Versions;

namespace Launcher.Core.Download;

public interface IAssetDownloader
{
    Task DownloadAssetsAsync(
        VersionDetails version,
        string gameRoot,
        InstallProgress? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Downloads the asset index and every referenced object into the shared "assets" tree, keyed by content hash.
/// NOTE: only the modern hashed layout is implemented; pre-1.7 versions use a "virtual" assets layout that
/// isn't handled yet (out of scope for MVP vanilla launch of current/recent versions).
/// </summary>
public sealed class AssetDownloader(HttpClient httpClient, IDownloadManager downloadManager) : IAssetDownloader
{
    private const string ResourcesBaseUrl = "https://resources.download.minecraft.net";
    private const int MaxParallelDownloads = 8;

    public async Task DownloadAssetsAsync(
        VersionDetails version,
        string gameRoot,
        InstallProgress? progress = null,
        CancellationToken ct = default)
    {
        var indexPath = Path.Combine(gameRoot, "assets", "indexes", $"{version.AssetIndex.Id}.json");
        progress?.AddToTotal(version.AssetIndex.Size);
        await downloadManager.DownloadFileAsync(version.AssetIndex.Url, indexPath, version.AssetIndex.Sha1, progress, ct);

        var assetIndex = await httpClient.GetFromJsonAsync<AssetIndexFile>(version.AssetIndex.Url, ct)
            ?? throw new InvalidOperationException("Asset index response was empty.");

        var objects = assetIndex.Objects.Values.DistinctBy(o => o.Hash).ToList();
        progress?.AddToTotal(objects.Sum(o => o.Size));

        await Parallel.ForEachAsync(
            objects,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct },
            async (asset, token) =>
            {
                var prefix = asset.Hash[..2];
                var objectPath = Path.Combine(gameRoot, "assets", "objects", prefix, asset.Hash);
                var url = $"{ResourcesBaseUrl}/{prefix}/{asset.Hash}";
                await downloadManager.DownloadFileAsync(url, objectPath, asset.Hash, progress, token);
            });
    }
}
