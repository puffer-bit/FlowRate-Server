using Server.Core.Models;
using Server.Core.Models.HybridSession;
using Server.Core.Models.TextChat;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Mappers;

public static class SessionEntityMapper
{
    // -----------------------------------------------------------------
    // Hybrid session
    // -----------------------------------------------------------------

    public static HybridSessionModel ToModel(this HybridSessionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new HybridSessionModel(entity.Id, entity.CoordinatorInstanceId, entity.Name, ToCapacity(entity.Capacity),
            entity.HostId, entity.EngineType, entity.IsHostMustApprove, entity.ToMedia());
    }

    private static HybridMediaSettings ToMedia(this HybridSessionEntity entity) => new(
        entity.CameraCodec,
        entity.ScreenCodec,
        entity.AudioCodec,
        entity.IsVideoNackEnabled,
        entity.IsVideoRtxEnabled,
        entity.KeyFrameRequestDelayMs,
        entity.IsAudioNackEnabled,
        entity.IsOpusFecEnabled,
        entity.OpusPacketLossPercent,
        entity.IsVoiceDtxEnabled,
        entity.VoiceJitterBufferMs,
        entity.VideoJitterBufferMs);

    /// <summary>Copies the media settings of the model onto a new or a stored entity.</summary>
    public static void SetMedia(this HybridSessionEntity entity, HybridMediaSettings media)
    {
        entity.CameraCodec = media.CameraCodec;
        entity.ScreenCodec = media.ScreenCodec;
        entity.AudioCodec = media.AudioCodec;
        entity.IsVideoNackEnabled = media.IsVideoNackEnabled;
        entity.IsVideoRtxEnabled = media.IsVideoRtxEnabled;
        entity.KeyFrameRequestDelayMs = media.KeyFrameRequestDelayMs;
        entity.IsAudioNackEnabled = media.IsAudioNackEnabled;
        entity.IsOpusFecEnabled = media.IsOpusFecEnabled;
        entity.OpusPacketLossPercent = media.OpusPacketLossPercent;
        entity.IsVoiceDtxEnabled = media.IsVoiceDtxEnabled;
        entity.VoiceJitterBufferMs = media.VoiceJitterBufferMs;
        entity.VideoJitterBufferMs = media.VideoJitterBufferMs;
    }

    public static List<HybridSessionModel> ToModelList(this IEnumerable<HybridSessionEntity>? entities)
    {
        if (entities == null) return new List<HybridSessionModel>();
        return entities.Select(e => e.ToModel()).ToList();
    }

    /// <summary>
    /// Maps what the model knows; ExpiresAt, IsActive, ConnectedUsersCount and HostPeerId
    /// are managed by the repository/orchestrator.
    /// </summary>
    public static HybridSessionEntity ToEntity(this HybridSessionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var entity = new HybridSessionEntity
        {
            Id = model.Id,
            CoordinatorInstanceId = model.CoordinatorInstanceId,
            Name = model.Name,
            EngineType = model.EngineType,
            HostId = model.HostId,
            Capacity = model.Capacity,
            IsHostMustApprove = model.IsHostMustApprove,
            CreatedAt = DateTime.UtcNow
        };
        entity.SetMedia(model.Media);
        return entity;
    }

    // -----------------------------------------------------------------
    // Chat session
    // -----------------------------------------------------------------

    public static ChatSessionModel ToModel(this ChatSessionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var options = new ChatSessionOptions(
            entity.IsReadOnly,
            entity.IsAttachmentsAllowed,
            entity.IsVoiceMessagesAllowed,
            entity.IsVideoMessagesAllowed,
            entity.IsDelayedMessagesAllowed);

        return new ChatSessionModel(entity.Id, entity.CoordinatorInstanceId, entity.Name, ToCapacity(entity.Capacity),
            entity.CreatorUserId, entity.AdminUserId, options, entity.Description, entity.IconPath, entity.IsArchived);
    }

    public static List<ChatSessionModel> ToModelList(this IEnumerable<ChatSessionEntity>? entities)
    {
        if (entities == null) return new List<ChatSessionModel>();
        return entities.Select(e => e.ToModel()).ToList();
    }

    public static ChatSessionEntity ToEntity(this ChatSessionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new ChatSessionEntity
        {
            Id = model.Id,
            CoordinatorInstanceId = model.CoordinatorInstanceId,
            CreatorUserId = model.CreatorUserId,
            AdminUserId = model.AdminUserId,
            Name = model.Name,
            Description = model.Description,
            IconPath = model.IconPath,
            Capacity = model.Capacity,
            IsReadOnly = model.Options.IsReadOnly,
            IsArchived = model.IsArchived,
            IsAttachmentsAllowed = model.Options.IsAttachmentsAllowed,
            IsVoiceMessagesAllowed = model.Options.IsVoiceMessagesAllowed,
            IsVideoMessagesAllowed = model.Options.IsVideoMessagesAllowed,
            IsDelayedMessagesAllowed = model.Options.IsDelayedMessagesAllowed
        };
    }

    // -----------------------------------------------------------------
    // Any session
    // -----------------------------------------------------------------

    public static SessionModel? ToModel(this SessionEntity? entity) => entity switch
    {
        null => null,
        HybridSessionEntity hybrid => hybrid.ToModel(),
        ChatSessionEntity chat => chat.ToModel(),
        _ => throw new NotSupportedException($"Unknown session entity {entity.GetType().Name}.")
    };

    // Rows written before capacity was validated may hold 0 or another non-positive value
    private static int ToCapacity(int capacity) => capacity > 0 ? capacity : SessionModel.NoCapacityLimit;
}
