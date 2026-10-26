using Server.Application.Coordinator.Connections;

namespace Server.Application.Tests.Coordinator;

public class ConnectionHandlingQueueTests
{
    [Fact]
    public async Task Work_RunsInOrder_EachAfterThePreviousCompleted()
    {
        var queue = new ConnectionHandlingQueue();
        var first = new TaskCompletionSource();
        var order = new List<int>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        queue.Enqueue(async () =>
        {
            await first.Task;
            order.Add(1);
        });
        queue.Enqueue(() =>
        {
            order.Add(2);
            done.SetResult();
            return Task.CompletedTask;
        });

        Assert.Empty(order);
        first.SetResult();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void Work_WhenIdle_StartsAtOnce()
    {
        var queue = new ConnectionHandlingQueue();
        var started = false;

        queue.Enqueue(() =>
        {
            started = true;
            return Task.CompletedTask;
        });

        Assert.True(started);
    }

    [Fact]
    public async Task FailedWork_DoesNotStopTheNext()
    {
        var queue = new ConnectionHandlingQueue();
        var first = new TaskCompletionSource();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        queue.Enqueue(async () =>
        {
            await first.Task;
            throw new InvalidOperationException();
        });
        queue.Enqueue(() =>
        {
            done.SetResult();
            return Task.CompletedTask;
        });

        first.SetResult();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
