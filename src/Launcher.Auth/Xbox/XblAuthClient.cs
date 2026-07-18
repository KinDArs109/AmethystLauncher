using System.Net.Http.Json;

namespace Launcher.Auth.Xbox;

public interface IXblAuthClient
{
    Task<XboxTokenResponse> AuthenticateAsync(string msaAccessToken, CancellationToken ct = default);
}

/// <summary>Exchanges a Microsoft access token for an Xbox Live (XBL) user token.</summary>
public sealed class XblAuthClient(HttpClient httpClient) : IXblAuthClient
{
    private const string Url = "https://user.auth.xboxlive.com/user/authenticate";

    public async Task<XboxTokenResponse> AuthenticateAsync(string msaAccessToken, CancellationToken ct = default)
    {
        var request = new XblAuthRequest
        {
            Properties = new XblAuthProperties { RpsTicket = $"d={msaAccessToken}" },
        };

        using var response = await httpClient.PostAsJsonAsync(Url, request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthException($"Xbox Live отклонил вход (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<XboxTokenResponse>(cancellationToken: ct)
            ?? throw new MicrosoftAuthException("Пустой ответ от Xbox Live.");
    }
}
