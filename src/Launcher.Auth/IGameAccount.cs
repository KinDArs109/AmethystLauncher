namespace Launcher.Auth;

/// <summary>
/// A launchable Minecraft identity — either an offline nickname profile (see <c>Offline/</c>) or,
/// from Phase 2 onward, an authenticated Microsoft/Xbox account. Both shapes plug into the same
/// launch pipeline via <c>UserType</c> ("legacy" for offline, "msa" for Microsoft accounts).
/// </summary>
public interface IGameAccount
{
    string Username { get; }
    Guid Uuid { get; }
    string AccessToken { get; }
    string UserType { get; }
}
