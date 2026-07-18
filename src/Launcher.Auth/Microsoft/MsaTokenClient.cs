using System.Net.Http.Json;

namespace Launcher.Auth.Microsoft;

public interface IMsaTokenClient
{
    Task<MsaTokenResponse> ExchangeAuthorizationCodeAsync(
        string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct = default);

    Task<MsaTokenResponse> RefreshAsync(string clientId, string refreshToken, CancellationToken ct = default);
}

/// <summary>
/// Authorization-code + PKCE flow against the "consumers" tenant (personal Microsoft accounts only).
/// PKCE lets a public desktop client complete this without a client secret.
/// </summary>
public sealed class MsaTokenClient(HttpClient httpClient) : IMsaTokenClient
{
    private const string Scope = "XboxLive.signin offline_access";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    public async Task<MsaTokenResponse> ExchangeAuthorizationCodeAsync(
        string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = Scope,
        });

        return await PostForTokenAsync(content, "обменять код входа на токен", ct);
    }

    public async Task<MsaTokenResponse> RefreshAsync(string clientId, string refreshToken, CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        });

        return await PostForTokenAsync(content, "обновить сессию Microsoft", ct);
    }

    private async Task<MsaTokenResponse> PostForTokenAsync(FormUrlEncodedContent content, string actionDescription, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(TokenUrl, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<MsaErrorResponse>(cancellationToken: ct);
            throw new MicrosoftAuthException($"Не удалось {actionDescription}: {error?.ErrorDescription ?? response.ReasonPhrase}");
        }

        return await response.Content.ReadFromJsonAsync<MsaTokenResponse>(cancellationToken: ct)
            ?? throw new MicrosoftAuthException("Пустой ответ от Microsoft при получении токена.");
    }
}
