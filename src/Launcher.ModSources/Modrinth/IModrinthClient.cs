namespace Launcher.ModSources.Modrinth;

public interface IModrinthClient
{
    /// <summary>General project browser used by the "Поиск проектов" page — spans all Modrinth project
    /// types, not just mods, and supports the sort/pagination controls shown there.</summary>
    Task<ModrinthSearchResult> SearchProjectsAsync(
        string query,
        string projectType,
        string? gameVersion,
        string? loader,
        string sortIndex,
        int offset = 0,
        int limit = 20,
        CancellationToken ct = default);

    Task<IReadOnlyList<ModrinthVersion>> GetProjectVersionsAsync(
        string projectIdOrSlug,
        string? gameVersion,
        string? loader,
        CancellationToken ct = default);

    Task<ModrinthProject?> GetProjectAsync(string projectIdOrSlug, CancellationToken ct = default);
}
