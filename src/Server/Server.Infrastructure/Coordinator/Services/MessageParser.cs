using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Server.Application.Abstractions.Gateway;

namespace Server.Infrastructure.Coordinator.Services;

public class MessageParser : IMessageParser
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;

    public MessageParser(ILogger logger, JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
        _logger = logger;
    }
    
    public BaseMessage? ParseBaseMessage(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var baseMessage = JsonSerializer.Deserialize<BaseMessage>(utf8Json, _jsonOptions);

            return baseMessage;
        }
        catch (Exception ex)
        {
            // Anyone can send garbage, so it is not worth an error with a stack trace
            _logger.LogDebug("Failed to parse a message: {Error}", ex.Message);
            return null;
        }
    }
}
