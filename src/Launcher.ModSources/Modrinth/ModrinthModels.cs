using System.Text.Json.Serialization;

namespace Launcher.ModSources.Modrinth;

public sealed class ModrinthSearchResult
{
    [JsonPropertyName("hits")] public List<ModrinthSearchHit> Hits { get; init; } = [];
    [JsonPropertyName("total_hits")] public int TotalHits { get; init; }
}

public sealed class ModrinthSearchHit
{
    [JsonPropertyName("project_id")] public string ProjectId { get; init; } = "";
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("project_type")] public string ProjectType { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("author")] public string Author { get; init; } = "";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }
    [JsonPropertyName("downloads")] public long Downloads { get; init; }
    [JsonPropertyName("follows")] public long Follows { get; init; }
    [JsonPropertyName("date_modified")] public DateTimeOffset DateModified { get; init; }
    [JsonPropertyName("display_categories")] public List<string> DisplayCategories { get; init; } = [];

    // Unlike display_categories (a curated, short list for chips), this includes every facet Modrinth
    // tags the project with — for "mod" projects that includes the loaders it supports (fabric/forge/
    // quilt/...), which is what a "does this fit instance X" compatibility check needs.
    [JsonPropertyName("categories")] public List<string> Categories { get; init; } = [];
}

public sealed class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("project_id")] public string ProjectId { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; init; } = "";
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; init; } = [];
    [JsonPropertyName("loaders")] public List<string> Loaders { get; init; } = [];
    [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; init; } = [];
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; init; } = [];
}

public sealed class ModrinthDependency
{
    [JsonPropertyName("version_id")] public string? VersionId { get; init; }
    [JsonPropertyName("project_id")] public string? ProjectId { get; init; }
    [JsonPropertyName("dependency_type")] public string DependencyType { get; init; } = "";
}

public sealed class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("filename")] public string Filename { get; init; } = "";
    [JsonPropertyName("primary")] public bool Primary { get; init; }
    [JsonPropertyName("hashes")] public ModrinthFileHashes Hashes { get; init; } = new();
}

public sealed class ModrinthFileHashes
{
    [JsonPropertyName("sha1")] public string? Sha1 { get; init; }
}

public sealed class ModrinthProject
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }
}

/// <summary>The manifest at the root of a .mrpack (a zip) — modrinth.index.json.</summary>
public sealed class ModrinthPackIndex
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("dependencies")] public Dictionary<string, string> Dependencies { get; init; } = [];
    [JsonPropertyName("files")] public List<ModrinthPackFile> Files { get; init; } = [];
}

public sealed class ModrinthPackFile
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("hashes")] public ModrinthFileHashes Hashes { get; init; } = new();
    [JsonPropertyName("downloads")] public List<string> Downloads { get; init; } = [];
    [JsonPropertyName("env")] public ModrinthPackFileEnv? Env { get; init; }
}

public sealed class ModrinthPackFileEnv
{
    [JsonPropertyName("client")] public string Client { get; init; } = "required";
}
