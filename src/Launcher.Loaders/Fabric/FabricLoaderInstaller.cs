using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;

namespace Launcher.Loaders.Fabric;

/// <summary>
/// Fabric needs no external installer: its meta API returns a ready-to-merge "profile" version JSON
/// in the same shape as a Mojang version JSON (id, inheritsFrom, mainClass, libraries, arguments).
/// </summary>
public sealed class FabricLoaderInstaller(HttpClient httpClient) : ILoaderInstaller
{
    private const string BaseUrl = "https://meta.fabricmc.net/v2";

    public ModLoaderType LoaderType => ModLoaderType.Fabric;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
    {
        var entries = await httpClient.GetFromJsonAsync<List<FabricLoaderEntry>>(
            $"{BaseUrl}/versions/loader/{minecraftVersion}", ct) ?? [];

        return entries.Select(e => new LoaderVersionInfo(e.Loader.Version, e.Loader.Stable)).ToList();
    }

    public async Task<VersionDetails> InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        VersionDetails vanillaVersion,
        string gameRoot,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        statusProgress?.Report("Загрузка профиля Fabric...");

        var profile = await httpClient.GetFromJsonAsync<VersionDetails>(
            $"{BaseUrl}/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json", ct)
            ?? throw new InvalidOperationException("Fabric meta вернул пустой профиль версии.");

        return VersionMerger.Merge(vanillaVersion, profile);
    }

    private sealed class FabricLoaderEntry
    {
        [JsonPropertyName("loader")] public FabricLoaderVersion Loader { get; init; } = new();
    }

    private sealed class FabricLoaderVersion
    {
        [JsonPropertyName("version")] public string Version { get; init; } = "";
        [JsonPropertyName("stable")] public bool Stable { get; init; }
    }
}
