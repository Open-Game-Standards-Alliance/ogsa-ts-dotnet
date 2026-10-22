#nullable enable
using Ogsa.Telemetry;
using Capnp;
using Xunit;

/// <summary>Guards the conformance corpus in the standard repo: every
/// golden datagram must decode (Capnp.Net, independent of the canonical
/// writers that generated it) to its manifest-declared semantics. The
/// encode direction is guarded by regeneration (git diff must be empty
/// unless a wire change is deliberate) and the standard's validate.py.
/// See conformance/README.md there.</summary>
public class ConformanceVectorTests
{
    static string CorpusDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null &&
               !File.Exists(Path.Combine(dir.FullName, "Ogsa.Telemetry.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        // sibling of the repo (local dev), then nested inside it (CI checks
        // the standard repo out into the workspace), then any ancestor
        string[] candidates =
        {
            Path.Combine(dir!.Parent!.FullName, "OGSA-Telemetry-Standard", "conformance"),
            Path.Combine(dir.FullName, "OGSA-Telemetry-Standard", "conformance"),
        };
        var found = candidates.FirstOrDefault(Directory.Exists);
        Assert.NotNull(found);
        return found!;
    }

    static (FrameType Type, DeserializerState Root) Load(string id)
    {
        var hex = string.Concat(File.ReadAllLines(
            Path.Combine(CorpusDir(), "vectors", id + ".hex")));
        var bytes = Convert.FromHexString(hex);
        Assert.True(Envelope.TryUnwrap(bytes, out var type, out var payload), id);
        return (type, MessageDecoder.DecodeRoot(payload.Span));
    }

    [Fact]
    public void Discovery_Minimal()
    {
        var (type, root) = Load("discovery-minimal");
        Assert.Equal(FrameType.Discovery, type);
        var d = CapnpGen.DiscoveryFrame.READER.create(root);
        Assert.Equal(1u, d.SchemaVersion);
        Assert.Equal("conformance", d.GameName);
        Assert.Equal(1_730_000_000_000_000L, d.SessionStartUnixUs);
        var obj = Assert.Single(d.Objects);
        Assert.Equal("obj", obj.Name);
        Assert.True(obj.Primary);
        Assert.Equal(CapnpGen.ObjectType.vehicle, obj.Type);
    }

    [Fact]
    public void Discovery_Channels()
    {
        var (_, root) = Load("discovery-channels");
        var d = CapnpGen.DiscoveryFrame.READER.create(root);
        var obj = Assert.Single(d.Objects);
        Assert.Equal("demo_car", obj.Name);
        Assert.Equal(3, obj.Channels.Count);
        Assert.Equal("engine.rpm", obj.Channels[0].Name);
        Assert.Equal("rpm", obj.Channels[0].Unit);
        Assert.Equal(0f, obj.Channels[0].Range.Min);
        Assert.Equal(9000f, obj.Channels[0].Range.Max);
        Assert.Equal("vehicle.speed", obj.Channels[1].Name);
        Assert.Equal("m/s", obj.Channels[1].Unit);
        Assert.Equal(30f, obj.Channels[1].Range.Max);
        Assert.Equal("lights.headlights", obj.Channels[2].Name);
        Assert.Equal("", obj.Channels[2].Unit);
    }

    [Fact]
    public void Motion_Pose()
    {
        var (type, root) = Load("motion-pose");
        Assert.Equal(FrameType.Motion, type);
        var m = CapnpGen.MotionFrame.READER.create(root);
        Assert.Equal(1.25, m.Timestamp);
        var obj = Assert.Single(m.Objects);
        Assert.Equal("obj", obj.Name);
        Assert.Equal(1.5f, obj.Position.X);
        Assert.Equal(0.25f, obj.Position.Y);
        Assert.Equal(-3.75f, obj.Position.Z);
        Assert.Equal(0f, obj.Orientation.X);
        Assert.Equal(1f, obj.Orientation.W);
    }

    [Fact]
    public void Motion_Channels()
    {
        var (_, root) = Load("motion-channels");
        var m = CapnpGen.MotionFrame.READER.create(root);
        Assert.Equal(2.5, m.Timestamp);
        var obj = Assert.Single(m.Objects);
        Assert.Equal(-2f, obj.Position.X);
        Assert.Equal(0.5f, obj.Position.Y);
        Assert.Equal(3f, obj.Position.Z);
        Assert.Equal(1f, obj.Orientation.Y); // 180° yaw
        Assert.Equal(3, obj.Channels.Count);
        Assert.Equal(0, obj.Channels[0].Id);
        Assert.Equal(3250.5f, obj.Channels[0].Number);
        Assert.Equal(8f, obj.Channels[1].Number);
        Assert.True(obj.Channels[2].Boolean);
    }

    [Fact]
    public void Motion_Multi()
    {
        var (_, root) = Load("motion-multi");
        var m = CapnpGen.MotionFrame.READER.create(root);
        Assert.Equal(3.75, m.Timestamp);
        Assert.Equal(2, m.Objects.Count);
        Assert.Equal("player_car", m.Objects[0].Name);
        Assert.Equal(1f, m.Objects[0].Position.X);
        Assert.Equal("wingman", m.Objects[1].Name);
        Assert.Equal(-1f, m.Objects[1].Position.X);
        Assert.Equal(0.5f, m.Objects[1].Position.Y);
        Assert.Equal(2.5f, m.Objects[1].Position.Z);
    }

    [Fact]
    public void Motion_BodyDynamics()
    {
        var (_, root) = Load("motion-bodydynamics");
        var m = CapnpGen.MotionFrame.READER.create(root);
        Assert.Equal(4.5, m.Timestamp);
        var obj = Assert.Single(m.Objects);
        Assert.Equal("obj", obj.Name);
        Assert.Equal(CapnpGen.MotionObject.WHICH.BodyDynamics, obj.which);
        Assert.Equal(0.5f, obj.Position.X);
        Assert.Equal(1.25f, obj.Position.Y);
        Assert.Equal(-2.5f, obj.Position.Z);
        Assert.Equal(0f, obj.Orientation.X);
        Assert.Equal(0.5f, obj.Orientation.Y);
        Assert.Equal(0f, obj.Orientation.Z);
        Assert.Equal(0.875f, obj.Orientation.W);
        // accelerometer-style specific force: +heave gravity reaction
        Assert.Equal(0.25f, obj.BodyDynamics.SpecificForce.X);
        Assert.Equal(9.5f, obj.BodyDynamics.SpecificForce.Y);
        Assert.Equal(-1.5f, obj.BodyDynamics.SpecificForce.Z);
        Assert.Equal(-0.125f, obj.BodyDynamics.AngularVelocity.X);
        Assert.Equal(0.375f, obj.BodyDynamics.AngularVelocity.Y);
        Assert.Equal(0.75f, obj.BodyDynamics.AngularVelocity.Z);
    }

    [Fact]
    public void Event_Impact()
    {
        var (type, root) = Load("event-impact");
        Assert.Equal(FrameType.Event, type);
        var e = CapnpGen.EventFrame.READER.create(root);
        Assert.Equal(0.125, e.Timestamp);
        var ev = Assert.Single(e.Events);
        Assert.Equal(7u, ev.Id);
        Assert.Equal("body.impact", ev.Name);
        Assert.Equal(0.75f, ev.Intensity);
        Assert.Equal(0.25f, ev.Duration);
        Assert.Equal(-1f, ev.Direction.X);
    }
}
