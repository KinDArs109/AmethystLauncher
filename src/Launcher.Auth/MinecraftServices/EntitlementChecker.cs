using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Launcher.Auth.MinecraftServices;

public interface IEntitlementChecker
{
    /// <summary>Confirms the account actually owns Minecraft — the ownership check that must never be bypassed.</summary>
    Task<bool> OwnsMinecraftAsync(string minecraftAccessToken, CancellationToken ct = default);
}

public sealed class EntitlementChecker(HttpClient httpClient) : IEntitlementChecker
{
    private const string Url = "https://api.minecraftservices.com/entitlements/mcstore";

    public async Task<bool> OwnsMinecraftAsync(string minecraftAccessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthException($"Не удалось проверить владение игрой (HTTP {(int)response.StatusCode}).");
        }

        var entitlements = await response.Content.ReadFromJsonAsync<MinecraftEntitlementsResponse>(cancellationToken: ct);
        return entitlements is { Items.Count: > 0 };
    }
}
