using Microsoft.Extensions.Logging;
using Supabase;

namespace Launcher.Backend.Supabase;

public interface ISupabaseClientProvider
{
    /// <summary>
    /// Returns the shared, initialized Supabase client, signed in with a stable anonymous identity
    /// (persisted across restarts) so Row Level Security policies keyed on auth.uid() work.
    /// </summary>
    Task<Client> GetClientAsync(CancellationToken ct = default);
}

public sealed class SupabaseClientProvider(
    SupabaseSessionStore sessionStore,
    ILogger<SupabaseClientProvider> logger) : ISupabaseClientProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Client? _client;

    public async Task<Client> GetClientAsync(CancellationToken ct = default)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var options = new SupabaseOptions { AutoConnectRealtime = true };
            var client = new Client(SupabaseConfig.Url, SupabaseConfig.AnonKey, options);
            client.Auth.SetPersistence(sessionStore);

            await client.InitializeAsync();

            // Auth.LoadSession() pulls the persisted session (via our SupabaseSessionStore) into CurrentSession —
            // it returns void, unlike IGotrueSessionPersistence.LoadSession() which returns the Session itself.
            client.Auth.LoadSession();

            if (client.Auth.CurrentSession is not null)
            {
                try
                {
                    await client.Auth.RetrieveSessionAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to restore persisted Supabase session, will sign in anonymously again");
                }
            }

            if (client.Auth.CurrentSession is null)
            {
                await client.Auth.SignInAnonymously();
            }

            _client = client;
            return client;
        }
        finally
        {
            _lock.Release();
        }
    }
}
