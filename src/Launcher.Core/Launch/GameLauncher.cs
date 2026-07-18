using System.Diagnostics;
using Launcher.Core.Download;
using Launcher.Core.Versions;
using Microsoft.Extensions.Logging;

namespace Launcher.Core.Launch;

public sealed record LaunchRequest(
    VersionDetails Version,
    string InstanceDirectory,
    string PlayerName,
    Guid PlayerUuid,
    string AccessToken,
    string UserType,
    int MinRamMb,
    int MaxRamMb,
    string? JavaPathOverride,
    string? ExtraJvmArgs = null,
    int? WindowWidth = null,
    int? WindowHeight = null,
    bool Fullscreen = false,
    IReadOnlyDictionary<string, string>? EnvVars = null);

public interface IGameLauncher
{
    /// <summary>
    /// <paramref name="onOutputLine"/>, if given, is called for every line the game process writes to
    /// stdout/stderr (line, isErrorStream) — from a background thread, same as Process.OutputDataReceived.
    /// </summary>
    Task<Process> LaunchAsync(
        LaunchRequest request,
        InstallProgress? progress = null,
        Action<string, bool>? onOutputLine = null,
        CancellationToken ct = default);
}

public sealed class GameLauncher(
    IInstancePreparer instancePreparer,
    IArgumentBuilder argumentBuilder,
    ILogger<GameLauncher> logger) : IGameLauncher
{
    public async Task<Process> LaunchAsync(
        LaunchRequest request,
        InstallProgress? progress = null,
        Action<string, bool>? onOutputLine = null,
        CancellationToken ct = default)
    {
        var gameRoot = InstancePreparer.GetSharedGameRoot();

        // Normally a fast no-op (everything's already on disk from instance creation) — re-verifying here
        // means a launch still succeeds even if something was deleted or the initial download failed midway.
        var prepared = await instancePreparer.PrepareAsync(
            request.Version, request.InstanceDirectory, request.JavaPathOverride, progress, ct);

        var context = new LaunchContext(
            request.Version,
            request.InstanceDirectory,
            Path.Combine(gameRoot, "assets"),
            prepared.NativesDirectory,
            ClasspathBuilder.Build(prepared.ClasspathJars),
            request.PlayerName,
            request.PlayerUuid,
            request.AccessToken,
            request.UserType);

        var jvmArgs = argumentBuilder.BuildJvmArguments(context, request.MinRamMb, request.MaxRamMb, request.ExtraJvmArgs);
        var gameArgs = argumentBuilder.BuildGameArguments(context, request.WindowWidth, request.WindowHeight);

        if (request.Fullscreen)
        {
            ApplyFullscreenOption(request.InstanceDirectory);
        }

        var startInfo = new ProcessStartInfo(prepared.JavaExecutablePath)
        {
            WorkingDirectory = request.InstanceDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (request.EnvVars is not null)
        {
            foreach (var (key, value) in request.EnvVars)
            {
                startInfo.Environment[key] = value;
            }
        }

        foreach (var arg in jvmArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }
        startInfo.ArgumentList.Add(request.Version.MainClass);
        foreach (var arg in gameArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        progress?.SetStatus("Запуск игры...");
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить процесс Minecraft.");
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            logger.LogInformation("[MC] {Line}", e.Data);
            onOutputLine?.Invoke(e.Data, false);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            logger.LogWarning("[MC] {Line}", e.Data);
            onOutputLine?.Invoke(e.Data, true);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>Vanilla has no "--fullscreen" launch flag — the game only reads it from options.txt on
    /// startup — so honoring the per-instance fullscreen toggle means patching that file beforehand.</summary>
    private static void ApplyFullscreenOption(string instanceDirectory)
    {
        var optionsPath = Path.Combine(instanceDirectory, "options.txt");
        var lines = File.Exists(optionsPath)
            ? File.ReadAllLines(optionsPath).ToList()
            : [];

        var index = lines.FindIndex(l => l.StartsWith("fullscreen:", StringComparison.Ordinal));
        if (index >= 0)
        {
            lines[index] = "fullscreen:true";
        }
        else
        {
            lines.Add("fullscreen:true");
        }

        File.WriteAllLines(optionsPath, lines);
    }
}
