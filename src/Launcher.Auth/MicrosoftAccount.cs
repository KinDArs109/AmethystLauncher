namespace Launcher.Auth;

public sealed class MicrosoftAccount(
    string username,
    Guid uuid,
    string minecraftAccessToken,
    string msaRefreshToken,
    DateTimeOffset minecraftTokenExpiresAt) : IGameAccount
{
    public string Username { get; } = username;
    public Guid Uuid { get; } = uuid;
    public string AccessToken { get; } = minecraftAccessToken;
    public string UserType => "msa";

    /// <summary>The long-lived Microsoft refresh token — the only credential that needs encrypted storage.</summary>
    public string MsaRefreshToken { get; } = msaRefreshToken;

    public DateTimeOffset MinecraftTokenExpiresAt { get; } = minecraftTokenExpiresAt;
}
