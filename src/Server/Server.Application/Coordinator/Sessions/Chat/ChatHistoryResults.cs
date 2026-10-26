using Server.Core.Models.TextChat.Messages;

namespace Server.Application.Coordinator.Sessions.Chat;

public record ChatHistoryPage(List<ChatMessageModel> Messages, long Revision);

public record ChatChanges(List<ChatMessageModel> Messages, long Revision, bool HasMore, bool Reset);
