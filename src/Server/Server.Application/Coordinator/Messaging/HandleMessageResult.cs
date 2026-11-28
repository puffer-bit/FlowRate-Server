namespace Server.Application.Coordinator.Messaging;

/// <summary>How a handler dealt with a message; what the client gets is sent by the handler itself.</summary>
public enum HandleMessageResult
{
    InternalError = -1,
    NoError = 0,
    UnexpectedError = 1,
    ForbiddenMessage = 2,
    ForbiddenRequest = 3,
    JsonParseError = 4,
    NoUserId = 5,
    GatewayNotFound = 6
}
