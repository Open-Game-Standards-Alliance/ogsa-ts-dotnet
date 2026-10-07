using Lts;
using Capnp;
using Xunit;

/// <summary>End-to-end loopback over real UDP multicast (requires loopback
/// multicast to be permitted by the OS/network environment).</summary>
public class LoopbackTests
{
    [Fact]
    public async Task Sender_And_Receiver_Roundtrip_Over_Multicast()
    {
        const int port = 41235; // away from the default 40123 to avoid interference
        using var receiver = new LtsReceiver(port: port);
        var discovery = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var motion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evnt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        receiver.DiscoveryReceived += d =>
            discovery.TrySetResult($"{d.GameName}/{d.Objects.Count}");
        receiver.MotionReceived += m =>
            motion.TrySetResult($"{m.Timestamp:F1}/{m.Objects[0].Name}");
        receiver.EventReceived += e =>
        {
            var first = e.Events.FirstOrDefault();
            if (first.Name != null)
                evnt.TrySetResult($"{first.Id}/{first.Name}");
        };
        receiver.Start();

        using var sender = new LtsSender(port: port);

        var mb = MessageBuilder.Create(512);
        new CapnpGen.DiscoveryFrame
        {
            SchemaVersion = 1,
            GameName = "loopback-test",
            Objects = new List<CapnpGen.ObjectDescriptor>
            {
                new() { Name = "car", Primary = true },
            },
        }.serialize(mb.BuildRoot<CapnpGen.DiscoveryFrame.WRITER>());
        sender.Send(FrameType.Discovery, mb);

        var mmb = MessageBuilder.Create(512);
        new CapnpGen.MotionFrame
        {
            Timestamp = 42.0,
            Objects = new List<CapnpGen.MotionObject>
            {
                new() { Name = "car",
                        Position = new CapnpGen.Vector3(),
                        Orientation = new CapnpGen.Quaternion { W = 1f } },
            },
        }.serialize(mmb.BuildRoot<CapnpGen.MotionFrame.WRITER>());
        sender.Send(FrameType.Motion, mmb);

        var evtMb = MessageBuilder.Create(512);
        new CapnpGen.EventFrame
        {
            Timestamp = 42.5,
            Events = new List<CapnpGen.Event>
            {
                new() { Id = 1, Name = "body.impact", Intensity = 1f, Duration = 0f },
            },
        }.serialize(evtMb.BuildRoot<CapnpGen.EventFrame.WRITER>());
        sender.Send(FrameType.Event, evtMb);

        var done = await Task.WhenAll(discovery.Task, motion.Task, evnt.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("loopback-test/1", done[0]);
        Assert.Equal("42.0/car", done[1]);
        Assert.Equal("1/body.impact", done[2]);
    }
}
