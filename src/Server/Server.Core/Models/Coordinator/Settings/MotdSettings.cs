namespace Server.Core.Models.Coordinator.Settings;

/// <summary>Message of the day shown to connecting users.</summary>
public sealed record MotdSettings(bool IsEnabled, string? Text)
{
    public static MotdSettings Disabled { get; } = new(false, null);
}
