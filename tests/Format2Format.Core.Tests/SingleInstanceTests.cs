using Format2Format.Core;

namespace Format2Format.Core.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task SecondInstanceForwardsArgumentsToFirst()
    {
        string id = "F2FTest-" + Guid.NewGuid().ToString("N")[..12];
        using var primary = new SingleInstance(id);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(primary.TryBecomePrimary());
        primary.ArgumentsReceived += args => received.TrySetResult(args);
        primary.StartServer();

        // Mutexes are re-entrant per thread, so simulate the second process on another thread.
        bool secondaryWasPrimary = true, sent = false;
        var thread = new Thread(() =>
        {
            using var secondary = new SingleInstance(id);
            secondaryWasPrimary = secondary.TryBecomePrimary();
            sent = secondary.TrySendToPrimary([@"C:\pics\a.jfif", @"C:\pics\b c.webp"], 5000);
        });
        thread.Start();
        thread.Join();

        Assert.False(secondaryWasPrimary);
        Assert.True(sent);
        string[] args = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([@"C:\pics\a.jfif", @"C:\pics\b c.webp"], args);
    }

    [Fact]
    public void SendFailsWhenNoPrimaryIsListening()
    {
        using var lonely = new SingleInstance("F2FTest-" + Guid.NewGuid().ToString("N")[..12]);

        Assert.False(lonely.TrySendToPrimary(["x"], 300));
    }
}
