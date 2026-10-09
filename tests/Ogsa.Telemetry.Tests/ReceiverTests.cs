using Ogsa.Telemetry;
using Xunit;

/// <summary>Receiver lifecycle: Dispose must stop the receive loop
/// deterministically — no faulted task, no unobserved exception.</summary>
public class ReceiverTests
{
    [Fact]
    public async Task Dispose_Stops_Receive_Loop_Without_Faulting()
    {
        var receiver = new TelemetryReceiver(port: 41242);
        receiver.Start();
        await Task.Delay(100); // let the loop enter its poll

        receiver.Dispose();

        // Before the ObjectDisposedException guard, closing the socket
        // faulted the loop task here (unobserved task exception).
        var completion = receiver.Completion;
        var first = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(completion, first);
        Assert.Null(completion.Exception);
    }

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var receiver = new TelemetryReceiver(port: 41243);
        receiver.Start();
        receiver.Dispose();
        receiver.Dispose(); // second call must be a no-op, not throw
    }
}
