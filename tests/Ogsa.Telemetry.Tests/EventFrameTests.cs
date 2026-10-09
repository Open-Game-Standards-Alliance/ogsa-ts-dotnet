using Ogsa.Telemetry;
using Capnp;
using Xunit;

public class EventFrameTests
{
    [Fact]
    public void Event_Frame_Serializes_And_Decodes()
    {
        var frame = new CapnpGen.EventFrame
        {
            Timestamp = 4.25,
            Events = new List<CapnpGen.Event>
            {
                new()
                {
                    Id = 7, Name = "body.impact", Intensity = 0.8f, Duration = 0.25f,
                    Direction = new CapnpGen.Vector3 { X = -1f },
                },
                new()
                {
                    Id = 8, Name = "weapon.fire", Intensity = 0.5f, Duration = 0f,
                    // noDirection: union default
                },
            },
        };

        var mb = MessageBuilder.Create(512);
        frame.serialize(mb.BuildRoot<CapnpGen.EventFrame.WRITER>());
        var bytes = Envelope.EncodeMessage(mb);

        var r = CapnpGen.EventFrame.READER.create(
            DeserializerState.CreateRoot(Envelope.DecodeMessage(bytes)));

        Assert.Equal(4.25, r.Timestamp, 6);
        Assert.Equal(2, r.Events.Count);

        var impact = r.Events[0];
        Assert.Equal(7u, impact.Id);
        Assert.Equal("body.impact", impact.Name);
        Assert.Equal(0.8f, impact.Intensity);
        Assert.Equal(0.25f, impact.Duration);
        Assert.Equal(CapnpGen.Event.WHICH.Direction, impact.which);
        Assert.Equal(-1f, impact.Direction.X); // hit from the left (C-10)

        var fire = r.Events[1];
        Assert.Equal(8u, fire.Id);
        // unset union = undefined discriminant; receivers treat undefined and
        // noDirection identically (omnidirectional, the Void default)
        Assert.NotEqual(CapnpGen.Event.WHICH.Direction, fire.which);
        Assert.Equal(0f, fire.Duration); // momentary impulse
    }

    [Fact]
    public void Envelope_Carries_Event_Frame_Type()
    {
        var mb = MessageBuilder.Create(512);
        new CapnpGen.EventFrame
        {
            Timestamp = 1.0,
            Events = new List<CapnpGen.Event>(),
        }.serialize(mb.BuildRoot<CapnpGen.EventFrame.WRITER>());

        var datagram = Envelope.Wrap(FrameType.Event, Envelope.EncodeMessage(mb));
        Assert.True(Envelope.TryUnwrap(datagram, out var type, out var payload));
        Assert.Equal(FrameType.Event, type);
        Assert.True(payload.Length > 0);
    }
}
