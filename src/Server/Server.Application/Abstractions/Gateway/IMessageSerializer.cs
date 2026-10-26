using Shared.Models;

namespace Server.Application.Abstractions.Gateway;

public interface IMessageSerializer
{
    byte[] Serialize(BaseMessage message);
}
