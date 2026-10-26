using Server.Application.Mappers;
using Server.Application.Orchestrator.Security;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>
/// What the handlers of the sender's account security share: the proof, and the state to answer with.
/// </summary>
public sealed class UserSecurityReplies(IUserSecurityService security, string coordinatorInstanceId)
{
    public IUserSecurityService Security => security;
    public string CoordinatorId => coordinatorInstanceId;

    public Task<bool> IsProvenAsync(int userId, UserSecurityProofDTO? proof) =>
        security.CheckProofAsync(userId, proof?.Password, proof?.TwoFactorCode);

    /// <summary>The result of a change, with what the user has now when it was made.</summary>
    public async Task<(UserSecurityResult Result, UserSecurityDTO? Security)> AfterAsync(int userId,
        UserSecurityStatus status)
    {
        if (status != UserSecurityStatus.Done)
            return (status.ToResult(), null);

        var overview = await security.GetAsync(coordinatorInstanceId, userId);
        return overview == null
            ? (UserSecurityResult.InternalError, null)
            : (UserSecurityResult.NoError, overview.ToDto());
    }
}
