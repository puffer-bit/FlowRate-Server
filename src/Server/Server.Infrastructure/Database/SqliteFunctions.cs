using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Server.Infrastructure.Database;

/// <summary>
/// Migration helpers: <c>sha256(text)</c> hashes identities, and <c>normalize_ip(text)</c> canonicalizes legacy IP bans.
/// </summary>
public static class SqliteFunctions
{
    /// <summary>
    /// Defines the functions on the connection; a connection keeps them for every time it is opened, so this is done
    /// once, before it is.
    /// </summary>
    public static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
        {
            sqlite.CreateFunction("normalize_ip", (string? value) => Server.Core.Models.Moderation.GlobalBan.NormalizeAddress(value), isDeterministic: true);
            sqlite.CreateFunction("sha256", (string? text) =>
                text == null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(text)), isDeterministic: true);
        }
    }
}
