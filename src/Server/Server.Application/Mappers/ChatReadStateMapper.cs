using Server.Core.Models.TextChat;
using Shared.Models.DataTransferObjects.ChatSession;

namespace Server.Application.Mappers;

public static class ChatReadStateMapper
{
    public static ChatReadStateDTO ToDto(this ChatReadState state) =>
        new(state.ChatId, state.LastReadMessageId, state.UnreadCount, state.MentionCount);
}
