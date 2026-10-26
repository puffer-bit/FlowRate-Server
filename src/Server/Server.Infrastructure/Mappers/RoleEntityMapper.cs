using Server.Core.Models.Roles;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class RoleEntityMapper
{
    public static RoleModel ToModel(this RoleEntity entity) =>
        new(entity.Id, entity.CoordinatorInstanceId, entity.Name, entity.Color, entity.Position, entity.Permissions,
            entity.IsEveryone, entity.Revision);

    public static RoleEntity ToEntity(this RoleModel model) => new()
    {
        Id = model.Id,
        CoordinatorInstanceId = model.CoordinatorInstanceId,
        Name = model.Name,
        Color = model.Color,
        Position = model.Position,
        Permissions = model.Permissions,
        IsEveryone = model.IsEveryone,
        Revision = model.Revision
    };
}
