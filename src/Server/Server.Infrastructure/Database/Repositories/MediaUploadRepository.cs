using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Media;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Repositories;

public class MediaUploadRepository : IMediaUploadRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public MediaUploadRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(MediaUploadModel upload, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.MediaUploads.Add(new MediaUploadEntity
        {
            Id = upload.Id,
            CoordinatorInstanceId = upload.CoordinatorInstanceId,
            OwnerUserId = upload.OwnerUserId,
            ChatId = upload.ChatId,
            Kind = upload.Kind,
            MimeType = upload.MimeType,
            Size = upload.Size,
            State = upload.State,
            CreatedAt = upload.CreatedAt,
            MessageId = upload.MessageId
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MediaUploadModel?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.MediaUploads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity == null
            ? null
            : new MediaUploadModel(entity.Id, entity.CoordinatorInstanceId, entity.OwnerUserId, entity.ChatId,
                entity.Kind, entity.MimeType, entity.Size, entity.State, entity.CreatedAt, entity.MessageId);
    }

    public async Task<bool> MarkCompletedAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.MediaUploads
            .Where(x => x.Id == id && x.State == MediaUploadState.Uploading)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, MediaUploadState.Completed), cancellationToken) == 1;
    }

    public async Task<bool> MarkAttachedAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.MediaUploads
            .Where(x => x.Id == id && x.State == MediaUploadState.Completed)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, MediaUploadState.Attached), cancellationToken) == 1;
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.MediaUploads.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public Task<IReadOnlyList<string>> RemoveByMessageAsync(int messageId, CancellationToken cancellationToken = default) =>
        RemoveAsync(uploads => uploads.Where(x => x.MessageId == messageId), cancellationToken);

    public Task<IReadOnlyList<string>> RemoveByChatAsync(int chatId, CancellationToken cancellationToken = default) =>
        RemoveAsync(uploads => uploads.Where(x => x.ChatId == chatId), cancellationToken);

    public Task<IReadOnlyList<string>> RemoveUnattachedAsync(string coordinatorInstanceId, DateTime createdBefore,
        CancellationToken cancellationToken = default) =>
        RemoveAsync(uploads => uploads.Where(x => x.CoordinatorInstanceId == coordinatorInstanceId
                                                  && x.State != MediaUploadState.Attached
                                                  && x.CreatedAt < createdBefore), cancellationToken);

    public async Task<long> GetTotalSizeAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.MediaUploads
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .SumAsync(x => x.Size, cancellationToken);
    }

    public async Task<int> CountUnattachedAsync(string coordinatorInstanceId, int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.MediaUploads.CountAsync(x => x.CoordinatorInstanceId == coordinatorInstanceId
                                                          && x.OwnerUserId == ownerUserId
                                                          && x.State != MediaUploadState.Attached, cancellationToken);
    }

    public async Task<ChatImageFileModel?> GetImageAsync(int imageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ChatImageContents.AsNoTracking()
            .Where(image => image.Id == imageId)
            .Join(context.ChatMessagesEntities.IgnoreQueryFilters(), image => image.MessageId, message => message.Id,
                (image, message) => new ChatImageFileModel(image.Id, message.ChatId, message.IsDeleted, image.MimeType,
                    image.UploadId, image.PreviewUploadId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ChatFileDownloadModel?> GetFileAsync(int fileId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ChatFileContents.AsNoTracking()
            .Where(file => file.Id == fileId)
            .Join(context.ChatMessagesEntities.IgnoreQueryFilters(), file => file.MessageId, message => message.Id,
                (file, message) => new ChatFileDownloadModel(file.Id, message.ChatId, message.IsDeleted, file.FileName,
                    file.UploadId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> RemoveAsync(
        Func<IQueryable<MediaUploadEntity>, IQueryable<MediaUploadEntity>> filter, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var ids = await filter(context.MediaUploads).Select(x => x.Id).ToListAsync(cancellationToken);
        if (ids.Count > 0)
            await context.MediaUploads.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ids;
    }
}
