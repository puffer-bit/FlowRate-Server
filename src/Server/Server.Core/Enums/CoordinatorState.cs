namespace Server.Core.Enums;

public enum CoordinatorState
{
    Offline = 0,
    Starting = 1,
    Online = 5,
    Restarting = 8,
    Closing = 9,
    Failed = 11,
    Closed = 12
}