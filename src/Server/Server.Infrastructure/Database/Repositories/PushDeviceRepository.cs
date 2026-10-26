using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Push;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Repositories;

public class PushDeviceRepository : IPushDeviceRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public PushDeviceRepository(IDbContextFactory<ServerDbContext> contextFactory, TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SaveAsync(PushDeviceModel device, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.PushDevices.FirstOrDefaultAsync(
            x => x.CoordinatorInstanceId == device.CoordinatorInstanceId && x.Token == device.Token,
            cancellationToken);

        if (entity == null)
        {
            entity = new PushDeviceEntity
            {
                CoordinatorInstanceId = device.CoordinatorInstanceId,
                Token = device.Token,
                KeyId = device.KeyId,
                Key = device.Key
            };
            context.PushDevices.Add(entity);
        }

        entity.UserId = device.UserId;
        entity.Platform = device.Platform;
        entity.KeyId = device.KeyId;
        entity.Key = device.Key;
        entity.ReplyTokenHash = device.ReplyTokenHash;
        entity.UpdatedTime = _timeProvider.GetUtcNow().UtcDateTime;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string coordinatorInstanceId, string token,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.PushDevices
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.Token == token)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RemoveAsync(string coordinatorInstanceId, int userId, string token,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.PushDevices
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.Token == token)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<PushDeviceModel?> FindByReplyTokenAsync(string coordinatorInstanceId, byte[] replyTokenHash,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PushDevices
            .AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.ReplyTokenHash == replyTokenHash)
            .Select(x => new PushDeviceModel(x.CoordinatorInstanceId, x.UserId, x.Token, x.Platform, x.KeyId, x.Key,
                x.ReplyTokenHash))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PushDeviceModel>> GetAllAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PushDevices
            .AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .Select(x => new PushDeviceModel(x.CoordinatorInstanceId, x.UserId, x.Token, x.Platform, x.KeyId, x.Key))
            .ToListAsync(cancellationToken);
    }
}
