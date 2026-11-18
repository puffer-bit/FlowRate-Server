namespace Server.Core.Models.TextChat;

/// <summary>
/// <paramref name="PeerReadMessageId"/> is the furthest position of the other members of a direct chat or a
/// group; 0 in server channels.
/// </summary>
public record ChatReadState(int ChatId, int LastReadMessageId, int UnreadCount, int MentionCount,
    int PeerReadMessageId = 0);

public record ChatUnreadMessage(int Id, string? Text);

/// <summary>The position after a move and whether it went forward.</summary>
public record ChatReadMove(int Position, bool Moved);

/// <summary>A member whose position reached a message; <paramref name="ReadAt"/> is null when it got there
/// before read times were kept.</summary>
public record ChatMessageReader(int UserId, DateTime? ReadAt);

/// <summary>What the readers request needs to know about the message.</summary>
public record ChatReadTarget(int SenderUserId, bool IsSystem, bool IsDeleted);
