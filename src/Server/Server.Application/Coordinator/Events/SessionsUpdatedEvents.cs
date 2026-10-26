using Server.Core.Models.TextChat;
using Server.Core.Models.HybridSession;

namespace Server.Application.Coordinator.Events;

/// <summary>A session was created, reconfigured or deleted.</summary>
public abstract record SessionsUpdatedEvent(int UserId);

public record HybridSessionCreatedEvent(HybridSessionModel Session, int UserId) : SessionsUpdatedEvent(UserId);

public record HybridSessionReconfiguredEvent(HybridSessionModel Session, int UserId) : SessionsUpdatedEvent(UserId);

public record HybridSessionDeletedEvent(int SessionId, int UserId) : SessionsUpdatedEvent(UserId);

public record ChatSessionCreatedEvent(ChatSessionModel Session, int UserId) : SessionsUpdatedEvent(UserId);

public record ChatSessionReconfiguredEvent(ChatSessionModel Session, int UserId) : SessionsUpdatedEvent(UserId);

public record ChatSessionDeletedEvent(int SessionId, int UserId) : SessionsUpdatedEvent(UserId);
