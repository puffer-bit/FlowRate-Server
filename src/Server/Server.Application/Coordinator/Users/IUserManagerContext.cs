using System.Collections.Concurrent;
using Server.Core.Models.User;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Users;

public interface IUserManagerContext
{
    ConcurrentDictionary<int, UserModel> AttachedUsers { get; init; }
}