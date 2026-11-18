using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Npgsql;

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

        var valid = true;
        try
        {
            if (Provider == DatabaseProvider.PostgreSql)
            {
                var connection = new NpgsqlConnectionStringBuilder(ConnectionString);
                valid = !string.IsNullOrWhiteSpace(connection.Host);
            }
            else if (Provider == DatabaseProvider.Sqlite)
            {
                var connection = new SqliteConnectionStringBuilder(ConnectionString);
                valid = !string.IsNullOrWhiteSpace(connection.DataSource);
            }
        }
        catch (ArgumentException)
        {
            valid = false;
        }

        if (!valid)
            yield return new ValidationResult("The connection string is invalid for the selected database provider.", [nameof(ConnectionString)]);
    }
}
