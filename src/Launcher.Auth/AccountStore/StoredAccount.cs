namespace Launcher.Auth.AccountStore;

public enum AccountKind
{
    Offline,
    Microsoft,
}

public sealed class StoredAccount
{
    public required AccountKind Kind { get; init; }
    public required string Username { get; set; }
    public required Guid Uuid { get; init; }

    /// <summary>DPAPI-protected (current user scope) MSA refresh token; only set for <see cref="AccountKind.Microsoft"/>.</summary>
    public string? ProtectedMsaRefreshTokenBase64 { get; set; }
}
