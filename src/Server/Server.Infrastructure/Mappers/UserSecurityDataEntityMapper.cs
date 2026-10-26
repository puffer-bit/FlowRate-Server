using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class UserSecurityDataEntityMapper
{
    public static UserSecurityData ToModel(this UserSecurityDataEntity entity) => new(
        entity.UserId,
        entity.PasswordHash,
        entity.TwoFactorKey,
        entity.IsTwoFactorEnabled,
        entity.IsPasswordEnabled,
        entity.IsFIDO2Enabled);

    public static UserSecurityDataEntity ToEntity(this UserSecurityData model) => new UserSecurityDataEntity
    {
        UserId = model.UserId
    }.CopyFrom(model);

    public static UserSecurityDataEntity CopyFrom(this UserSecurityDataEntity entity, UserSecurityData model)
    {
        entity.PasswordHash = model.PasswordHash;
        entity.TwoFactorKey = model.TwoFactorKey;
        entity.IsTwoFactorEnabled = model.IsTwoFactorEnabled;
        entity.IsPasswordEnabled = model.IsPasswordEnabled;
        entity.IsFIDO2Enabled = model.IsFIDO2Enabled;
        return entity;
    }
}
