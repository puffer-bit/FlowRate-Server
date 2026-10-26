using Server.Core.Models.Push;

namespace Server.Application.Abstractions.Persistence;

public interface IPushDeviceRepository
{
    Task SaveAsync(PushDeviceModel device, CancellationToken cancellationToken = default);
    Task RemoveAsync(string coordinatorInstanceId, string token, CancellationToken cancellationToken = default);
    Task RemoveAsync(string coordinatorInstanceId, int userId, string token, CancellationToken cancellationToken = default);
    Task<PushDeviceModel?> FindByReplyTokenAsync(string coordinatorInstanceId, byte[] replyTokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PushDeviceModel>> GetAllAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default);
}
