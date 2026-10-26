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

public sealed class ChatReadService : IEventHandler<ChatSessionMessageAddedEvent>
{
    private readonly ILogger _logger;
    private readonly IChatReadStateRepository _repository;
    private readonly IMessageSender _messageSender;

    private IChatSessionManager? _chats;

    public ChatReadService(ILogger logger, IChatReadStateRepository repository, IMessageSender messageSender)
    {
        _logger = logger;
        _repository = repository;
        _messageSender = messageSender;
    }

    public void Watch(IChatSessionManager chats)
    {
        _chats = chats;
    }

    public async Task<List<ChatReadState>> GetStatesAsync(int userId, CancellationToken ct = default)
    {
        if (_chats == null)
            return [];

        var positions = await _repository.GetPositionsAsync(userId, ct);
        var states = new List<ChatReadState>();
        foreach (var chat in _chats.GetAllSessions())
        {
            states.Add(await StateOfAsync(userId, chat.Id, positions.GetValueOrDefault(chat.Id), ct));
        }

        return states;
    }

    public async Task<(ChatReadResult result, ChatReadState? state)> MarkReadAsync(int userId, int chatId,
        int messageId, CancellationToken ct = default)
    {
        if (_chats == null || !_chats.TryGetSession(chatId, out _))
            return (ChatReadResult.ChatNotExists, null);

        var lastMessageId = await _repository.GetLastMessageIdAsync(chatId, ct);
        var position = await _repository.MoveAsync(userId, chatId, Math.Min(messageId, lastMessageId), ct);
        var state = await StateOfAsync(userId, chatId, position, ct);

        _messageSender.SendMessageByUserId(userId, new ChatReadStateChangedNotification(state.ToDto()));
        return (ChatReadResult.NoError, state);
    }

    public async Task HandleAsync(ChatSessionMessageAddedEvent evt, CancellationToken ct = default)
    {
        if (evt.Message.IsSystem)
            return;

        try
        {
            await _repository.MoveAsync(evt.Message.SenderUserId, evt.ChatId, evt.Message.Id, ct);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Moving the read position of user {UserId} in chat {ChatId} failed.",
                evt.Message.SenderUserId, evt.ChatId);
        }
    }

    private async Task<ChatReadState> StateOfAsync(int userId, int chatId, int position, CancellationToken ct)
    {
        var unread = await _repository.GetUnreadAsync(chatId, userId, position, ChatReadStateDTO.MaxCount, ct);
        var mentions = unread.Count(message => Mentions(message.Text, userId));
        return new ChatReadState(chatId, position, unread.Count, mentions);
    }

    private static bool Mentions(string? text, int userId) =>
        ChatMentions.Find(text).Any(token => token.Kind == ChatMentionKind.Everyone
                                             || (token.Kind == ChatMentionKind.User && token.UserId == userId));
}
