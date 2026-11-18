using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Server.Database;

namespace Server.Infrastructure.Tests.Database;

/// <summary>
/// Writes rows of the current entities into a database migrated only part of the way, where later
/// columns do not exist yet: only the columns the table has are written.
/// </summary>
internal static class OldSchemaRows
{
    public static async Task InsertAsync<TEntity>(ServerDbContext db, TEntity entity) where TEntity : class
    {
        var type = db.Model.FindEntityType(typeof(TEntity))!;
        var table = type.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, type.GetSchema());

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existing.Add(reader.GetString(1));
        }

        await using var insert = connection.CreateCommand();
        var columns = new List<string>();
        foreach (var property in type.GetProperties())
        {
            var column = property.GetColumnName(store);
            if (column == null || !existing.Contains(column))
                continue;

            var value = property.GetGetter().GetClrValue(entity);
            if (property.GetTypeMapping().Converter is { } converter)
                value = converter.ConvertToProvider(value);

            columns.Add(column);
            insert.Parameters.AddWithValue($"@p{columns.Count}", value ?? DBNull.Value);
        }

        insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", columns.Select(c => $"\"{c}\""))}) " +
                             $"VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i + 1}"))})";
        await insert.ExecuteNonQueryAsync();
    }
}
