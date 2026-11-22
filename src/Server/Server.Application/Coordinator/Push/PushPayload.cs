using System.Text.Json;
using Server.Core.Models.TextChat.Messages;

namespace Server.Application.Coordinator.Push;

public enum PushAttachmentKind
{
    None = 0,
    Image = 1,
    Video = 2,
    File = 3
}

public static class PushPayload
{
    public const int MaxTextLength = 300;

    public static byte[] Create(string coordinatorInstanceId, string chatName, string senderName,
        string? senderAvatarUrl, ChatMessageModel message, string? text, bool isMention)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("coordinatorId", coordinatorInstanceId);
            writer.WriteNumber("chatId", message.ChatId);
            writer.WriteString("chatName", chatName);
            writer.WriteNumber("messageId", message.Id);
            writer.WriteNumber("senderId", message.SenderUserId);
            writer.WriteString("senderName", senderName);
            if (senderAvatarUrl != null)
                writer.WriteString("senderAvatarUrl", senderAvatarUrl);
            writer.WriteString("text", Shorten(text));
            if (isMention)
                writer.WriteBoolean("mention", true);
            writer.WriteNumber("attachment", (int)AttachmentOf(message));
            writer.WriteNumber("sentTime", message.SentTime.ToUnixTimeMilliseconds());
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A ring for one member. callState is 0 while it rings, 1 once they joined and 2 otherwise, so the phone
    /// shows the call or takes it away. chatName is the group name, or the caller in a direct chat.
    /// </summary>
    public static byte[] CreateCall(string coordinatorInstanceId, string chatName, string callerName,
        string? callerAvatarUrl, Shared.Models.DataTransferObjects.ChatCallDTO call, CallPushRecipient recipient)
    {
        var joined = call.Participants.Any(p => p.UserId == recipient.UserId
            && p.State == Shared.Models.DataTransferObjects.ChatCallParticipantState.Joined);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("coordinatorId", coordinatorInstanceId);
            writer.WriteNumber("chatId", call.ChatId);
            writer.WriteString("chatName", chatName);
            writer.WriteNumber("senderId", call.CallerId);
            writer.WriteNumber("recipientId", recipient.UserId);
            writer.WriteString("senderName", callerName);
            if (callerAvatarUrl != null)
                writer.WriteString("senderAvatarUrl", callerAvatarUrl);
            writer.WriteNumber("sentTime", call.CreatedAt.ToUnixTimeMilliseconds());
            writer.WriteString("callId", call.CallId);
            writer.WriteNumber("callState", recipient.IsRinging ? 0 : joined ? 1 : 2);
            writer.WriteNumber("expiresAt", recipient.ExpiresAt.ToUnixTimeMilliseconds());
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static PushAttachmentKind AttachmentOf(ChatMessageModel message)
    {
        if (message.Images.Count > 0)
            return PushAttachmentKind.Image;
        if (message.Videos.Count > 0)
            return PushAttachmentKind.Video;
        return message.Files.Count > 0 ? PushAttachmentKind.File : PushAttachmentKind.None;
    }

    private static string? Shorten(string? text)
    {
        if (text == null || text.Length <= MaxTextLength)
            return text;

        var length = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
        return text[..length];
    }
}
