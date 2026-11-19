using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Models.DataTransferObjects.ChatSession;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;
using Shared.Models.Notifications.SessionInfo;

namespace Server.Application.Coordinator.Sessions.Chat;

/// <summary>
/// Read positions and unread counts of every chat; in direct chats and groups also read receipts: the peer
/// position, live <see cref="ChatPeerReadNotification"/>s and who read a message of the author and when.
/// </summary>
public sealed class ChatReadService : IEventHandler<ChatSessionMessageAddedEvent>
{
    private readonly ILogger _logger;
    private readonly IChatReadStateRepository _repository;
    private readonly IMessageSender _messageSender;
    private readonly TimeProvider _time;

    private IChatSessionManager? _chats;

    public ChatReadService(ILogger logger, IChatReadStateRepository repository, IMessageSender messageSender,
        TimeProvider? time = null)
    {
        _logger = logger;
        _repository = repository;
        _messageSender = messageSender;
        _time = time ?? TimeProvider.System;
    }

    public void Watch(IChatSessionManager chats)
    {
        _chats = chats;
    }

    public async Task<List<ChatReadState>> GetStatesAsync(int userId, CancellationToken ct = default)
    {
        if (_chats == null)
            return [];

        var chats = _chats.GetAllSessions().ToList();
        var positions = await _repository.GetPositionsAsync(userId, ct);
        var receiptChats = chats.Where(chat => HasReceipts(chat, userId)).Select(chat => chat.Id).ToList();
        var peers = receiptChats.Count == 0
            ? new Dictionary<int, int>()
            : await _repository.GetPeerPositionsAsync(userId, receiptChats, ct);

        var states = new List<ChatReadState>();
        foreach (var chat in chats)
        {
            states.Add(await StateOfAsync(userId, chat.Id, positions.GetValueOrDefault(chat.Id),
                peers.GetValueOrDefault(chat.Id), ct));
        }

        return states;
    }

    public async Task<(ErrorCode? result, ChatReadState? state)> MarkReadAsync(int userId, int chatId,
        int messageId, CancellationToken ct = default)
    {
        if (_chats == null || !_chats.TryGetSession(chatId, out var chat))
            return (ErrorCode.ChatNotFound, null);

        var lastMessageId = await _repository.GetLastMessageIdAsync(chatId, ct);
        var move = await MoveAsync(userId, chatId, chat, Math.Min(messageId, lastMessageId), ct);

        var peer = 0;
        if (HasReceipts(chat, userId))
            peer = (await _repository.GetPeerPositionsAsync(userId, [chatId], ct)).GetValueOrDefault(chatId);

        var state = await StateOfAsync(userId, chatId, move.Position, peer, ct);
        _messageSender.SendMessageByUserId(userId, new ChatReadStateChangedNotification(state.ToDto()));
        return (null, state);
    }

    /// <summary>Who of the other members has read a message of <paramref name="userId"/>, and when.</summary>
    public async Task<(ErrorCode? result, IReadOnlyList<ChatMessageReader>? readers)> GetReadersAsync(int userId,
        int chatId, int messageId, CancellationToken ct = default)
    {
        if (_chats == null || !_chats.TryGetSession(chatId, out var chat))
            return (ErrorCode.ChatNotFound, null);
        if (chat.Kind == ChatKind.ServerChannel)
            return (ErrorCode.WrongSessionType, null);

        var target = await _repository.GetTargetAsync(chatId, messageId, ct);
        if (target == null || target.IsDeleted)
            return (ErrorCode.MessageNotFound, null);
        if (target.IsSystem || target.SenderUserId != userId)
            return (ErrorCode.NotMessageAuthor, null);

        return (null, await _repository.GetReadersAsync(chatId, messageId, userId, ct));
    }

    public async Task HandleAsync(ChatSessionMessageAddedEvent evt, CancellationToken ct = default)
    {
        if (evt.Message.IsSystem || _chats == null || !_chats.TryGetSession(evt.ChatId, out var chat))
            return;

        try
        {
            // Writing in a chat means having read it up to there
            await MoveAsync(evt.Message.SenderUserId, evt.ChatId, chat, evt.Message.Id, ct);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Moving the read position of user {UserId} in chat {ChatId} failed.",
                evt.Message.SenderUserId, evt.ChatId);
        }
    }

    private async Task<ChatReadMove> MoveAsync(int userId, int chatId, ChatSessionModel chat, int messageId,
        CancellationToken ct)
    {
        var receipts = chat.Kind != ChatKind.ServerChannel;
        var now = _time.GetUtcNow().UtcDateTime;
        var move = await _repository.MoveAsync(userId, chatId, messageId, receipts ? now : null, ct);

        if (receipts && move.Moved)
        {
            var others = chat.MemberIds.Where(id => id != userId).ToList();
            if (others.Count > 0)
                _messageSender.BroadcastMessage(others,
                    new ChatPeerReadNotification(chatId, userId, move.Position, now));
        }

        return move;
    }

    private static bool HasReceipts(ChatSessionModel chat, int userId) =>
        chat.Kind != ChatKind.ServerChannel && chat.MemberIds.Contains(userId);

    private async Task<ChatReadState> StateOfAsync(int userId, int chatId, int position, int peerPosition,
        CancellationToken ct)
    {
        var unread = await _repository.GetUnreadAsync(chatId, userId, position, ChatReadStateDTO.MaxCount, ct);
        var mentions = unread.Count(message => Mentions(message.Text, userId));
        return new ChatReadState(chatId, position, unread.Count, mentions, peerPosition);
    }

    private static bool Mentions(string? text, int userId) =>
        ChatMentions.Find(text).Any(token => token.Kind == ChatMentionKind.Everyone
                                             || (token.Kind == ChatMentionKind.User && token.UserId == userId));
}
