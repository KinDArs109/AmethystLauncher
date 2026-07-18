using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Launcher.Auth.AccountStore;

public interface IAccountStore
{
    Task<IReadOnlyList<StoredAccount>> LoadAccountsAsync(CancellationToken ct = default);

    Task SaveMicrosoftAccountAsync(MicrosoftAccount account, CancellationToken ct = default);

    Task RemoveAccountAsync(Guid uuid, CancellationToken ct = default);

    /// <summary>Decrypts the refresh token for a stored Microsoft account (current Windows user only).</summary>
    string UnprotectRefreshToken(StoredAccount account);
}

public sealed class AccountStore : IAccountStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "accounts.json");

    public async Task<IReadOnlyList<StoredAccount>> LoadAccountsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<StoredAccount>>(stream, cancellationToken: ct) ?? [];
    }

    public async Task SaveMicrosoftAccountAsync(MicrosoftAccount account, CancellationToken ct = default)
    {
        var accounts = (await LoadAccountsAsync(ct)).Where(a => a.Uuid != account.Uuid).ToList();

        var protectedToken = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(account.MsaRefreshToken), optionalEntropy: null, DataProtectionScope.CurrentUser);

        accounts.Add(new StoredAccount
        {
            Kind = AccountKind.Microsoft,
            Username = account.Username,
            Uuid = account.Uuid,
            ProtectedMsaRefreshTokenBase64 = Convert.ToBase64String(protectedToken),
        });

        await SaveAsync(accounts, ct);
    }

    public async Task RemoveAccountAsync(Guid uuid, CancellationToken ct = default)
    {
        var accounts = (await LoadAccountsAsync(ct)).Where(a => a.Uuid != uuid).ToList();
        await SaveAsync(accounts, ct);
    }

    public string UnprotectRefreshToken(StoredAccount account)
    {
        if (account.ProtectedMsaRefreshTokenBase64 is null)
        {
            throw new InvalidOperationException($"Account '{account.Username}' has no stored Microsoft refresh token.");
        }

        var protectedBytes = Convert.FromBase64String(account.ProtectedMsaRefreshTokenBase64);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task SaveAsync(List<StoredAccount> accounts, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, accounts, new JsonSerializerOptions { WriteIndented = true }, ct);
    }
}
