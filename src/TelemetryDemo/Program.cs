using Ogsa.Telemetry;
using Capnp;

// Demo sender: one vehicle object, discovery every 2 s, motion at ~60 Hz.
// Exercises the pose core (position + quaternion orientation), a wheel
// drive point with suspension spec, reference point, and two channels.

var discovery = new CapnpGen.DiscoveryFrame
{
    SchemaVersion = 1,
    GameName = "TelemetryDemo",
    SessionStartUnixUs = DateTimeOffset.UtcNow.UtcTicks / 10, // µs
    Environment = new CapnpGen.Environment
    {
        AirDensity = 1.225f, Temperature = 20f, Pressure = 1.01325f,
        Gravity = 9.81f, Medium = CapnpGen.ContactMedium.asphalt,
    },
    Objects = new List<CapnpGen.ObjectDescriptor>
    {
        new()
        {
            Name = "player_car",
            Type = CapnpGen.ObjectType.vehicle,
            TypeLabel = "OGSA-TS demo car",
            Primary = true,
            Location = "loopback",
            ReferencePoint = new CapnpGen.NamedPoint
            {
                Name = "origin", Position = new CapnpGen.Vector3(),
            },
            DrivePoints = BuildWheels(),
            Channels = new List<CapnpGen.ChannelDescriptor>
            {
                new() { Id = 0, Name = "engine.rpm", Unit = "rpm",
                        Range = new CapnpGen.Range { Min = 0, Max = 9000 },
                        Description = "Engine revolutions per minute" },
                new() { Id = 1, Name = "lights.headlights", Unit = "",
                        Description = "Headlights on/off" },
            },
        },
    },
};

List<CapnpGen.DrivePointDescriptor> BuildWheels()
{
    var names = new[] { "wheel_fl", "wheel_fr", "wheel_rl", "wheel_rr" };
    var list = new List<CapnpGen.DrivePointDescriptor>();
    for (int i = 0; i < 4; i++)
    {
        list.Add(new CapnpGen.DrivePointDescriptor
        {
            Id = (ushort)i,
            Name = names[i],
            Offset = new CapnpGen.Vector3 { X = i < 2 ? 1.2f : -1.2f, Y = 0, Z = (i & 1) == 0 ? 0.8f : -0.8f },
            Wheel = new CapnpGen.WheelSpec
            {
                Radius = 0.34f, Driven = i >= 2, Steered = i < 2,
                Suspension = new CapnpGen.SuspensionSpec
                {
                    Travel = new CapnpGen.Range { Min = -0.05, Max = 0.15 },
                    Stiffness = 65000f, Damping = 5000f,
                },
            },
        });
    }
    return list;
}

using var sender = new TelemetrySender();
Console.WriteLine("TelemetryDemo: sending to 239.255.17.17:40123 (Ctrl-C to stop)");

uint nextEventId = 1;
var lastDiscovery = DateTimeOffset.MinValue;
var lastEvent = DateTimeOffset.MinValue;
var lastEventSend = DateTimeOffset.MinValue;
// recent events re-carried for ~100 ms per CONVENTIONS C-10 (replay + dedup)
var recent = new Queue<(CapnpGen.Event ev, DateTimeOffset at)>();
long frame = 0;
while (true)
{
    var now = DateTimeOffset.UtcNow;
    if ((now - lastDiscovery).TotalSeconds >= 2.0)
    {
        var mb = MessageBuilder.Create(512);
        discovery.serialize(mb.BuildRoot<CapnpGen.DiscoveryFrame.WRITER>());
        sender.Send(FrameType.Discovery, mb);
        lastDiscovery = now;
    }

    if ((now - lastEvent).TotalSeconds >= 3.0)
    {
        recent.Enqueue((new CapnpGen.Event
        {
            Id = nextEventId++,
            Name = "body.impact",
            Intensity = 0.8f,
            Duration = 0.25f,
            Direction = new CapnpGen.Vector3 { X = -1f, Y = 0f, Z = 0f },
            Object = "",
        }, now));
        lastEvent = now;
    }

    while (recent.Count > 0 && (now - recent.Peek().at).TotalMilliseconds > 100)
        recent.Dequeue();
    if (recent.Count > 0 && (now - lastEventSend).TotalMilliseconds >= 50)
    {
        var emb = MessageBuilder.Create(512);
        new CapnpGen.EventFrame
        {
            Timestamp = (float)frame / 60.0,
            Events = recent.Select(r => r.ev).ToList(),
        }.serialize(emb.BuildRoot<CapnpGen.EventFrame.WRITER>());
        sender.Send(FrameType.Event, emb);
        lastEventSend = now;
    }

    double t = frame / 60.0;
    float yaw = (float)(t * 0.8); // slow left-hand yaw
    var motion = new CapnpGen.MotionFrame
    {
        Timestamp = (float)t,
        Objects = new List<CapnpGen.MotionObject>
        {
            new()
            {
                Name = "player_car",
                Position = new CapnpGen.Vector3
                {
                    X = 10f * MathF.Cos(yaw), Y = 0f, Z = 10f * MathF.Sin(yaw),
                },
                Orientation = new CapnpGen.Quaternion
                {
                    X = 0f, Y = MathF.Sin(yaw / 2), Z = 0f, W = MathF.Cos(yaw / 2),
                },
            },
        },
    };
    var mmb = MessageBuilder.Create(512);
    motion.serialize(mmb.BuildRoot<CapnpGen.MotionFrame.WRITER>());
    sender.Send(FrameType.Motion, mmb);

    if (frame % 120 == 0)
        Console.WriteLine($"  frame {frame}: t={t:F1}s yaw={yaw:F2}rad");
    frame++;
    await Task.Delay(16);
}
