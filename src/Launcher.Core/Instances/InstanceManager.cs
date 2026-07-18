using System.Text.Json;

namespace Launcher.Core.Instances;

public interface IInstanceManager
{
    Task<IReadOnlyList<LauncherInstance>> GetInstancesAsync(CancellationToken ct = default);

    /// <summary>Returns the existing instance for this name/version, or creates a new one.</summary>
    Task<LauncherInstance> GetOrCreateInstanceAsync(
        string name,
        string versionId,
        string loaderType = "Vanilla",
        string? loaderVersion = null,
        CancellationToken ct = default);

    /// <summary>Removes the instance from the index and deletes its directory (saves, worlds, mods — everything).</summary>
    Task DeleteInstanceAsync(LauncherInstance instance, CancellationToken ct = default);

    Task<LauncherInstance> RenameInstanceAsync(LauncherInstance instance, string newName, CancellationToken ct = default);

    Task<LauncherInstance> MarkPlayedAsync(LauncherInstance instance, CancellationToken ct = default);

    /// <summary>Overwrites the stored entry for <paramref name="instance"/>'s directory with its current
    /// field values — used to persist settings edits (RAM, Java, window, group, etc.).</summary>
    Task<LauncherInstance> UpdateAsync(LauncherInstance instance, CancellationToken ct = default);

    /// <summary>Copies an instance's entire directory (mods, saves, settings) under a new name.</summary>
    Task<LauncherInstance> CloneInstanceAsync(LauncherInstance instance, string newName, CancellationToken ct = default);
}

public sealed class InstanceManager : IInstanceManager
{
    private readonly string _instancesRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "instances");

    private readonly string _indexPath;

    // Every mutation is read-modify-write over the whole index file, so two calls racing within this
    // process (e.g. a background download's MarkPlayedAsync landing mid-delete) could otherwise clobber
    // each other's changes — this serializes them. It does not protect against a second OS process
    // (e.g. the app launched twice) racing the same file; SaveAsync's atomic temp-then-move at least
    // guarantees a concurrent reader never sees a half-written or empty file in that case.
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public InstanceManager()
    {
        Directory.CreateDirectory(_instancesRoot);
        _indexPath = Path.Combine(_instancesRoot, "instances.json");
    }

    public async Task<IReadOnlyList<LauncherInstance>> GetInstancesAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        // A write from another process can land between the Exists check and the read below, or even
        // mid-read; retrying a couple of times a few milliseconds later rides out that window instead
        // of surfacing a transient JsonException to the whole Library page.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await using var stream = File.Open(_indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return await JsonSerializer.DeserializeAsync<List<LauncherInstance>>(stream, cancellationToken: ct) ?? [];
            }
            catch (JsonException) when (attempt < 3)
            {
                await Task.Delay(50, ct);
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(50, ct);
            }
        }

        try
        {
            await using var finalStream = File.Open(_indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<List<LauncherInstance>>(finalStream, cancellationToken: ct) ?? [];
        }
        catch (JsonException)
        {
            // Persistent (non-transient) parse failure — almost always a hand-edited file with raw
            // Windows paths ("C:\Users\..." instead of "C:\\Users\\..."). Try to repair instead of
            // bricking the whole Library page.
            return await TryRepairIndexAsync(ct);
        }
    }

    /// <summary>Re-escapes lone backslashes (the classic hand-edit mistake) and re-parses; on success the
    /// repaired index is saved back. If it still doesn't parse, the broken file is set aside as a
    /// timestamped .corrupt backup so the launcher starts with an empty library instead of an error.</summary>
    private async Task<IReadOnlyList<LauncherInstance>> TryRepairIndexAsync(CancellationToken ct)
    {
        var raw = await File.ReadAllTextAsync(_indexPath, ct);

        // Consume valid "\\" pairs atomically first so they're untouched; only a lone backslash that
        // doesn't start a legal JSON escape gets doubled.
        var repaired = System.Text.RegularExpressions.Regex.Replace(
            raw,
            @"(\\\\)|\\(?![""/bfnrtu])",
            m => m.Groups[1].Success ? m.Value : @"\\");

        try
        {
            var instances = JsonSerializer.Deserialize<List<LauncherInstance>>(repaired) ?? [];
            await SaveAsync(instances, ct);
            return instances;
        }
        catch (JsonException)
        {
            var backupPath = _indexPath + ".corrupt-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_indexPath, backupPath, overwrite: true);
            File.Delete(_indexPath);
            return [];
        }
    }

    public async Task<LauncherInstance> GetOrCreateInstanceAsync(
        string name,
        string versionId,
        string loaderType = "Vanilla",
        string? loaderVersion = null,
        CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var instances = (await GetInstancesAsync(ct)).ToList();

            var existing = instances.FirstOrDefault(i => i.VersionId == versionId && i.Name == name);
            if (existing is not null)
            {
                return existing;
            }

            var directory = Path.Combine(_instancesRoot, SanitizeFolderName(name));
            Directory.CreateDirectory(directory);

            var instance = new LauncherInstance
            {
                Name = name,
                VersionId = versionId,
                DirectoryPath = directory,
                LoaderType = loaderType,
                LoaderVersion = loaderVersion,
            };
            instances.Add(instance);
            await SaveAsync(instances, ct);
            return instance;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteInstanceAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var instances = (await GetInstancesAsync(ct)).Where(i => i.DirectoryPath != instance.DirectoryPath).ToList();
            await SaveAsync(instances, ct);

            if (Directory.Exists(instance.DirectoryPath))
            {
                Directory.Delete(instance.DirectoryPath, recursive: true);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<LauncherInstance> RenameInstanceAsync(LauncherInstance instance, string newName, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var instances = (await GetInstancesAsync(ct)).ToList();
            var index = instances.FindIndex(i => i.DirectoryPath == instance.DirectoryPath);
            if (index < 0)
            {
                throw new InvalidOperationException($"Сборка '{instance.Name}' не найдена.");
            }

            // Renaming only changes the display name — DirectoryPath stays put so nothing on disk needs moving.
            var renamed = CopyWith(instances[index], name: newName);
            instances[index] = renamed;
            await SaveAsync(instances, ct);
            return renamed;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<LauncherInstance> MarkPlayedAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var instances = (await GetInstancesAsync(ct)).ToList();
            var index = instances.FindIndex(i => i.DirectoryPath == instance.DirectoryPath);
            if (index < 0)
            {
                return instance;
            }

            instances[index].LastPlayedAt = DateTimeOffset.UtcNow;
            await SaveAsync(instances, ct);
            return instances[index];
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<LauncherInstance> UpdateAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var instances = (await GetInstancesAsync(ct)).ToList();
            var index = instances.FindIndex(i => i.DirectoryPath == instance.DirectoryPath);
            if (index < 0)
            {
                throw new InvalidOperationException($"Сборка '{instance.Name}' не найдена.");
            }

            instances[index] = instance;
            await SaveAsync(instances, ct);
            return instance;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<LauncherInstance> CloneInstanceAsync(LauncherInstance instance, string newName, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var instances = (await GetInstancesAsync(ct)).ToList();

            var directory = Path.Combine(_instancesRoot, SanitizeFolderName(newName));
            if (Directory.Exists(directory))
            {
                throw new InvalidOperationException($"Папка для сборки '{newName}' уже существует.");
            }

            CopyDirectory(instance.DirectoryPath, directory);

            var clone = CopyWith(instance, name: newName, directoryPath: directory, resetHistory: true);
            instances.Add(clone);
            await SaveAsync(instances, ct);
            return clone;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)));
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            // The instance's own persisted-log history belongs to the run history of the original
            // build, not the clone — skip it so "Клонировать" doesn't drag someone else's log files along.
            if (Path.GetFileName(subDir) == "launcher_logs")
            {
                continue;
            }

            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }

    private static LauncherInstance CopyWith(
        LauncherInstance source, string? name = null, string? directoryPath = null, bool resetHistory = false) => new()
    {
        Name = name ?? source.Name,
        VersionId = source.VersionId,
        DirectoryPath = directoryPath ?? source.DirectoryPath,
        LoaderType = source.LoaderType,
        LoaderVersion = source.LoaderVersion,
        CreatedAt = resetHistory ? DateTimeOffset.UtcNow : source.CreatedAt,
        LastPlayedAt = resetHistory ? null : source.LastPlayedAt,
        GroupTag = source.GroupTag,
        MinRamMb = source.MinRamMb,
        MaxRamMb = source.MaxRamMb,
        JavaPathOverride = source.JavaPathOverride,
        JvmArgs = source.JvmArgs,
        WindowWidth = source.WindowWidth,
        WindowHeight = source.WindowHeight,
        Fullscreen = source.Fullscreen,
        EnvVars = source.EnvVars is null ? null : new Dictionary<string, string>(source.EnvVars),
    };

    /// <summary>Writes to a uniquely-named temp file and renames over the index, so a reader (including
    /// another process) never observes a half-written or momentarily-empty file the way a direct
    /// File.Create write could. The temp name is unique per call (not just "<index>.tmp") so that a
    /// second, unsynchronized writer (e.g. a second OS process racing the in-process semaphore) writes
    /// its own file instead of interleaving bytes into the same temp file as this one.</summary>
    private async Task SaveAsync(List<LauncherInstance> instances, CancellationToken ct)
    {
        var tempPath = _indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, instances, new JsonSerializerOptions { WriteIndented = true }, ct);
            }

            File.Move(tempPath, _indexPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
