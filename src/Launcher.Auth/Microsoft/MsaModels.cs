using System.Text.Json.Serialization;

namespace Launcher.Auth.Microsoft;

public sealed class MsaTokenResponse
{
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresInSeconds { get; init; }
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = "";
}

public sealed class MsaErrorResponse
{
    [JsonPropertyName("error")] public string Error { get; init; } = "";
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; init; }
}
