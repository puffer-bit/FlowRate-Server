using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Events;
using Server.Application.Mappers;
using Server.Core.Models.Media;
using Server.Core.Models.User;
using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Application.Abstractions.Media;
using Server.Core.Models.TextChat.Messages;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession.Models;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Sessions.Chat;

public class ChatSessionManager : IChatSessionManager
{
    private readonly ILogger _logger;
    private readonly ICoordinatorEventBus _coordinatorEventBus;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessagesRepository _messagesRepository;
    private readonly IMediaUploadRepository _mediaUploads;
    private readonly IMediaStorage _mediaStorage;
    private readonly string _coordinatorInstanceId;

    private readonly ConcurrentDictionary<int, ChatSessionModel> _sessions = new();

    // Serializes creation and deletion, so that two chats cannot take the same name
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    // Serializes the writes of one chat, so that its events come out in revision order
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _messageWriteLocks = new();

    private const int ChangesResetThreshold = 500;

    public ChatSessionManager(
        ILogger logger,
        ICoordinatorEventBus coordinatorEventBus,
        IChatSessionRepository chatSessionRepository,
        IMessagesRepository messagesRepository,
        IMediaUploadRepository mediaUploads,
        IMediaStorage mediaStorage,
        string coordinatorInstanceId)
    {
        _mediaUploads = mediaUploads;
        _mediaStorage = mediaStorage;
        _logger = logger;
        _coordinatorEventBus = coordinatorEventBus;
        _chatSessionRepository = chatSessionRepository;
        _messagesRepository = messagesRepository;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task LoadAsync()
    {
        foreach (var chat in await _chatSessionRepository.GetByCoordinatorIdAsync(_coordinatorInstanceId))
        {
            chat.SetLastMessage((await _messagesRepository.GetLastMessagesAsync(chat.Id, 1))?.FirstOrDefault());
            _sessions.TryAdd(chat.Id, chat);
        }
    }

    public async Task<(ErrorCode? result, ChatSessionModel? session)> CreateAsync(CreateSessionModel request,
        int userId)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            var data = request.SessionData as ChatSessionCreateData;
            var kind = data?.Kind ?? ChatKind.ServerChannel;
            var members = (data?.MemberIds ?? []).Append(userId).Distinct().Order().ToArray();
            if (!Enum.IsDefined(kind) || (kind == ChatKind.Direct && members.Length != 2)
                || (kind == ChatKind.Group && members.Length is < 2 or > 100))
                return (ErrorCode.InvalidValue, null);
            if (kind == ChatKind.Direct && _sessions.Values.FirstOrDefault(s =>
                    s.Kind == ChatKind.Direct && s.MemberIds.SetEquals(members)) is { } existing)
                return (null, existing);
            if (kind == ChatKind.ServerChannel && _sessions.Values.Any(s => !s.IsPrivate && s.Name == request.Name))
                return (ErrorCode.NameTaken, null);

            var chat = ChatSessionModel.Create(_coordinatorInstanceId, request.Name, userId);
            if (request.SessionData is ChatSessionCreateData chatData)
            {
                chat.Describe(chatData.Description, chatData.IconPath);
                chat.Configure(new ChatSessionOptions(
                    IsAttachmentsAllowed: chatData.IsAttachmentsAllowed,
                    IsVoiceMessagesAllowed: chatData.IsVoiceMessagesAllowed,
                    IsVideoMessagesAllowed: chatData.IsVideoMessagesAllowed,
                    IsDelayedMessagesAllowed: chatData.IsDelayedMessagesAllowed));
            }

            chat.SetAudience(kind, kind == ChatKind.ServerChannel ? [] : members);
            chat.Id = await _chatSessionRepository.AddAsync(chat);
            _sessions[chat.Id] = chat;

            await _coordinatorEventBus.PublishAsync(new ChatSessionCreatedEvent(chat, userId));
            return (null, chat);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while creating chat session.");
            return (ErrorCode.Internal, null);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<ErrorCode?> DeleteAsync(int sessionId, int userId)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var chat))
                return ErrorCode.SessionNotFound;
            if (chat.IsPrivate && (chat.Kind != ChatKind.Group || chat.AdminUserId != userId))
                return ErrorCode.InsufficientPermissions;

            await _chatSessionRepository.RemoveAsync(sessionId);
            _sessions.TryRemove(sessionId, out _);
            _messageWriteLocks.TryRemove(sessionId, out _);

            await _coordinatorEventBus.PublishAsync(new ChatSessionDeletedEvent(sessionId, userId));
            await DeleteMediaAsync(() => _mediaUploads.RemoveByChatAsync(sessionId));
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while deleting chat session.");
            return ErrorCode.Internal;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<ErrorCode?> ChangeGroupAsync(int sessionId, string name, int[] memberIds, int ownerId,
        int userId, bool leave)
    {
        await _lifecycleLock.WaitAsync();
        var writeLock = MessageWriteLockOf(sessionId);
        await writeLock.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var chat) || chat.Kind != ChatKind.Group)
                return ErrorCode.ChatNotFound;
            if (!chat.MemberIds.Contains(userId)) return ErrorCode.InsufficientPermissions;
            var members = leave ? chat.MemberIds.Where(id => id != userId).ToArray() : memberIds.Distinct().ToArray();
            if (leave && chat.AdminUserId == userId) return ErrorCode.Conflict;
            if (!leave && chat.AdminUserId != userId) return ErrorCode.InsufficientPermissions;
            if (members.Length is < 1 or > 100 || !members.Contains(leave ? chat.AdminUserId : ownerId))
                return ErrorCode.InvalidValue;
            var changed = new ChatSessionModel(chat.Id, chat.CoordinatorInstanceId, leave ? chat.Name : name,
                chat.Capacity, chat.CreatorUserId, leave ? chat.AdminUserId : ownerId, chat.Options,
                chat.Description, chat.IconPath, chat.IsArchived);
            changed.SetAudience(ChatKind.Group, members);
            changed.SetLastMessage(chat.LastMessage);
            var now = DateTime.UtcNow;
            var messages = members.Except(chat.MemberIds).Order().Select(id =>
                ChatMessageModel.CreateSystem(sessionId, new ChatSystemContentModel
                {
                    Kind = ChatSystemEventKind.MemberAdded, ActorUserId = userId, SubjectUserId = id
                }, now)).Concat(chat.MemberIds.Except(members).Order().Select(id =>
                ChatMessageModel.CreateSystem(sessionId, new ChatSystemContentModel
                {
                    Kind = leave ? ChatSystemEventKind.MemberLeft : ChatSystemEventKind.MemberRemoved,
                    ActorUserId = userId, SubjectUserId = id
                }, now))).ToList();
            var stored = await _chatSessionRepository.UpdateWithSystemMessagesAsync(changed, messages);
            chat.SetAudience(ChatKind.Group, members);
            chat.Rename(changed.Name);
            chat.ChangeAdmin(changed.AdminUserId);
            // Revoke removed members before delivering any of the new messages.
            await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(chat, userId));
            foreach (var message in stored)
                await PublishAddedMessageAsync(chat, message, userId);
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while changing group settings.");
            return ErrorCode.Internal;
        }
        finally
        {
            writeLock.Release();
            _lifecycleLock.Release();
        }
    }

    public bool TryGetSession(int sessionId, [NotNullWhen(true)] out ChatSessionModel? session) =>
        _sessions.TryGetValue(sessionId, out session);

    public IReadOnlyCollection<ChatSessionModel> GetAllSessions() => _sessions.Values.ToList();

    public async Task<(ErrorCode? result, List<ChatMessageModel>? messages)> FetchMessagesAsync(
        int sessionId, int take, int? skip)
    {
        try
        {
            if (!_sessions.ContainsKey(sessionId))
                return (ErrorCode.ChatNotFound, null);

            var messages = await _messagesRepository.GetByTakeSkipAsync(sessionId, skip ?? 0, take);
            return (null, messages);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while fetching chat messages.");
            return (ErrorCode.Internal, null);
        }
    }

    public async Task<(ErrorCode? result, ChatHistoryPage? page)> FetchMessagesBeforeAsync(
        int sessionId, int? beforeMessageId, int take)
    {
        try
        {
            if (!_sessions.ContainsKey(sessionId))
                return (ErrorCode.ChatNotFound, null);

            var revision = await _messagesRepository.GetRevisionAsync(sessionId);
            var messages = await _messagesRepository.GetBeforeAsync(sessionId, beforeMessageId, take);
            return (null, new ChatHistoryPage(messages, revision));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while fetching chat messages.");
            return (ErrorCode.Internal, null);
        }
    }

    public async Task<(ErrorCode? result, ChatChanges? changes)> FetchChangesAsync(
        int sessionId, long afterRevision, int take)
    {
        try
        {
            if (!_sessions.ContainsKey(sessionId))
                return (ErrorCode.ChatNotFound, null);

            var revision = await _messagesRepository.GetRevisionAsync(sessionId);
            if (afterRevision > revision)
                return (null, new ChatChanges([], revision, false, true));

            var count = await _messagesRepository.CountChangesAsync(sessionId, afterRevision);
            if (count > ChangesResetThreshold)
                return (null, new ChatChanges([], revision, false, true));

            var messages = await _messagesRepository.GetChangesAsync(sessionId, afterRevision, take);
            return (null, new ChatChanges(messages, revision, count > messages.Count, false));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while fetching chat changes.");
            return (ErrorCode.Internal, null);
        }
    }

    public async Task<(ErrorCode? result, ChatMessageModel? message)> FetchMessageAsync(int sessionId,
        int messageId)
    {
        try
        {
            // Message ids are shared by all coordinators, so the chat must be one of this coordinator
            var message = _sessions.ContainsKey(sessionId) ? await GetMessageAsync(sessionId, messageId) : null;
            return message == null
                ? (ErrorCode.MessageNotFound, null)
                : (null, message);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while fetching chat message.");
            return (ErrorCode.Internal, null);
        }
    }

    public async Task<ErrorCode?> AddMessageAsync(int sessionId, NewChatMessageModel message, int senderUserId)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var chat))
                return ErrorCode.ChatNotFound;

            if (!chat.CanPost)
                return ErrorCode.ChatReadOnly;

            var command = ChatCommandRequest.Parse(message.TextContent?.Text);
            if (command is { IsValid: false }) return ErrorCode.InvalidValue;

            var (attachmentsResult, images, files) = await ResolveAttachmentsAsync(chat, message, senderUserId);
            if (attachmentsResult != null)
                return attachmentsResult;

            var messageModel = message.ToModel(senderUserId, DateTime.UtcNow, images, files);
            if (command != null) messageModel.ExecuteCommand(command.ToModel());

            if (message.ReplyToMessageId is { } replyToMessageId)
            {
                var repliedMessage = await GetMessageAsync(sessionId, replyToMessageId);
                if (repliedMessage == null)
                    return ErrorCode.ReplyTargetNotFound;

                messageModel.ReplyTo(repliedMessage);
            }

            var writeLock = MessageWriteLockOf(sessionId);
            await writeLock.WaitAsync();
            try
            {
                if (chat.IsPrivate && !chat.MemberIds.Contains(senderUserId))
                    return ErrorCode.InsufficientPermissions;
                var stored = await _messagesRepository.AddAsync(messageModel);
                chat.SetLastMessage(stored);
                await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(chat, senderUserId));
                await _coordinatorEventBus.PublishAsync(new ChatSessionMessageAddedEvent(sessionId, stored));
            }
            finally
            {
                writeLock.Release();
            }

            return null;
        }
        catch (MediaUploadUnavailableException)
        {
            return ErrorCode.AttachmentNotReady;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while adding chat message.");
            return ErrorCode.Internal;
        }
    }

    /// <summary>Server-only entry point for the call lifecycle; never exposed as a client command.</summary>
    public async Task<ErrorCode?> AddCallSystemMessageAsync(int sessionId, ChatSystemContent content)
    {
        if (!content.IsValid() || content.Kind is not (ChatSystemEventKind.CallStarted or ChatSystemEventKind.CallEnded))
            return ErrorCode.InvalidValue;
        var writeLock = MessageWriteLockOf(sessionId);
        await writeLock.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var chat)) return ErrorCode.ChatNotFound;
            var message = ChatMessageModel.CreateSystem(sessionId, content.ToModel(), DateTime.UtcNow);
            var stored = await _messagesRepository.AddAsync(message);
            await PublishAddedMessageAsync(chat, stored, content.ActorUserId ?? UserModel.SystemUserId);
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while adding a call system message.");
            return ErrorCode.Internal;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task PublishAddedMessageAsync(ChatSessionModel chat, ChatMessageModel message, int actorUserId)
    {
        chat.SetLastMessage(message);
        await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(chat, actorUserId));
        await _coordinatorEventBus.PublishAsync(new ChatSessionMessageAddedEvent(chat.Id, message));
    }

    public async Task<ErrorCode?> SetPinnedAsync(int sessionId, int messageId, bool pinned, int userId)
    {
        if (!_sessions.TryGetValue(sessionId, out var chat)) return ErrorCode.ChatNotFound;
        if (chat.IsArchived) return ErrorCode.ChatReadOnly;
        var gate = MessageWriteLockOf(sessionId);
        await gate.WaitAsync();
        try
        {
            if (chat.IsPrivate && !chat.MemberIds.Contains(userId)) return ErrorCode.InsufficientPermissions;
            var message = await GetMessageAsync(sessionId, messageId);
            if (message == null || message.IsDeleted) return ErrorCode.MessageNotFound;
            if (message.IsSystem) return ErrorCode.InsufficientPermissions;
            if (message.IsPinned == pinned) return null;
            // Bound the independently loaded list of pins.
            if (pinned && (await _messagesRepository.GetPinnedAsync(sessionId)).Count >= 100)
                return ErrorCode.InvalidRequest;
            message.SetPinned(pinned, userId, DateTime.UtcNow);
            message.MarkChanged(await _messagesRepository.UpdateAsync(message));
            if (_sessions.TryGetValue(sessionId, out var changedChat) && changedChat.LastMessage?.Id == message.Id)
            {
                changedChat.SetLastMessage(message);
                await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(changedChat, userId));
            }
            await _coordinatorEventBus.PublishAsync(new ChatSessionMessageEditedEvent(sessionId, message));
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while pinning a chat message.");
            return ErrorCode.Internal;
        }
        finally { gate.Release(); }
    }

    public async Task<(ErrorCode? result, ChatHistoryPage? page)> FetchPinnedAsync(int sessionId)
    {
        if (!_sessions.ContainsKey(sessionId)) return (ErrorCode.ChatNotFound, null);
        var gate = MessageWriteLockOf(sessionId);
        await gate.WaitAsync();
        try
        {
            var revision = await _messagesRepository.GetRevisionAsync(sessionId);
            return (null, new ChatHistoryPage(await _messagesRepository.GetPinnedAsync(sessionId), revision));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while fetching pinned messages.");
            return (ErrorCode.Internal, null);
        }
        finally { gate.Release(); }
    }

    public async Task<(ErrorCode? result, ChatHistoryWindow? window)> FetchWindowAsync(int sessionId,
        int messageId, ChatHistoryDirection direction, int take)
    {
        if (!_sessions.ContainsKey(sessionId)) return (ErrorCode.ChatNotFound, null);
        if (take < 1 || take > Shared.Validation.Limits.ChatHistoryMaxTake || !Enum.IsDefined(direction))
            return (ErrorCode.InvalidRequest, null);
        var gate = MessageWriteLockOf(sessionId);
        await gate.WaitAsync();
        try
        {
            // A soft-deleted boundary still allows before/after pagination, but cannot be opened.
            var anchor = await GetMessageAsync(sessionId, messageId);
            if (anchor == null || (anchor.IsDeleted && direction == ChatHistoryDirection.Around))
                return (ErrorCode.MessageNotFound, null);
            var revision = await _messagesRepository.GetRevisionAsync(sessionId);
            List<ChatMessageModel> messages;
            switch (direction)
            {
                case ChatHistoryDirection.Before:
                    messages = await _messagesRepository.GetBeforeAsync(sessionId, messageId, take);
                    break;
                case ChatHistoryDirection.After:
                    messages = await _messagesRepository.GetAfterAsync(sessionId, messageId, take);
                    break;
                default:
                    messages = await _messagesRepository.GetBeforeAsync(sessionId, messageId, take / 2);
                    messages.Add(anchor!);
                    messages.AddRange(await _messagesRepository.GetAfterAsync(sessionId, messageId, take - take / 2 - 1));
                    break;
            }
            var first = messages.Count > 0 ? messages[0].Id : messageId;
            var last = messages.Count > 0 ? messages[^1].Id : messageId;
            var older = (await _messagesRepository.GetBeforeAsync(sessionId, first, 1)).Count > 0;
            var newer = (await _messagesRepository.GetAfterAsync(sessionId, last, 1)).Count > 0;
            return (null, new ChatHistoryWindow(messages, revision, older, newer));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while fetching a chat history window.");
            return (ErrorCode.Internal, null);
        }
        finally { gate.Release(); }
    }

    public async Task<ErrorCode?> SetReactionAsync(int sessionId, int messageId, string emoji, bool isAdded, int userId)
    {
        if (!Shared.Models.DataTransferObjects.ChatSession.Messages.Reactions.ChatReactionEmoji.IsSupported(emoji))
            return ErrorCode.InvalidRequest;

        try
        {
            if (!_sessions.TryGetValue(sessionId, out var chat))
                return ErrorCode.ChatNotFound;
            if (!chat.CanPost)
                return ErrorCode.ChatReadOnly;

            var writeLock = MessageWriteLockOf(sessionId);
            await writeLock.WaitAsync();
            try
            {
                var message = await GetMessageAsync(sessionId, messageId);
                if (message == null || message.IsDeleted)
                    return ErrorCode.MessageNotFound;
                if (message.IsSystem || message.IsReadOnly)
                    return ErrorCode.InsufficientPermissions;

                var changed = isAdded ? message.AddReaction(userId, emoji) : message.RemoveReaction(userId, emoji);
                if (!changed)
                    return null;

                message.MarkChanged(await _messagesRepository.UpdateAsync(message));
                if (_sessions.TryGetValue(sessionId, out var changedChat) && changedChat.LastMessage?.Id == message.Id)
                {
                    changedChat.SetLastMessage(message);
                    await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(changedChat, userId));
                }
                await _coordinatorEventBus.PublishAsync(new ChatSessionMessageEditedEvent(sessionId, message));
                return null;
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while setting a chat reaction.");
            return ErrorCode.Internal;
        }
    }

    public async Task<ErrorCode?> EditMessageAsync(int sessionId, int messageId, string text, int userId)
    {
        try
        {
            if (!_sessions.ContainsKey(sessionId))
                return ErrorCode.ChatNotFound;

            var writeLock = MessageWriteLockOf(sessionId);
            await writeLock.WaitAsync();
            try
            {
                var message = await GetMessageAsync(sessionId, messageId);
                if (message == null)
                    return ErrorCode.MessageNotFound;

                var result = message.Edit(userId, text, DateTime.UtcNow);
                if (result != null)
                    return result;

                message.MarkChanged(await _messagesRepository.UpdateAsync(message));
                if (_sessions.TryGetValue(sessionId, out var changedChat) && changedChat.LastMessage?.Id == message.Id)
                {
                    changedChat.SetLastMessage(message);
                    await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(changedChat, userId));
                }
                await _coordinatorEventBus.PublishAsync(new ChatSessionMessageEditedEvent(sessionId, message));
                return null;
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while editing chat message.");
            return ErrorCode.Internal;
        }
    }

    public async Task<ErrorCode?> DeleteMessageAsync(int sessionId, int messageId, int userId,
        bool mayDeleteAny = false)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var chat))
                return ErrorCode.ChatNotFound;

            var writeLock = MessageWriteLockOf(sessionId);
            await writeLock.WaitAsync();
            try
            {
                var message = await GetMessageAsync(sessionId, messageId);
                if (message == null || message.IsDeleted)
                    return ErrorCode.MessageNotFound;

                if (message.IsSystem || (!mayDeleteAny && !chat.CanDeleteMessage(message, userId)))
                    return ErrorCode.InsufficientPermissions;

                var revision = await _messagesRepository.DeleteAsync(messageId);
                if (revision == null)
                    return ErrorCode.MessageNotFound;

                if (chat.LastMessage?.Id == messageId)
                {
                    chat.SetLastMessage((await _messagesRepository.GetLastMessagesAsync(sessionId, 1))?.FirstOrDefault());
                    await _coordinatorEventBus.PublishAsync(new ChatSessionReconfiguredEvent(chat, userId));
                }
                await _coordinatorEventBus.PublishAsync(
                    new ChatSessionMessageDeletedEvent(sessionId, messageId, revision.Value));
                await DeleteMediaAsync(() => _mediaUploads.RemoveByMessageAsync(messageId));
                return null;
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while deleting chat message.");
            return ErrorCode.Internal;
        }
    }

    private async Task<(ErrorCode? result, IReadOnlyList<ChatImageContentModel> images,
        IReadOnlyList<ChatFileContentModel> files)> ResolveAttachmentsAsync(
        ChatSessionModel chat, NewChatMessageModel message, int senderUserId)
    {
        if (message.Images.Count + message.Video.Count + message.Files.Count == 0)
            return (null, [], []);

        if (!chat.Options.IsAttachmentsAllowed)
            return (ErrorCode.AttachmentsNotAllowed, [], []);

        if (message.Video.Count > 0)
            return (ErrorCode.AttachmentInvalid, [], []);

        var images = new List<ChatImageContentModel>(message.Images.Count);
        foreach (var image in message.Images)
        {
            var original = await GetUploadAsync(image.UploadId, chat.Id, senderUserId, MediaUploadKind.Image);
            var preview = await GetUploadAsync(image.PreviewUploadId, chat.Id, senderUserId, MediaUploadKind.ImagePreview);
            if (original == null || preview == null)
                return (ErrorCode.AttachmentInvalid, [], []);

            if (original.State != MediaUploadState.Completed || preview.State != MediaUploadState.Completed)
                return (original.State == MediaUploadState.Attached || preview.State == MediaUploadState.Attached
                    ? ErrorCode.AttachmentInvalid
                    : ErrorCode.AttachmentNotReady, [], []);

            images.Add(new ChatImageContentModel
            {
                Id = 0,
                FileName = image.FileName,
                Description = image.Description,
                Size = original.Size,
                MimeType = original.MimeType,
                Width = image.Width,
                Height = image.Height,
                UploadId = original.Id,
                PreviewUploadId = preview.Id
            });
        }

        var files = new List<ChatFileContentModel>(message.Files.Count);
        foreach (var file in message.Files)
        {
            var upload = await GetUploadAsync(file.UploadId, chat.Id, senderUserId, MediaUploadKind.File);
            if (upload == null || upload.State == MediaUploadState.Attached)
                return (ErrorCode.AttachmentInvalid, [], []);
            if (upload.State != MediaUploadState.Completed)
                return (ErrorCode.AttachmentNotReady, [], []);

            files.Add(new ChatFileContentModel
            {
                Id = 0,
                FileName = file.FileName,
                Size = upload.Size,
                MimeType = upload.MimeType,
                UploadId = upload.Id
            });
        }

        return (null, images, files);
    }

    private async Task<MediaUploadModel?> GetUploadAsync(string id, int chatId, int ownerUserId, MediaUploadKind kind)
    {
        var upload = await _mediaUploads.GetAsync(id.ToLowerInvariant());
        return upload != null
               && upload.CoordinatorInstanceId == _coordinatorInstanceId
               && upload.ChatId == chatId
               && upload.IsOwnedBy(ownerUserId)
               && upload.Kind == kind
            ? upload
            : null;
    }

    private async Task DeleteMediaAsync(Func<Task<IReadOnlyList<string>>> removeUploads)
    {
        try
        {
            foreach (var id in await removeUploads())
                await _mediaStorage.DeleteAsync(id);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Deleting the files of removed messages failed.");
        }
    }

    private SemaphoreSlim MessageWriteLockOf(int sessionId) =>
        _messageWriteLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    /// <returns>Null also when the message belongs to another chat.</returns>
    private async Task<ChatMessageModel?> GetMessageAsync(int sessionId, int messageId)
    {
        var message = await _messagesRepository.GetByIdAsync(messageId);
        return message?.ChatId == sessionId ? message : null;
    }
}
