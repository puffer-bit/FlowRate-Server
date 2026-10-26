namespace Server.Application.Coordinator.Connections;

/// <summary>
/// Handles the messages of one connection one after another, in the order they came.
/// </summary>
public sealed class ConnectionHandlingQueue
{
    private readonly Lock _lock = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Starts the work at once when nothing of this connection is handled now, otherwise after it.</summary>
    public void Enqueue(Func<Task> work)
    {
        lock (_lock)
        {
            _tail = _tail.IsCompleted ? work() : RunAfterAsync(_tail, work);
        }
    }

    private static async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
        }
        catch
        {
            // The previous work reported its own failure
        }

        await work();
    }
}
