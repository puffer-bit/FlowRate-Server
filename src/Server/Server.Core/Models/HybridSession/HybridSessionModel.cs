using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Core.Models.HybridSession;

public sealed class HybridSessionModel : SessionModel
{
    public int HostId { get; }
    public WebRTCEngine EngineType { get; }
    public bool IsHostMustApprove { get; private set; }
    public HybridMediaSettings Media { get; private set; }

    /// <summary>Runtime state, not persisted: whether the session has an SFU node to run on.</summary>
    public HybridSessionNodeState State { get; private set; }

    public override SessionType SessionType => SessionType.Hybrid;

    public HybridSessionModel(int id, string coordinatorInstanceId, string name, int capacity,
        int hostId, WebRTCEngine engineType, bool isHostMustApprove = false, HybridMediaSettings? media = null)
        : base(id, coordinatorInstanceId, name, capacity)
    {
        HostId = hostId;
        EngineType = engineType;
        IsHostMustApprove = isHostMustApprove;
        Media = media ?? HybridMediaSettings.Default;
    }

    /// <summary>A new session that is not saved yet.</summary>
    public static HybridSessionModel Create(string coordinatorInstanceId, string name, int capacity,
        int hostId, WebRTCEngine engineType, bool isHostMustApprove = false, HybridMediaSettings? media = null) =>
        new(0, coordinatorInstanceId, name, capacity, hostId, engineType, isHostMustApprove, media);

    public bool IsHost(int userId) => userId == HostId;

    public void SetHostMustApprove(bool isHostMustApprove) => IsHostMustApprove = isHostMustApprove;

    public void SetMedia(HybridMediaSettings media) => Media = media;

    /// <returns>Whether the state changed.</returns>
    public bool SetState(HybridSessionNodeState state)
    {
        if (State == state)
            return false;

        State = state;
        return true;
    }
}
