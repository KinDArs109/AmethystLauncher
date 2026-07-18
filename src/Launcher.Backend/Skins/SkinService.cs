using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Backend.Supabase;

namespace Launcher.Backend.Skins;

public sealed record PlayerSkin(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("png_base64")] string PngBase64,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    public byte[] PngBytes => Convert.FromBase64String(PngBase64);
}

/// <summary>Cloud-stored player skins (Supabase, RPC-backed). The `skins` edge function serves the
/// same data to the game via the CustomSkinLoader mod, so a skin uploaded here shows up in-game
/// for the player and for other launcher players on shared servers.</summary>
public interface ISkinService
{
    /// <summary>Model is "default" (classic/Steve arms) or "slim" (Alex arms).</summary>
    Task UploadAsync(string username, byte[] pngBytes, string model, CancellationToken ct = default);

    Task<PlayerSkin?> GetAsync(string username, CancellationToken ct = default);

    Task DeleteAsync(string username, CancellationToken ct = default);
}

public sealed class SkinService : ISkinService
{
    public async Task UploadAsync(string username, byte[] pngBytes, string model, CancellationToken ct = default) =>
        await SupabaseRpc.CallAsync(
            "set_skin",
            new { p_username = username, p_model = model, p_png_base64 = Convert.ToBase64String(pngBytes) },
            ct);

    public async Task<PlayerSkin?> GetAsync(string username, CancellationToken ct = default)
    {
        var body = await SupabaseRpc.CallAsync("get_skin", new { p_username = username }, ct);
        var rows = JsonSerializer.Deserialize<List<PlayerSkin>>(body) ?? [];
        return rows.FirstOrDefault();
    }

    public async Task DeleteAsync(string username, CancellationToken ct = default) =>
        await SupabaseRpc.CallAsync("delete_skin", new { p_username = username }, ct);
}
