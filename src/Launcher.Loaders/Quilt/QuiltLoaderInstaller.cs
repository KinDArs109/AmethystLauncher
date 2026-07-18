using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;

namespace Launcher.Loaders.Quilt;

/// <summary>Quilt's meta API (v3) is deliberately Fabric-compatible: same profile-JSON shape, different host.</summary>
public sealed class QuiltLoaderInstaller(HttpClient httpClient) : ILoaderInstaller
{
    private const string BaseUrl = "https://meta.quiltmc.org/v3";

    public ModLoaderType LoaderType => ModLoaderType.Quilt;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
    {
        var entries = await httpClient.GetFromJsonAsync<List<QuiltLoaderEntry>>(
            $"{BaseUrl}/versions/loader/{minecraftVersion}", ct) ?? [];

        return entries.Select(e => new LoaderVersionInfo(e.Loader.Version, e.Loader.Stable ?? true)).ToList();
    }

    public async Task<VersionDetails> InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        VersionDetails vanillaVersion,
        string gameRoot,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        statusProgress?.Report("Загрузка профиля Quilt...");

        var profile = await httpClient.GetFromJsonAsync<VersionDetails>(
            $"{BaseUrl}/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json", ct)
            ?? throw new InvalidOperationException("Quilt meta вернул пустой профиль версии.");

        return VersionMerger.Merge(vanillaVersion, profile);
    }

    private sealed class QuiltLoaderEntry
    {
        [JsonPropertyName("loader")] public QuiltLoaderVersion Loader { get; init; } = new();
    }

    private sealed class QuiltLoaderVersion
    {
        [JsonPropertyName("version")] public string Version { get; init; } = "";
        [JsonPropertyName("stable")] public bool? Stable { get; init; }
    }
}
