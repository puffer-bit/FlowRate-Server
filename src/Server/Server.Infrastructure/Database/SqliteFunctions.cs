using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Server.Infrastructure.Database;

/// <summary>
/// The functions SQLite lacks: <c>sha256(text)</c>, the hash of the UTF-8 text as a blob, which a migration uses to
/// replace the stored identities with their hashes.
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
            sqlite.CreateFunction("sha256", (string? text) =>
                text == null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(text)), isDeterministic: true);
    }
}
