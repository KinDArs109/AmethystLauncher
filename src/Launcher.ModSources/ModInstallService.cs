using Launcher.Core.Download;
using Launcher.ModSources.Modrinth;

namespace Launcher.ModSources;

public interface IModInstallService
{
    /// <summary>General project browser search spanning any Modrinth project type — see
    /// <see cref="IModrinthClient.SearchProjectsAsync"/>.</summary>
    Task<ModrinthSearchResult> SearchProjectsAsync(
        string query,
        string projectType,
        string? gameVersion,
        string? loader,
        string sortIndex,
        int offset = 0,
        CancellationToken ct = default);

    Task<List<InstalledMod>> GetInstalledModsAsync(string instanceDirectory, CancellationToken ct = default);

    /// <summary>Downloads the project's best-matching version for (gameVersion, loader) plus any required
    /// dependencies, recursively. Already-installed projects (by Modrinth project id) are skipped.
    /// <paramref name="projectType"/> picks the destination subfolder (mods/resourcepacks/shaderpacks/datapacks).
    /// <paramref name="versionId"/> pins the root project to that exact Modrinth version instead of the
    /// best match (dependencies still resolve to their own best match).</summary>
    Task<InstalledMod> InstallModAsync(
        string projectId,
        string title,
        string gameVersion,
        string loader,
        string instanceDirectory,
        IProgress<string>? progress = null,
        string projectType = "mod",
        string? versionId = null,
        CancellationToken ct = default);

    Task UninstallModAsync(string instanceDirectory, string projectId, CancellationToken ct = default);

    /// <summary>Toggles a mod on/off by renaming its file to/from "&lt;name&gt;.disabled" — Minecraft ignores
    /// the disabled extension, and the jar stays on disk so re-enabling is instant.</summary>
    Task<InstalledMod> SetEnabledAsync(string instanceDirectory, string projectId, bool enabled, CancellationToken ct = default);
}

public sealed class ModInstallService(
    IModrinthClient modrinthClient,
    IDownloadManager downloadManager,
    IInstalledModsStore store) : IModInstallService
{
    public Task<ModrinthSearchResult> SearchProjectsAsync(
        string query,
        string projectType,
        string? gameVersion,
        string? loader,
        string sortIndex,
        int offset = 0,
        CancellationToken ct = default) =>
        modrinthClient.SearchProjectsAsync(query, projectType, gameVersion, loader, sortIndex, offset, ct: ct);

    public Task<List<InstalledMod>> GetInstalledModsAsync(string instanceDirectory, CancellationToken ct = default) =>
        store.LoadAsync(instanceDirectory, ct);

    /// <summary>Modrinth project_type → the instance subfolder its files belong in.</summary>
    private static string FolderFor(string projectType) => projectType switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        _ => "mods",
    };

    public async Task<InstalledMod> InstallModAsync(
        string projectId,
        string title,
        string gameVersion,
        string loader,
        string instanceDirectory,
        IProgress<string>? progress = null,
        string projectType = "mod",
        string? versionId = null,
        CancellationToken ct = default)
    {
        var installed = await store.LoadAsync(instanceDirectory, ct);
        var result = await InstallRecursiveAsync(
            projectId, title, gameVersion, loader, instanceDirectory, installed, isDependency: false, progress, projectType, versionId, ct);
        await store.SaveAsync(instanceDirectory, installed, ct);
        return result;
    }

    private async Task<InstalledMod> InstallRecursiveAsync(
        string projectId,
        string title,
        string gameVersion,
        string loader,
        string instanceDirectory,
        List<InstalledMod> installed,
        bool isDependency,
        IProgress<string>? progress,
        string projectType,
        string? versionId = null,
        CancellationToken ct = default)
    {
        var existing = installed.FirstOrDefault(m => m.ProjectId == projectId);
        if (existing is not null)
        {
            return existing;
        }

        // Only mods actually depend on the instance's loader for compatibility — resource packs, shaders
        // and data packs are loader-agnostic, so filtering their version list by it would just find nothing.
        var effectiveLoader = projectType == "mod" ? loader : null;

        progress?.Report($"Поиск версии «{title}»...");
        var versions = await modrinthClient.GetProjectVersionsAsync(projectId, gameVersion, effectiveLoader, ct);
        var version = (versionId is not null ? versions.FirstOrDefault(v => v.Id == versionId) : null)
            ?? versions.FirstOrDefault()
            ?? throw new InvalidOperationException($"Для «{title}» нет сборки под {gameVersion}.");

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
            ?? throw new InvalidOperationException($"У версии «{version.Name}» «{title}» нет файлов.");

        var destination = Path.Combine(instanceDirectory, FolderFor(projectType), file.Filename);

        progress?.Report($"Загрузка «{title}»...");
        await downloadManager.DownloadFileAsync(file.Url, destination, file.Hashes.Sha1, ct: ct);

        var installedMod = new InstalledMod
        {
            ProjectId = projectId,
            VersionId = version.Id,
            Title = title,
            FileName = file.Filename,
            ProjectType = projectType,
            IsDependency = isDependency,
        };
        installed.Add(installedMod);

        foreach (var dependency in version.Dependencies.Where(d => d.DependencyType == "required" && d.ProjectId is not null))
        {
            if (installed.Any(m => m.ProjectId == dependency.ProjectId))
            {
                continue;
            }

            var depProject = await modrinthClient.GetProjectAsync(dependency.ProjectId!, ct);
            await InstallRecursiveAsync(
                dependency.ProjectId!,
                depProject?.Title ?? dependency.ProjectId!,
                gameVersion,
                loader,
                instanceDirectory,
                installed,
                isDependency: true,
                progress,
                projectType,
                versionId: null,
                ct);
        }

        return installedMod;
    }

    public async Task UninstallModAsync(string instanceDirectory, string projectId, CancellationToken ct = default)
    {
        var installed = await store.LoadAsync(instanceDirectory, ct);
        var mod = installed.FirstOrDefault(m => m.ProjectId == projectId);
        if (mod is null)
        {
            return;
        }

        var path = Path.Combine(instanceDirectory, FolderFor(mod.ProjectType), mod.FileName);
        var disabledPath = path + ".disabled";
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (File.Exists(disabledPath))
        {
            File.Delete(disabledPath);
        }

        installed.Remove(mod);
        await store.SaveAsync(instanceDirectory, installed, ct);
    }

    public async Task<InstalledMod> SetEnabledAsync(string instanceDirectory, string projectId, bool enabled, CancellationToken ct = default)
    {
        var installed = await store.LoadAsync(instanceDirectory, ct);
        var mod = installed.FirstOrDefault(m => m.ProjectId == projectId)
            ?? throw new InvalidOperationException("Мод не найден среди установленных.");

        if (mod.IsEnabled != enabled)
        {
            var enabledPath = Path.Combine(instanceDirectory, FolderFor(mod.ProjectType), mod.FileName);
            var disabledPath = enabledPath + ".disabled";

            if (enabled && File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath, overwrite: true);
            }
            else if (!enabled && File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath, overwrite: true);
            }

            mod.IsEnabled = enabled;
            await store.SaveAsync(instanceDirectory, installed, ct);
        }

        return mod;
    }
}
