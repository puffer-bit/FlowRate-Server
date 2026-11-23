using Server.Core.Models.TextChat.Messages.Content;
using System.Diagnostics.CodeAnalysis;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
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
    Task<ErrorCode?> ChangeSettingsAsync(int sessionId, string name, string? description, int userId);
    Task<ErrorCode?> SetPinnedAsync(int sessionId, int messageId, bool pinned, int userId);
    Task<(ErrorCode? result, ChatHistoryPage? page)> FetchPinnedAsync(int sessionId);
    Task<(ErrorCode? result, ChatHistoryWindow? window)> FetchWindowAsync(int sessionId, int messageId,
        Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection direction, int take);

    Task<(ErrorCode? result, ChatSessionModel? session)> CreateAsync(CreateSessionModel request, int userId);
    Task<ErrorCode?> DeleteAsync(int sessionId, int userId);
    Task<ErrorCode?> ChangeGroupAsync(int sessionId, string name, int[] memberIds, int ownerId, int userId, bool leave);
    /// <summary>The owner sets or (with null) clears the picture of a group; answers the one it replaced.</summary>
    Task<(ErrorCode? result, string? previousAvatarUrl)> SetGroupAvatarAsync(int sessionId, string? avatarUrl, int userId);

    bool TryGetSession(int sessionId, [NotNullWhen(true)] out ChatSessionModel? session);
    IReadOnlyCollection<ChatSessionModel> GetAllSessions();

    Task<(ErrorCode? result, List<ChatMessageModel>? messages)> FetchMessagesAsync(int sessionId, int take, int? skip);
    Task<(ErrorCode? result, ChatHistoryPage? page)> FetchMessagesBeforeAsync(int sessionId, int? beforeMessageId, int take);
    Task<(ErrorCode? result, ChatChanges? changes)> FetchChangesAsync(int sessionId, long afterRevision, int take);
    Task<(ErrorCode? result, ChatMessageModel? message)> FetchMessageAsync(int sessionId, int messageId);
    Task<ErrorCode?> AddCallSystemMessageAsync(int sessionId,
        ChatSystemContent content);
    Task<ErrorCode?> AddMessageAsync(int sessionId, NewChatMessageModel message, int senderUserId);
    Task<ErrorCode?> SetReactionAsync(int sessionId, int messageId, string emoji, bool isAdded, int userId);
    /// <param name="isLinkPreviewDisabled">Turns the preview of the link off or on again; null leaves it as it was.</param>
    Task<ErrorCode?> EditMessageAsync(int sessionId, int messageId, string text, int userId,
        bool? isLinkPreviewDisabled = null);

    /// <summary>
    /// Puts a fetched preview into a message, as a change of it. Nothing happens when the message has been
    /// deleted or no longer shows that address meanwhile.
    /// </summary>
    Task SetLinkPreviewAsync(int sessionId, int messageId, ChatLinkPreviewModel preview);
    /// <param name="mayDeleteAny">The user may delete messages of others, by their coordinator roles.</param>
    Task<ErrorCode?> DeleteMessageAsync(int sessionId, int messageId, int userId, bool mayDeleteAny = false);
}
