namespace Launcher.Auth.Offline;

public sealed class OfflineAccount(string username, Guid uuid) : IGameAccount
{
    public string Username { get; } = username;
    public Guid Uuid { get; } = uuid;

    // Offline sessions never talk to Mojang's session servers, so no real token exists;
    // the game only checks that this is non-empty, its value is otherwise irrelevant.
    public string AccessToken => "0";

    public string UserType => "legacy";
}
