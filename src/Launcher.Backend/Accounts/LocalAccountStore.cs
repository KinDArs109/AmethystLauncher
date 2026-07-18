using System.Net.Http.Json;
using System.Text.Json;
using Launcher.Backend.Supabase;

namespace Launcher.Backend.Accounts;

/// <summary>A username+password account for the private-community launcher — no email, no Mojang/
/// Microsoft ownership check. Stored in the same Supabase project as News/Support (project
/// camxoptnyxrljaamsfym), NOT on the local machine, so it survives a full uninstall/reinstall and
/// works from any install.</summary>
public interface ILocalAccountStore
{
    /// <summary>Registers a new account. Password hashing (bcrypt via pgcrypto) happens entirely
    /// server-side in the `register_local_account` RPC — the plaintext password only ever travels
    /// over TLS to Supabase, never touches disk. Throws <see cref="InvalidOperationException"/> with a
    /// user-facing message (e.g. "already taken", "too short") on failure.</summary>
    Task RegisterAsync(string username, string password, CancellationToken ct = default);

    /// <summary>True if the username exists and the password matches, verified server-side by the
    /// `verify_local_account` RPC.</summary>
    Task<bool> ValidateAsync(string username, string password, CancellationToken ct = default);
}

/// <summary>
/// Calls the two PostgREST RPC endpoints directly over HttpClient instead of going through
/// Supabase.Client.Rpc(...) — that wrapper (Supabase 1.1.1 / Postgrest-csharp 4.0.3) was observed
/// sending a request PostgREST read as zero arguments (server logs showed a 42883 "no function
/// matches" for both a Dictionary and a POCO parameter object), so this bypasses it for a plain,
/// unambiguous POST with a hand-built JSON body.
/// </summary>
public sealed class LocalAccountStore(ISupabaseClientProvider clientProvider) : ILocalAccountStore
{
    private static readonly HttpClient Http = new();

    public async Task RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        var (isSuccess, body) = await CallRpcAsync("register_local_account", username, password, ct);
        if (!isSuccess)
        {
            throw new InvalidOperationException(ExtractErrorMessage(body));
        }
    }

    public async Task<bool> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        var (isSuccess, body) = await CallRpcAsync("verify_local_account", username, password, ct);
        return isSuccess && bool.TryParse(body.Trim().Trim('"'), out var ok) && ok;
    }

    private async Task<(bool IsSuccess, string Body)> CallRpcAsync(string procedureName, string username, string password, CancellationToken ct)
    {
        var client = await clientProvider.GetClientAsync(ct);
        var accessToken = client.Auth.CurrentSession?.AccessToken ?? SupabaseConfig.AnonKey;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseConfig.Url}/rest/v1/rpc/{procedureName}")
        {
            Content = JsonContent.Create(new { p_username = username, p_password = password }),
        };
        request.Headers.Add("apikey", SupabaseConfig.AnonKey);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.IsSuccessStatusCode, body);
    }

    /// <summary>PostgREST wraps the RAISE EXCEPTION text from the SQL function in a JSON error object
    /// (e.g. {"message":"Имя «foo» уже занято."}); fall back to the raw body if that shape ever changes.</summary>
    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON — surface the raw body below.
        }

        return body;
    }
}
