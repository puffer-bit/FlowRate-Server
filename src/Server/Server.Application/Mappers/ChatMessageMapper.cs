using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.TextChat.Messages.Reactions;
using Server.Core.Models.User;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;
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
            ClientMessageId = model.ClientMessageId,
            SentAt = model.SentTime,
            ScheduledAt = model.DelayedSentTime,
            ExpiresAt = model.DisposeTime,
            EditedAt = model.EditTime,
            PinnedAt = model.PinnedAt,
            PinnedByUserId = model.PinnedByUserId,
            IsDeleted = model.IsDeleted,
            IsReadOnly = model.IsReadOnly,
            IsSystem = model.IsSystem,
            SystemContent = model.SystemContent?.ToDto(),

            TextContent = model.TextContent?.ToDto()!,
            ReplyContent = model.ReplyContent?.ToDto()!,
            // Images and other files are kept apart, so images come first
            Files = model.Images.Select(i => i.ToDto()).Concat(model.Files.Select(f => f.ToDto())).ToList(),
            LinkPreview = model.LinkPreview?.ToDto(),
            IsLinkPreviewDisabled = model.IsLinkPreviewDisabled,
            Reactions = model.Reactions.Select(r => r.ToDto()).ToList(),
            MentionedUserIds = ChatMentions.UserIdsOf(mentions),
            MentionsEveryone = mentions.Any(m => m.Kind == ChatMentionKind.Everyone),
            MentionsHere = mentions.Any(m => m.Kind == ChatMentionKind.Here)
        };
    }

    public static ChatLinkPreviewDTO ToDto(this ChatLinkPreviewModel model) => new()
    {
        Id = model.Id,
        Url = model.Url,
        SiteName = model.SiteName,
        Title = model.Title,
        Description = model.Description,
        HasImage = model.HasImage,
        ImageWidth = model.ImageWidth,
        ImageHeight = model.ImageHeight,
        IsLargeImage = model.IsLargeImage
    };

    public static ChatSystemContentModel ToModel(this ChatSystemContent dto) => new()
    {
        Kind = dto.Kind,
        ActorUserId = dto.ActorUserId,
        SubjectUserId = dto.SubjectUserId,
        CallId = dto.CallId,
        CallEndReason = dto.CallEndReason,
        CallDurationSeconds = dto.CallDurationSeconds
    };

    public static ChatSystemContent ToDto(this ChatSystemContentModel model) => new()
    {
        Kind = model.Kind,
        ActorUserId = model.ActorUserId,
        SubjectUserId = model.SubjectUserId,
        CallId = model.CallId,
        CallEndReason = model.CallEndReason,
        CallDurationSeconds = model.CallDurationSeconds
    };

    public static ChatCommandModel ToModel(this ChatCommandRequest dto) =>
        new(dto.Kind, dto.Minimum, dto.Maximum);

    public static ChatCommandResult ToDto(this ChatCommandResultModel model) => new()
    {
        Kind = model.Kind,
        Minimum = model.Minimum,
        Maximum = model.Maximum,
        Value = model.Value
    };

    public static List<ChatMessageDTO> ToDtoList(this IEnumerable<ChatMessageModel>? models)
    {
        if (models == null) return new List<ChatMessageDTO>();
        return models.Select(m => m.ToDto()).ToList();
    }

    // content and reaction -> dto conversion helpers

    public static ChatTextContentDTO ToDto(this ChatTextContentModel? model)
    {
        if (model == null) return null!;
        return new ChatTextContentDTO { Text = model.Text, CommandResult = model.CommandResult?.ToDto() };
    }

    public static ChatReplyContentDTO ToDto(this ChatReplyContentModel? model)
    {
        if (model == null) return null!;
        return new ChatReplyContentDTO
        {
            MessageId = model.MessageId,
            HasAttachments = model.HasAttachments,
            TextContent = model.TextContent?.ToDto(),
            SenderUserId = model.SenderUserId,
            SentAt = model.SentTime,
            IsDeleted = model.IsDeleted
        };
    }

    public static ChatImageFileDTO ToDto(this ChatImageContentModel model) => new()
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

    public static ChatFileDTO ToDto(this ChatFileContentModel model) => model.Video is { } video
        ? new ChatVideoFileDTO
        {
            Id = model.Id,
            FileName = model.FileName,
            Size = model.Size,
            MimeType = model.MimeType,
            Width = video.Width,
            Height = video.Height,
            Duration = video.Duration,
            HasPreview = model.PreviewUploadId != null
        }
        : new ChatFileDTO { Id = model.Id, FileName = model.FileName, Size = model.Size, MimeType = model.MimeType };

    public static ChatReactionDTO ToDto(this ChatReactionModel? model)
    {
        if (model == null) return null!;
        return new ChatReactionDTO
        {
            UserId = model.UserId,
            Emoji = model.Emoji,
            ReactedAt = model.ReactedAt
        };
    }

    // -------------------------------------------------------------
    // request -> model converters
    // -------------------------------------------------------------

    public static ChatMessageModel ToModel(this NewChatMessageModel newChatMessageModel, int senderUserId, DateTimeOffset now,
        IReadOnlyList<ChatImageContentModel> images, IReadOnlyList<ChatFileContentModel> files)
    {
        return ChatMessageModel.Create(
            newChatMessageModel.ChatId,
            senderUserId,
            now,
            newChatMessageModel.TextContent?.ToModel(),
            images,
            null,
            files,
            newChatMessageModel.ScheduledAt,
            newChatMessageModel.ExpiresAt,
            newChatMessageModel.IsLinkPreviewDisabled,
            newChatMessageModel.ClientMessageId);
    }

    private static ChatTextContentModel ToModel(this ChatTextContentDTO dto) => new() { Text = dto.Text };
}
