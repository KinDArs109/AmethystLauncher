using System.Security.Cryptography;
using System.Text;

namespace Launcher.Auth.Offline;

public interface IOfflineProfileFactory
{
    OfflineAccount CreateAccount(string playerName);
}

/// <summary>
/// Builds an offline-mode account the same way vanilla Minecraft does: a nickname plus a UUID
/// deterministically derived from it, so the same name always maps to the same UUID across launches
/// and across any offline-mode server (Java's <c>UUID.nameUUIDFromBytes("OfflinePlayer:" + name)</c>).
/// </summary>
public sealed class OfflineProfileFactory : IOfflineProfileFactory
{
    public OfflineAccount CreateAccount(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            throw new ArgumentException("Player name must not be empty.", nameof(playerName));
        }

        return new OfflineAccount(playerName, CreateOfflineUuid(playerName));
    }

    private static Guid CreateOfflineUuid(string playerName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{playerName}"));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // UUID version 3 (name-based, MD5)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 variant

        // `hash` is now a standard big-endian RFC4122 UUID byte sequence, but System.Guid's byte
        // constructor expects the first three fields little-endian; reorder those, leave the rest as-is.
        Span<byte> reordered =
        [
            hash[3], hash[2], hash[1], hash[0],
            hash[5], hash[4],
            hash[7], hash[6],
            hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15],
        ];

        return new Guid(reordered);
    }
}
