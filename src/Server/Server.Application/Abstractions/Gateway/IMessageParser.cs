using Shared.Models;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Gateway;

public interface IMessageParser
{
    BaseMessage? ParseBaseMessage(ReadOnlySpan<byte> utf8Json);
}