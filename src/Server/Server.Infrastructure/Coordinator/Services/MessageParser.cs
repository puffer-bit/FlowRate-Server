using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Responses;
using Server.Application.Abstractions.Gateway;

namespace Server.Infrastructure.Coordinator.Services;

public class MessageParser : IMessageParser
{
    private const int MaxDetailsLength = 200;

    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;
    private readonly HashSet<string> _knownTypes;

    public MessageParser(ILogger logger, JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
        _logger = logger;
        _knownTypes = (jsonOptions.GetTypeInfo(typeof(BaseMessage)).PolymorphismOptions?.DerivedTypes ?? [])
            .Select(derived => derived.TypeDiscriminator?.ToString())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    public BaseMessage? ParseBaseMessage(ReadOnlySpan<byte> utf8Json, out ErrorResponse? rejection)
    {
        rejection = null;
        try
        {
            return JsonSerializer.Deserialize<BaseMessage>(utf8Json, _jsonOptions);
        }
        catch (Exception ex)
        {
            // Anyone can send garbage, so it is not worth an error with a stack trace
            _logger.LogDebug("Failed to parse a message: {Error}", ex.Message);
            rejection = DescribeRejection(utf8Json, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Finds the <c>$type</c> and <c>RequestId</c> of a message that did not deserialize, so that a client
    /// newer than the server learns at once that its request is not known, instead of waiting for an answer.
    /// </summary>
    private ErrorResponse? DescribeRejection(ReadOnlySpan<byte> utf8Json, string error)
    {
        string? type = null;
        Guid? requestId = null;
        try
        {
            var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { MaxDepth = _jsonOptions.MaxDepth });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isType = reader.ValueTextEquals("$type");
                var isRequestId = !isType && string.Equals(reader.GetString(), "RequestId", StringComparison.OrdinalIgnoreCase);
                reader.Read();

                if (isType && reader.TokenType == JsonTokenType.String)
                    type = reader.GetString();
                else if (isRequestId && reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var id))
                    requestId = id;
                else
                    reader.Skip();
            }
        }
        catch (JsonException)
        {
            // Whatever was found before the text broke off still names the request
        }

        if (requestId is not { } knownId || knownId == Guid.Empty || type == null)
            return null;

        return _knownTypes.Contains(type)
            ? new ErrorResponse(knownId, ErrorCode.InvalidRequest, Truncate(error))
            : new ErrorResponse(knownId, ErrorCode.UnsupportedRequest, Truncate($"Unknown message type {type}."));
    }

    private static string Truncate(string text) => text.Length <= MaxDetailsLength ? text : text[..MaxDetailsLength];
}
