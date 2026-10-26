using Server.Core.Models;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface ISessionsRepository
{
    Task<IReadOnlyList<SessionModel>> GetAllAsync();
}