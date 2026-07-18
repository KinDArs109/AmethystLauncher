using System.Net.Http.Json;
using System.Text.Json;

namespace Launcher.Backend.Supabase;

/// <summary>Shared raw-HttpClient PostgREST RPC transport (see <c>LocalAccountStore</c> remarks for
/// why the Supabase.Client.Rpc wrapper is bypassed). Throws <see cref="InvalidOperationException"/>
/// with the server's user-facing message on non-success.</summary>
public static class SupabaseRpc
{
    private static readonly HttpClient Http = new();

    public static async Task<string> CallAsync(string procedureName, object args, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseConfig.Url}/rest/v1/rpc/{procedureName}")
        {
            Content = JsonContent.Create(args),
        };
        request.Headers.Add("apikey", SupabaseConfig.AnonKey);
        request.Headers.Add("Authorization", $"Bearer {SupabaseConfig.AnonKey}");

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(body));
        }

        return body;
    }

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
