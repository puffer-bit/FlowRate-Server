using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class CoordinatorEntityMapper
{
    public static CoordinatorInstanceModel ToModel(this CoordinatorInstanceEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new CoordinatorInstanceModel(
            entity.Id, entity.Name, entity.IsDefault, entity.Ip, entity.Port,
            entity.CreateTime, entity.FirstLaunchTime, entity.CurrentLaunchTime,
            entity.MaxOnlineUsers, entity.MaxUsers,
            new MotdSettings(entity.IsMOTDEnabled, entity.MotdText),
            new AccessListSettings(entity.AllowedIPs, entity.BannedIPs),
            new CoordinatorHeartbeatSettings(entity.HeartbeatIntervalSeconds, entity.HeartbeatTimeoutSeconds),
            new CoordinatorMediaSettings(entity.MediaMaxFileSizeBytes, entity.MediaStorageQuotaBytes),
            new CoordinatorUploadSettings(entity.MaxUnattachedUploadsPerUser, entity.UnattachedUploadLifetimeHours,
                entity.MediaTokenLifetimeMinutes),
            entity.RateLimits,
            entity.SfuNodes.Select(node => node.Id), entity.PasswordHash, entity.Revision);
    }

    /// <summary>Maps scalar values only; the SFU node links are handled by the repository.</summary>
    public static CoordinatorInstanceEntity ToEntity(this CoordinatorInstanceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new CoordinatorInstanceEntity
        {
            Id = model.Id,
            Name = model.Name,
            IsDefault = model.IsDefault,
            Ip = model.Ip,
            Port = model.Port,
            PasswordHash = model.PasswordHash,
            CreateTime = model.CreateTime,
            FirstLaunchTime = model.FirstLaunchTime,
            CurrentLaunchTime = model.CurrentLaunchTime,
            MaxOnlineUsers = model.MaxOnlineUsers,
            MaxUsers = model.MaxUsers,
            Revision = model.Revision,

            IsMOTDEnabled = model.Motd.IsEnabled,
            MotdText = model.Motd.Text,
            AllowedIPs = model.AccessList.AllowedIps.ToList(),
            BannedIPs = model.AccessList.BannedIps.ToList(),

            HeartbeatIntervalSeconds = model.Heartbeat.IntervalSeconds,
            HeartbeatTimeoutSeconds = model.Heartbeat.TimeoutSeconds,

            MediaMaxFileSizeBytes = model.Media.MaxFileSizeBytes,
            MediaStorageQuotaBytes = model.Media.StorageQuotaBytes,

            MaxUnattachedUploadsPerUser = model.Uploads.MaxUnattachedUploadsPerUser,
            UnattachedUploadLifetimeHours = model.Uploads.UnattachedLifetimeHours,
            MediaTokenLifetimeMinutes = model.Uploads.TokenLifetimeMinutes,

            RateLimits = model.RateLimits
        };
    }

    public static List<CoordinatorInstanceModel> ToModelList(this IEnumerable<CoordinatorInstanceEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return entities.Select(entity => entity.ToModel()).ToList();
    }
}
