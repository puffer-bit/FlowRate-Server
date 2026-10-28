using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Events;
using Server.Application.Mappers;
using Server.Core.Models.Media;
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
            _sessions.TryAdd(chat.Id, chat);
    }

    public async Task<(ErrorCode? result, ChatSessionModel? session)> CreateAsync(CreateSessionModel request,
        int userId)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_sessions.Values.Any(s => s.Name == request.Name))
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
            if (!_sessions.ContainsKey(sessionId))
                return ErrorCode.SessionNotFound;

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

            var (attachmentsResult, images, files) = await ResolveAttachmentsAsync(chat, message, senderUserId);
            if (attachmentsResult != null)
                return attachmentsResult;

            var messageModel = message.ToModel(senderUserId, DateTime.UtcNow, images, files);

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
                var stored = await _messagesRepository.AddAsync(messageModel);
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

                if (!mayDeleteAny && !chat.CanDeleteMessage(message, userId))
                    return ErrorCode.InsufficientPermissions;

                var revision = await _messagesRepository.DeleteAsync(messageId);
                if (revision == null)
                    return ErrorCode.MessageNotFound;

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
