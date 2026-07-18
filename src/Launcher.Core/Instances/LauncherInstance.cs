namespace Launcher.Core.Instances;

public sealed class LauncherInstance
{
    public required string Name { get; init; }
    public required string VersionId { get; init; }
    public required string DirectoryPath { get; init; }

    /// <summary>"Vanilla", "Fabric", "Quilt" or "Forge" — kept as a plain string since Launcher.Core doesn't
    /// depend on Launcher.Loaders (which owns the actual ModLoaderType enum).</summary>
    public string LoaderType { get; init; } = "Vanilla";

    public string? LoaderVersion { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPlayedAt { get; set; }

    /// <summary>Free-form label for grouping instances in the Library grid (e.g. "Survival", "Modded").
    /// Null/empty means "Без группы".</summary>
    public string? GroupTag { get; set; }

    // Per-instance overrides — null means "use the global default from ISettingsService".
    public int? MinRamMb { get; set; }
    public int? MaxRamMb { get; set; }
    public string? JavaPathOverride { get; set; }
    public string? JvmArgs { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool Fullscreen { get; set; }
    public Dictionary<string, string>? EnvVars { get; set; }
}
