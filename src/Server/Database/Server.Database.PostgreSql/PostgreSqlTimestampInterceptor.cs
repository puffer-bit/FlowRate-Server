using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Database.PostgreSql;

/// <summary>
/// PostgreSQL timestamps have microsecond precision. Keeps tracked values identical to what a later read returns,
/// including entities returned directly after saving without a reload.
/// </summary>
public sealed class PostgreSqlTimestampInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Normalize(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Normalize(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Normalize(DbContext? context)
    {
        if (context == null)
            return;

        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        foreach (var property in entry.Properties)
        {
            if (property.CurrentValue is DateTimeOffset time && time != DateTimeOffset.MaxValue && time.Ticks % 10 != 0)
                property.CurrentValue = new DateTimeOffset(time.Ticks - time.Ticks % 10, time.Offset);
        }
    }
}
