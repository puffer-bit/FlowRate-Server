using Server.Core.Models;
using Server.Core.Models.HybridSession;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Core.Tests.Models;

public class SessionModelTests
{
    [Fact]
    public void SessionType_FollowsTheConcreteSession()
    {
        Assert.Equal(SessionType.Chat, ChatSessionModel.Create("coordinator", "chat", 1).SessionType);
        Assert.Equal(SessionType.Hybrid,
            HybridSessionModel.Create("coordinator", "room", 5, WebRTCEngine.Mediasoup).SessionType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InvalidCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridSessionModel.Create("coordinator", "room", capacity, WebRTCEngine.Mediasoup));
    }

    [Fact]
    public void IsFull_RespectsCapacity()
    {
        var room = HybridSessionModel.Create("coordinator", "room", 2, WebRTCEngine.Mediasoup);

        Assert.False(room.IsFull(1));
        Assert.True(room.IsFull(2));
    }

    [Fact]
    public void ArchivedOrReadOnlyChat_DoesNotAcceptPosts()
    {
        var chat = ChatSessionModel.Create("coordinator", "chat", 1);
        Assert.True(chat.CanPost);

        chat.Archive();
        Assert.False(chat.CanPost);

        chat.Unarchive();
        chat.Configure(new ChatSessionOptions(IsReadOnly: true));
        Assert.False(chat.CanPost);
    }

    [Fact]
    public void ChatMemberWithAdminRole_IsAdmin()
    {
        var chat = ChatSessionModel.Create("coordinator", "chat", creatorUserId: 1);
        chat.AddUser(2);

        Assert.True(chat.IsAdmin(1));
        Assert.False(chat.IsAdmin(2));

        chat.Users[2].MakeAdmin();
        Assert.True(chat.IsAdmin(2));
    }
}
