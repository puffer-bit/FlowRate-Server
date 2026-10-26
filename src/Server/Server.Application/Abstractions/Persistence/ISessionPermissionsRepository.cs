using Shared.Permissions;

namespace Server.Application.Abstractions.Persistence;

/// <summary>The permission overrides of sessions.</summary>
public interface ISessionPermissionsRepository
{
    /// <summary>The overrides of every session of the coordinator that has any, by session.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<PermissionOverride>>> GetByCoordinatorAsync(
        string coordinatorInstanceId, CancellationToken cancellationToken = default);

    /// <summary>Replaces all overrides of the session.</summary>
    Task ReplaceAsync(int sessionId, IReadOnlyList<PermissionOverride> overrides,
        CancellationToken cancellationToken = default);
}
