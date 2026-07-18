using Launcher.Core.Versions;

namespace Launcher.Loaders.Abstractions;

/// <summary>
/// Merges a loader "profile" version (Fabric/Quilt/Forge — has <c>inheritsFrom</c>, only its own libraries
/// and mainClass) with the vanilla <see cref="VersionDetails"/> it depends on, producing a single effective
/// version that <c>GameLauncher</c> can launch exactly like a normal vanilla version.
/// </summary>
public static class VersionMerger
{
    public static VersionDetails Merge(VersionDetails vanilla, VersionDetails loaderProfile) => new()
    {
        Id = loaderProfile.Id,
        Type = vanilla.Type,
        MainClass = string.IsNullOrEmpty(loaderProfile.MainClass) ? vanilla.MainClass : loaderProfile.MainClass,
        AssetIndex = vanilla.AssetIndex,
        Downloads = vanilla.Downloads,
        Libraries = [.. loaderProfile.Libraries, .. vanilla.Libraries],
        Arguments = MergeArguments(vanilla.Arguments, loaderProfile.Arguments),
        MinecraftArguments = loaderProfile.MinecraftArguments ?? vanilla.MinecraftArguments,
        JavaVersion = vanilla.JavaVersion,
    };

    private static ModernArguments? MergeArguments(ModernArguments? vanilla, ModernArguments? loader)
    {
        if (vanilla is null && loader is null)
        {
            return null;
        }

        return new ModernArguments
        {
            Game = [.. vanilla?.Game ?? [], .. loader?.Game ?? []],
            Jvm = [.. vanilla?.Jvm ?? [], .. loader?.Jvm ?? []],
        };
    }
}
