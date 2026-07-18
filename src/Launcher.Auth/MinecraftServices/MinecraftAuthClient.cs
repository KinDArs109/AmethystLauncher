using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Launcher.Auth.MinecraftServices;

public interface IMinecraftAuthClient
{
    Task<MinecraftAuthResponse> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken ct = default);
}

/// <summary>Exchanges an XSTS token for a Minecraft Services bearer token.</summary>
public sealed class MinecraftAuthClient(HttpClient httpClient, ILogger<MinecraftAuthClient> logger) : IMinecraftAuthClient
{
    private const string Url = "https://api.minecraftservices.com/authentication/login_with_xbox";

    public async Task<MinecraftAuthResponse> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken ct = default)
    {
        var request = new MinecraftAuthRequest { IdentityToken = $"XBL3.0 x={userHash};{xstsToken}" };

        using var response = await httpClient.PostAsJsonAsync(Url, request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Minecraft Services login_with_xbox failed: HTTP {Status}. Body: {Body}", (int)response.StatusCode, body);
            throw new MicrosoftAuthException($"Minecraft Services отклонил вход (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<MinecraftAuthResponse>(cancellationToken: ct)
            ?? throw new MicrosoftAuthException("Пустой ответ от Minecraft Services.");
    }
}
