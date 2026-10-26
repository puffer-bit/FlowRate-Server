using Server.Core.Models.Coordinator;
using Server.Application.Abstractions.Persistence;

namespace Server.Application.Orchestrator.Settings;

public class CoordinatorSettingsStore : ICoordinatorSettingsStore
{
    public event Func<string, Task>? Changed;

    private readonly ICoordinatorInstanceRepository _repository;

    public CoordinatorSettingsStore(ICoordinatorInstanceRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyCollection<CoordinatorInstanceModel>> GetAllAsync(CancellationToken ct = default) =>
        _repository.GetAllAsync(ct);

    public Task<CoordinatorInstanceModel?> GetAsync(string coordinatorId, CancellationToken ct = default) =>
        _repository.GetByIdAsync(coordinatorId, ct);

    public async Task AddAsync(CoordinatorInstanceModel coordinator, CancellationToken ct = default)
    {
        await _repository.AddAsync(coordinator, ct);
        await RaiseChanged(coordinator.Id);
    }

    public async Task UpdateAsync(CoordinatorInstanceModel coordinator, CancellationToken ct = default)
    {
        coordinator.NextRevision();
        await _repository.UpdateAsync(coordinator, ct);
        await RaiseChanged(coordinator.Id);
    }

    private Task RaiseChanged(string coordinatorId) => Changed?.Invoke(coordinatorId) ?? Task.CompletedTask;
}
