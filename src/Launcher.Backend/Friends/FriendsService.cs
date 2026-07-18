using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Backend.Supabase;

namespace Launcher.Backend.Friends;

public sealed record FriendInfo(
    [property: JsonPropertyName("friend")] string Username,
    [property: JsonPropertyName("is_online")] bool IsOnline,
    [property: JsonPropertyName("playing_instance")] string? PlayingInstance);

public sealed record FriendRequest(
    [property: JsonPropertyName("requester")] string Requester,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>Friends and presence for local (username) accounts. All calls go through SECURITY DEFINER
/// RPCs in the shared Supabase project — the tables themselves are closed to direct REST access.</summary>
public interface IFriendsService
{
    /// <summary>Sends a friend request. Returns true if the other side had already requested us and the
    /// friendship auto-accepted, false if a pending request was created. Throws
    /// <see cref="InvalidOperationException"/> with a user-facing Russian message on failure.</summary>
    Task<bool> SendRequestAsync(string from, string to, CancellationToken ct = default);

    Task RespondAsync(string username, string requester, bool accept, CancellationToken ct = default);

    Task RemoveAsync(string username, string other, CancellationToken ct = default);

    Task<IReadOnlyList<FriendInfo>> GetFriendsAsync(string username, CancellationToken ct = default);

    Task<IReadOnlyList<FriendRequest>> GetRequestsAsync(string username, CancellationToken ct = default);

    /// <summary>Marks the user online; <paramref name="playingInstance"/> is the instance name while a
    /// game is running, null when just sitting in the launcher. Call every ~60s — "online" server-side
    /// means a heartbeat within the last 2 minutes.</summary>
    Task HeartbeatAsync(string username, string? playingInstance, CancellationToken ct = default);

    Task GoOfflineAsync(string username, CancellationToken ct = default);
}

/// <summary>Same raw-HttpClient PostgREST RPC transport as <c>LocalAccountStore</c> — the
/// Supabase.Client.Rpc wrapper was observed mangling arguments (see that class's remarks).</summary>
public sealed class FriendsService : IFriendsService
{
    private static readonly HttpClient Http = new();

    public async Task<bool> SendRequestAsync(string from, string to, CancellationToken ct = default)
    {
        var body = await CallRpcAsync("send_friend_request", new { p_from = from, p_to = to }, ct);
        return body.Trim().Trim('"') == "accepted";
    }

    public async Task RespondAsync(string username, string requester, bool accept, CancellationToken ct = default) =>
        await CallRpcAsync("respond_friend_request", new { p_username = username, p_requester = requester, p_accept = accept }, ct);

    public async Task RemoveAsync(string username, string other, CancellationToken ct = default) =>
        await CallRpcAsync("remove_friend", new { p_username = username, p_other = other }, ct);

    public async Task<IReadOnlyList<FriendInfo>> GetFriendsAsync(string username, CancellationToken ct = default)
    {
        var body = await CallRpcAsync("get_friends", new { p_username = username }, ct);
        return JsonSerializer.Deserialize<List<FriendInfo>>(body) ?? [];
    }

    public async Task<IReadOnlyList<FriendRequest>> GetRequestsAsync(string username, CancellationToken ct = default)
    {
        var body = await CallRpcAsync("get_friend_requests", new { p_username = username }, ct);
        return JsonSerializer.Deserialize<List<FriendRequest>>(body) ?? [];
    }

    public async Task HeartbeatAsync(string username, string? playingInstance, CancellationToken ct = default) =>
        await CallRpcAsync("presence_heartbeat", new { p_username = username, p_instance = playingInstance }, ct);

    public async Task GoOfflineAsync(string username, CancellationToken ct = default) =>
        await CallRpcAsync("presence_offline", new { p_username = username }, ct);

    private static async Task<string> CallRpcAsync(string procedureName, object args, CancellationToken ct)
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
