using Launcher.Core.Versions;

namespace Launcher.Loaders.Abstractions;

public interface ILoaderInstaller
{
    ModLoaderType LoaderType { get; }

    Task<IReadOnlyList<LoaderVersionInfo>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default);

    /// <summary>
    /// Produces the effective <see cref="VersionDetails"/> to launch: the loader's own libraries/mainClass
    /// merged on top of the vanilla version it depends on. May download files into <paramref name="gameRoot"/>
    /// (e.g. Forge's installer).
    /// </summary>
    Task<VersionDetails> InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        VersionDetails vanillaVersion,
        string gameRoot,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default);
}
