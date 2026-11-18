using System.ComponentModel.DataAnnotations;
using Server.Database.PostgreSql;
using Server.Database.Sqlite;

namespace Server.Infrastructure.Options;

public enum DatabaseProvider
{
    Sqlite,
    PostgreSql
}

public class DatabaseOptions : IValidatableObject
{
    public const string SectionName = "Database";

    [EnumDataType(typeof(DatabaseProvider))]
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    public string? ConnectionString { get; set; }

    public string GetConnectionString(StorageOptions storage) =>
        string.IsNullOrWhiteSpace(ConnectionString) ? storage.DatabaseConnectionString : ConnectionString;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            if (Provider == DatabaseProvider.PostgreSql)
                yield return new ValidationResult("A connection string is required for PostgreSQL.", [nameof(ConnectionString)]);
            yield break;
        }

        var error = Provider switch
        {
            DatabaseProvider.Sqlite => SqliteDatabase.ValidateConnectionString(ConnectionString),
            DatabaseProvider.PostgreSql => PostgreSqlDatabase.ValidateConnectionString(ConnectionString),
            _ => null
        };
        if (error != null)
            yield return new ValidationResult(error, [nameof(ConnectionString)]);
    }
}
