using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK.Tests;

public class AsyncAuthStoreTests
{
    [Fact]
    public async Task Constructor_LoadsInitial_AndPersistsThroughQueue()
    {
        var calls = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = new AsyncAuthStore(
            save: async payload =>
            {
                await gate.Task;
                lock (calls) calls.Add(payload);
            },
            initial: "{\"token\":\"t1\",\"record\":{\"id\":\"r1\"}}"
        );

        await Task.Delay(20);
        Assert.Empty(calls);

        gate.SetResult();
        await WaitAsync(() => calls.Count == 1);

        Assert.Equal("t1", store.Token);
        Assert.Equal("r1", store.Record?.Id);
        Assert.Contains("\"token\":\"t1\"", calls[0]);
    }

    [Fact]
    public async Task Clear_UsesExplicitClearDelegate_WhenProvided()
    {
        var saves = new List<string>();
        var clears = 0;

        var store = new AsyncAuthStore(
            save: payload => { lock (saves) saves.Add(payload); return Task.CompletedTask; },
            clear: () => { Interlocked.Increment(ref clears); return Task.CompletedTask; });

        store.Save("t", RecordModel.FromJson("{\"id\":\"r\"}"));
        store.Clear();

        await WaitAsync(() => clears == 1);

        Assert.Single(saves);
        Assert.Equal("", store.Token);
        Assert.Null(store.Record);
    }

    [Fact]
    public async Task Queue_ContinuesAfterPersistenceFailure()
    {
        var calls = new List<string>();
        var fail = true;

        var store = new AsyncAuthStore(save: payload =>
        {
            if (fail)
            {
                fail = false;
                throw new InvalidOperationException("fail");
            }

            lock (calls) calls.Add(payload);
            return Task.CompletedTask;
        });

        store.Save("a", RecordModel.FromJson("{\"id\":\"1\"}"));
        store.Save("b", RecordModel.FromJson("{\"id\":\"2\"}"));

        await WaitAsync(() => calls.Count == 1);
        Assert.Contains("\"token\":\"b\"", calls[0]);
    }

    private static async Task WaitAsync(Func<bool> predicate, int timeoutMs = 1500)
    {
        var start = Environment.TickCount64;
        while (!predicate())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("Condition not reached in time.");

            await Task.Delay(10);
        }
    }
}
