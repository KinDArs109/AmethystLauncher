using System.IO;

namespace Launcher.App.Services;

/// <summary>Reads &lt;instance&gt;/saves — single-player worlds only, no servers.dat NBT parsing. Shared by
/// the instance Worlds tab and Home's "Jump back in" so both enumerate saves identically.</summary>
public static class WorldScanner
{
    public static IEnumerable<(string Name, string FullPath, DateTimeOffset Modified)> ScanSaves(string instanceDirectory)
    {
        var savesDir = Path.Combine(instanceDirectory, "saves");
        if (!Directory.Exists(savesDir))
        {
            yield break;
        }

        foreach (var dir in Directory.GetDirectories(savesDir))
        {
            var info = new DirectoryInfo(dir);
            yield return (info.Name, dir, info.LastWriteTimeUtc);
        }
    }
}
