using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Push;

/// <summary>A member whose ring started or stopped: a push rings their phone or takes the ring away.</summary>
public sealed record CallPushRecipient(int UserId, bool IsRinging, DateTimeOffset ExpiresAt);

/// <summary>What changed in a call for the members it rings. GroupName is null in direct chats.</summary>
public sealed record CallPush(ChatCallDTO Call, string? GroupName, IReadOnlyList<CallPushRecipient> Recipients);
