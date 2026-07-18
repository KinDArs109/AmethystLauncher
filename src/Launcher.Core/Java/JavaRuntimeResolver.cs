using Launcher.Core.Download;
using Launcher.Core.Versions;

namespace Launcher.Core.Java;

public interface IJavaRuntimeResolver
{
    Task<string> ResolveJavaExecutableAsync(
        VersionDetails version, string? manualOverridePath, InstallProgress? progress = null, CancellationToken ct = default);
}

public sealed class JavaRuntimeResolver(IJreProvisioner jreProvisioner) : IJavaRuntimeResolver
{
    public Task<string> ResolveJavaExecutableAsync(
        VersionDetails version, string? manualOverridePath, InstallProgress? progress = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(manualOverridePath) && File.Exists(manualOverridePath))
        {
            return Task.FromResult(manualOverridePath);
        }

        // Very old versions (pre-1.17) don't declare javaVersion at all; they all ran on Java 8.
        var majorVersion = version.JavaVersion?.MajorVersion ?? 8;
        return jreProvisioner.EnsureJavaAsync(majorVersion, progress, ct);
    }
}
