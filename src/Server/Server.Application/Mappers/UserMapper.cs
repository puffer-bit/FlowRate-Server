using Server.Core.Models.User;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Mappers;

public static class UserMapperExtensions
{
    /// <param name="viewerId">
    /// The user the DTO is for: they get the status they picked, everyone else what <see cref="UserModel.Presence"/>
    /// shows. Null when it goes to several users.
    /// </param>
    /// <param name="showIp">The address goes to the user themselves only, unless the viewer may see the user list.</param>
    public static UserDTO ToDto(this UserModel? entity, int? viewerId = null, bool showIp = false)
    {
        if (entity == null) return null!;
        return new UserDTO
        {
            Id = entity.Id,
            Username = entity.Username,
            Ip = showIp || entity.Id == viewerId ? entity.Ip : null,
            CoordinatorInstanceId = entity.CoordinatorInstanceId,
            RegionName = entity.RegionName,
            RegionCode = entity.RegionCode,
            DisplayName = entity.DisplayName,
            Prefix = entity.Prefix,
            AvatarUrl = entity.AvatarUrl,
            State = entity.State,
            Status = entity.Id == viewerId ? entity.Status : entity.Presence,
            FirstConnectionTime = entity.FirstConnectionTime,
            LastConnectionTime = entity.LastConnectionTime,
            RoleIds = entity.RoleIds.Order().ToList()
        };
    }
}
