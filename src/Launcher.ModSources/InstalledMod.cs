namespace Launcher.ModSources;

/// <summary>A mod jar the launcher itself placed in an instance's mods folder, tracked so it can be listed/removed.</summary>
public sealed class InstalledMod
{
    public required string ProjectId { get; init; }
    public required string VersionId { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }

    /// <summary>Modrinth project_type ("mod", "resourcepack", "shader", "datapack") — decides which
    /// subfolder the file lives in. Defaults to "mod" so entries saved before this field existed still
    /// deserialize correctly.</summary>
    public string ProjectType { get; init; } = "mod";

    /// <summary>True if this was pulled in only as a dependency of another mod the user explicitly installed.</summary>
    public bool IsDependency { get; init; }

    /// <summary>False means the file sits on disk as "&lt;FileName&gt;.disabled" — Minecraft won't load it,
    /// but it isn't deleted, so re-enabling doesn't need a re-download. Defaults to true so entries saved
    /// before this field existed still deserialize as enabled.</summary>
    public bool IsEnabled { get; set; } = true;
}
