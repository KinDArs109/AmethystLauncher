using System.Net.Http.Json;
using System.Text.Json;

namespace Launcher.ModSources.Modrinth;

/// <summary>Thin wrapper over the public Modrinth API (api.modrinth.com/v2) — no auth required for reads.</summary>
public sealed class ModrinthClient(HttpClient httpClient) : IModrinthClient
{
    public async Task<ModrinthSearchResult> SearchProjectsAsync(
        string query,
        string projectType,
        string? gameVersion,
        string? loader,
        string sortIndex,
        int offset = 0,
        int limit = 20,
        CancellationToken ct = default)
    {
        var facetGroups = new List<string[]>();
        if (!string.IsNullOrWhiteSpace(projectType))
        {
            facetGroups.Add([$"project_type:{projectType}"]);
        }

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            facetGroups.Add([$"versions:{gameVersion}"]);
        }

        // The "loaders" facet only makes sense for mods (resource packs/data packs are loader-agnostic,
        // and shader "loaders" — iris/optifine — aren't the instance's mod loader).
        if (projectType == "mod" && !string.IsNullOrWhiteSpace(loader))
        {
            facetGroups.Add([$"categories:{loader}"]);
        }

        var facets = JsonSerializer.Serialize(facetGroups);

        var url = "search" +
            $"?query={Uri.EscapeDataString(query)}" +
            $"&facets={Uri.EscapeDataString(facets)}" +
            $"&index={Uri.EscapeDataString(sortIndex)}" +
            $"&offset={offset}&limit={limit}";

        return await httpClient.GetFromJsonAsync<ModrinthSearchResult>(url, ct)
            ?? new ModrinthSearchResult();
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetProjectVersionsAsync(
        string projectIdOrSlug,
        string? gameVersion,
        string? loader,
        CancellationToken ct = default)
    {
        var query = "";
        if (!string.IsNullOrWhiteSpace(loader))
        {
            query += $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}";
        }

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            query += $"&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}";
        }

        var url = $"project/{projectIdOrSlug}/version" + (query.Length > 0 ? "?" + query[1..] : "");

        return await httpClient.GetFromJsonAsync<List<ModrinthVersion>>(url, ct) ?? [];
    }

    public async Task<ModrinthProject?> GetProjectAsync(string projectIdOrSlug, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<ModrinthProject>($"project/{projectIdOrSlug}", ct);
}
