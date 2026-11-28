using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Core.Models.HybridSession;

public sealed class HybridSessionModel : SessionModel
{
    /// <summary>Unlimited capacity.</summary>
    public const int NoCapacityLimit = -1;

    public int Capacity { get; private set; }
    /// <summary>
    /// The device each member in a chat call takes part from; only these may join. The call keeps it up to date.
    /// Null for server channels.
    /// </summary>
    public IReadOnlyDictionary<int, string>? CallDevices { get; init; }
    /// <summary>Who may kick from a chat call: the owner of the group. Null in direct chats and server channels.</summary>
    public int? CallModeratorId { get; private set; }

    public void SetCallModerator(int? userId) => CallModeratorId = userId;
    public WebRTCEngine EngineType { get; }
    /// <summary>Joiners wait for someone with ApproveMembers to let them in.</summary>
    public bool IsApprovalRequired { get; private set; }
    public HybridMediaSettings Media { get; private set; }

    /// <summary>Runtime state, not persisted: whether the session has an SFU node to run on.</summary>
    public HybridSessionNodeState State { get; private set; }

    public override SessionType SessionType => SessionType.Hybrid;

    public HybridSessionModel(int id, string coordinatorInstanceId, string name, int capacity,
        WebRTCEngine engineType, bool isApprovalRequired = false, HybridMediaSettings? media = null)
        : base(id, coordinatorInstanceId, name)
    {
        Capacity = ValidateCapacity(capacity);
        EngineType = engineType;
        IsApprovalRequired = isApprovalRequired;
        Media = media ?? HybridMediaSettings.Default;
    }

    /// <summary>A new session that is not saved yet.</summary>
    public static HybridSessionModel Create(string coordinatorInstanceId, string name, int capacity,
        WebRTCEngine engineType, bool isApprovalRequired = false, HybridMediaSettings? media = null) =>
        new(0, coordinatorInstanceId, name, capacity, engineType, isApprovalRequired, media);

    public void ChangeCapacity(int capacity) => Capacity = ValidateCapacity(capacity);

    public bool IsFull(int membersCount) => Capacity != NoCapacityLimit && membersCount >= Capacity;

    public void SetApprovalRequired(bool isApprovalRequired) => IsApprovalRequired = isApprovalRequired;

    public void SetMedia(HybridMediaSettings media) => Media = media;

    /// <returns>Whether the state changed.</returns>
    public bool SetState(HybridSessionNodeState state)
    {
        if (State == state)
            return false;

        State = state;
        return true;
    }

    private static int ValidateCapacity(int capacity)
    {
        if (capacity != NoCapacityLimit && capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive or unlimited.");
        return capacity;
    }
}
