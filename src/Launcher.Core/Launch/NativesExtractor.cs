using System.IO.Compression;

namespace Launcher.Core.Launch;

public interface INativesExtractor
{
    /// <summary>Extracts the given native jars into a fresh per-launch directory and returns its path.</summary>
    string ExtractNatives(IReadOnlyList<string> nativeJars, string instanceDirectory);
}

public sealed class NativesExtractor : INativesExtractor
{
    public string ExtractNatives(IReadOnlyList<string> nativeJars, string instanceDirectory)
    {
        var nativesDir = Path.Combine(instanceDirectory, "natives", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nativesDir);

        foreach (var jarPath in nativeJars)
        {
            using var archive = ZipFile.OpenRead(jarPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // directory entry or jar metadata, nothing to extract
                }

                entry.ExtractToFile(Path.Combine(nativesDir, entry.Name), overwrite: true);
            }
        }

        return nativesDir;
    }
}
