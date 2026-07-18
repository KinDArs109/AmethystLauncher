using Launcher.Core.Download;
using Launcher.Core.Java;
using Launcher.Core.Versions;

namespace Launcher.Core.Launch;

public sealed record PreparedInstanceFiles(string JavaExecutablePath, string NativesDirectory, IReadOnlyList<string> ClasspathJars);

public interface IInstancePreparer
{
    /// <summary>
    /// Downloads everything a version needs to run (libraries, assets, a matching Java runtime) and
    /// extracts natives — but does not spawn the game process. Used both to pre-download a freshly
    /// created instance and, via <see cref="GameLauncher"/>, as the first half of an actual launch
    /// (where it's normally a fast no-op since everything is already on disk).
    /// </summary>
    Task<PreparedInstanceFiles> PrepareAsync(
        VersionDetails version,
        string instanceDirectory,
        string? javaPathOverride = null,
        InstallProgress? progress = null,
        CancellationToken ct = default);
}

public sealed class InstancePreparer(
    ILibraryDownloader libraryDownloader,
    IAssetDownloader assetDownloader,
    IJavaRuntimeResolver javaRuntimeResolver,
    INativesExtractor nativesExtractor) : IInstancePreparer
{
    public async Task<PreparedInstanceFiles> PrepareAsync(
        VersionDetails version,
        string instanceDirectory,
        string? javaPathOverride = null,
        InstallProgress? progress = null,
        CancellationToken ct = default)
    {
        var gameRoot = GetSharedGameRoot();
        Directory.CreateDirectory(instanceDirectory);

        progress?.SetStatus("Скачивание клиента и библиотек...");
        var libraries = await libraryDownloader.DownloadLibrariesAsync(version, gameRoot, progress, ct);

        progress?.SetStatus("Скачивание ресурсов...");
        await assetDownloader.DownloadAssetsAsync(version, gameRoot, progress, ct);

        progress?.SetStatus("Подготовка Java...");
        var javaPath = await javaRuntimeResolver.ResolveJavaExecutableAsync(version, javaPathOverride, progress, ct);

        progress?.SetStatus("Распаковка нативных библиотек...");
        var nativesDir = nativesExtractor.ExtractNatives(libraries.NativeJars, instanceDirectory);

        progress?.SetStatus("Готово");
        return new PreparedInstanceFiles(javaPath, nativesDir, libraries.ClasspathJars);
    }

    public static string GetSharedGameRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "shared");
}
