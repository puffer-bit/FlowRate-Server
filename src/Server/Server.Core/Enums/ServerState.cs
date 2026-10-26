namespace Server.Core.Enums;

public enum ServerState
{
    Offline = 0,
    Starting = 1,
    WaitingForRemoteDb = 2,
    WaitingForDbValidation = 3,
    WaitingForFrameworksInit = 4,
    Online = 5,
    OnlineOverloaded = 7,
    Restarting = 8,
    Closing = 9,
    InMaintenanceMode = 10,
    Failed = 11
}
