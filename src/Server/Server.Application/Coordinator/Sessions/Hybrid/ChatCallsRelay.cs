using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Sessions.Hybrid;

/// <summary>
/// Hands changes of chats to their calls. The event bus is built before the hybrid session manager, so the
/// manager is attached later.
/// </summary>
public sealed class ChatCallsRelay : IEventHandler<ChatSessionReconfiguredEvent>, IEventHandler<ChatSessionDeletedEvent>
{
    private IHybridSessionManager? _calls;

    public void Attach(IHybridSessionManager calls) => _calls = calls;

    public Task HandleAsync(ChatSessionReconfiguredEvent evt, CancellationToken ct = default) => Follow(evt.Session.Id);

    public Task HandleAsync(ChatSessionDeletedEvent evt, CancellationToken ct = default) => Follow(evt.SessionId);

    // The chat publishes these under its write lock, which ending a call takes to record it: run apart
    private Task Follow(int chatId)
    {
        if (_calls is { } calls)
            _ = Task.Run(() => calls.OnChatChangedAsync(chatId));
        return Task.CompletedTask;
    }
}
