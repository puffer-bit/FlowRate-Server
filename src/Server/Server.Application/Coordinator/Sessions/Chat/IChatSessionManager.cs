using System.Diagnostics.CodeAnalysis;
using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession.Models;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Sessions.Chat;

public interface IChatSessionManager
{
    Task LoadAsync();

    Task<(CreateSessionResult result, ChatSessionModel? session)> CreateAsync(CreateSessionModel request, int userId);
    Task<DeleteSessionResult> DeleteAsync(int sessionId, int userId);
    bool TryGetSession(int sessionId, [NotNullWhen(true)] out ChatSessionModel? session);
    IReadOnlyCollection<ChatSessionModel> GetAllSessions();

    Task<(FetchMessagesResult result, List<ChatMessageModel>? messages)> FetchMessagesAsync(int sessionId, int take, int? skip);
    Task<(FetchMessagesResult result, ChatHistoryPage? page)> FetchMessagesBeforeAsync(int sessionId, int? beforeMessageId, int take);
    Task<(FetchMessagesResult result, ChatChanges? changes)> FetchChangesAsync(int sessionId, long afterRevision, int take);
    Task<(FetchMessageResult result, ChatMessageModel? message)> FetchMessageAsync(int sessionId, int messageId);
    Task<AddMessageResult> AddMessageAsync(int sessionId, NewChatMessageModel message, int senderUserId);
    Task<EditMessageResult> EditMessageAsync(int sessionId, int messageId, string text, int userId);
    /// <param name="mayDeleteAny">The user may delete messages of others, by their coordinator roles.</param>
    Task<DeleteMessageResult> DeleteMessageAsync(int sessionId, int messageId, int userId, bool mayDeleteAny = false);
}
