using Lts;
using Capnp;

// Demo sender: one vehicle object, discovery every 2 s, motion at ~60 Hz.
// Exercises the pose core (position + quaternion orientation), a wheel
// drive point with suspension spec, reference point, and two channels.

var discovery = new CapnpGen.DiscoveryFrame
{
    SchemaVersion = 1,
    GameName = "LtsDemo",
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
            TypeLabel = "Lts demo car",
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

using var sender = new LtsSender();
Console.WriteLine("LtsDemo: sending to 239.255.17.17:40123 (Ctrl-C to stop)");

var lastDiscovery = DateTimeOffset.MinValue;
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
