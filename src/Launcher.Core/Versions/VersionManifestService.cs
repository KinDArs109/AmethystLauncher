using System.Net.Http.Json;

namespace Launcher.Core.Versions;

public interface IVersionManifestService
{
    Task<VersionManifestV2> GetManifestAsync(CancellationToken ct = default);

    Task<VersionDetails> GetVersionDetailsAsync(VersionManifestEntry entry, CancellationToken ct = default);
}

public sealed class VersionManifestService(HttpClient httpClient) : IVersionManifestService
{
    private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    public async Task<VersionManifestV2> GetManifestAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<VersionManifestV2>(ManifestUrl, ct)
        ?? throw new InvalidOperationException("Version manifest response was empty.");

    public async Task<VersionDetails> GetVersionDetailsAsync(VersionManifestEntry entry, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<VersionDetails>(entry.Url, ct)
        ?? throw new InvalidOperationException($"Version details response for '{entry.Id}' was empty.");
}
