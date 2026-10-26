using Server.Application.Abstractions.Persistence;
using Server.Core.Models.User;

namespace Server.Application.Tests.Fakes;

/// <summary>Security data kept as copies, so a test sees only what was saved.</summary>
public sealed class InMemoryUserSecurityDataRepository : IUserSecurityDataRepository
{
    private readonly Dictionary<int, UserSecurityData> _data = new();

    public UserSecurityData? Saved(int userId) => _data.TryGetValue(userId, out var data) ? Copy(data) : null;

    public Task<UserSecurityData?> GetByUserIdAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult(Saved(userId));

    public Task AddAsync(UserSecurityData model, CancellationToken ct = default) => UpdateAsync(model, ct);

    public Task UpdateAsync(UserSecurityData model, CancellationToken ct = default)
    {
        _data[model.UserId] = Copy(model);
        return Task.CompletedTask;
    }

    private static UserSecurityData Copy(UserSecurityData d) =>
        new(d.UserId, d.PasswordHash, d.TwoFactorKey, d.IsTwoFactorEnabled, d.IsPasswordEnabled, d.IsFIDO2Enabled);
}

public sealed class InMemoryUserPasskeyRepository : IUserPasskeyRepository
{
    private readonly Dictionary<int, UserPasskey> _passkeys = new();
    private int _nextId = 1;

    public IReadOnlyList<UserPasskey> All => _passkeys.Values.Select(Copy).ToList();

    public Task<UserPasskey?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(_passkeys.TryGetValue(id, out var key) ? Copy(key) : null);

    public Task<UserPasskey?> GetByCredentialIdAsync(byte[] credentialId, CancellationToken ct = default) =>
        Task.FromResult(_passkeys.Values.Where(key => key.CredentialId.SequenceEqual(credentialId))
            .Select(Copy).FirstOrDefault());

    public Task<List<UserPasskey>> GetByUserIdAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult(_passkeys.Values.Where(key => key.UserId == userId).Select(Copy).ToList());

    public Task AddAsync(UserPasskey model, CancellationToken ct = default)
    {
        model.Id = _nextId++;
        _passkeys[model.Id] = Copy(model);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserPasskey model, CancellationToken ct = default)
    {
        _passkeys[model.Id] = Copy(model);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        _passkeys.Remove(id);
        return Task.CompletedTask;
    }

    private static UserPasskey Copy(UserPasskey k) => new(k.Id, k.UserId, k.CredentialId, k.PublicKey,
        k.SignatureCounter, k.Label, k.AaGuid, k.CreatedAt, k.LastUsedAt, k.IsEnabled);
}
