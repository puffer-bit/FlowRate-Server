using Server.Core.Models.HybridSession;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public interface IHybridSession : IDisposable
{
    HybridSessionModel Model { get; }

    IHybridSessionPeerManager PeerManager { get; }

    /// <summary>
    /// Keeps the current SFU node while it is in the pool; otherwise moves the session to the best available node,
    /// or puts it into <see cref="Shared.Enums.HybridSessionNodeState.WaitingForSfuNode"/> when there is none.
    /// </summary>
    Task AssignSfuNodeAsync();

    Task CloseSession();
}
