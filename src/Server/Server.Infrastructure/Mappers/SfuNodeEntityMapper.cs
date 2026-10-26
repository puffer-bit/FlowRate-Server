using Server.Core.Models.Orchestrator;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class SfuNodeEntityMapper
{
    public static SfuNodeSettingsModel ToModel(this SfuNodeEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Host = entity.Host,
        Port = entity.Port,
        Username = entity.Username,
        Key = entity.Key,
        IsSecured = entity.IsSecured,
        IsEnabled = entity.IsEnabled
    };

    public static SfuNodeEntity ToEntity(this SfuNodeSettingsModel model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Host = model.Host,
        Port = model.Port,
        Username = model.Username,
        Key = model.Key,
        IsSecured = model.IsSecured,
        IsEnabled = model.IsEnabled
    };
}
