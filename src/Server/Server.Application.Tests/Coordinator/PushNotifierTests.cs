using Server.Application.Coordinator.Roles;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Push;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Notifications;
using Server.Application.Coordinator.Push;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Push;
using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;

namespace Server.Application.Tests.Coordinator;

public class PushNotifierTests
{
    private const string CoordinatorId = "coordinator";
    private const int ChatId = 7;
    private const int AuthorId = 1;
    private const int OfflineUserId = 2;
    private const int OnlineUserId = 3;

    private static readonly byte[] Key = Enumerable.Range(0, PushCipher.KeyLength).Select(i => (byte)i).ToArray();

    private readonly Mock<IPushDeviceRepository> _devices = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly RecordingSender _sender = new();

    public PushNotifierTests()
    {
        ChatSessionModel? chat = new(ChatId, CoordinatorId, "general", 10, AuthorId, AuthorId, ChatSessionOptions.Default);
        _chats.Setup(c => c.TryGetSession(ChatId, out chat)).Returns(true);

        UserModel? online = User(OnlineUserId, "carol");
        _users.Setup(u => u.TryGetConnectedUser(OnlineUserId, out online)).Returns(true);
        UserModel? author = User(AuthorId, "alice");
        author.SetAvatar("/media/avatars/abc");
        _users.Setup(u => u.GetUserAsync(AuthorId)).ReturnsAsync(author);

        _devices.Setup(d => d.GetAllAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Device(AuthorId, "author-token"),
            Device(OfflineUserId, "offline-token"),
            Device(OnlineUserId, "online-token")
        ]);
    }

    private static UserModel User(int id, string name) =>
        new(id, CoordinatorId, name, UserState.Normal, UserStatus.Online, DateTime.UtcNow, DateTime.UtcNow);

    private static PushDeviceModel Device(int userId, string token) =>
        new(CoordinatorId, userId, token, PushPlatform.Android, "0123456789abcdef0123456789abcdef", Key);

    private static ChatMessageModel Message(string text, bool system = false)
    {
        var message = ChatMessageModel.Create(ChatId, AuthorId, new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc),
            new ChatTextContentModel { Text = text });
        message.Id = 42;
        return system ? Clone(message) : message;
    }

    private static ChatMessageModel Clone(ChatMessageModel message) => new()
    {
        Id = message.Id,
        ChatId = message.ChatId,
        SenderUserId = message.SenderUserId,
        SentTime = message.SentTime,
        TextContent = message.TextContent,
        IsSystem = true
    };

    private async Task Publish(ChatMessageModel message, IPushSender? sender = null)
    {
        var notifier = new PushNotifier(NullLogger.Instance, CoordinatorId, sender ?? _sender, _devices.Object);
        var permissions = new Mock<ICoordinatorPermissions>();
        permissions.Setup(p => p.PermissionsInAsync(It.IsAny<int>(), It.IsAny<Core.Models.SessionModel>()))
            .ReturnsAsync(Permission.All);
        notifier.Watch(_users.Object, _chats.Object, permissions.Object);

        await notifier.HandleAsync(new ChatSessionMessageAddedEvent(ChatId, message));
        await notifier.DrainAsync();
    }

    [Fact]
    public async Task Message_GoesOnlyToDevicesOfUsersWhoAreNotConnected()
    {
        await Publish(Message("hello"));

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("offline-token", sent.Token);
    }

    [Fact]
    public async Task Payload_IsEncryptedWithTheDeviceKeyAndDescribesTheMessage()
    {
        await Publish(Message("hello"));

        var sent = Assert.Single(_sender.Sent);
        using var payload = JsonDocument.Parse(PushCipher.Decrypt(Key, sent.Payload));
        var root = payload.RootElement;

        Assert.Equal(CoordinatorId, root.GetProperty("coordinatorId").GetString());
        Assert.Equal(ChatId, root.GetProperty("chatId").GetInt32());
        Assert.Equal("general", root.GetProperty("chatName").GetString());
        Assert.Equal(42, root.GetProperty("messageId").GetInt32());
        Assert.Equal(AuthorId, root.GetProperty("senderId").GetInt32());
        Assert.Equal("alice", root.GetProperty("senderName").GetString());
        Assert.Equal("/media/avatars/abc", root.GetProperty("senderAvatarUrl").GetString());
        Assert.Equal("hello", root.GetProperty("text").GetString());
        Assert.Equal(0, root.GetProperty("attachment").GetInt32());
    }

    [Fact]
    public async Task LongText_IsCutWithoutSplittingASurrogatePair()
    {
        await Publish(Message(new string('a', PushPayload.MaxTextLength - 1) + "😀 tail"));

        using var payload = JsonDocument.Parse(PushCipher.Decrypt(Key, Assert.Single(_sender.Sent).Payload));
        Assert.Equal(new string('a', PushPayload.MaxTextLength - 1), payload.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task InvalidToken_RemovesTheDevice()
    {
        _sender.Status = PushSendStatus.TokenInvalid;

        await Publish(Message("hello"));

        _devices.Verify(d => d.RemoveAsync(CoordinatorId, "offline-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedSend_KeepsTheDevice()
    {
        _sender.Status = PushSendStatus.Failed;

        await Publish(Message("hello"));

        _devices.Verify(d => d.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SystemMessage_IsNotPushed()
    {
        await Publish(Message("joined", system: true));

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task DisabledSender_DoesNotReadDevices()
    {
        var disabled = new RecordingSender { IsEnabled = false };

        await Publish(Message("hello"), disabled);

        Assert.Empty(disabled.Sent);
        _devices.Verify(d => d.GetAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class RecordingSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];
        public PushSendStatus Status { get; set; } = PushSendStatus.Delivered;
        public bool IsEnabled { get; init; } = true;

        public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(new PushSendResult(Status));
        }
    }
}
