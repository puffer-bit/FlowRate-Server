namespace Server.Application.Coordinator.Connections;

public enum ConnectionCloseReason
{
    Normal,
    AuthenticationTimeout,
    TooManyRejectedRequests
}
