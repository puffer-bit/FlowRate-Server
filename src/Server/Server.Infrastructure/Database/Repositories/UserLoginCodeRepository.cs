using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Database;
using Server.Database.Entities;

namespace Server.Infrastructure.Database.Repositories;

public class UserLoginCodeRepository(IDbContextFactory<ServerDbContext> contextFactory) : IUserLoginCodeRepository
{
    public async Task AddAsync(string coordinatorInstanceId, int userId, byte[] codeHash, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.UserLoginCodes.Where(x => x.ExpiresAt <= createdAt).ExecuteDeleteAsync(cancellationToken);
        context.UserLoginCodes.Add(new UserLoginCodeEntity
        {
            Id = Guid.NewGuid(),
            CoordinatorInstanceId = coordinatorInstanceId,
            UserId = userId,
            CodeHash = codeHash,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int?> TakeAsync(string coordinatorInstanceId, byte[] codeHash, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var code = await context.UserLoginCodes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CodeHash == codeHash && x.CoordinatorInstanceId == coordinatorInstanceId,
                cancellationToken);
        if (code == null)
            return null;

        // Deleted first, so a code raced by two logins lets one of them in
        var taken = await context.UserLoginCodes.Where(x => x.Id == code.Id).ExecuteDeleteAsync(cancellationToken);
        return taken > 0 && code.ExpiresAt > now ? code.UserId : null;
    }

    public async Task DeleteByUserAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.UserLoginCodes
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
