namespace Server.Core.Models.Orchestrator;

/// <summary>A node of the orchestrator's global SFU pool as stored in the database.</summary>
public sealed record SfuNodeSettingsModel
{
    // Assigned by the database after the first save
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Host { get; init; }
    public required int Port
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
            field = value;
        }
    }
    public required string Username { get; init; }
    public required string Key { get; init; }
    public bool IsSecured { get; init; }
    public bool IsEnabled { get; init; } = true;
}
