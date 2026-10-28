using Shared.Enums;
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

    public static ErrorCode? ToResult(this UserSecurityStatus status) => status switch
    {
        UserSecurityStatus.Done => null,
        UserSecurityStatus.Invalid => ErrorCode.InvalidValue,
        UserSecurityStatus.Refused => ErrorCode.VerificationFailed,
        UserSecurityStatus.Unavailable => ErrorCode.FeatureDisabled,
        UserSecurityStatus.PasskeyNotFound => ErrorCode.PasskeyNotFound,
        // The sender is a user of this coordinator; anything else is a fault
        _ => ErrorCode.Internal
    };
}
