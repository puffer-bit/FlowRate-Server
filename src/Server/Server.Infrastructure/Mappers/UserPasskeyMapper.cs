using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class UserPasskeyMapper
{
    public static UserPasskey ToModel(this UserPasskeyEntity entity) => new(
        entity.Id,
        entity.UserId,
        entity.CredentialId,
        entity.PublicKey,
        entity.SignatureCounter,
        entity.Label,
        entity.AaGuid,
        entity.CreatedAt,
        entity.LastUsedAt,
        entity.IsEnabled);

    public static UserPasskeyEntity ToEntity(this UserPasskey model) => new()
    {
        Id = model.Id,
        UserId = model.UserId,
        CredentialId = model.CredentialId,
        PublicKey = model.PublicKey,
        SignatureCounter = model.SignatureCounter,
        Label = model.Label,
        AaGuid = model.AaGuid,
        CreatedAt = model.CreatedAt,
        LastUsedAt = model.LastUsedAt,
        IsEnabled = model.IsEnabled
    };
}
