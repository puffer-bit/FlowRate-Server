using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.TextChat.Messages.Reactions;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Mappers;

public static class ChatMessageEntityMapper
{
    // -----------------------------------------------------------------
    // message conversions between model and database entity
    // -----------------------------------------------------------------

    public static ChatMessageEntity ToEntity(this ChatMessageModel? model)
    {
        if (model == null) return null!;

        var entity = new ChatMessageEntity
        {
            Id = model.Id,
            ChatId = model.ChatId,
            Revision = model.Revision,
            SenderUserId = model.SenderUserId,
            SentTime = model.SentTime,
            DelayedSentTime = model.DelayedSentTime,
            DisposeTime = model.DisposeTime,
            EditTime = model.EditTime,
            IsDelayed = model.IsDelayed,
            IsEdited = model.IsEdited,
            IsReposted = model.IsReposted,
            IsDelivered = model.IsDelivered,
            IsDeleted = model.IsDeleted,
            IsReadOnly = model.IsReadOnly,
            IsSystem = model.IsSystem,
            IsDisposable = model.IsDisposable,

            TextContent = model.TextContent?.ToEntity()!,
            ReplyToMessageId = model.ReplyContent?.MessageId,
            Reactions = model.Reactions.Select(r => r.ToEntity()).ToList(),
            UsersIds = model.UsersIds.ToList(),
            Images = model.Images.Select(i => i.ToEntity(model.Id)).ToList(),
            Videos = model.Videos.Select(v => v.ToEntity(model.Id)).ToList(),
            Files = model.Files.Select(f => f.ToEntity(model.Id)).ToList()
        };

        return entity;
    }

    public static ChatMessageModel ToModel(this ChatMessageEntity? entity, ChatReplyContentModel? replyContent = null)
    {
        if (entity == null)
            return null!;
        
        return new ChatMessageModel
        {
            Id = entity.Id,
            ChatId = entity.ChatId,
            Revision = entity.Revision,
            SenderUserId = entity.SenderUserId,
            SentTime = entity.SentTime,
            DelayedSentTime = entity.DelayedSentTime,
            DisposeTime = entity.DisposeTime,
            EditTime = entity.EditTime,
            IsReposted = entity.IsReposted,
            IsDelivered = entity.IsDelivered,
            IsDeleted = entity.IsDeleted,
            IsReadOnly = entity.IsReadOnly,
            IsSystem = entity.IsSystem,

            TextContent = entity.TextContent?.ToModel(),
            ReplyContent = replyContent,
            Images = entity.Images?.Select(i => i.ToModel()).ToList() ?? [],
            Videos = entity.Videos?.Select(v => v.ToModel()).ToList() ?? [],
            Files = entity.Files?.Select(f => f.ToModel()).ToList() ?? [],
            Reactions = entity.Reactions?.Select(r => r.ToModel()).ToList() ?? [],
            UsersIds = entity.UsersIds ?? []
        };
    }
    
    // -----------------------------------------------------------------
    // content conversions (model <-> entity)
    // -----------------------------------------------------------------

    public static ChatTextContentEntity ToEntity(this ChatTextContentModel? model)
    {
        if (model == null) return null!;
        return new ChatTextContentEntity 
        {
            Text = model.Text
        };
    }

    public static ChatTextContentModel ToModel(this ChatTextContentEntity? entity)
    {
        if (entity == null) return null!;
        return new ChatTextContentModel
        {
            Text = entity.Text
        };
    }

    public static ChatReplyContentModel ToReplyContentModel(this ChatMessageEntity? target, int targetId)
    {
        if (target == null)
            return new ChatReplyContentModel { MessageId = targetId, IsDeleted = true };

        return new ChatReplyContentModel
        {
            MessageId = target.Id,
            SenderUserId = target.SenderUserId,
            SentTime = target.SentTime,
            TextContent = target.IsDeleted ? null : target.TextContent?.ToModel(),
            IsDeleted = target.IsDeleted
        };
    }

    public static ChatImageContentEntity ToEntity(this ChatImageContentModel? model, int messageId)
    {
        if (model == null) return null!;
        return new ChatImageContentEntity
        {
            MessageId = messageId,
            Id = model.Id,
            FileName = model.FileName,
            Thumbnail = model.Thumbnail,
            Description = model.Description,
            Size = model.Size,
            MimeType = model.MimeType,
            Width = model.Width,
            Height = model.Height,
            UploadId = model.UploadId,
            PreviewUploadId = model.PreviewUploadId
        };
    }

    public static ChatImageContentModel ToModel(this ChatImageContentEntity? entity)
    {
        if (entity == null) return null!;
        return new ChatImageContentModel
        {
            Id = entity.Id,
            FileName = entity.FileName,
            Thumbnail = entity.Thumbnail,
            Description = entity.Description,
            Size = entity.Size,
            MimeType = entity.MimeType,
            Width = entity.Width,
            Height = entity.Height,
            UploadId = entity.UploadId,
            PreviewUploadId = entity.PreviewUploadId
        };
    }

    public static ChatVideoContentEntity ToEntity(this ChatVideoContentModel? model, int messageId)
    {
        if (model == null) return null!;
        return new ChatVideoContentEntity
        {
            MessageId = messageId,
            Id = model.Id,
            FileName = model.FileName,
            Thumbnail = model.Thumbnail,
            Description = model.Description,
            Size = model.Size,
            MimeType = model.MimeType,
            Width = model.Width,
            Height = model.Height,
            Duration = model.Duration
        };
    }

    public static ChatVideoContentModel ToModel(this ChatVideoContentEntity? entity)
    {
        if (entity == null) return null!;
        return new ChatVideoContentModel
        {
            Id = entity.Id,
            FileName = entity.FileName,
            Thumbnail = entity.Thumbnail,
            Description = entity.Description,
            Size = entity.Size,
            MimeType = entity.MimeType,
            Width = entity.Width,
            Height = entity.Height,
            Duration = entity.Duration
        };
    }

    public static ChatFileContentEntity ToEntity(this ChatFileContentModel? model, int messageId)
    {
        if (model == null) return null!;
        return new ChatFileContentEntity
        {
            MessageId = messageId,
            Id = model.Id,
            FileName = model.FileName,
            Size = model.Size,
            MimeType = model.MimeType,
            UploadId = model.UploadId
        };
    }

    public static ChatFileContentModel ToModel(this ChatFileContentEntity? entity)
    {
        if (entity == null) return null!;
        return new ChatFileContentModel
        {
            Id = entity.Id,
            FileName = entity.FileName,
            Size = entity.Size,
            MimeType = entity.MimeType,
            UploadId = entity.UploadId
        };
    }

    public static ChatReactionEntity ToEntity(this ChatReactionModel? model)
    {
        if (model == null) return null!;
        return new ChatReactionEntity
        {
            Emoji = model.Emoji,
            UserId = model.UserId
        };
    }

    public static ChatReactionModel ToModel(this ChatReactionEntity? entity)
    {
        if (entity == null) return null!;
        return new ChatReactionModel
        {
            Emoji = entity.Emoji,
            UserId = entity.UserId
        };
    }
}