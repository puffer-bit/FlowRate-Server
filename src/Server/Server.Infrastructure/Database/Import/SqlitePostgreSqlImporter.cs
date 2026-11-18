using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Server.Infrastructure.Database.Import;

public sealed record ImportedTable(string Name, long Rows);
public sealed record DatabaseImportResult(IReadOnlyList<ImportedTable> Tables, bool DryRun);

/// <summary>Offline import into an empty PostgreSQL schema. The source is never upgraded or written.</summary>
public sealed class SqlitePostgreSqlImporter
{
    public async Task<DatabaseImportResult> ImportAsync(string sourcePath, ServerDbContext target, bool dryRun = false,
        Action<ImportedTable>? progress = null, CancellationToken ct = default)
    {
        if (!target.Database.IsNpgsql())
            throw new InvalidOperationException("The import target must use PostgreSQL.");
        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The SQLite source file does not exist.", sourcePath);

        await using var sourceConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, ForeignKeys = true
        }.ConnectionString);
        await using var source = new ServerDbContext(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(sourceConnection).Options);
        await source.Database.OpenConnectionAsync(ct);
        await using var sourceTransaction = sourceConnection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        await source.Database.UseTransactionAsync(sourceTransaction, ct);
        var sourceTables = source.Model.GetRelationalModel().Tables.ToDictionary(t => t.Name);
        var targetTables = OrderTables(target.Model.GetRelationalModel().Tables.ToArray());
        if (!sourceTables.Keys.ToHashSet().SetEquals(targetTables.Select(t => t.Name)))
            throw new InvalidOperationException("Source and target models have different tables.");
        if (!(await source.Database.GetAppliedMigrationsAsync(ct)).SequenceEqual(source.Database.GetMigrations()))
            throw new InvalidOperationException("The SQLite schema is not at this server version. Upgrade a backup copy with the matching server first.");
        await ValidateSourceAsync(sourceConnection, sourceTransaction, sourceTables, ct);

        await target.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)target.Database.GetDbConnection();
        await AssertEmptyAsync(connection, null, targetTables, ct);
        await target.Database.MigrateAsync(ct);
        await using var transaction = await target.Database.BeginTransactionAsync(ct);
        var pgTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await ExecuteAsync(connection, pgTransaction,
            "LOCK TABLE " + string.Join(", ", targetTables.Select(TableSql)) + " IN ACCESS EXCLUSIVE MODE", ct);
        // Check again under the locks: a concurrent server/import must not turn this into a merge.
        await AssertEmptyAsync(connection, pgTransaction, targetTables, ct);
        var results = new List<ImportedTable>();
        var verification = new List<(ITable Table, IColumn[] Columns, RelationalTypeMapping[] Mappings, RowDigest Expected)>();
        foreach (var table in targetTables)
        {
            ct.ThrowIfCancellationRequested();
            var columns = table.Columns.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
            var original = sourceTables[table.Name];
            var sourceColumns = columns.Select(c => original.FindColumn(c.Name)
                ?? throw new InvalidOperationException($"Source column {table.Name}.{c.Name} is missing.")).ToArray();
            var sourceMappings = sourceColumns.Select(Mapping).ToArray();
            var targetMappings = columns.Select(Mapping).ToArray();
            var sourceReaders = sourceMappings.Select(Reader).ToArray();
            var columnList = string.Join(", ", columns.Select(c => Quote(c.Name)));
            var expected = new RowDigest();
            await using (var command = sourceConnection.CreateCommand())
            {
                command.Transaction = sourceTransaction;
                command.CommandText = $"SELECT {columnList} FROM {Quote(table.Name)}";
                await using var reader = await command.ExecuteReaderAsync(ct);
                await using var parameters = connection.CreateCommand();
                var parameterMappings = targetMappings.Select((m, i) =>
                    (NpgsqlParameter)m.CreateParameter(parameters, "p" + i, null, nullable: true)).ToArray();
                await using var writer = await connection.BeginBinaryImportAsync(
                    $"COPY {TableSql(table)} ({columnList}) FROM STDIN (FORMAT BINARY)", ct);
                while (await reader.ReadAsync(ct))
                {
                    var values = ReadRow(reader, sourceMappings, sourceReaders);
                    expected.Add(values);
                    await writer.StartRowAsync(ct);
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (values[i] == null)
                            await writer.WriteNullAsync(ct);
                        else
                        {
                            var value = targetMappings[i].Converter?.ConvertToProvider(values[i]) ?? values[i];
                            await writer.WriteAsync(value, parameterMappings[i].NpgsqlDbType, ct);
                        }
                    }
                }
                await writer.CompleteAsync(ct);
            }
            verification.Add((table, columns, targetMappings, expected));
        }
        // Verify the final state of every table, including changes made by target triggers during later copies.
        foreach (var (table, columns, mappings, expected) in verification)
        {
            var actual = new RowDigest();
            var columnList = string.Join(", ", columns.Select(c => Quote(c.Name)));
            await using (var command = new NpgsqlCommand($"SELECT {columnList} FROM {TableSql(table)}", connection, pgTransaction))
            {
                await using var reader = await command.ExecuteReaderAsync(ct);
                var readers = mappings.Select(Reader).ToArray();
                while (await reader.ReadAsync(ct))
                    actual.Add(ReadRow(reader, mappings, readers));
            }
            if (!expected.Equals(actual))
                throw new InvalidOperationException($"Verification failed for table '{table.Name}'. The import will be rolled back.");
            var result = new ImportedTable(table.Name, expected.Count);
            results.Add(result);
            progress?.Invoke(result);
        }
        await ResetSequencesAsync(sourceConnection, sourceTransaction, connection, pgTransaction, targetTables, ct);
        if (dryRun) await transaction.RollbackAsync(ct);
        else await transaction.CommitAsync(ct);
        return new DatabaseImportResult(results, dryRun);
    }

    private static async Task ValidateSourceAsync(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyDictionary<string, ITable> tables, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA quick_check";
        if (!Equals(await command.ExecuteScalarAsync(ct), "ok"))
            throw new InvalidOperationException("The SQLite integrity check failed.");
        command.CommandText = "PRAGMA foreign_key_check";
        await using (var reader = await command.ExecuteReaderAsync(ct))
            if (await reader.ReadAsync(ct))
                throw new InvalidOperationException("The SQLite source contains broken foreign keys.");
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT GLOB 'sqlite_*'";
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (!tables.ContainsKey(name) && name is not "__EFMigrationsHistory" and not "__EFMigrationsLock")
                    throw new InvalidOperationException($"Unsupported SQLite table '{name}'; refusing to omit its data.");
            }
        foreach (var table in tables.Values)
        {
            command.CommandText = $"PRAGMA table_info({Quote(table.Name)})";
            var columns = new HashSet<string>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            if (!columns.SetEquals(table.Columns.Select(c => c.Name)))
                throw new InvalidOperationException($"SQLite table '{table.Name}' does not match the expected columns.");
        }
    }

    private static async Task AssertEmptyAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        IReadOnlyList<ITable> tables, CancellationToken ct)
    {
        var existing = new List<string>();
        await using (var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'",
            connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) existing.Add(reader.GetString(0));
        var names = tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in existing)
        {
            if (name == "__EFMigrationsHistory") continue;
            if (!names.Contains(name))
                throw new InvalidOperationException($"Target schema contains an unsupported table '{name}'. Use an empty database/schema.");
            await using var command = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {Quote(name)})", connection, transaction);
            if ((bool)(await command.ExecuteScalarAsync(ct))!)
                throw new InvalidOperationException($"Target table '{name}' is not empty. Import never merges or deletes existing data.");
        }
    }

    private static async Task ResetSequencesAsync(SqliteConnection source, SqliteTransaction sourceTransaction,
        NpgsqlConnection target, NpgsqlTransaction transaction, IReadOnlyList<ITable> tables, CancellationToken ct)
    {
        foreach (var table in tables)
        foreach (var column in table.Columns.Where(c => c.PropertyMappings.Any(m => m.Property.ValueGenerated == ValueGenerated.OnAdd)))
        {
            await using var sequenceCommand = new NpgsqlCommand("SELECT pg_get_serial_sequence(@table, @column)", target, transaction);
            sequenceCommand.Parameters.AddWithValue("table", TableSql(table));
            sequenceCommand.Parameters.AddWithValue("column", column.Name);
            if (await sequenceCommand.ExecuteScalarAsync(ct) is not string sequence) continue;
            await using var maxCommand = new NpgsqlCommand($"SELECT COALESCE(MAX({Quote(column.Name)}), 0) FROM {TableSql(table)}", target, transaction);
            var maximum = Convert.ToInt64(await maxCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            await using var watermark = source.CreateCommand();
            watermark.Transaction = sourceTransaction;
            watermark.CommandText = "SELECT seq FROM sqlite_sequence WHERE name = $table";
            watermark.Parameters.AddWithValue("$table", table.Name);
            var previous = await watermark.ExecuteScalarAsync(ct);
            if (previous is not null and not DBNull)
                maximum = Math.Max(maximum, Convert.ToInt64(previous, CultureInfo.InvariantCulture));
            var next = Math.Max(1, checked(maximum + 1));
            // RESTART is transactional, unlike setval, so dry runs and failed imports preserve sequences too.
            await ExecuteAsync(target, transaction, $"ALTER SEQUENCE {sequence} RESTART WITH {next.ToString(CultureInfo.InvariantCulture)}", ct);
        }
    }

    private static IReadOnlyList<ITable> OrderTables(IReadOnlyList<ITable> tables)
    {
        var result = new List<ITable>();
        var remaining = tables.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(t => t.ForeignKeyConstraints.All(f =>
                f.PrincipalTable == t || result.Contains(f.PrincipalTable)));
            if (next == null) throw new InvalidOperationException("The database model has cyclic table dependencies.");
            result.Add(next);
            remaining.Remove(next);
        }
        return result;
    }

    private static RelationalTypeMapping Mapping(IColumn column) => column.PropertyMappings.First().TypeMapping;

    private static Func<DbDataReader, int, object> Reader(RelationalTypeMapping mapping)
    {
        var reader = Expression.Parameter(typeof(DbDataReader));
        var ordinal = Expression.Parameter(typeof(int));
        var method = mapping.GetDataReaderMethod();
        var expression = Expression.Call(Expression.Convert(reader, method.DeclaringType!), method, ordinal);
        return Expression.Lambda<Func<DbDataReader, int, object>>(
            Expression.Convert(mapping.CustomizeDataReaderExpression(expression), typeof(object)), reader, ordinal).Compile();
    }

    private static object?[] ReadRow(DbDataReader reader, RelationalTypeMapping[] mappings,
        Func<DbDataReader, int, object>[] readers)
    {
        var values = new object?[mappings.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (reader.IsDBNull(i)) continue;
            var provider = readers[i](reader, i);
            var value = mappings[i].Converter?.ConvertFromProvider(provider) ?? provider;
            values[i] = value switch
            {
                DateTime time when time == DateTime.MinValue || time == DateTime.MaxValue => time,
                DateTime time => new DateTime(time.Ticks - time.Ticks % 10, DateTimeKind.Utc),
                TimeSpan span => TimeSpan.FromTicks(span.Ticks - span.Ticks % 10),
                _ => value
            };
        }
        return values;
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static string TableSql(ITable table) => table.Schema == null ? Quote(table.Name) : Quote(table.Schema) + "." + Quote(table.Name);

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    // Order-independent, bounded-memory fingerprint: row order and database collations may differ.
    private sealed class RowDigest : IEquatable<RowDigest>
    {
        private static readonly BigInteger Mask = (BigInteger.One << 256) - 1;
        private BigInteger _sum;
        private BigInteger _xor;
        public long Count { get; private set; }
        public void Add(object?[] row)
        {
            var hash = new BigInteger(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row)), isUnsigned: true);
            _sum = (_sum + hash) & Mask;
            _xor ^= hash;
            Count++;
        }
        public bool Equals(RowDigest? other) => other != null && Count == other.Count && _sum == other._sum && _xor == other._xor;
    }
}
