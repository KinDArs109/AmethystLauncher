using System.Text.Json;
using Launcher.Core.Download;

namespace Launcher.Core.Java;

public interface IJreProvisioner
{
    /// <summary>Ensures the given Java major version is available locally and returns the path to javaw.exe.</summary>
    Task<string> EnsureJavaAsync(int requiredMajorVersion, InstallProgress? progress = null, CancellationToken ct = default);
}

/// <summary>
/// Downloads Mojang's own trimmed JRE builds (the same ones the official launcher and Prism/MultiMC use),
/// which sidesteps bundling/licensing questions since they're Mojang-distributed.
/// </summary>
public sealed class JreProvisioner(HttpClient httpClient, IDownloadManager downloadManager) : IJreProvisioner
{
    // Stable, long-lived index URL used by Mojang and third-party launchers alike; not versioned by MC release.
    private const string RuntimeIndexUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private const string Platform = "windows-x64";
    private const int MaxParallelDownloads = 8;

    public async Task<string> EnsureJavaAsync(int requiredMajorVersion, InstallProgress? progress = null, CancellationToken ct = default)
    {
        var component = ResolveComponent(requiredMajorVersion);
        var runtimeRoot = Path.Combine(GetRuntimesRoot(), component);
        var javaExePath = Path.Combine(runtimeRoot, "bin", "javaw.exe");

        if (File.Exists(javaExePath))
        {
            return javaExePath;
        }

        await using var indexStream = await httpClient.GetStreamAsync(RuntimeIndexUrl, ct);
        using var index = await JsonDocument.ParseAsync(indexStream, cancellationToken: ct);

        var componentEntries = index.RootElement.GetProperty(Platform).GetProperty(component);
        if (componentEntries.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"No '{component}' Java runtime is published for {Platform}.");
        }

        var manifestUrl = componentEntries[0].GetProperty("manifest").GetProperty("url").GetString()!;
        await using var filesStream = await httpClient.GetStreamAsync(manifestUrl, ct);
        using var filesManifest = await JsonDocument.ParseAsync(filesStream, cancellationToken: ct);

        var fileEntries = filesManifest.RootElement.GetProperty("files").EnumerateObject()
            .Where(entry => entry.Value.GetProperty("type").GetString() == "file")
            .ToList();

        foreach (var entry in fileEntries)
        {
            var raw = entry.Value.GetProperty("downloads").GetProperty("raw");
            if (raw.TryGetProperty("size", out var sizeProp))
            {
                progress?.AddToTotal(sizeProp.GetInt64());
            }
        }

        await Parallel.ForEachAsync(
            fileEntries,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct },
            async (entry, token) =>
            {
                var raw = entry.Value.GetProperty("downloads").GetProperty("raw");
                var url = raw.GetProperty("url").GetString()!;
                var sha1 = raw.GetProperty("sha1").GetString()!;
                var destPath = Path.Combine(runtimeRoot, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                await downloadManager.DownloadFileAsync(url, destPath, sha1, progress, token);
            });

        if (!File.Exists(javaExePath))
        {
            throw new InvalidOperationException($"Java runtime '{component}' did not contain '{javaExePath}' after provisioning.");
        }

        return javaExePath;
    }

    private static string ResolveComponent(int majorVersion) => majorVersion switch
    {
        <= 8 => "jre-legacy",
        <= 16 => "java-runtime-alpha",
        <= 17 => "java-runtime-gamma",
        _ => "java-runtime-delta",
    };

    private static string GetRuntimesRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "runtime");
}
