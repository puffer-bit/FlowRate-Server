using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests;

namespace Server.Application.Coordinator.Messaging;

public static class RequestAnswers
{
    /// <summary>The response of a request that was done, or the error it failed with.</summary>
    public static BaseMessage Answer(this Request request, ErrorCode? error, Func<BaseMessage> response) =>
        error is { } code ? request.ToError(code) : response();
}
