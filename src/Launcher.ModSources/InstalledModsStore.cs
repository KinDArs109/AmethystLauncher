using System.Text.Json;

namespace Launcher.ModSources;

public interface IInstalledModsStore
{
    Task<List<InstalledMod>> LoadAsync(string instanceDirectory, CancellationToken ct = default);

    Task SaveAsync(string instanceDirectory, List<InstalledMod> mods, CancellationToken ct = default);
}

/// <summary>
/// Tracks which jars in an instance's mods/ folder were installed by the launcher (and from where),
/// since a plain directory listing can't tell a Modrinth project id or a "was this only a dependency" apart.
/// </summary>
public sealed class InstalledModsStore : IInstalledModsStore
{
    private const string FileName = "mods.launcher.json";

    public async Task<List<InstalledMod>> LoadAsync(string instanceDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(instanceDirectory, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<InstalledMod>>(stream, cancellationToken: ct) ?? [];
    }

    public async Task SaveAsync(string instanceDirectory, List<InstalledMod> mods, CancellationToken ct = default)
    {
        var path = Path.Combine(instanceDirectory, FileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, mods, new JsonSerializerOptions { WriteIndented = true }, ct);
    }
}
