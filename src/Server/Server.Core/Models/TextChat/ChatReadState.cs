namespace Server.Core.Models.TextChat;

public record ChatReadState(int ChatId, int LastReadMessageId, int UnreadCount, int MentionCount);

public record ChatUnreadMessage(int Id, string? Text);
