using System.Text.Json.Serialization;

namespace Launcher.Auth.Xbox;

public sealed class XblAuthRequest
{
    [JsonPropertyName("Properties")] public XblAuthProperties Properties { get; init; } = new();
    [JsonPropertyName("RelyingParty")] public string RelyingParty { get; init; } = "http://auth.xboxlive.com";
    [JsonPropertyName("TokenType")] public string TokenType { get; init; } = "JWT";
}

public sealed class XblAuthProperties
{
    [JsonPropertyName("AuthMethod")] public string AuthMethod { get; init; } = "RPS";
    [JsonPropertyName("SiteName")] public string SiteName { get; init; } = "user.auth.xboxlive.com";
    [JsonPropertyName("RpsTicket")] public string RpsTicket { get; init; } = "";
}

public sealed class XstsAuthRequest
{
    [JsonPropertyName("Properties")] public XstsAuthProperties Properties { get; init; } = new();
    [JsonPropertyName("RelyingParty")] public string RelyingParty { get; init; } = "rp://api.minecraftservices.com/";
    [JsonPropertyName("TokenType")] public string TokenType { get; init; } = "JWT";
}

public sealed class XstsAuthProperties
{
    [JsonPropertyName("SandboxId")] public string SandboxId { get; init; } = "RETAIL";
    [JsonPropertyName("UserTokens")] public List<string> UserTokens { get; init; } = [];
}

public sealed class XboxTokenResponse
{
    [JsonPropertyName("Token")] public string Token { get; init; } = "";
    [JsonPropertyName("DisplayClaims")] public XboxDisplayClaims DisplayClaims { get; init; } = new();
}

public sealed class XboxDisplayClaims
{
    [JsonPropertyName("xui")] public List<XboxUserClaim> Xui { get; init; } = [];
}

public sealed class XboxUserClaim
{
    [JsonPropertyName("uhs")] public string Uhs { get; init; } = "";
}

/// <summary>Present on XSTS 401 responses; XErr identifies the specific account-level failure reason.</summary>
public sealed class XstsErrorResponse
{
    [JsonPropertyName("XErr")] public long XErr { get; init; }
    [JsonPropertyName("Message")] public string? Message { get; init; }
}
