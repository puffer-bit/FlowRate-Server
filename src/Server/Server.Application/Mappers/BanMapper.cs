using Server.Core.Models.Moderation;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Mappers;

public static class BanMapper
{
    public static BanDTO ToDto(this GlobalBan ban) => new(ban.Id, ban.Kind, ban.UserId, ban.Address,
        ban.Reason, ban.CreatedBy, ban.CreatedAt, ban.ExpiresAt, ban.RevokedBy, ban.RevokedAt);
}
