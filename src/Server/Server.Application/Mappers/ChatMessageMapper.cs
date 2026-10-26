using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.TextChat.Messages.Reactions;
using Server.Core.Models.User;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Content;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Reactions;
using Shared.Models.Requests.SessionActions.ChatSession.Models;

namespace Server.Application.Mappers;

public static class ChatMessageMapperExtensions
{
    // -----------------------------------------------------------------
    // DTO conversions for messages, reactions and content
    // -----------------------------------------------------------------

    public static ChatMessageDTO ToDto(this ChatMessageModel? model)
    {
        if (model == null) return null!;
        var mentions = ChatMentions.Find(model.TextContent?.Text);
        return new ChatMessageDTO
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

            TextContent = model.TextContent?.ToDto()!,
            ReplyContent = model.ReplyContent?.ToDto()!,
            Images = model.Images.Select(i => i.ToDto()).ToList(),
            Video = model.Videos.Select(v => v.ToDto()).ToList(),
            Files = model.Files.Select(f => f.ToDto()).ToList(),
            Reactions = model.Reactions.Select(r => r.ToDto()).ToList(),
            UsersIds = model.UsersIds.ToList(),
            MentionedUserIds = ChatMentions.UserIdsOf(mentions),
            MentionsEveryone = mentions.Any(m => m.Kind == ChatMentionKind.Everyone),
            MentionsHere = mentions.Any(m => m.Kind == ChatMentionKind.Here)
        };
    }

    public static List<ChatMessageDTO> ToDtoList(this IEnumerable<ChatMessageModel>? models)
    {
        if (models == null) return new List<ChatMessageDTO>();
        return models.Select(m => m.ToDto()).ToList();
    }

    // content and reaction -> dto conversion helpers

    public static ChatTextContentDTO ToDto(this ChatTextContentModel? model)
    {
        if (model == null) return null!;
        return new ChatTextContentDTO { Text = model.Text };
    }

    public static ChatReplyContentDTO ToDto(this ChatReplyContentModel? model)
    {
        if (model == null) return null!;
        return new ChatReplyContentDTO
        {
            MessageId = model.MessageId,
            TextContent = model.TextContent?.ToDto(),
            SenderUserId = model.SenderUserId,
            SentTime = model.SentTime,
            IsDeleted = model.IsDeleted
        };
    }

    public static ChatImageContentDTO ToDto(this ChatImageContentModel? model)
    {
        if (model == null) return null!;
        return new ChatImageContentDTO
        {
            Id = model.Id,
            FileName = model.FileName,
            Thumbnail = model.Thumbnail,
            Description = model.Description,
            Size = model.Size,
            MimeType = model.MimeType,
            Width = model.Width,
            Height = model.Height
        };
    }

    public static ChatVideoContentDTO ToDto(this ChatVideoContentModel? model)
    {
        if (model == null) return null!;
        return new ChatVideoContentDTO
        {
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

    public static ChatFileContentDTO ToDto(this ChatFileContentModel? model)
    {
        if (model == null) return null!;
        return new ChatFileContentDTO
        {
            Id = model.Id,
            FileName = model.FileName,
            Size = model.Size,
            MimeType = model.MimeType
        };
    }

    public static ChatReactionDTO ToDto(this ChatReactionModel? model)
    {
        if (model == null) return null!;
        return new ChatReactionDTO
        {
            UserId = model.UserId,
            Emoji = model.Emoji
        };
    }

    // -------------------------------------------------------------
    // request -> model converters
    // -------------------------------------------------------------

    public static ChatMessageModel ToModel(this NewChatMessageModel newChatMessageModel, int senderUserId, DateTime now,
        IReadOnlyList<ChatImageContentModel> images, IReadOnlyList<ChatFileContentModel> files)
    {
        return ChatMessageModel.Create(
            newChatMessageModel.ChatId,
            senderUserId,
            now,
            newChatMessageModel.TextContent?.ToModel(),
            images,
            newChatMessageModel.Video.Select(v => v.ToModel()),
            files,
            newChatMessageModel.DelayedSentTime,
            newChatMessageModel.DisposeTime);
    }

    private static ChatTextContentModel ToModel(this ChatTextContentDTO dto) => new() { Text = dto.Text };

    private static ChatVideoContentModel ToModel(this ChatVideoContentDTO dto) => new()
    {
        Id = dto.Id,
        FileName = dto.FileName,
        Thumbnail = dto.Thumbnail,
        Description = dto.Description,
        Size = dto.Size,
        MimeType = dto.MimeType,
        Width = dto.Width,
        Height = dto.Height,
        Duration = dto.Duration
    };
}
