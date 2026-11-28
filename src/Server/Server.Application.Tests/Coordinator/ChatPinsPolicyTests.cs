using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Connections.RateLimiting;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Tests.Coordinator;

public class ChatPinsPolicyTests
{
    [Fact]
    public void PinWritesRequireModerationAndReadsRequireViewAccess()
    {
        var pin = new SetMessagePinnedRequest(7, 9, true);
        Assert.Equal(RequestPolicy.InSession(7, Permission.ViewSessions | Permission.ManageMessages), RequestPolicies.Of(pin));
        Assert.False(RequestPolicies.IsAllowedWhenFrozen(pin));
        Assert.Equal(RequestRateGroup.ChatWrite, RequestRateLimits.GroupOf(pin));
        var read = new ChatPinnedMessagesRequest(7);
        Assert.Equal(RequestPolicy.InSession(7, Permission.ViewSessions), RequestPolicies.Of(read));
        Assert.True(RequestPolicies.IsAllowedWhenFrozen(read));
        Assert.Equal(RequestRateGroup.ChatRead, RequestRateLimits.GroupOf(read));
        var window = new ChatHistoryWindowRequest(7, 9, ChatHistoryDirection.After, 30);
        Assert.Equal(RequestPolicy.InSession(7, Permission.ViewSessions), RequestPolicies.Of(window));
        Assert.True(RequestPolicies.IsAllowedWhenFrozen(window));
        Assert.Equal(RequestRateGroup.ChatRead, RequestRateLimits.GroupOf(window));
    }
}
