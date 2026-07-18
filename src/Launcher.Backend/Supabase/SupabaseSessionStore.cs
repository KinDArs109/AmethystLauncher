using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace Launcher.Backend.Supabase;

/// <summary>
/// Persists the anonymous Supabase session (DPAPI-encrypted, current Windows user only) so the launcher
/// keeps the same identity — and therefore the same support-chat history — across restarts.
/// </summary>
public sealed class SupabaseSessionStore : IGotrueSessionPersistence<Session>
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "supabase_session.json");

    public void SaveSession(Session session)
    {
        var json = JsonConvert.SerializeObject(session);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public void DestroySession()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    public Session? LoadSession()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser));
            return JsonConvert.DeserializeObject<Session>(json);
        }
        catch
        {
            // Corrupt or undecryptable session file (e.g. copied to another machine) — treat as no session.
            return null;
        }
    }
}
