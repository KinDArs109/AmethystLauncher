using Launcher.Core.Versions;

namespace Launcher.Core.Download;

public sealed record LibraryDownloadResult(List<string> ClasspathJars, List<string> NativeJars);

public interface ILibraryDownloader
{
    Task<LibraryDownloadResult> DownloadLibrariesAsync(
        VersionDetails version, string gameRoot, InstallProgress? progress = null, CancellationToken ct = default);
}

public sealed class LibraryDownloader(IDownloadManager downloadManager) : ILibraryDownloader
{
    private const string NativesOsKey = "windows";

    public async Task<LibraryDownloadResult> DownloadLibrariesAsync(
        VersionDetails version, string gameRoot, InstallProgress? progress = null, CancellationToken ct = default)
    {
        var librariesRoot = Path.Combine(gameRoot, "libraries");
        var classpathJars = new List<string>();
        var nativeJars = new List<string>();

        foreach (var library in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules))
            {
                continue;
            }

            if (library.Downloads?.Artifact is { } artifact && !string.IsNullOrEmpty(artifact.Path))
            {
                var path = Path.Combine(librariesRoot, RelativePath(artifact.Path, library.Name, null));

                if (!string.IsNullOrEmpty(artifact.Url))
                {
                    progress?.AddToTotal(artifact.Size);
                    await downloadManager.DownloadFileAsync(artifact.Url, path, artifact.Sha1, progress, ct);
                }
                else if (!File.Exists(path))
                {
                    // Empty URL means the file is expected to already exist locally (e.g. a Forge installer
                    // produces its patched client jar on disk with no downloadable source) — a missing file
                    // here means that generation step failed, not something we can fetch instead.
                    throw new FileNotFoundException(
                        $"Библиотека '{library.Name}' не имеет URL для загрузки и отсутствует на диске: {path}");
                }

                classpathJars.Add(path);
            }
            else if (library.Downloads is null && !string.IsNullOrEmpty(library.Url))
            {
                // Loader (Fabric/Quilt) profile library: base repo URL + Maven layout path, no pre-resolved artifact.
                var relativePath = RelativePath(null, library.Name, null);
                var path = Path.Combine(librariesRoot, relativePath);
                var url = library.Url.TrimEnd('/') + "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
                // Loader (Fabric/Quilt) profile libraries don't declare a size — their bytes still land on
                // disk and count toward DownloadedBytes, they just aren't reflected in TotalBytes upfront.
                await downloadManager.DownloadFileAsync(url, path, library.Sha1, progress, ct);
                classpathJars.Add(path);
            }

            if (library.Natives?.GetValueOrDefault(NativesOsKey) is { } nativeKey &&
                library.Downloads?.Classifiers?.GetValueOrDefault(nativeKey) is { Url.Length: > 0 } nativeArtifact)
            {
                var path = Path.Combine(librariesRoot, RelativePath(nativeArtifact.Path, library.Name, nativeKey));
                progress?.AddToTotal(nativeArtifact.Size);
                await downloadManager.DownloadFileAsync(nativeArtifact.Url, path, nativeArtifact.Sha1, progress, ct);
                nativeJars.Add(path);
            }
        }

        if (version.Downloads.Client is { } client)
        {
            var clientPath = Path.Combine(gameRoot, "versions", version.Id, $"{version.Id}.jar");
            progress?.AddToTotal(client.Size);
            await downloadManager.DownloadFileAsync(client.Url, clientPath, client.Sha1, progress, ct);
            classpathJars.Add(clientPath);
        }

        return new LibraryDownloadResult(classpathJars, nativeJars);
    }

    /// <summary>Prefers the server-provided relative path; falls back to deriving the Maven layout from the library name.</summary>
    private static string RelativePath(string? providedPath, string mavenName, string? classifier)
    {
        if (!string.IsNullOrEmpty(providedPath))
        {
            return providedPath.Replace('/', Path.DirectorySeparatorChar);
        }

        // group:artifact:version -> group/with/slashes/artifact/version/artifact-version[-classifier].jar
        var parts = mavenName.Split(':');
        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var suffix = classifier is null ? "" : $"-{classifier}";
        return Path.Combine(group, artifact, version, $"{artifact}-{version}{suffix}.jar");
    }
}
