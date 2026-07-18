using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Launcher.Core.Download;
using Launcher.Core.Java;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;
using Microsoft.Extensions.Logging;

namespace Launcher.Loaders.NeoForge;

/// <summary>
/// NeoForge is a fork of Forge and installs the same way: download its installer jar and run it headless
/// (`java -jar neoforge-&lt;version&gt;-installer.jar --installClient &lt;gameRoot&gt;`). The installer patches the
/// vanilla client and writes gameRoot/versions/&lt;id&gt;/&lt;id&gt;.json, which we read back and merge.
///
/// Version scheme differs from Forge: for Minecraft 1.20.2+ a NeoForge version looks like
/// <c>&lt;mcMinor&gt;.&lt;mcPatch&gt;.&lt;build&gt;</c> (e.g. 1.21.1 → 21.1.x, 1.20.4 → 20.4.x), so we filter the maven
/// metadata by that series prefix. Minecraft 1.20.1 used the legacy 47.x line under a different artifact
/// and isn't offered here.
/// </summary>
public sealed class NeoForgeLoaderInstaller(
    HttpClient httpClient,
    IDownloadManager downloadManager,
    IJavaRuntimeResolver javaRuntimeResolver,
    ILogger<NeoForgeLoaderInstaller> logger) : ILoaderInstaller
{
    private const string MavenBase = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    public ModLoaderType LoaderType => ModLoaderType.NeoForge;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
    {
        var series = ToNeoForgeSeries(minecraftVersion);
        if (series is null)
        {
            return [];
        }

        var xml = await httpClient.GetStringAsync($"{MavenBase}/maven-metadata.xml", ct);
        var doc = XDocument.Parse(xml);
        var prefix = series + ".";

        return doc.Descendants("version")
            .Select(e => e.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => new LoaderVersionInfo(v, Stable: !v.Contains("-beta", StringComparison.OrdinalIgnoreCase)))
            .Reverse() // maven-metadata lists oldest first; newest-first reads better in the UI
            .ToList();
    }

    /// <summary>Maps a Minecraft release ("1.21.1", "1.20.4", "1.21") to the NeoForge version series
    /// ("21.1", "20.4", "21.0"). Returns null for versions older than 1.20.2 (legacy 47.x line).</summary>
    private static string? ToNeoForgeSeries(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length < 2 || parts[0] != "1" || !int.TryParse(parts[1], out var minor))
        {
            return null;
        }

        var patch = parts.Length >= 3 && int.TryParse(parts[2], out var p) ? p : 0;

        // NeoForge (net.neoforged:neoforge) covers 1.20.2 and up; 1.20.1/1.20 used the legacy line.
        if (minor < 20 || (minor == 20 && patch < 2))
        {
            return null;
        }

        return $"{minor}.{patch}";
    }

    public async Task<VersionDetails> InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        VersionDetails vanillaVersion,
        string gameRoot,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        var installerUrl = $"{MavenBase}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
        var installerPath = Path.Combine(GetInstallerCacheDir(), $"neoforge-{loaderVersion}-installer.jar");

        statusProgress?.Report("Скачивание установщика NeoForge...");
        await downloadManager.DownloadFileAsync(installerUrl, installerPath, expectedSha1: null, ct: ct);

        var expectedId = ReadEmbeddedVersionId(installerPath) ?? $"neoforge-{loaderVersion}";
        var versionJsonPath = Path.Combine(gameRoot, "versions", expectedId, $"{expectedId}.json");

        if (!File.Exists(versionJsonPath))
        {
            EnsureLauncherProfilesStub(gameRoot);
            statusProgress?.Report("Установка NeoForge (это может занять пару минут)...");
            await RunInstallerAsync(installerPath, gameRoot, vanillaVersion, ct);
        }

        if (!File.Exists(versionJsonPath))
        {
            versionJsonPath = FindNewestVersionJson(gameRoot, loaderVersion)
                ?? throw new InvalidOperationException(
                    $"Установщик NeoForge отработал, но файл версии не найден (ожидался '{versionJsonPath}').");
        }

        await using var stream = File.OpenRead(versionJsonPath);
        var profile = await JsonSerializer.DeserializeAsync<VersionDetails>(stream, cancellationToken: ct)
            ?? throw new InvalidOperationException("Не удалось разобрать сгенерированный NeoForge version.json.");

        return VersionMerger.Merge(vanillaVersion, profile);
    }

    private async Task RunInstallerAsync(string installerPath, string gameRoot, VersionDetails vanillaVersion, CancellationToken ct)
    {
        var javaPath = await javaRuntimeResolver.ResolveJavaExecutableAsync(vanillaVersion, manualOverridePath: null, ct: ct);

        var startInfo = new ProcessStartInfo(javaPath)
        {
            WorkingDirectory = Path.GetDirectoryName(installerPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("--installClient");
        startInfo.ArgumentList.Add(gameRoot);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить установщик NeoForge.");

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("[NeoForge installer] {Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("[NeoForge installer] {Line}", e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(InstallTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Установщик NeoForge не завершился за 10 минут.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Установщик NeoForge завершился с кодом {process.ExitCode}.");
        }
    }

    private static string? ReadEmbeddedVersionId(string installerPath)
    {
        using var zip = ZipFile.OpenRead(installerPath);
        var entry = zip.GetEntry("version.json");
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
    }

    private static string? FindNewestVersionJson(string gameRoot, string loaderVersion)
    {
        var versionsDir = Path.Combine(gameRoot, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return null;
        }

        return Directory.EnumerateDirectories(versionsDir)
            .Select(dir => Path.Combine(dir, Path.GetFileName(dir) + ".json"))
            .Where(File.Exists)
            .Where(path => Path.GetFileName(path).Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                           && Path.GetFileName(path).Contains(loaderVersion))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void EnsureLauncherProfilesStub(string gameRoot)
    {
        var path = Path.Combine(gameRoot, "launcher_profiles.json");
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(path, """{"profiles":{},"settings":{"enableSnapshots":false,"enableAdvanced":false,"keepLauncherOpen":false},"version":3}""");
    }

    private static string GetInstallerCacheDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "cache", "neoforge-installers");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
