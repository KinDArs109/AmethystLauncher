using Launcher.Core.Versions;

namespace Launcher.Core.Launch;

public sealed record LaunchContext(
    VersionDetails Version,
    string GameDirectory,
    string AssetsRoot,
    string NativesDirectory,
    string Classpath,
    string PlayerName,
    Guid PlayerUuid,
    string AccessToken,
    string UserType);
