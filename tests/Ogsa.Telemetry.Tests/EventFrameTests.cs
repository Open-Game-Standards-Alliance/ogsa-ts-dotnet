using Ogsa.Telemetry;
using Ogsa.Telemetry.Schema;
using Ogsa.Telemetry.Wire;
using Xunit;

public class EventFrameTests
{
    [Fact]
    public void Event_Frame_Serializes_And_Decodes()
    {
        var impact = new Event { Id = 7, Name = "body.impact", Intensity = 0.8f, Duration = 0.25f };
        impact.InitDirection().X = -1f;
        var frame = new EventFrame
        {
            Timestamp = 4.25,
            Events = new List<Event>
            {
                impact,
                new() { Id = 8, Name = "weapon.fire", Intensity = 0.5f }, // noDirection: union default
            },
        };

        var r = CapnpGen.EventFrame.READER.create(
            MessageDecoder.DecodeRoot(Envelope.EncodeMessage(CanonicalEncoder.Encode(frame))));

        Assert.Equal(4.25, r.Timestamp, 6);
        Assert.Equal(2, r.Events.Count);

        var hit = r.Events[0];
        Assert.Equal(7u, hit.Id);
        Assert.Equal("body.impact", hit.Name);
        Assert.Equal(0.8f, hit.Intensity);
        Assert.Equal(0.25f, hit.Duration);
        Assert.Equal(CapnpGen.Event.WHICH.Direction, hit.which);
        Assert.Equal(-1f, hit.Direction.X); // hit from the left (C-10)

        var fire = r.Events[1];
        Assert.Equal(8u, fire.Id);
        // an untouched optionality union is zero: the Void default
        Assert.Equal(CapnpGen.Event.WHICH.NoDirection, fire.which);
        Assert.Equal(0f, fire.Duration); // momentary impulse
        Assert.Equal("", fire.Object);   // "" = primary object, written explicitly
    }

    [Fact]
    public void Envelope_Carries_Event_Frame_Type()
    {
        var datagram = CanonicalEncoder.EncodeDatagram(FrameType.Event,
            new EventFrame { Timestamp = 1.0, Events = new List<Event>() });
        Assert.True(Envelope.TryUnwrap(datagram, out var type, out var payload));
        Assert.Equal(FrameType.Event, type);
        Assert.True(payload.Length > 0);
    }
}
