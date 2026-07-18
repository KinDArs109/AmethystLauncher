using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

/// <summary>Controls the bundled zapret DPI-bypass (winws.exe). zapret defeats ISP deep-packet
/// inspection at the OS level, so once it runs, the whole machine can reach Modrinth/Supabase even
/// where a Russian provider blocks them — the launcher needs no per-request proxying.
///
/// winws.exe loads the WinDivert kernel driver, which requires admin rights. To avoid a UAC prompt
/// (and a flashing console window) on every toggle, the bypass is driven through two Windows Scheduled
/// Tasks registered once with "highest privileges": starting/stopping then only needs
/// <c>schtasks /run</c>, which carries the task's own elevated token — no prompt, no window. The
/// one-time registration is the single UAC prompt the user ever sees. If the Task Scheduler path fails
/// for any reason, we fall back to the legacy <c>ShellExecute verb=runas</c> on the .bat files.</summary>
public interface IBypassService
{
    bool IsRunning { get; }

    /// <summary>Starts the bypass. The first ever call registers the scheduled tasks (one UAC prompt);
    /// later calls are silent. Returns false if the user declined elevation or the files are missing.</summary>
    Task<bool> StartAsync();

    /// <summary>Stops the bypass. Silent once the tasks are registered. Returns false on failure.</summary>
    Task<bool> StopAsync();
}

public sealed class BypassService(ILogger<BypassService> logger) : IBypassService
{
    private const string TaskFolder = "AmethystLauncher";
    private const string StartTaskName = TaskFolder + "\\Bypass";
    private const string StopTaskName = TaskFolder + "\\BypassStop";

    // Bump when the task definitions change (not just the path) so existing installs re-register the
    // tasks on the next start instead of silently keeping a stale definition. v2 = hidden VBS launcher.
    private const string TaskSchemaVersion = "v2";

    private static readonly string ZapretDirectory = Path.Combine(AppContext.BaseDirectory, "zapret");
    private static readonly string BinDirectory = Path.Combine(ZapretDirectory, "bin");
    private static readonly string ListsDirectory = Path.Combine(ZapretDirectory, "lists");
    private static readonly string WinwsPath = Path.Combine(BinDirectory, "winws.exe");

    // Records the winws.exe path the scheduled tasks were last registered against. Comparing this file
    // to the current path (instead of parsing schtasks' console output) sidesteps stdout code-page
    // issues with non-ASCII install paths, and lets us detect when an update moved the app so the tasks
    // must be re-registered against the new location.
    private static readonly string RegisteredPathMarker = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncher",
        "bypass-tasks-path.txt");

    // Helper scripts the start task launches. winws.exe is a console app, so running it directly (even
    // from a Hidden task) still flashes a console window in the interactive session. We instead have the
    // task run a VBScript that starts winws with window style 0 (fully hidden) — no window ever appears.
    private static readonly string HelperDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncher",
        "bypass");
    private static readonly string RunCmdPath = Path.Combine(HelperDir, "run-bypass.cmd");
    private static readonly string RunVbsPath = Path.Combine(HelperDir, "run-hidden.vbs");

    /// <summary>What the marker records: the install path plus the task-schema version, so either a moved
    /// install or a changed task definition triggers a one-time re-registration.</summary>
    private static string MarkerContent => $"{WinwsPath}|{TaskSchemaVersion}";

    public bool IsRunning => Process.GetProcessesByName("winws").Length > 0;

    public async Task<bool> StartAsync()
    {
        if (!File.Exists(WinwsPath))
        {
            logger.LogError("Bypass binary not found: {Path}", WinwsPath);
            return false;
        }

        try
        {
            if (!TasksRegisteredForCurrentPath())
            {
                if (!await RegisterTasksElevatedAsync())
                {
                    // Registration failed or was declined — try the legacy elevated-bat path so the
                    // user can still turn the bypass on (it just prompts for UAC each time).
                    return await LegacyRunElevatedAsync("zapret-launcher.bat");
                }
            }

            return await RunScheduledTaskAsync(StartTaskName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled-task start failed; falling back to elevated bat");
            return await LegacyRunElevatedAsync("zapret-launcher.bat");
        }
    }

    public async Task<bool> StopAsync()
    {
        try
        {
            if (TaskExists(StopTaskName))
            {
                return await RunScheduledTaskAsync(StopTaskName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled-task stop failed; falling back to elevated bat");
        }

        // No stop task registered (or it failed) — fall back to the legacy elevated cleanup script.
        return await LegacyRunElevatedAsync("zapret-stop.bat");
    }

    // ---- Scheduled task plumbing ----

    /// <summary>True only when both tasks exist AND were registered against the winws.exe of the current
    /// install. After a launcher update the install path changes, so this returns false and the tasks
    /// are re-registered (one fresh UAC prompt) against the new path.</summary>
    private bool TasksRegisteredForCurrentPath()
    {
        if (!TaskExists(StartTaskName) || !TaskExists(StopTaskName))
        {
            return false;
        }

        try
        {
            return File.Exists(RegisteredPathMarker)
                   && string.Equals(File.ReadAllText(RegisteredPathMarker).Trim(), MarkerContent, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool TaskExists(string taskName) =>
        RunSchtasks($"/query /tn \"{taskName}\"") == 0;

    /// <summary>Writes both task XMLs and registers them in a single elevated shell-out — the one and
    /// only UAC prompt. Returns false if the user declined or registration errored.</summary>
    private async Task<bool> RegisterTasksElevatedAsync()
    {
        await WriteHelperScriptsAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), "amethyst-bypass");
        Directory.CreateDirectory(tempDir);
        var startXmlPath = Path.Combine(tempDir, "bypass-start.xml");
        var stopXmlPath = Path.Combine(tempDir, "bypass-stop.xml");

        await File.WriteAllTextAsync(startXmlPath, BuildStartTaskXml(), Encoding.Unicode);
        await File.WriteAllTextAsync(stopXmlPath, BuildStopTaskXml(), Encoding.Unicode);

        // One elevated cmd registers both tasks. /f overwrites any stale definition from a prior install.
        var command =
            $"/c schtasks /create /f /tn \"{StartTaskName}\" /xml \"{startXmlPath}\" " +
            $"&& schtasks /create /f /tn \"{StopTaskName}\" /xml \"{stopXmlPath}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                logger.LogError("Bypass task registration exited with code {Code}", process.ExitCode);
                return false;
            }

            // Record which install path the tasks now point at, so we don't re-register (and re-prompt)
            // until the app actually moves.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RegisteredPathMarker)!);
                await File.WriteAllTextAsync(RegisteredPathMarker, MarkerContent);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write bypass task path marker");
            }

            logger.LogInformation("Bypass scheduled tasks registered");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logger.LogInformation("User declined elevation for bypass task registration");
            return false;
        }
    }

    /// <summary>Runs a registered task via <c>schtasks /run</c> — silent, windowless, and (because the
    /// task itself is elevated) without a UAC prompt.</summary>
    private async Task<bool> RunScheduledTaskAsync(string taskName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/run /tn \"{taskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            logger.LogError("schtasks /run {Task} failed ({Code}): {Error}", taskName, process.ExitCode, err.Trim());
            return false;
        }

        return true;
    }

    private static int RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    // ---- Task XML builders ----

    /// <summary>Generates the hidden-launch helper pair: a .cmd that runs winws in the foreground and a
    /// .vbs that launches that .cmd with a hidden window. The task runs the .vbs, so winws never shows a
    /// console. The .cmd is UTF-8 with chcp 65001 (the install path may contain non-ASCII characters);
    /// the .vbs is UTF-16 so cscript reads any non-ASCII path in it correctly.</summary>
    private async Task WriteHelperScriptsAsync()
    {
        Directory.CreateDirectory(HelperDir);

        var cmd = string.Join("\r\n",
            "@echo off",
            "chcp 65001 >nul",
            $"cd /d \"{BinDirectory}\"",
            "netsh interface tcp set global timestamps=enabled >nul 2>&1",
            $"\"{WinwsPath}\" {BuildWinwsArguments()}");
        await File.WriteAllTextAsync(RunCmdPath, cmd, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // In VBScript a doubled quote ("") is a literal quote inside a string, so the Run argument below
        // resolves to:  cmd /c "<RunCmdPath>"  — launched with window style 0 (hidden), fire-and-forget.
        var vbs = "CreateObject(\"WScript.Shell\").Run \"cmd /c \"\"" + RunCmdPath + "\"\"\", 0, False";
        await File.WriteAllTextAsync(RunVbsPath, vbs, Encoding.Unicode);
    }

    private string BuildStartTaskXml()
    {
        var vbsArg = XmlEscape($"\"{RunVbsPath}\"");
        var user = XmlEscape(CurrentUserId());

        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Amethyst Launcher DPI bypass (zapret winws)</Description>
          </RegistrationInfo>
          <Principals>
            <Principal id="Author">
              <UserId>{user}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>true</Hidden>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>wscript.exe</Command>
              <Arguments>{vbsArg}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    private string BuildStopTaskXml()
    {
        var user = XmlEscape(CurrentUserId());

        // Three plain Exec actions (no cmd.exe wrapper) so nothing flashes a console. taskkill ends
        // winws; net stop + sc delete unload the WinDivert driver so it doesn't linger between sessions.
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Amethyst Launcher DPI bypass — stop and clean up</Description>
          </RegistrationInfo>
          <Principals>
            <Principal id="Author">
              <UserId>{user}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>true</Hidden>
            <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>taskkill.exe</Command>
              <Arguments>/IM winws.exe /F</Arguments>
            </Exec>
            <Exec>
              <Command>net.exe</Command>
              <Arguments>stop WinDivert</Arguments>
            </Exec>
            <Exec>
              <Command>sc.exe</Command>
              <Arguments>delete WinDivert</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    /// <summary>Derives the winws.exe argument string from the bundled zapret-launcher.bat, so the
    /// tested strategy stays the single source of truth: we parse out the command after winws.exe,
    /// join the caret-continued lines, and expand the bat's %BIN%/%LISTS%/game-filter variables to
    /// absolute paths for this install.</summary>
    private static string BuildWinwsArguments()
    {
        var batPath = Path.Combine(ZapretDirectory, "zapret-launcher.bat");
        var text = File.ReadAllText(batPath);

        // Collapse "caret + newline" line continuations into a single logical line.
        text = Regex.Replace(text, @"\^\s*\r?\n\s*", " ");

        // Grab everything after the winws.exe invocation on the (now single) start line.
        var match = Regex.Match(text, @"winws\.exe""\s*(?<args>.+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not parse winws arguments from zapret-launcher.bat");
        }

        var args = match.Groups["args"].Value.Trim();

        // Expand the bat variables. %BIN% and %LISTS% end with a backslash in the bat.
        args = args
            .Replace("%BIN%", BinDirectory + "\\", StringComparison.OrdinalIgnoreCase)
            .Replace("%LISTS%", ListsDirectory + "\\", StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterTCP%", "12", StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterUDP%", "12", StringComparison.OrdinalIgnoreCase);

        return args;
    }

    private static string CurrentUserId()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value
                   ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
        catch
        {
            return $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
    }

    private static string XmlEscape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    // ---- Legacy fallback (per-toggle UAC) ----

    private Task<bool> LegacyRunElevatedAsync(string batFileName)
    {
        var batPath = Path.Combine(ZapretDirectory, batFileName);
        if (!File.Exists(batPath))
        {
            logger.LogError("Bypass script not found: {Path}", batPath);
            return Task.FromResult(false);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = batPath,
                WorkingDirectory = ZapretDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi);
            return Task.FromResult(true);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logger.LogInformation("User declined elevation for the bypass");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run bypass script {File}", batFileName);
            return Task.FromResult(false);
        }
    }
}
