using Server.Application.Coordinator;
using Shared.Enums;
using Shared.Models.Notifications.Heartbeat;

namespace Server.Application.Orchestrator.Restart;

/// <summary>A restart, update or shutdown an administrator announced for a time.</summary>
/// <param name="RestartsAtEnd">The server stops itself at that time, to be started again by its supervisor.</param>
public sealed record ServerRestartAnnouncement(ServerRestartReason Reason, DateTimeOffset At, string? Message,
    bool RestartsAtEnd);

/// <summary>
/// Warns the users of every coordinator that the server goes down. The server cannot tell a restart from an update
/// or a shutdown when it is stopped, so the reason it tells is the announced one, and a restart without one.
/// </summary>
public interface IServerRestartNotice
{
    ServerRestartAnnouncement? Current { get; }

    /// <summary>The announced time came for an announcement that restarts the server by itself.</summary>
    event Action? Due;

    /// <summary>
    /// Replaces the current announcement and tells every connected user. An announcement whose server does not
    /// stop by itself is dropped silently once its time has passed, so late users are not warned about the past.
    /// </summary>
    void Announce(ServerRestartAnnouncement announcement);

    /// <summary>
    /// Restarts the server at once, without a warning first: the users are told only that it goes down now,
    /// with <paramref name="message"/>.
    /// </summary>
    void RestartNow(string? message);

    /// <summary>Calls the announcement off and tells every connected user; false when there was none.</summary>
    bool Cancel();

    /// <summary>The warning for a user who connects while an announcement stands; null when none does.</summary>
    ServerRestartNotification? Warning { get; }

    /// <summary>What the users are told when the server goes down now.</summary>
    ServerRestartNotification GoingDown();
}

public sealed class ServerRestartNotice(IOrchestratorInstance orchestrator, TimeProvider time)
    : IServerRestartNotice, IDisposable
{
    private readonly Lock _lock = new();
    private ServerRestartAnnouncement? _current;
    private ITimer? _timer;

    public event Action? Due;

    public ServerRestartAnnouncement? Current
    {
        get { lock (_lock) return _current; }
    }

    public ServerRestartNotification? Warning =>
        Current is { } current ? new ServerRestartNotification(current.Reason, current.At, current.Message) : null;

    public void Announce(ServerRestartAnnouncement announcement)
    {
        lock (_lock)
        {
            _current = announcement;
            _timer?.Dispose();
            var delay = announcement.At - time.GetUtcNow();
            _timer = time.CreateTimer(_ => OnDue(announcement), null,
                delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
        Broadcast(new ServerRestartNotification(announcement.Reason, announcement.At, announcement.Message));
    }

    public bool Cancel()
    {
        lock (_lock)
        {
            if (_current == null)
                return false;
            _current = null;
            _timer?.Dispose();
            _timer = null;
        }
        Broadcast(new ServerRestartCancelledNotification());
        return true;
    }

    public void RestartNow(string? message)
    {
        lock (_lock)
        {
            // Kept for GoingDown, which tells the reason and the message when the process stops
            _current = new ServerRestartAnnouncement(ServerRestartReason.Restart, time.GetUtcNow(), message, true);
            _timer?.Dispose();
            _timer = null;
        }
        Due?.Invoke();
    }

    public ServerRestartNotification GoingDown()
    {
        var current = Current;
        return new ServerRestartNotification(current?.Reason ?? ServerRestartReason.Restart, null, current?.Message);
    }

    private void OnDue(ServerRestartAnnouncement announcement)
    {
        lock (_lock)
        {
            // Replaced or called off in the meantime
            if (!ReferenceEquals(_current, announcement))
                return;
            // Nothing happens by itself: the warning is over and must not reach those who connect later
            if (!announcement.RestartsAtEnd)
            {
                _current = null;
                _timer?.Dispose();
                _timer = null;
                return;
            }
        }
        Due?.Invoke();
    }

    private void Broadcast(Shared.Models.BaseMessage message)
    {
        foreach (var coordinator in orchestrator.CoordinatorsPool.Values)
            coordinator.Broadcast(message);
    }

    public void Dispose()
    {
        lock (_lock) _timer?.Dispose();
    }
}
