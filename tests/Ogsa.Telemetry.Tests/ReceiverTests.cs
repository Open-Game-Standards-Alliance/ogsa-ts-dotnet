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
    public async Task Throwing_Handler_Does_Not_Stop_The_Receive_Loop()
    {
        const int port = 41244;
        using var receiver = new TelemetryReceiver(port: port,
            localInterface: System.Net.IPAddress.Loopback);
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.HandlerFaulted += ex => faulted.TrySetResult(ex);
        receiver.MotionReceived += m =>
        {
            if (m.Timestamp == 1.0) throw new InvalidOperationException("handler bug");
            second.TrySetResult(m.Timestamp);
        };
        receiver.Start();

        using var sender = new TelemetrySender(port: port,
            localInterface: System.Net.IPAddress.Loopback);
        sender.Send(FrameType.Motion, new Ogsa.Telemetry.Schema.MotionFrame { Timestamp = 1.0 });
        var ex = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("handler bug", ex.Message);

        sender.Send(FrameType.Motion, new Ogsa.Telemetry.Schema.MotionFrame { Timestamp = 2.0 });
        Assert.Equal(2.0, await second.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, receiver.HandlerErrors);
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
