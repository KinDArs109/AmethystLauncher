using Launcher.Auth.MinecraftServices;
using Launcher.Auth.Microsoft;
using Launcher.Auth.Xbox;

namespace Launcher.Auth;

public interface IMicrosoftAuthenticator
{
    /// <summary>
    /// Runs an interactive sign-in: builds the Microsoft authorize URL (with PKCE) and hands it to
    /// <paramref name="obtainAuthorizationCode"/>, which is expected to show it in a browser/WebView2,
    /// capture the redirect back to <see cref="RedirectUri"/>, and return the "code" query parameter.
    /// </summary>
    Task<MicrosoftAccount> SignInInteractiveAsync(
        string clientId,
        Func<string, CancellationToken, Task<string>> obtainAuthorizationCode,
        CancellationToken ct = default);

    Task<MicrosoftAccount> RefreshAsync(string clientId, string msaRefreshToken, CancellationToken ct = default);
}

/// <summary>
/// Runs the full chain a third-party launcher must complete for an "Original" account:
/// MSA (auth code + PKCE) → XBL → XSTS → Minecraft Services → entitlement check → profile.
/// The entitlement check is never skipped — it's the actual ownership verification.
/// </summary>
public sealed class MicrosoftAuthenticator(
    IMsaTokenClient msaTokenClient,
    IXblAuthClient xblAuthClient,
    IXstsAuthClient xstsAuthClient,
    IMinecraftAuthClient minecraftAuthClient,
    IEntitlementChecker entitlementChecker,
    IProfileClient profileClient) : IMicrosoftAuthenticator
{
    /// <summary>
    /// Microsoft's own "blank" redirect target made for exactly this purpose (native/desktop apps capturing
    /// the auth code from the URL before the page loads) — no server of our own needed. Must be added as a
    /// redirect URI under the Azure app registration's "Mobile and desktop applications" platform.
    /// </summary>
    public const string RedirectUri = "https://login.microsoftonline.com/common/oauth2/nativeclient";

    private const string Scope = "XboxLive.signin offline_access";

    public async Task<MicrosoftAccount> SignInInteractiveAsync(
        string clientId,
        Func<string, CancellationToken, Task<string>> obtainAuthorizationCode,
        CancellationToken ct = default)
    {
        var (verifier, challenge) = PkceHelper.Generate();
        var authorizeUrl = BuildAuthorizeUrl(clientId, challenge);

        var code = await obtainAuthorizationCode(authorizeUrl, ct);
        var msaToken = await msaTokenClient.ExchangeAuthorizationCodeAsync(clientId, code, RedirectUri, verifier, ct);

        return await CompleteAsync(msaToken, ct);
    }

    public async Task<MicrosoftAccount> RefreshAsync(string clientId, string msaRefreshToken, CancellationToken ct = default)
    {
        var msaToken = await msaTokenClient.RefreshAsync(clientId, msaRefreshToken, ct);
        return await CompleteAsync(msaToken, ct);
    }

    private static string BuildAuthorizeUrl(string clientId, string codeChallenge)
    {
        var redirect = Uri.EscapeDataString(RedirectUri);
        var scope = Uri.EscapeDataString(Scope);
        return "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize" +
               $"?client_id={Uri.EscapeDataString(clientId)}" +
               "&response_type=code" +
               $"&redirect_uri={redirect}" +
               "&response_mode=query" +
               $"&scope={scope}" +
               $"&code_challenge={codeChallenge}" +
               "&code_challenge_method=S256";
    }

    private async Task<MicrosoftAccount> CompleteAsync(MsaTokenResponse msaToken, CancellationToken ct)
    {
        var xbl = await xblAuthClient.AuthenticateAsync(msaToken.AccessToken, ct);
        var xsts = await xstsAuthClient.AuthorizeAsync(xbl.Token, ct);

        var userHash = xsts.DisplayClaims.Xui.FirstOrDefault()?.Uhs
            ?? throw new MicrosoftAuthException("XSTS не вернул идентификатор пользователя (uhs).");

        var mcAuth = await minecraftAuthClient.LoginWithXboxAsync(userHash, xsts.Token, ct);

        var owns = await entitlementChecker.OwnsMinecraftAsync(mcAuth.AccessToken, ct);
        if (!owns)
        {
            throw new MicrosoftAuthException("На этом аккаунте Microsoft не куплена Minecraft.");
        }

        var profile = await profileClient.GetProfileAsync(mcAuth.AccessToken, ct);
        var uuid = Guid.ParseExact(profile.Id, "N");
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(mcAuth.ExpiresInSeconds);

        return new MicrosoftAccount(profile.Name, uuid, mcAuth.AccessToken, msaToken.RefreshToken, expiresAt);
    }
}
