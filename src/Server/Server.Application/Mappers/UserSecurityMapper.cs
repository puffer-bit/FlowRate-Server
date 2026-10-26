using Server.Application.Orchestrator.Security;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Mappers;

public static class UserSecurityMapper
{
    public static UserSecurityDTO ToDto(this UserSecurityOverview overview) => new(
        overview.Data.IsPasswordEnabled,
        overview.Data.IsTwoFactorEnabled,
        overview.Data.IsTwoFactorPending,
        overview.PasskeysAvailable,
        overview.Passkeys
            .Where(key => key.IsEnabled)
            .Select(key => new UserPasskeyDTO(key.Id, key.Label, key.CreatedAt, key.LastUsedAt))
            .ToList());

    public static UserSecurityResult ToResult(this UserSecurityStatus status) => status switch
    {
        UserSecurityStatus.Done => UserSecurityResult.NoError,
        UserSecurityStatus.Invalid => UserSecurityResult.InvalidValue,
        UserSecurityStatus.Refused => UserSecurityResult.Refused,
        UserSecurityStatus.Unavailable => UserSecurityResult.PasskeysUnavailable,
        UserSecurityStatus.PasskeyNotFound => UserSecurityResult.PasskeyNotFound,
        // The sender is a user of this coordinator; anything else is a fault
        _ => UserSecurityResult.InternalError
    };
}
