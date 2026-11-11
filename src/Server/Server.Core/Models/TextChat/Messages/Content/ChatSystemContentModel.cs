using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Server.Core.Models.TextChat.Messages.Content;

/// <summary>Server-owned event data. TextContent supplies a fallback for older clients.</summary>
public sealed record ChatSystemContentModel
{
    public required ChatSystemEventKind Kind { get; init; }
    public int? ActorUserId { get; init; }
    public int? SubjectUserId { get; init; }
    public Guid? CallId { get; init; }
    public Shared.Models.DataTransferObjects.DirectCallEndReason? CallEndReason { get; init; }
    public int? CallDurationSeconds { get; init; }

    public bool IsValid() => Enum.IsDefined(Kind)
        && (CallEndReason == null || Enum.IsDefined(CallEndReason.Value))
        && CallDurationSeconds is not < 0
        && (Kind == ChatSystemEventKind.CallEnded || (CallEndReason == null && CallDurationSeconds == null)) && ActorUserId is not <= 0 && (Kind switch
    {
        ChatSystemEventKind.MemberAdded or ChatSystemEventKind.MemberRemoved =>
            ActorUserId.HasValue && SubjectUserId is > 0 && CallId == null,
        ChatSystemEventKind.MemberLeft =>
            ActorUserId is > 0 && SubjectUserId == ActorUserId && CallId == null,
        ChatSystemEventKind.CallStarted =>
            ActorUserId.HasValue && SubjectUserId == null && CallId.HasValue && CallId != Guid.Empty,
        ChatSystemEventKind.CallEnded =>
            SubjectUserId == null && CallId.HasValue && CallId != Guid.Empty,
        _ => false
    });

    public string ToFallbackText() => Kind switch
    {
        ChatSystemEventKind.MemberAdded => $"User {ActorUserId} added user {SubjectUserId}.",
        ChatSystemEventKind.MemberRemoved => $"User {ActorUserId} removed user {SubjectUserId}.",
        ChatSystemEventKind.MemberLeft => $"User {SubjectUserId} left the group.",
        ChatSystemEventKind.CallStarted => $"User {ActorUserId} started a call.",
        ChatSystemEventKind.CallEnded => $"Call {CallEndReason?.ToString().ToLowerInvariant() ?? "ended"}." +
            (CallDurationSeconds is { } duration ? $" Duration: {duration} seconds." : string.Empty),
        _ => throw new InvalidOperationException("Unknown system event.")
    };
}
