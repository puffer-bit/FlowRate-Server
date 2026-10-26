using Server.Core.Models.TextChat.Messages;

namespace Server.Application.Coordinator.Events;

/// <summary>Something changed inside one chat.</summary>
public abstract record ChatSessionEvent(int ChatId);

public record ChatSessionMessageAddedEvent(int ChatId, ChatMessageModel Message) : ChatSessionEvent(ChatId);

public record ChatSessionMessageEditedEvent(int ChatId, ChatMessageModel Message) : ChatSessionEvent(ChatId);

public record ChatSessionMessageDeletedEvent(int ChatId, int MessageId, long Revision) : ChatSessionEvent(ChatId);

public record ChatSessionTypingEvent(int ChatId, int UserId) : ChatSessionEvent(ChatId);
