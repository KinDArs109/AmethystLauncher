using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Launcher.Auth.MinecraftServices;

public interface IProfileClient
{
    Task<MinecraftProfileResponse> GetProfileAsync(string minecraftAccessToken, CancellationToken ct = default);
}

public sealed class ProfileClient(HttpClient httpClient) : IProfileClient
{
    private const string Url = "https://api.minecraftservices.com/minecraft/profile";

    public async Task<MinecraftProfileResponse> GetProfileAsync(string minecraftAccessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthException($"Не удалось получить профиль Minecraft (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<MinecraftProfileResponse>(cancellationToken: ct)
            ?? throw new MicrosoftAuthException("Пустой ответ при получении профиля Minecraft.");
    }
}
