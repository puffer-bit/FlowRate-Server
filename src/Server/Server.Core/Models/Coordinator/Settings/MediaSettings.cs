namespace Server.Core.Models.Coordinator.Settings;

public sealed record CoordinatorMediaSettings
{
    public const long UnlimitedStorage = 0;

    public long MaxFileSizeBytes { get; }
    public long StorageQuotaBytes { get; }

    public bool IsStorageLimited => StorageQuotaBytes != UnlimitedStorage;

    public static CoordinatorMediaSettings Default { get; } = new(500L * 1024 * 1024, 20L * 1024 * 1024 * 1024);

    public CoordinatorMediaSettings(long maxFileSizeBytes, long storageQuotaBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(storageQuotaBytes);

        MaxFileSizeBytes = maxFileSizeBytes;
        StorageQuotaBytes = storageQuotaBytes;
    }
}
