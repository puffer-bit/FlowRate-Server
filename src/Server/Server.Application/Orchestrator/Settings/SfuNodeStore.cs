using Server.Core.Models.Orchestrator;
using Server.Application.Abstractions.Persistence;

namespace Server.Application.Orchestrator.Settings;

public class SfuNodeStore : ISfuNodeStore
{
    public event Func<Task>? Changed;

    private readonly ISfuNodeRepository _repository;

    public SfuNodeStore(ISfuNodeRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyCollection<SfuNodeSettingsModel>> GetAllAsync(CancellationToken ct = default) =>
        _repository.GetAllAsync(ct);

    public async Task<int> AddAsync(SfuNodeSettingsModel node, CancellationToken ct = default)
    {
        var id = await _repository.AddAsync(node, ct);
        await RaiseChanged();
        return id;
    }

    public async Task UpdateAsync(SfuNodeSettingsModel node, CancellationToken ct = default)
    {
        await _repository.UpdateAsync(node, ct);
        await RaiseChanged();
    }

    public async Task RemoveAsync(int nodeId, CancellationToken ct = default)
    {
        await _repository.RemoveAsync(nodeId, ct);
        await RaiseChanged();
    }

    private Task RaiseChanged() => Changed?.Invoke() ?? Task.CompletedTask;
}
