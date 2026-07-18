using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Instances;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

/// <summary>Puts the CustomSkinLoader mod + a config pointing at our Supabase `skins` edge function
/// into an instance, so cloud-uploaded skins show up in-game (yours and other launcher players').
/// Jars are fetched once from the mod's GitHub releases and cached per-loader in AppData, so an
/// offline launch after the first install still works.</summary>
public interface ISkinModInstaller
{
    /// <summary>No-op for Vanilla instances (a mod loader is required to render custom skins).</summary>
    Task EnsureInstalledAsync(LauncherInstance instance, CancellationToken ct = default);
}

public sealed class SkinModInstaller(ILogger<SkinModInstaller> logger) : ISkinModInstaller
{
    private const string ReleasesLatestUrl = "https://api.github.com/repos/xfl03/MCCustomSkinLoader/releases/latest";
    private const string SkinApiRoot = "https://camxoptnyxrljaamsfym.supabase.co/functions/v1/skins/";

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncher",
        "skinmod");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftLauncher/1.0");
        return client;
    }

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    public async Task EnsureInstalledAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        var loaderKey = instance.LoaderType switch
        {
            "Fabric" or "Quilt" => "Fabric",
            "Forge" => "Forge",
            "NeoForge" => "NeoForge",
            _ => null,
        };
        if (loaderKey is null)
        {
            return;
        }

        var modsDirectory = Path.Combine(instance.DirectoryPath, "mods");
        Directory.CreateDirectory(modsDirectory);

        // Already present (enabled or disabled)? Just refresh the config.
        var existing = Directory.EnumerateFiles(modsDirectory, "CustomSkinLoader_*").Any();
        if (!existing)
        {
            var jarPath = await GetCachedJarAsync(loaderKey, ct);
            if (jarPath is null)
            {
                return; // Download failed — logged inside; never block the launch over a skin.
            }

            File.Copy(jarPath, Path.Combine(modsDirectory, Path.GetFileName(jarPath)), overwrite: true);
        }

        WriteConfig(instance.DirectoryPath);
    }

    private async Task<string?> GetCachedJarAsync(string loaderKey, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDirectory);
        var cached = Directory.EnumerateFiles(CacheDirectory, $"CustomSkinLoader_{loaderKey}-*.jar").FirstOrDefault();

        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesLatestUrl, ct);
            var asset = release?.Assets.FirstOrDefault(a =>
                a.Name.StartsWith($"CustomSkinLoader_{loaderKey}-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                logger.LogWarning("No CustomSkinLoader asset for loader {Loader} in latest release", loaderKey);
                return cached;
            }

            var target = Path.Combine(CacheDirectory, asset.Name);
            if (!File.Exists(target))
            {
                var bytes = await Http.GetByteArrayAsync(asset.DownloadUrl, ct);
                await File.WriteAllBytesAsync(target, bytes, ct);
            }

            return target;
        }
        catch (Exception ex)
        {
            // Rate limit / offline: fall back to whatever version we cached earlier.
            logger.LogWarning(ex, "Failed to fetch CustomSkinLoader release info; using cached jar: {Cached}", cached);
            return cached;
        }
    }

    private static void WriteConfig(string instanceDirectory)
    {
        var configDirectory = Path.Combine(instanceDirectory, "CustomSkinLoader");
        Directory.CreateDirectory(configDirectory);

        // Our API first, Mojang as fallback — CSL fills in any settings missing from this file.
        var config = new
        {
            loadlist = new object[]
            {
                new { name = "LauncherSkins", type = "CustomSkinAPI", root = SkinApiRoot },
                new { name = "Mojang", type = "MojangAPI" },
            },
            enableCape = true,
            cacheExpiry = 5,
        };

        File.WriteAllText(
            Path.Combine(configDirectory, "CustomSkinLoader.json"),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
