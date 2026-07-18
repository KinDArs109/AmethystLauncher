using System.Text.Json.Serialization;

namespace Launcher.Auth.MinecraftServices;

public sealed class MinecraftAuthRequest
{
    [JsonPropertyName("identityToken")] public string IdentityToken { get; init; } = "";
}

public sealed class MinecraftAuthResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresInSeconds { get; init; }
}

public sealed class MinecraftEntitlementsResponse
{
    [JsonPropertyName("items")] public List<MinecraftEntitlementItem> Items { get; init; } = [];
}

public sealed class MinecraftEntitlementItem
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

public sealed class MinecraftProfileResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}
