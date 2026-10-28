using Microsoft.Extensions.Time.Testing;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Shared.Models.Requests.SessionActions.HybridSession;
using Shared.Models.Requests.Heartbeat;
using Xunit;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Core.Models.Coordinator.Settings;

namespace Server.Infrastructure.Tests.RateLimiting;

public class RateLimitingTests
{
    private readonly FakeTimeProvider _time = new();

    private static CreateSessionRequest CreateSession() =>
        new(new CreateSessionModel("room", 10, new ChatSessionCreateData()));

    [Fact]
    public void TokenBucket_RefillsOverTime()
    {
        var bucket = new TokenBucket(2, 1, _time);

        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void TokenBucket_NeverExceedsCapacity()
    {
        var bucket = new TokenBucket(3, 10, _time);
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(3, Enumerable.Range(0, 10).Count(_ => bucket.TryTake()));
    }

    [Fact]
    public void SessionManagement_AllowsTenPerMinute()
    {
        var guard = new ConnectionRequestGuard(_time);

        for (var i = 0; i < 10; i++)
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(CreateSession(), out _));
        Assert.Equal(RequestGuardVerdict.Rejected, guard.Check(new DeleteSessionRequest(1), out var group));
        Assert.Equal(RequestRateGroup.SessionManagement, group);

        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(CreateSession(), out _));
    }

    [Fact]
    public void NewLimits_ApplyToARunningConnection()
    {
        var limits = new RequestRateLimits(CoordinatorRateLimitSettings.Default);
        var guard = new ConnectionRequestGuard(_time, limits);
        for (var i = 0; i < 10; i++)
            guard.Check(CreateSession(), out _);
        Assert.Equal(RequestGuardVerdict.Rejected, guard.Check(CreateSession(), out _));

        limits.Apply(CoordinatorRateLimitSettings.Default with { SessionManagement = new RequestRateSetting(20, 60) });

        for (var i = 0; i < 20; i++)
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(CreateSession(), out _));
        Assert.Equal(RequestGuardVerdict.Rejected, guard.Check(CreateSession(), out _));
    }

    [Fact]
    public void SessionMedia_AllowsTenPerFiveSeconds()
    {
        var guard = new ConnectionRequestGuard(_time);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new HybridSessionJoinRequest(1), out _));
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new HybridSessionStartVoiceRequest(1), out _));
        }
        Assert.Equal(RequestGuardVerdict.Rejected, guard.Check(new HybridSessionConnectTransportRequest(1)
        {
            DTLSParameters = new() { Role = "client", Fingerprints = [] }
        }, out _));

        _time.Advance(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 10; i++)
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new HybridSessionLeaveRequest(1), out _));
    }

    [Fact]
    public void Groups_HaveSeparateBudgets()
    {
        var guard = new ConnectionRequestGuard(_time);
        for (var i = 0; i < 10; i++)
            guard.Check(CreateSession(), out _);

        Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new HybridSessionSetMicrophoneMuteRequest(1, true), out var group));
        Assert.Equal(RequestRateGroup.MicrophoneMute, group);
        Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new SendMessageRequest(1, null!), out group));
        Assert.Equal(RequestRateGroup.ChatWrite, group);
    }

    [Fact]
    public void Queries_AllowALargeBurst()
    {
        var guard = new ConnectionRequestGuard(_time);

        for (var i = 0; i < 200; i++)
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new SessionInfoRequest(null), out _));
        Assert.Equal(RequestGuardVerdict.Rejected, guard.Check(new CoordinatorInfoRequest(), out _));
    }

    [Fact]
    public void GlobalLimit_AppliesAcrossGroups()
    {
        var guard = new ConnectionRequestGuard(_time);
        var allowed = 0;
        for (var i = 0; i < 400; i++)
        {
            if (guard.Check(new SessionInfoRequest(null), out _) == RequestGuardVerdict.Allowed) allowed++;
            if (guard.Check(new ChatSessionHistoryRequest(1, 10, 0), out _) == RequestGuardVerdict.Allowed) allowed++;
            _time.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.InRange(allowed, 200, 300 + 100);
    }

    [Fact]
    public void TwentyRejectionsWithinAMinute_RequestClosing()
    {
        var guard = new ConnectionRequestGuard(_time);
        for (var i = 0; i < 10; i++)
            guard.Check(CreateSession(), out _);

        for (var i = 1; i < RequestRateLimits.Default.MaxRejectionsPerWindow; i++)
            Assert.Equal(RequestGuardVerdict.Rejected, guard.Check(CreateSession(), out _));

        Assert.Equal(RequestGuardVerdict.RejectedTooOften, guard.Check(CreateSession(), out _));
    }

    [Fact]
    public void Rejections_OutsideTheWindow_AreForgotten()
    {
        var guard = new ConnectionRequestGuard(_time);
        for (var i = 1; i < RequestRateLimits.Default.MaxRejectionsPerWindow; i++)
            guard.RegisterRejection();

        _time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        Assert.Equal(RequestGuardVerdict.Rejected, guard.RegisterRejection());
    }

    [Fact]
    public void Heartbeat_IsLimitedSeparately()
    {
        var guard = new ConnectionRequestGuard(_time);

        Assert.Equal(4, Enumerable.Range(0, 10).Count(attempt =>
            guard.Check(new HeartbeatPingRequest(), out _) == RequestGuardVerdict.Allowed));
    }

    [Fact]
    public void Authentication_IsGroupedButNotLimitedPerConnection()
    {
        var guard = new ConnectionRequestGuard(_time);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(RequestGuardVerdict.Allowed, guard.Check(new UserAuthRequest("p", "u"), out var group));
            Assert.Equal(RequestRateGroup.Authentication, group);
        }
    }

    [Fact]
    public void AuthenticationAttempts_AreLimitedPerIp()
    {
        var limiter = new AuthenticationAttemptLimiter(_time);

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("10.0.0.1"));
        Assert.False(limiter.TryAcquire("10.0.0.1"));
        Assert.True(limiter.TryAcquire("10.0.0.2"));

        _time.Advance(TimeSpan.FromSeconds(12));
        Assert.True(limiter.TryAcquire("10.0.0.1"));
        Assert.False(limiter.TryAcquire("10.0.0.1"));
    }
}
