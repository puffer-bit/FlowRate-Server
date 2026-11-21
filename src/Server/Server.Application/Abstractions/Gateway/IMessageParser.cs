using Shared.Models;
using Shared.Models.Responses;

namespace Server.Application.Abstractions.Gateway;

public interface IMessageParser
{
    /// <summary>Reads one message; null when the text is not a message the server can read.</summary>
    /// <param name="rejection">
    /// When reading fails but the text is a request with a readable <c>RequestId</c>: the answer to it,
    /// <c>UnsupportedRequest</c> for a <c>$type</c> the server does not know and <c>InvalidRequest</c> otherwise.
    /// </param>
    BaseMessage? ParseBaseMessage(ReadOnlySpan<byte> utf8Json, out ErrorResponse? rejection);
}
