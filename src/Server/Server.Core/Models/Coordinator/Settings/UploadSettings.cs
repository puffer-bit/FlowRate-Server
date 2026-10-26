namespace Server.Core.Models.Coordinator.Settings;

/// <summary>Limits of uploads not attached to a message yet and of the tokens media requests carry.</summary>
public sealed record CoordinatorUploadSettings
{
    public int MaxUnattachedUploadsPerUser { get; }
    public int UnattachedLifetimeHours { get; }
    public int TokenLifetimeMinutes { get; }

    public static CoordinatorUploadSettings Default { get; } = new(40, 24, 10);

    public CoordinatorUploadSettings(int maxUnattachedUploadsPerUser, int unattachedLifetimeHours, int tokenLifetimeMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnattachedUploadsPerUser);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unattachedLifetimeHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenLifetimeMinutes);

        MaxUnattachedUploadsPerUser = maxUnattachedUploadsPerUser;
        UnattachedLifetimeHours = unattachedLifetimeHours;
        TokenLifetimeMinutes = tokenLifetimeMinutes;
    }
}
