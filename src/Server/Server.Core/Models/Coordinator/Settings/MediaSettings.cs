using Shared.Validation;

namespace Server.Core.Models.Coordinator.Settings;

public sealed record CoordinatorMediaSettings
{
    public const long UnlimitedStorage = 0;

    public long MaxFileSizeBytes { get; }
    public long StorageQuotaBytes { get; }

    public const int DefaultImageMaxSide = 2560;
    public const int DefaultImageQuality = 85;

    /// <summary>The longest side clients reduce images to when they compress them.</summary>
    public int ImageMaxSide { get; }

    /// <summary>The JPEG or WebP quality clients compress images with.</summary>
    public int ImageQuality { get; }

    /// <summary>Images may go uncompressed; otherwise none larger than <see cref="ImageMaxSide"/> is taken.</summary>
    public bool AllowOriginalImages { get; }

    public const int DefaultVideoMaxSide = 720;

    /// <summary>The shorter side clients reduce videos to when they compress them, like 720 for 720p.</summary>
    public int VideoMaxSide { get; }

    /// <summary>
    /// Videos may go uncompressed; otherwise only an MP4 within <see cref="VideoMaxSide"/> and its bitrate is taken.
    /// </summary>
    public bool AllowOriginalVideos { get; }

    public bool IsStorageLimited => StorageQuotaBytes != UnlimitedStorage;

    public static CoordinatorMediaSettings Default { get; } = new(500L * 1024 * 1024, 20L * 1024 * 1024 * 1024);

    public CoordinatorMediaSettings(long maxFileSizeBytes, long storageQuotaBytes,
        int imageMaxSide = DefaultImageMaxSide, int imageQuality = DefaultImageQuality, bool allowOriginalImages = true,
        int videoMaxSide = DefaultVideoMaxSide, bool allowOriginalVideos = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(storageQuotaBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(imageMaxSide, Limits.ChatImageCompressedSideMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(imageMaxSide, Limits.ChatImageMaxSide);
        ArgumentOutOfRangeException.ThrowIfLessThan(imageQuality, Limits.ChatImageQualityMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(imageQuality, Limits.ChatImageQualityMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(videoMaxSide, Limits.ChatVideoCompressedSideMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(videoMaxSide, Limits.ChatVideoCompressedSideMax);

        MaxFileSizeBytes = maxFileSizeBytes;
        StorageQuotaBytes = storageQuotaBytes;
        ImageMaxSide = imageMaxSide;
        ImageQuality = imageQuality;
        AllowOriginalImages = allowOriginalImages;
        VideoMaxSide = videoMaxSide;
        AllowOriginalVideos = allowOriginalVideos;
    }
}
