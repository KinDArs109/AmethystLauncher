using System.Net;
using System.Net.Http.Json;

namespace Launcher.Auth.Xbox;

public interface IXstsAuthClient
{
    Task<XboxTokenResponse> AuthorizeAsync(string xblToken, CancellationToken ct = default);
}

/// <summary>Exchanges an XBL token for an XSTS token scoped to Minecraft Services.</summary>
public sealed class XstsAuthClient(HttpClient httpClient) : IXstsAuthClient
{
    private const string Url = "https://xsts.auth.xboxlive.com/xsts/authorize";

    public async Task<XboxTokenResponse> AuthorizeAsync(string xblToken, CancellationToken ct = default)
    {
        var request = new XstsAuthRequest
        {
            Properties = new XstsAuthProperties { UserTokens = [xblToken] },
        };

        using var response = await httpClient.PostAsJsonAsync(Url, request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var error = await response.Content.ReadFromJsonAsync<XstsErrorResponse>(cancellationToken: ct);
            throw new MicrosoftAuthException(DescribeError(error?.XErr));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthException($"XSTS отклонил вход (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<XboxTokenResponse>(cancellationToken: ct)
            ?? throw new MicrosoftAuthException("Пустой ответ от XSTS.");
    }

    private static string DescribeError(long? xErr) => xErr switch
    {
        2148916233 => "На этом аккаунте Microsoft нет профиля Xbox. Создайте его на xbox.com и попробуйте снова.",
        2148916235 => "Xbox Live недоступен в этой стране/регионе.",
        2148916236 or 2148916237 => "Аккаунт требует подтверждения возраста на xbox.com.",
        2148916238 => "Это детский аккаунт — нужно добавить его в семейную группу Microsoft (family.microsoft.com) и повторить вход.",
        _ => $"XSTS отклонил вход (код {xErr?.ToString() ?? "неизвестен"}).",
    };
}
