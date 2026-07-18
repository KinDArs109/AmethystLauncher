using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Launcher.Core.Download;
using Launcher.Core.Java;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;
using Microsoft.Extensions.Logging;

namespace Launcher.Loaders.Forge;

/// <summary>
/// Forge (1.13+) ships no meta API like Fabric/Quilt — instead we download its installer jar and run it
/// headless (`java -jar forge-installer.jar --installClient &lt;gameRoot&gt;`). The installer downloads the
/// vanilla client itself, patches it, and writes the resulting version JSON under
/// gameRoot/versions/&lt;id&gt;/&lt;id&gt;.json, which we then read back and merge with the vanilla version.
/// Pre-1.13 Forge used a different (patch-file based) installer format and isn't supported here.
/// </summary>
public sealed class ForgeLoaderInstaller(
    HttpClient httpClient,
    IDownloadManager downloadManager,
    IJavaRuntimeResolver javaRuntimeResolver,
    ILogger<ForgeLoaderInstaller> logger) : ILoaderInstaller
{
    private const string MavenBase = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    public ModLoaderType LoaderType => ModLoaderType.Forge;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
    {
        var xml = await httpClient.GetStringAsync($"{MavenBase}/maven-metadata.xml", ct);
        var doc = XDocument.Parse(xml);
        var prefix = minecraftVersion + "-";

        return doc.Descendants("version")
            .Select(e => e.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => new LoaderVersionInfo(v[prefix.Length..], Stable: true))
            .Reverse() // maven-metadata lists oldest first; newest-first reads better in the UI
            .ToList();
    }

    public async Task<VersionDetails> InstallAsync(
        string minecraftVersion,
        string loaderVersion,
        VersionDetails vanillaVersion,
        string gameRoot,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        var combinedVersion = $"{minecraftVersion}-{loaderVersion}";
        var installerUrl = $"{MavenBase}/{combinedVersion}/forge-{combinedVersion}-installer.jar";
        var installerPath = Path.Combine(GetInstallerCacheDir(), $"forge-{combinedVersion}-installer.jar");

        statusProgress?.Report("Скачивание установщика Forge...");
        await downloadManager.DownloadFileAsync(installerUrl, installerPath, expectedSha1: null, ct: ct);

        var expectedId = ReadEmbeddedVersionId(installerPath) ?? combinedVersion;
        var versionJsonPath = Path.Combine(gameRoot, "versions", expectedId, $"{expectedId}.json");

        if (!File.Exists(versionJsonPath))
        {
            EnsureLauncherProfilesStub(gameRoot);
            statusProgress?.Report("Установка Forge (это может занять пару минут)...");
            await RunInstallerAsync(installerPath, gameRoot, vanillaVersion, ct);
        }

        if (!File.Exists(versionJsonPath))
        {
            // The installer's own version.json id didn't match what actually got written to disk;
            // fall back to scanning for whatever new version folder just appeared.
            versionJsonPath = FindNewestVersionJson(gameRoot, minecraftVersion, loaderVersion)
                ?? throw new InvalidOperationException(
                    $"Установщик Forge отработал, но файл версии не найден (ожидался '{versionJsonPath}').");
        }

        await using var stream = File.OpenRead(versionJsonPath);
        var profile = await JsonSerializer.DeserializeAsync<VersionDetails>(stream, cancellationToken: ct)
            ?? throw new InvalidOperationException("Не удалось разобрать сгенерированный Forge version.json.");

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
            ?? throw new InvalidOperationException("Не удалось запустить установщик Forge.");

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("[Forge installer] {Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("[Forge installer] {Line}", e.Data); };
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
            throw new TimeoutException("Установщик Forge не завершился за 10 минут.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Установщик Forge завершился с кодом {process.ExitCode}.");
        }
    }

    /// <summary>Reads the "id" field out of version.json inside the installer jar without running anything.</summary>
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

    private static string? FindNewestVersionJson(string gameRoot, string minecraftVersion, string loaderVersion)
    {
        var versionsDir = Path.Combine(gameRoot, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return null;
        }

        return Directory.EnumerateDirectories(versionsDir)
            .Select(dir => Path.Combine(dir, Path.GetFileName(dir) + ".json"))
            .Where(File.Exists)
            .Where(path => Path.GetFileName(path).Contains(minecraftVersion) && Path.GetFileName(path).Contains(loaderVersion))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// The Forge installer refuses to run ("There is no Minecraft launcher profile ... you need to run the
    /// launcher first!") unless it finds a launcher_profiles.json in the target dir — a leftover sanity check
    /// from the official launcher it doesn't skip even in --installClient mode. A minimal stub satisfies it.
    /// </summary>
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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "cache", "forge-installers");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
