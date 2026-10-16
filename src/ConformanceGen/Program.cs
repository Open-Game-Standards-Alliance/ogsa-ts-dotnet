#nullable enable
using System.Text;
using Capnp;
using Ogsa.Telemetry;

// ConformanceGen — deterministic OGSA-TS conformance corpus generator.
// Writes golden datagrams (hex) + semantic manifest into the standard repo
// (default ../OGSA-Telemetry-Standard/conformance). All floats are chosen
// float32-exact; regeneration must produce byte-identical files — if it
// does not, the change is a wire-format change and must be deliberate.
// Usage: dotnet run --project src/ConformanceGen [outputDir]

var outDir = args.Length > 0 ? args[0]
    : "../OGSA-Telemetry-Standard/conformance";
var vecDir = Path.Combine(outDir, "vectors");
Directory.CreateDirectory(vecDir);

const long sessionUs = 1_730_000_000_000_000; // fixed session anchor (2024-10-27)

static CapnpGen.Environment Env() => new()
{
    AirDensity = 1.25f, Temperature = 20f, Pressure = 1f,
    Gravity = 9.5f, Medium = CapnpGen.ContactMedium.asphalt,
};

var manifest = new
{
    corpusVersion = 3,
    schemaVersion = 1,
    envelope = new { magic = "OMT1", headerBytes = 8 },
    floatRule = "all floats in this corpus are float32-exact; implementations compare exactly",
    vectors = new List<object>(),
};

void Emit(string id, string frameType, string description, FrameType type,
          Action<MessageBuilder> serialize, object input)
{
    var mb = MessageBuilder.Create(512);
    serialize(mb);
    var datagram = Envelope.EncodeDatagram(type, mb.Frame.Segments);
    var hex = Convert.ToHexString(datagram).ToLowerInvariant();
    var wrapped = new StringBuilder();
    for (int i = 0; i < hex.Length; i += 64)
        wrapped.AppendLine(hex.Substring(i, Math.Min(64, hex.Length - i)));
    File.WriteAllText(Path.Combine(vecDir, id + ".hex"), wrapped.ToString());
    manifest.vectors.Add(new
    {
        id, file = $"vectors/{id}.hex", frameType, description, input,
    });
    Console.WriteLine($"  {id}: {datagram.Length} bytes");
}

// ---- vector 1: minimal discovery -------------------------------------
var discoveryMinimal = new CapnpGen.DiscoveryFrame
{
    SchemaVersion = 1,
    GameName = "conformance",
    SessionStartUnixUs = sessionUs,
    Environment = Env(),
    Objects = new List<CapnpGen.ObjectDescriptor>
    {
        new()
        {
            Name = "obj", Type = CapnpGen.ObjectType.vehicle, Primary = true,
            Location = "local",
            ReferencePoint = new CapnpGen.NamedPoint
                { Name = "origin", Position = new CapnpGen.Vector3() },
        },
    },
};
Emit("discovery-minimal", "discovery",
    "Smallest valid DiscoveryFrame: one primary object, no channels or drive points",
    FrameType.Discovery,
    mb => discoveryMinimal.serialize(
        mb.BuildRoot<CapnpGen.DiscoveryFrame.WRITER>()),
    new
    {
        schemaVersion = 1, gameName = "conformance", sessionStartUnixUs = sessionUs,
        environment = new { airDensity = 1.25, temperature = 20.0, pressure = 1.0, gravity = 9.5, medium = "asphalt" },
        objects = new[] { new { name = "obj", type = "vehicle", primary = true } },
    });

// ---- vector 2: discovery with channels (mirrors the Unity demo car) --
var discoveryChannels = new CapnpGen.DiscoveryFrame
{
    SchemaVersion = 1,
    GameName = "conformance",
    SessionStartUnixUs = sessionUs,
    Environment = Env(),
    Objects = new List<CapnpGen.ObjectDescriptor>
    {
        new()
        {
            Name = "demo_car", Type = CapnpGen.ObjectType.vehicle,
            TypeLabel = "conformance car", Primary = true, Location = "local",
            ReferencePoint = new CapnpGen.NamedPoint
                { Name = "origin", Position = new CapnpGen.Vector3() },
            Channels = new List<CapnpGen.ChannelDescriptor>
            {
                new() { Id = 0, Name = "engine.rpm", Unit = "rpm",
                        Range = new CapnpGen.Range { Min = 0, Max = 9000 } },
                new() { Id = 1, Name = "vehicle.speed", Unit = "m/s",
                        Range = new CapnpGen.Range { Min = 0, Max = 30 } },
                new() { Id = 2, Name = "lights.headlights", Unit = "" },
            },
        },
    },
};
Emit("discovery-channels", "discovery",
    "Object with three channels incl. ranges and a unitless boolean channel",
    FrameType.Discovery,
    mb => discoveryChannels.serialize(
        mb.BuildRoot<CapnpGen.DiscoveryFrame.WRITER>()),
    new
    {
        schemaVersion = 1, gameName = "conformance", sessionStartUnixUs = sessionUs,
        objects = new[] { new { name = "demo_car", type = "vehicle", primary = true, channels = new object[]
        {
            new { id = 0, name = "engine.rpm", unit = "rpm", range = new { min = 0.0, max = 9000.0 } },
            new { id = 1, name = "vehicle.speed", unit = "m/s", range = new { min = 0.0, max = 30.0 } },
            new { id = 2, name = "lights.headlights", unit = "" },
        } } },
    });

// ---- vector 3: motion, pose only -------------------------------------
var motionPose = new CapnpGen.MotionFrame
{
    Timestamp = 1.25,
    Objects = new List<CapnpGen.MotionObject>
    {
        new()
        {
            Name = "obj",
            Position = new CapnpGen.Vector3 { X = 1.5f, Y = 0.25f, Z = -3.75f },
            Orientation = new CapnpGen.Quaternion { X = 0, Y = 0, Z = 0, W = 1 },
        },
    },
};
Emit("motion-pose", "motion",
    "Pose core only: position + identity quaternion",
    FrameType.Motion,
    mb => motionPose.serialize(mb.BuildRoot<CapnpGen.MotionFrame.WRITER>()),
    new
    {
        timestamp = 1.25,
        objects = new[] { new { name = "obj",
            position = new { x = 1.5, y = 0.25, z = -3.75 },
            orientation = new { x = 0.0, y = 0.0, z = 0.0, w = 1.0 } } },
    });

// ---- vector 4: motion with channel values ----------------------------
var motionChannels = new CapnpGen.MotionFrame
{
    Timestamp = 2.5,
    Objects = new List<CapnpGen.MotionObject>
    {
        new()
        {
            Name = "demo_car",
            Position = new CapnpGen.Vector3 { X = -2f, Y = 0.5f, Z = 3f },
            Orientation = new CapnpGen.Quaternion { X = 0, Y = 1, Z = 0, W = 0 },
            Channels = new List<CapnpGen.ChannelValue>
            {
                new() { Id = 0, Number = 3250.5f },
                new() { Id = 1, Number = 8f },
                new() { Id = 2, Boolean = true },
            },
        },
    },
};
Emit("motion-channels", "motion",
    "Pose + channel values: numbers and a boolean keyed by discovery ids",
    FrameType.Motion,
    mb => motionChannels.serialize(mb.BuildRoot<CapnpGen.MotionFrame.WRITER>()),
    new
    {
        timestamp = 2.5,
        objects = new[] { new { name = "demo_car",
            position = new { x = -2.0, y = 0.5, z = 3.0 },
            orientation = new { x = 0.0, y = 1.0, z = 0.0, w = 0.0 },
            channels = new object[]
            {
                new { id = 0, number = 3250.5 },
                new { id = 1, number = 8.0 },
                new { id = 2, boolean = true },
            } } },
    });

// ---- vector 5: motion, two objects -----------------------------------
var motionMulti = new CapnpGen.MotionFrame
{
    Timestamp = 3.75,
    Objects = new List<CapnpGen.MotionObject>
    {
        new()
        {
            Name = "player_car",
            Position = new CapnpGen.Vector3 { X = 1, Y = 0, Z = 0 },
            Orientation = new CapnpGen.Quaternion { W = 1 },
        },
        new()
        {
            Name = "wingman",
            Position = new CapnpGen.Vector3 { X = -1, Y = 0.5f, Z = 2.5f },
            Orientation = new CapnpGen.Quaternion { X = 0, Y = 1, Z = 0, W = 0 },
        },
    },
};
Emit("motion-multi", "motion",
    "Primary object first, second object after (multi-car grids)",
    FrameType.Motion,
    mb => motionMulti.serialize(mb.BuildRoot<CapnpGen.MotionFrame.WRITER>()),
    new
    {
        timestamp = 3.75,
        objects = new object[]
        {
            new { name = "player_car", position = new { x = 1.0, y = 0.0, z = 0.0 },
                  orientation = new { x = 0.0, y = 0.0, z = 0.0, w = 1.0 } },
            new { name = "wingman", position = new { x = -1.0, y = 0.5, z = 2.5 },
                  orientation = new { x = 0.0, y = 1.0, z = 0.0, w = 0.0 } },
        },
    });

// ---- vector 6: motion with body dynamics ----------------------------
var motionBodyDynamics = new CapnpGen.MotionFrame
{
    Timestamp = 4.5,
    Objects = new List<CapnpGen.MotionObject>
    {
        new()
        {
            Name = "obj",
            Position = new CapnpGen.Vector3 { X = 0.5f, Y = 1.25f, Z = -2.5f },
            Orientation = new CapnpGen.Quaternion { X = 0, Y = 0.5f, Z = 0, W = 0.875f },
            // accelerometer-style specific force (f = a − g: +1 g on +heave
            // at rest) and C-4 body angular rates — the motion-cueing pair
            BodyDynamics = new CapnpGen.BodyDynamics
            {
                SpecificForce = new CapnpGen.Vector3
                    { X = 0.25f, Y = 9.5f, Z = -1.5f },
                AngularVelocity = new CapnpGen.Vector3
                    { X = -0.125f, Y = 0.375f, Z = 0.75f },
            },
        },
    },
};
Emit("motion-bodydynamics", "motion",
    "Pose + bodyDynamics union variant: accelerometer-style specific force " +
    "(+heave gravity reaction at rest) and C-4 body angular velocity",
    FrameType.Motion,
    mb => motionBodyDynamics.serialize(mb.BuildRoot<CapnpGen.MotionFrame.WRITER>()),
    new
    {
        timestamp = 4.5,
        objects = new[] { new { name = "obj",
            position = new { x = 0.5, y = 1.25, z = -2.5 },
            orientation = new { x = 0.0, y = 0.5, z = 0.0, w = 0.875 },
            bodyDynamics = new
            {
                specificForce = new { x = 0.25, y = 9.5, z = -1.5 },
                angularVelocity = new { x = -0.125, y = 0.375, z = 0.75 },
            } } },
    });

// ---- vector 7: event frame -------------------------------------------
var eventImpact = new CapnpGen.EventFrame
{
    Timestamp = 0.125,
    Events = new List<CapnpGen.Event>
    {
        new()
        {
            Id = 7, Name = "body.impact", Intensity = 0.75f, Duration = 0.25f,
            Direction = new CapnpGen.Vector3 { X = -1, Y = 0, Z = 0 },
            Object = "",
        },
    },
};
Emit("event-impact", "event",
    "One body.impact stimulus with direction, intensity and duration",
    FrameType.Event,
    mb => eventImpact.serialize(mb.BuildRoot<CapnpGen.EventFrame.WRITER>()),
    new
    {
        timestamp = 0.125,
        events = new[] { new { id = 7u, name = "body.impact", intensity = 0.75,
                               duration = 0.25, direction = new { x = -1.0, y = 0.0, z = 0.0 } } },
    });

// ---- manifest ----------------------------------------------------------
var options = new System.Text.Json.JsonSerializerOptions
{
    WriteIndented = true,
};
File.WriteAllText(Path.Combine(outDir, "manifest.json"),
    System.Text.Json.JsonSerializer.Serialize(manifest, options) + "\n");
Console.WriteLine($"corpus written to {Path.GetFullPath(outDir)}");
