using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Push;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Push;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Push;
using Shared.Models.Requests.SessionActions.ChatSession.Models;

namespace Server.Application.Tests.Coordinator;

public class PushReplyServiceTests
{
    private const string CoordinatorId = "coordinator";
    private const string Token = "reply-token";
    private const int UserId = 5;
    private const int ChatId = 7;

    private readonly Mock<IPushDeviceRepository> _devices = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IChatReadStateRepository> _reads = new();
    private readonly Mock<IMessageSender> _sender = new();

    public PushReplyServiceTests()
    {
        _devices.Setup(d => d.FindByReplyTokenAsync(CoordinatorId,
                It.Is<byte[]>(hash => hash.SequenceEqual(PushReplyToken.Hash(Token))), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushDeviceModel(CoordinatorId, UserId, "fcm", PushPlatform.Android, "00", new byte[32]));
        _users.Setup(u => u.GetUserAsync(UserId)).ReturnsAsync(User(UserState.Normal));
        _chats.Setup(c => c.AddMessageAsync(ChatId, It.IsAny<NewChatMessageModel>(), UserId))
            .ReturnsAsync((ErrorCode?)null);
        var chat = ChatSessionModel.Create(CoordinatorId, "general", UserId);
        _chats.Setup(c => c.TryGetSession(ChatId, out chat)).Returns(true);
        _reads.Setup(r => r.GetLastMessageIdAsync(ChatId, It.IsAny<CancellationToken>())).ReturnsAsync(50);
        _reads.Setup(r => r.MoveAsync(UserId, ChatId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int messageId, CancellationToken _) => messageId);
        _reads.Setup(r => r.GetUnreadAsync(ChatId, UserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private static UserModel User(UserState state) =>
        new(UserId, CoordinatorId, "alice", state, UserStatus.Online, DateTime.UtcNow, DateTime.UtcNow);

    private PushReplyService Create(CoordinatorRateLimitSettings? options = null)
    {
        var reads = new ChatReadService(NullLogger.Instance, _reads.Object, _sender.Object);
        reads.Watch(_chats.Object);
        return new(NullLogger.Instance, _devices.Object, _users.Object, _chats.Object, reads,
            new RequestRateLimits(options ?? CoordinatorRateLimitSettings.Default), CoordinatorId);
    }

    [Fact]
    public async Task Reply_AddsTheTextAsTheUserOfTheDevice()
    {
        var result = await Create().ReplyAsync(Token, new PushReplyRequest(ChatId, "hi"));

        Assert.Equal(PushReplyResult.Sent, result);
        _chats.Verify(c => c.AddMessageAsync(ChatId,
            It.Is<NewChatMessageModel>(m => m.ChatId == ChatId && m.TextContent!.Text == "hi" && m.Images.Count == 0),
            UserId), Times.Once);
    }

    [Fact]
    public async Task Reply_WithAnUnknownToken_IsUnauthorized()
    {
        var result = await Create().ReplyAsync("other", new PushReplyRequest(ChatId, "hi"));

        Assert.Equal(PushReplyResult.Unauthorized, result);
        _chats.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(UserState.Banned)]
    [InlineData(UserState.Suspended)]
    public async Task Reply_OfAUserWhoMayNotWrite_IsForbidden(UserState state)
    {
        _users.Setup(u => u.GetUserAsync(UserId)).ReturnsAsync(User(state));

        var result = await Create().ReplyAsync(Token, new PushReplyRequest(ChatId, "hi"));

        Assert.Equal(PushReplyResult.Forbidden, result);
        _chats.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\u0000b")]
    public async Task Reply_OfInvalidText_IsInvalid(string text)
    {
        var result = await Create().ReplyAsync(Token, new PushReplyRequest(ChatId, text));

        Assert.Equal(PushReplyResult.Invalid, result);
        _devices.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ErrorCode.ChatNotFound, PushReplyResult.ChatNotExists)]
    [InlineData(ErrorCode.ChatReadOnly, PushReplyResult.Forbidden)]
    [InlineData(ErrorCode.Internal, PushReplyResult.InternalError)]
    public async Task Reply_ReportsWhatTheChatAnswered(ErrorCode? added, PushReplyResult expected)
    {
        _chats.Setup(c => c.AddMessageAsync(ChatId, It.IsAny<NewChatMessageModel>(), UserId)).ReturnsAsync(added);

        Assert.Equal(expected, await Create().ReplyAsync(Token, new PushReplyRequest(ChatId, "hi")));
    }

    [Fact]
    public async Task Reply_BeyondTheChatWriteLimit_IsRefused()
    {
        var service = Create(CoordinatorRateLimitSettings.Default with { ChatWrite = new RequestRateSetting(2, 60) });

        Assert.Equal(PushReplyResult.Sent, await service.ReplyAsync(Token, new PushReplyRequest(ChatId, "1")));
        Assert.Equal(PushReplyResult.Sent, await service.ReplyAsync(Token, new PushReplyRequest(ChatId, "2")));
        Assert.Equal(PushReplyResult.TooManyRequests, await service.ReplyAsync(Token, new PushReplyRequest(ChatId, "3")));
    }

    [Fact]
    public async Task MarkRead_MovesThePositionOfTheUserOfTheDevice()
    {
        var result = await Create().MarkReadAsync(Token, new PushMarkReadRequest(ChatId, 42));

        Assert.Equal(PushReplyResult.Sent, result);
        _reads.Verify(r => r.MoveAsync(UserId, ChatId, 42, It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.SendMessageByUserId(UserId, It.IsAny<ChatReadStateChangedNotification>()), Times.Once);
    }

    [Fact]
    public async Task MarkRead_WithAnUnknownToken_IsUnauthorized()
    {
        var result = await Create().MarkReadAsync("other", new PushMarkReadRequest(ChatId, 42));

        Assert.Equal(PushReplyResult.Unauthorized, result);
        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkRead_OfAChatThatDoesNotExist_IsNotFound()
    {
        var result = await Create().MarkReadAsync(Token, new PushMarkReadRequest(ChatId + 1, 42));

        Assert.Equal(PushReplyResult.ChatNotExists, result);
        _reads.VerifyNoOtherCalls();
    }
}
