using System.Security.Cryptography;
using System.Text;

namespace Launcher.Auth.Microsoft;

/// <summary>PKCE (RFC 7636) code verifier/challenge — lets a public client (no client secret) use the auth-code flow safely.</summary>
public static class PkceHelper
{
    public static (string Verifier, string Challenge) Generate()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
