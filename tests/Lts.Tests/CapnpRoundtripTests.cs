using Lts;
using Capnp;
using Xunit;

public class CapnpRoundtripTests
{
    [Fact]
    public void Discovery_Frame_Serializes_And_Decodes()
    {
        var discovery = new CapnpGen.DiscoveryFrame
        {
            SchemaVersion = 1,
            GameName = "test-game",
            Objects = new List<CapnpGen.ObjectDescriptor>
            {
                new()
                {
                    Name = "car", Type = CapnpGen.ObjectType.vehicle, Primary = true,
                    Channels = new List<CapnpGen.ChannelDescriptor>
                    {
                        new() { Id = 7, Name = "engine.rpm", Unit = "rpm" },
                    },
                },
            },
        };

        var mb = MessageBuilder.Create(512);
        discovery.serialize(mb.BuildRoot<CapnpGen.DiscoveryFrame.WRITER>());
        var bytes = Envelope.EncodeMessage(mb);

        var wf = Envelope.DecodeMessage(bytes);
        var r = CapnpGen.DiscoveryFrame.READER.create(DeserializerState.CreateRoot(wf));

        Assert.Equal(1u, r.SchemaVersion);
        Assert.Equal("test-game", r.GameName);
        var obj = Assert.Single(r.Objects);
        Assert.Equal("car", obj.Name);
        Assert.True(obj.Primary);
        var ch = Assert.Single(obj.Channels);
        Assert.Equal(7u, ch.Id);
        Assert.Equal("engine.rpm", ch.Name);
        Assert.Equal("rpm", ch.Unit);
    }

    [Fact]
    public void Motion_Frame_Quaternion_Survives_Roundtrip()
    {
        var motion = new CapnpGen.MotionFrame
        {
            Timestamp = 12.5,
            Objects = new List<CapnpGen.MotionObject>
            {
                new()
                {
                    Name = "drone",
                    Position = new CapnpGen.Vector3 { X = 1.5f, Y = -2.5f, Z = 3.5f },
                    Orientation = new CapnpGen.Quaternion
                    {
                        X = 0.1f, Y = 0.2f, Z = -0.3f,
                        W = MathF.Sqrt(1f - 0.01f - 0.04f - 0.09f),
                    },
                },
            },
        };

        var mb = MessageBuilder.Create(512);
        motion.serialize(mb.BuildRoot<CapnpGen.MotionFrame.WRITER>());
        var bytes = Envelope.EncodeMessage(mb);

        var r = CapnpGen.MotionFrame.READER.create(
            DeserializerState.CreateRoot(Envelope.DecodeMessage(bytes)));

        Assert.Equal(12.5, r.Timestamp, 6);
        var o = Assert.Single(r.Objects);
        Assert.Equal("drone", o.Name);
        Assert.Equal(1.5f, o.Position.X);
        Assert.Equal(3.5f, o.Position.Z);
        Assert.Equal(-0.3f, o.Orientation.Z);
        Assert.InRange(o.Orientation.W, 0.91f, 0.95f);
    }
}
