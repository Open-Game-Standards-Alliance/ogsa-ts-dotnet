#nullable enable
using System.Text;
using Ogsa.Telemetry;
using Ogsa.Telemetry.Schema;
using Ogsa.Telemetry.Wire;

// ConformanceGen — deterministic OGSA-TS conformance corpus generator.
// Writes golden datagrams (hex) + semantic manifest into the standard repo
// (default ../OGSA-Telemetry-Standard/conformance). Frames are written in
// canonical OGSA encoding, so the bytes are a function of the content
// alone. Each manifest input lists every field that differs from its
// default — an independent encoder can rebuild the vector from it.
// All floats are chosen float32-exact.
// Usage: dotnet run --project src/ConformanceGen [outputDir]

var outDir = args.Length > 0 ? args[0]
    : "../OGSA-Telemetry-Standard/conformance";
var vecDir = Path.Combine(outDir, "vectors");
Directory.CreateDirectory(vecDir);

const long sessionUs = 1_730_000_000_000_000; // fixed session anchor (2024-10-27)

static void SetEnv(Ogsa.Telemetry.Schema.Environment e)
{
    e.AirDensity = 1.25f; e.Temperature = 20f; e.Pressure = 1f;
    e.Gravity = 9.5f; e.Medium = ContactMedium.Asphalt;
}
var envInput = new { airDensity = 1.25, temperature = 20.0, pressure = 1.0, gravity = 9.5, medium = "asphalt" };

var manifest = new
{
    corpusVersion = 4,
    schemaVersion = 1,
    envelope = new { magic = "OMT1", headerBytes = 8 },
    floatRule = "all floats in this corpus are float32-exact; implementations compare exactly",
    inputRule = "each input lists every field that differs from its default; omitted fields are " +
                "default (0, false, \"\", empty list, all-default struct, first enumerant, first union variant)",
    vectors = new List<object>(),
};

void Emit(string id, string frameType, string description, FrameType type,
          WireStruct frame, object input)
{
    var datagram = CanonicalEncoder.EncodeDatagram(type, frame);
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
var discoveryMinimal = new DiscoveryFrame
{
    SchemaVersion = 1,
    GameName = "conformance",
    SessionStartUnixUs = sessionUs,
    Objects = new List<ObjectDescriptor>
    {
        new() { Name = "obj", Type = ObjectType.Vehicle, Primary = true },
    },
};
SetEnv(discoveryMinimal.Environment);
Emit("discovery-minimal", "discovery",
    "Smallest valid DiscoveryFrame: one primary object, no channels or drive points",
    FrameType.Discovery, discoveryMinimal,
    new
    {
        schemaVersion = 1, gameName = "conformance", sessionStartUnixUs = sessionUs,
        environment = envInput,
        objects = new[] { new { name = "obj", type = "vehicle", primary = true } },
    });

// ---- vector 2: discovery with channels (mirrors the Unity demo car) --
var channelsObject = new ObjectDescriptor
{
    Name = "demo_car", Type = ObjectType.Vehicle,
    TypeLabel = "conformance car", Primary = true,
    Channels = new List<ChannelDescriptor>
    {
        new() { Id = 0, Name = "engine.rpm", Unit = "rpm",
                Description = "Engine revolutions per minute" },
        new() { Id = 1, Name = "vehicle.speed", Unit = "m/s" },
        new() { Id = 2, Name = "lights.headlights", Unit = "" },
    },
};
var rpmRange = channelsObject.Channels[0].InitRange();
rpmRange.Min = 0; rpmRange.Max = 9000;
channelsObject.Channels[1].InitRange().Max = 30;
var discoveryChannels = new DiscoveryFrame
{
    SchemaVersion = 1,
    GameName = "conformance",
    SessionStartUnixUs = sessionUs,
    Objects = new List<ObjectDescriptor> { channelsObject },
};
SetEnv(discoveryChannels.Environment);
Emit("discovery-channels", "discovery",
    "Object with three channels incl. ranges, a description and a unitless boolean channel",
    FrameType.Discovery, discoveryChannels,
    new
    {
        schemaVersion = 1, gameName = "conformance", sessionStartUnixUs = sessionUs,
        environment = envInput,
        objects = new[] { new { name = "demo_car", type = "vehicle", typeLabel = "conformance car",
                                primary = true, channels = new object[]
        {
            new { id = 0, name = "engine.rpm", unit = "rpm", range = new { min = 0.0, max = 9000.0 },
                  description = "Engine revolutions per minute" },
            new { id = 1, name = "vehicle.speed", unit = "m/s", range = new { max = 30.0 } },
            new { id = 2, name = "lights.headlights" },
        } } },
    });

// ---- vector 3: motion, pose only -------------------------------------
var motionPose = new MotionFrame
{
    Timestamp = 1.25,
    Objects = new List<MotionObject>
    {
        new()
        {
            Name = "obj",
            Position = { X = 1.5f, Y = 0.25f, Z = -3.75f },
            Orientation = { W = 1 },
        },
    },
};
Emit("motion-pose", "motion",
    "Pose core only: position + identity quaternion",
    FrameType.Motion, motionPose,
    new
    {
        timestamp = 1.25,
        objects = new[] { new { name = "obj",
            position = new { x = 1.5, y = 0.25, z = -3.75 },
            orientation = new { w = 1.0 } } },
    });

// ---- vector 4: motion with channel values ----------------------------
var motionChannels = new MotionFrame
{
    Timestamp = 2.5,
    Objects = new List<MotionObject>
    {
        new()
        {
            Name = "demo_car",
            Position = { X = -2f, Y = 0.5f, Z = 3f },
            Orientation = { Y = 1 },
            Channels = new List<ChannelValue>
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
    FrameType.Motion, motionChannels,
    new
    {
        timestamp = 2.5,
        objects = new[] { new { name = "demo_car",
            position = new { x = -2.0, y = 0.5, z = 3.0 },
            orientation = new { y = 1.0 },
            channels = new object[]
            {
                new { number = 3250.5 },
                new { id = 1, number = 8.0 },
                new { id = 2, boolean = true },
            } } },
    });

// ---- vector 5: motion, two objects -----------------------------------
var motionMulti = new MotionFrame
{
    Timestamp = 3.75,
    Objects = new List<MotionObject>
    {
        new() { Name = "player_car", Position = { X = 1 }, Orientation = { W = 1 } },
        new() { Name = "wingman", Position = { X = -1, Y = 0.5f, Z = 2.5f }, Orientation = { Y = 1 } },
    },
};
Emit("motion-multi", "motion",
    "Primary object first, second object after (multi-car grids)",
    FrameType.Motion, motionMulti,
    new
    {
        timestamp = 3.75,
        objects = new object[]
        {
            new { name = "player_car", position = new { x = 1.0 }, orientation = new { w = 1.0 } },
            new { name = "wingman", position = new { x = -1.0, y = 0.5, z = 2.5 },
                  orientation = new { y = 1.0 } },
        },
    });

// ---- vector 6: motion with body dynamics ----------------------------
var bodyObject = new MotionObject
{
    Name = "obj",
    Position = { X = 0.5f, Y = 1.25f, Z = -2.5f },
    Orientation = { Y = 0.5f, W = 0.875f },
};
// accelerometer-style specific force (f = a − g: +1 g on +heave at rest)
// and C-4 body angular rates — the motion-cueing pair
var bd = bodyObject.InitBodyDynamics();
bd.SpecificForce.X = 0.25f; bd.SpecificForce.Y = 9.5f; bd.SpecificForce.Z = -1.5f;
bd.AngularVelocity.X = -0.125f; bd.AngularVelocity.Y = 0.375f; bd.AngularVelocity.Z = 0.75f;
Emit("motion-bodydynamics", "motion",
    "Pose + bodyDynamics union variant: accelerometer-style specific force " +
    "(+heave gravity reaction at rest) and C-4 body angular velocity",
    FrameType.Motion,
    new MotionFrame { Timestamp = 4.5, Objects = new List<MotionObject> { bodyObject } },
    new
    {
        timestamp = 4.5,
        objects = new[] { new { name = "obj",
            position = new { x = 0.5, y = 1.25, z = -2.5 },
            orientation = new { y = 0.5, w = 0.875 },
            bodyDynamics = new
            {
                specificForce = new { x = 0.25, y = 9.5, z = -1.5 },
                angularVelocity = new { x = -0.125, y = 0.375, z = 0.75 },
            } } },
    });

// ---- vector 7: event frame -------------------------------------------
var impact = new Event { Id = 7, Name = "body.impact", Intensity = 0.75f, Duration = 0.25f };
impact.InitDirection().X = -1;
Emit("event-impact", "event",
    "One body.impact stimulus with direction, intensity and duration",
    FrameType.Event,
    new EventFrame { Timestamp = 0.125, Events = new List<Event> { impact } },
    new
    {
        timestamp = 0.125,
        events = new[] { new { id = 7u, name = "body.impact", intensity = 0.75,
                               duration = 0.25, direction = new { x = -1.0 } } },
    });

// ---- manifest ----------------------------------------------------------
var options = new System.Text.Json.JsonSerializerOptions
{
    WriteIndented = true,
};
File.WriteAllText(Path.Combine(outDir, "manifest.json"),
    System.Text.Json.JsonSerializer.Serialize(manifest, options) + "\n");
Console.WriteLine($"corpus written to {Path.GetFullPath(outDir)}");
