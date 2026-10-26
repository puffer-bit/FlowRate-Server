using System.Collections.Concurrent;
using Server.Core.Models.User;

namespace Server.Application.Coordinator.Users;

public class UserManagerContext : IUserManagerContext
{
    public ConcurrentDictionary<int, UserModel> AttachedUsers { get; init; } = new();
}