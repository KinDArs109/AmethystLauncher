using System.Text.Json;

namespace Launcher.Core.Settings;

public sealed class LauncherSettings
{
    public int MinRamMb { get; set; } = 1024;
    public int MaxRamMb { get; set; } = 4096;
    public string? JavaPathOverride { get; set; }

    /// <summary>Offline-mode nickname used when <see cref="UseMicrosoftAccount"/> is false.</summary>
    public string PreferredNickname { get; set; } = "Player";

    /// <summary>Which identity "Играть" resolves to: the stored Microsoft account, or the offline nickname.</summary>
    public bool UseMicrosoftAccount { get; set; }

    /// <summary>Username of the currently signed-in local (username+password) account, if any — set on
    /// successful local sign-in/registration, cleared on local sign-out. Signing in locally also sets
    /// <see cref="PreferredNickname"/> to this same username, since local accounts play under the
    /// offline-profile identity (there is no separate server backing them).</summary>
    public string? LocalAccountUsername { get; set; }

    /// <summary>
    /// Azure AD "Application (client) ID" — retained only so old settings.json files keep deserializing;
    /// Microsoft account support has been removed from the launcher.
    /// </summary>
    public string? MicrosoftClientId { get; set; }

    /// <summary>Whether the bundled zapret DPI-bypass should be running (to reach Modrinth/Supabase when
    /// a Russian ISP blocks them). Remembered so the launcher can re-arm it on the next start.</summary>
    public bool BypassEnabled { get; set; }
}

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(LauncherSettings settings, CancellationToken ct = default);
}

public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "settings.json");

    public async Task<LauncherSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new LauncherSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, cancellationToken: ct) ?? new LauncherSettings();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, ct);
    }
}
