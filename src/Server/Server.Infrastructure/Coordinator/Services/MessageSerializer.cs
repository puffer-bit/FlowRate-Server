using System.Text.Json;
using Shared.Models;
using Server.Application.Abstractions.Gateway;

namespace Server.Infrastructure.Coordinator.Services;

public class MessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _jsonOptions;

    public MessageSerializer(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public byte[] Serialize(BaseMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
}
