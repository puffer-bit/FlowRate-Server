using Server.Application.Orchestrator.Settings;
using Server.Core.Models.Coordinator.Settings;

namespace Server.Application.Tests.Orchestrator;

public class CoordinatorSettingsValidatorTests
{
    private static readonly IReadOnlySet<int> Nodes = new HashSet<int> { 1, 2 };

    [Fact]
    public void ConsistentValues_AreAccepted()
    {
        Assert.Empty(CoordinatorSettingsValidator.Validate(Valid(), Nodes));
    }

    [Theory]
    [InlineData("not an ip", 10, 100, null, false, CoordinatorSettingsFields.Ip)]
    [InlineData("127.0.0.1", 200, 100, null, false, CoordinatorSettingsFields.MaxOnlineUsers)]
    [InlineData("127.0.0.1", 0, 100, null, false, CoordinatorSettingsFields.MaxOnlineUsers)]
    [InlineData("127.0.0.1", 10, 100, "secret", true, CoordinatorSettingsFields.NewPassword)]
    [InlineData("127.0.0.1", 10, 100, "", false, CoordinatorSettingsFields.NewPassword)]
    public void InconsistentValues_AreRefusedForTheirField(string ip, int maxOnline, int maxUsers,
        string? newPassword, bool removePassword, string field)
    {
        var change = Valid() with
        {
            Ip = ip, MaxOnlineUsers = maxOnline, MaxUsers = maxUsers, NewPassword = newPassword,
            RemovePassword = removePassword
        };

        Assert.Contains(field, CoordinatorSettingsValidator.Validate(change, Nodes).Keys);
    }

    [Fact]
    public void HeartbeatTimeout_MustExceedTheInterval()
    {
        var change = Valid() with { HeartbeatIntervalSeconds = 10, HeartbeatTimeoutSeconds = 10 };

        Assert.Contains(CoordinatorSettingsFields.HeartbeatTimeout,
            CoordinatorSettingsValidator.Validate(change, Nodes).Keys);
    }

    [Fact]
    public void AccessList_TakesOnlyIpAddresses()
    {
        var change = Valid() with { AllowedIps = ["10.0.0.1"], BannedIps = ["10.0.0.2", "somewhere"] };

        var errors = CoordinatorSettingsValidator.Validate(change, Nodes);

        Assert.Equal([CoordinatorSettingsFields.BannedIps], errors.Keys);
    }

    [Fact]
    public void UnknownSfuNodes_AreNamed()
    {
        var change = Valid() with { SfuNodeIds = [1, 7, 9] };

        var messages = CoordinatorSettingsValidator.Validate(change, Nodes)[CoordinatorSettingsFields.SfuNodeIds];

        Assert.Equal(2, messages.Length);
    }

    [Theory]
    [InlineData(0, 10, "RateLimits.ChatWrite.Count")]
    [InlineData(10, 0, "RateLimits.ChatWrite.PeriodSeconds")]
    [InlineData(10, double.NaN, "RateLimits.ChatWrite.PeriodSeconds")]
    [InlineData(10, 100_000, "RateLimits.ChatWrite.PeriodSeconds")]
    public void RequestRates_AreRefusedForTheirPart(int count, double periodSeconds, string field)
    {
        var change = Valid() with
        {
            RateLimits = Valid().RateLimits with { ChatWrite = new RequestRateChange(count, periodSeconds) }
        };

        Assert.Equal([field], CoordinatorSettingsValidator.Validate(change, Nodes).Keys);
    }

    [Fact]
    public void ConnectionsPerIp_MustBeLetAtLeastOne()
    {
        var change = Valid() with { RateLimits = Valid().RateLimits with { MaxConnectionsPerIp = 0 } };

        Assert.Equal(["RateLimits.MaxConnectionsPerIp"], CoordinatorSettingsValidator.Validate(change, Nodes).Keys);
    }

    [Fact]
    public void UploadLimits_AreRefusedForTheirFields()
    {
        var change = Valid() with
        {
            MaxUnattachedUploadsPerUser = 0, UnattachedUploadLifetimeHours = 10_000, MediaTokenLifetimeMinutes = -1
        };

        Assert.Equivalent(
            new[]
            {
                CoordinatorSettingsFields.MaxUnattachedUploads, CoordinatorSettingsFields.UnattachedUploadLifetime,
                CoordinatorSettingsFields.MediaTokenLifetime
            },
            CoordinatorSettingsValidator.Validate(change, Nodes).Keys, strict: true);
    }

    private static CoordinatorSettingsChange Valid() => new()
    {
        Name = "coordinator",
        Ip = "127.0.0.1",
        Port = 5000,
        MaxOnlineUsers = 10,
        MaxUsers = 100,
        HeartbeatIntervalSeconds = 5,
        HeartbeatTimeoutSeconds = 30,
        MaxFileSizeBytes = 1024,
        MaxUnattachedUploadsPerUser = 40,
        UnattachedUploadLifetimeHours = 24,
        MediaTokenLifetimeMinutes = 10,
        RateLimits = RateLimitsChange.From(CoordinatorRateLimitSettings.Default),
        SfuNodeIds = [1]
    };
}
