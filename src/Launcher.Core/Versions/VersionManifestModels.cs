using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Core.Versions;

public sealed class VersionManifestV2
{
    [JsonPropertyName("latest")] public LatestVersions Latest { get; init; } = new();
    [JsonPropertyName("versions")] public List<VersionManifestEntry> Versions { get; init; } = [];
}

public sealed class LatestVersions
{
    [JsonPropertyName("release")] public string Release { get; init; } = "";
    [JsonPropertyName("snapshot")] public string Snapshot { get; init; } = "";
}

public sealed class VersionManifestEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("releaseTime")] public DateTimeOffset ReleaseTime { get; init; }
}

public sealed class VersionDetails
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("mainClass")] public string MainClass { get; init; } = "";
    [JsonPropertyName("assetIndex")] public AssetIndexRef AssetIndex { get; init; } = new();
    [JsonPropertyName("downloads")] public VersionDownloads Downloads { get; init; } = new();
    [JsonPropertyName("libraries")] public List<LibraryEntry> Libraries { get; init; } = [];
    [JsonPropertyName("arguments")] public ModernArguments? Arguments { get; init; }
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; init; }
    [JsonPropertyName("javaVersion")] public JavaVersionRef? JavaVersion { get; init; }

    /// <summary>
    /// Set on loader (Fabric/Quilt/Forge) "profile" version JSONs, which only describe the loader's own
    /// libraries/mainClass and point back at the vanilla version they layer on top of. Loader installers
    /// merge such a profile with the vanilla VersionDetails it inherits from; null on a normal vanilla version.
    /// </summary>
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; init; }
}

public sealed class AssetIndexRef
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("sha1")] public string Sha1 { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
}

public sealed class VersionDownloads
{
    [JsonPropertyName("client")] public DownloadArtifact? Client { get; init; }
}

public sealed class DownloadArtifact
{
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("sha1")] public string Sha1 { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
}

public sealed class LibraryEntry
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; init; }
    [JsonPropertyName("rules")] public List<Rule>? Rules { get; init; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; init; }

    /// <summary>
    /// Loader (Fabric/Quilt) profile libraries give a base Maven repo URL here instead of a resolved
    /// "downloads.artifact" block; the full download URL is this + the library's Maven layout path.
    /// </summary>
    [JsonPropertyName("url")] public string? Url { get; init; }

    /// <summary>Sha1 for the above URL-based library form (loader profiles put it at the top level, not nested).</summary>
    [JsonPropertyName("sha1")] public string? Sha1 { get; init; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public DownloadArtifact? Artifact { get; init; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadArtifact>? Classifiers { get; init; }
}

public sealed class Rule
{
    [JsonPropertyName("action")] public string Action { get; init; } = "allow";
    [JsonPropertyName("os")] public OsRule? Os { get; init; }
    [JsonPropertyName("features")] public Dictionary<string, bool>? Features { get; init; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("arch")] public string? Arch { get; init; }
}

public sealed class JavaVersionRef
{
    [JsonPropertyName("component")] public string Component { get; init; } = "";
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; init; }
}

/// <summary>
/// Each element is either a plain string or a conditional { rules, value } object (1.13+ format);
/// kept as raw <see cref="JsonElement"/>s and resolved by ArgumentBuilder, which understands both shapes.
/// </summary>
public sealed class ModernArguments
{
    [JsonPropertyName("game")] public List<JsonElement> Game { get; init; } = [];
    [JsonPropertyName("jvm")] public List<JsonElement> Jvm { get; init; } = [];
}

public sealed class AssetIndexFile
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject> Objects { get; init; } = new();
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
}
