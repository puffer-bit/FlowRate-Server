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
            writer.WriteNumber("sentTime", new DateTimeOffset(message.SentTime, TimeSpan.Zero).ToUnixTimeMilliseconds());
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
