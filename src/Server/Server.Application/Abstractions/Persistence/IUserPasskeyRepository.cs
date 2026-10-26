using Server.Core.Models.User;

namespace Server.Application.Abstractions.Persistence;

public interface IUserPasskeyRepository
{
    Task<UserPasskey?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<UserPasskey?> GetByCredentialIdAsync(byte[] credentialId, CancellationToken ct = default);
    Task<List<UserPasskey>> GetByUserIdAsync(int userId, CancellationToken ct = default);
    Task AddAsync(UserPasskey model, CancellationToken ct = default);
    Task UpdateAsync(UserPasskey model, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}