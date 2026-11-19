namespace Server.Application.Abstractions.Persistence;

/// <summary>Public metadata about the server database, without connection details.</summary>
public interface IDatabaseInfo
{
    string Provider { get; }
}
