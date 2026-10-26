using Server.Core.Models.Layout;

namespace Server.Application.Abstractions.Persistence;

public interface ISessionLayoutRepository
{
    Task<SessionLayoutModel> GetAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default);
    Task<int> AddCategoryAsync(string coordinatorInstanceId, string name, CancellationToken cancellationToken = default);
    Task SaveAsync(string coordinatorInstanceId, SessionLayoutModel layout, CancellationToken cancellationToken = default);
}
