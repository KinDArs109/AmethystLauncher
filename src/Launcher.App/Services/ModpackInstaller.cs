using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Instances;
using Launcher.Core.Launch;
using Launcher.Loaders.Abstractions;
using Launcher.ModSources.Modrinth;

namespace Launcher.App.Services;

public interface IModpackInstaller
{
    /// <summary>Creates a brand-new instance from a Modrinth modpack project: resolves the loader/game
    /// version the pack declares, installs vanilla+loader, then downloads the pack's file list and
    /// extracts its overrides on top.</summary>
    Task<LauncherInstance> InstallAsync(
        string projectId, string title, string instanceName, InstallProgress progress, CancellationToken ct = default);
}

public sealed class ModpackInstaller(
    IModrinthClient modrinthClient,
    IDownloadManager downloadManager,
    IInstanceLibrary instanceLibrary,
    IInstanceVersionResolver instanceVersionResolver,
    IInstancePreparer instancePreparer) : IModpackInstaller
{
    // modrinth.index.json keys dependencies by loader name — mutually exclusive in practice, since a
    // pack targets exactly one loader.
    private static readonly Dictionary<string, ModLoaderType> LoaderDependencyKeys = new()
    {
        ["fabric-loader"] = ModLoaderType.Fabric,
        ["quilt-loader"] = ModLoaderType.Quilt,
        ["forge"] = ModLoaderType.Forge,
    };

    public async Task<LauncherInstance> InstallAsync(
        string projectId, string title, string instanceName, InstallProgress progress, CancellationToken ct = default)
    {
        progress.SetStatus($"Поиск сборки «{title}»...");
        var versions = await modrinthClient.GetProjectVersionsAsync(projectId, gameVersion: null, loader: null, ct);
        var version = versions.FirstOrDefault()
            ?? throw new InvalidOperationException($"У модпака «{title}» нет версий.");

        var packFile = version.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"У версии «{version.Name}» модпака «{title}» нет .mrpack файла.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mrpack");
        progress.SetStatus($"Загрузка «{title}»...");
        await downloadManager.DownloadFileAsync(packFile.Url, tempPath, packFile.Hashes.Sha1, progress, ct);

        try
        {
            using var archive = ZipFile.OpenRead(tempPath);
            var indexEntry = archive.GetEntry("modrinth.index.json")
                ?? throw new InvalidOperationException($"«{title}»: в архиве нет modrinth.index.json.");

            ModrinthPackIndex index;
            await using (var indexStream = indexEntry.Open())
            {
                index = await JsonSerializer.DeserializeAsync<ModrinthPackIndex>(indexStream, cancellationToken: ct)
                    ?? throw new InvalidOperationException($"«{title}»: не удалось прочитать modrinth.index.json.");
            }

            var gameVersion = index.Dependencies.GetValueOrDefault("minecraft")
                ?? throw new InvalidOperationException($"«{title}»: в модпаке не указана версия Minecraft.");

            var loaderType = ModLoaderType.Vanilla;
            string? loaderVersion = null;
            foreach (var (key, type) in LoaderDependencyKeys)
            {
                if (index.Dependencies.TryGetValue(key, out var value))
                {
                    loaderType = type;
                    loaderVersion = value;
                    break;
                }
            }

            progress.SetStatus("Создание сборки...");
            var instance = await instanceLibrary.CreateAsync(instanceName, gameVersion, loaderType.ToString(), loaderVersion, ct);

            var effectiveVersion = await instanceVersionResolver.ResolveAsync(instance, progress.SetStatus, ct);
            await instancePreparer.PrepareAsync(effectiveVersion, instance.DirectoryPath, progress: progress, ct: ct);

            progress.SetStatus($"Загрузка файлов «{title}»...");
            foreach (var file in index.Files)
            {
                if (file.Env?.Client == "unsupported")
                {
                    continue;
                }

                var url = file.Downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException($"«{title}»: у файла {file.Path} нет ссылки на скачивание.");
                var destination = Path.Combine(instance.DirectoryPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
                await downloadManager.DownloadFileAsync(url, destination, file.Hashes.Sha1, progress, ct);
            }

            progress.SetStatus("Копирование дополнительных файлов...");
            ExtractOverrides(archive, instance.DirectoryPath);

            progress.SetStatus("Готово");
            return instance;
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void ExtractOverrides(ZipArchive archive, string instanceDirectory)
    {
        const string overridesPrefix = "overrides/";
        const string clientOverridesPrefix = "client-overrides/";

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // directory placeholder entry
            }

            string? relative = entry.FullName.StartsWith(overridesPrefix, StringComparison.OrdinalIgnoreCase)
                ? entry.FullName[overridesPrefix.Length..]
                : entry.FullName.StartsWith(clientOverridesPrefix, StringComparison.OrdinalIgnoreCase)
                    ? entry.FullName[clientOverridesPrefix.Length..]
                    : null;

            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var destination = Path.Combine(instanceDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }
}
