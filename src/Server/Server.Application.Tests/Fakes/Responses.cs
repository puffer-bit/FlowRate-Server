using Shared.Enums;
using Shared.Models;
using Shared.Models.Responses;

namespace Server.Application.Tests.Fakes;

public static class Responses
{
    /// <summary>The code a request failed with, or null for a regular response.</summary>
    public static ErrorCode? Error(this BaseMessage message) => (message as ErrorResponse)?.Code;
}
