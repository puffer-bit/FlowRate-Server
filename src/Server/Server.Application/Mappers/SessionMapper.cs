using Server.Core.Models;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Permissions;
using Shared.Models.DataTransferObjects;
using Shared.Models.DataTransferObjects.ChatSession;
using Shared.Models.DataTransferObjects.ChatSession.User;
using Server.Core.Models.HybridSession;

namespace Server.Application.Mappers;

public static class SessionMapperExtensions
{
    /// <summary>Maps to the DTO of the session's actual type, so that type specific fields are not lost.</summary>
    public static SessionDTO ToDto(this SessionModel? entity) => entity switch
    {
        null => null!,
        ChatSessionModel chat => chat.ToDto(),
        HybridSessionModel hybrid => hybrid.ToDto(),
        _ => new SessionDTO
        {
            Id = entity.Id,
            CoordinatorInstanceId = entity.CoordinatorInstanceId,
            Name = entity.Name,
            Capacity = entity.Capacity,
            SessionType = entity.SessionType,
            PermissionOverrides = entity.PermissionOverrides.ToDtos()
        }
    };

    public static List<SessionPermissionOverrideDTO> ToDtos(this IEnumerable<PermissionOverride> overrides) =>
        overrides.Select(o => new SessionPermissionOverrideDTO { RoleId = o.RoleId, Allow = o.Allow, Deny = o.Deny })
            .ToList();

    public static List<PermissionOverride> ToModels(this IEnumerable<SessionPermissionOverrideDTO> dtos) =>
        dtos.Select(dto => new PermissionOverride(dto.RoleId, dto.Allow, dto.Deny)).ToList();

    public static List<SessionDTO> ToDtoList(this IEnumerable<SessionModel>? models)
    {
        if (models == null) return new List<SessionDTO>();
        return models.Select(m => m.ToDto()).ToList();
    }

    public static ChatSessionDTO ToDto(this ChatSessionModel? model)
    {
        if (model == null) return null!;
        var dto = new ChatSessionDTO
        {
            Id = model.Id,
            CoordinatorInstanceId = model.CoordinatorInstanceId,
            CreatorUserId = model.CreatorUserId,
            AdminUserId = model.AdminUserId,
            Name = model.Name,
            Description = model.Description,
            IconPath = model.IconPath,
            Capacity = model.Capacity,
            Users = model.Users.ToDictionary(
                x => x.Key,
                x => new TextChatUserDTO { UserId = x.Value.UserId, IsAdmin = x.Value.IsAdmin }),
            IsReadOnly = model.Options.IsReadOnly,
            IsArchived = model.IsArchived,
            IsAttachmentsAllowed = model.Options.IsAttachmentsAllowed,
            IsVoiceMessagesAllowed = model.Options.IsVoiceMessagesAllowed,
            IsVideoMessagesAllowed = model.Options.IsVideoMessagesAllowed,
            IsDelayedMessagesAllowed = model.Options.IsDelayedMessagesAllowed,
            SessionType = model.SessionType,
            PermissionOverrides = model.PermissionOverrides.ToDtos()
        };
        return dto;
    }

    public static List<ChatSessionDTO> ToDtoList(this IEnumerable<ChatSessionModel>? models)
    {
        if (models == null) return new List<ChatSessionDTO>();
        return models.Select(m => m.ToDto()).ToList();
    }

    public static HybridSessionDTO ToDto(this HybridSessionModel? entity)
    {
        if (entity == null) return null!;
        
        return new HybridSessionDTO
        {
            Id = entity.Id,
            CoordinatorInstanceId = entity.CoordinatorInstanceId,
            Name = entity.Name,
            EngineType = entity.EngineType,
            HostId = entity.HostId,
            Capacity = entity.Capacity,
            SessionType = entity.SessionType,
            IsHostMustApprove = entity.IsHostMustApprove,
            State = entity.State,
            Media = entity.Media.ToDto(),
            PermissionOverrides = entity.PermissionOverrides.ToDtos()
        };
    }

    public static HybridMediaSettingsDTO ToDto(this HybridMediaSettings media) => new()
    {
        CameraCodec = media.CameraCodec,
        ScreenCodec = media.ScreenCodec,
        AudioCodec = media.AudioCodec,
        IsVideoNackEnabled = media.IsVideoNackEnabled,
        IsVideoRtxEnabled = media.IsVideoRtxEnabled,
        KeyFrameRequestDelayMs = media.KeyFrameRequestDelayMs,
        IsAudioNackEnabled = media.IsAudioNackEnabled,
        IsOpusFecEnabled = media.IsOpusFecEnabled,
        OpusPacketLossPercent = media.OpusPacketLossPercent,
        IsVoiceDtxEnabled = media.IsVoiceDtxEnabled,
        VoiceJitterBufferMs = media.VoiceJitterBufferMs,
        VideoJitterBufferMs = media.VideoJitterBufferMs
    };

    public static HybridMediaSettings ToModel(this HybridMediaSettingsDTO media) => new(
        media.CameraCodec,
        media.ScreenCodec,
        media.AudioCodec,
        media.IsVideoNackEnabled,
        media.IsVideoRtxEnabled,
        media.KeyFrameRequestDelayMs,
        media.IsAudioNackEnabled,
        media.IsOpusFecEnabled,
        media.OpusPacketLossPercent,
        media.IsVoiceDtxEnabled,
        media.VoiceJitterBufferMs,
        media.VideoJitterBufferMs);

    public static List<HybridSessionDTO> ToDtoList(this IEnumerable<HybridSessionModel>? models)
    {
        if (models == null) return new List<HybridSessionDTO>();
        return models.Select(m => m.ToDto()).ToList();
    }
}
