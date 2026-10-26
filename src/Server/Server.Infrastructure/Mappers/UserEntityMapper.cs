using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class UserEntityMapper
{
    public static UserModel ToModel(this UserEntity? entity)
    {
        if (entity == null) return null!;
        return new UserModel(entity.Id, entity.CoordinatorInstanceId, entity.Username, entity.State, entity.LastStatus,
            entity.FirstConnectionTime, entity.LastConnectionTime,
            entity.Ip, entity.RegionName, entity.RegionCode, entity.DisplayName, entity.Prefix, entity.AvatarUrl,
            entity.Roles.Select(role => role.RoleId));
    }

    public static UserEntity ToEntity(this UserModel? model, string userIdentity)
    {
        if (model == null) return null!;
        return new UserEntity
        {
            Id = model.Id,
            UserIdentity = userIdentity,
            Username = model.Username,
            Ip = model.Ip ?? string.Empty,
            CoordinatorInstanceId = model.CoordinatorInstanceId,
            RegionName = model.RegionName,
            RegionCode = model.RegionCode,
            DisplayName = model.DisplayName,
            Prefix = model.Prefix,
            AvatarUrl = model.AvatarUrl,
            State = model.State,
            LastStatus = model.Status,
            FirstConnectionTime = model.FirstConnectionTime,
            LastConnectionTime = model.LastConnectionTime
        };
    }
    
    public static IDictionary<int, UserModel> ToModelDictionary(
        this IDictionary<int, UserEntity> source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToModel()
        );
    }
    
    public static IDictionary<string, UserEntity> ToEntityDictionary(
        this IDictionary<string, (UserModel model, string userIdentity)> source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.model.ToEntity(pair.Value.userIdentity)
        );
    }
}