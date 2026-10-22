using Ogsa.Telemetry;
using CapnpGen;

// Reference receiver: joins the multicast group, prints discovery once and
// motion throttled, and runs conformance checks on every frame.

using var receiver = new TelemetryReceiver();
var printedDiscovery = false;
long motionSeen = 0;
var motionPerObject = new Dictionary<string, long>();
long violations = 0;
var seenEventIds = new HashSet<uint>();
long deduped = 0;

receiver.DiscoveryReceived += d =>
{
    if (d.SchemaVersion != 1) Warn($"schemaVersion {d.SchemaVersion} != 1 (attempting anyway)");
    if (!d.Objects.Any(o => o.Primary)) Warn("no primary object in discovery");
    Console.WriteLine($"Discovery: game={d.GameName} schema={d.SchemaVersion} " +
                      $"objects={d.Objects.Count} medium={d.Environment.Medium}");
    foreach (var o in d.Objects)
    {
        Console.WriteLine($"  object '{o.Name}' type={o.Type} typeLabel='{o.TypeLabel}' " +
                          $"primary={o.Primary} drivePoints={o.DrivePoints.Count} " +
                          $"channels={o.Channels.Count}");
        foreach (var ch in o.Channels)
            Console.WriteLine($"    channel {ch.Id}: {ch.Name} [{ch.Unit}] range={DescribeRange(ch)}");
    }
    printedDiscovery = true;
};

receiver.EventReceived += e =>
{
    foreach (var ev in e.Events)
    {
        var dir = ev.which == CapnpGen.Event.WHICH.Direction
            ? $" dir=({ev.Direction.X:F1},{ev.Direction.Y:F1},{ev.Direction.Z:F1})" : "";
        if (!seenEventIds.Add(ev.Id)) { deduped++; continue; }
        Console.WriteLine($"Event: id={ev.Id} {ev.Name} intensity={ev.Intensity:F2} " +
                          $"duration={ev.Duration:F2}s{dir} object='{ev.Object}'");
    }
};

receiver.MotionReceived += m =>
{
    if (m.Objects.Count == 0) return;
    var primary = m.Objects[0];
    var q = primary.Orientation;
    float norm = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
    if (MathF.Abs(norm - 1f) > 1e-3f) Warn($"quaternion norm {norm:F4} != 1 on '{primary.Name}'");
    motionSeen++;
    // throttle per object: a global frame counter would only ever print
    // one sender when several publishers interleave (modulo parity)
    long seen = motionPerObject.TryGetValue(primary.Name ?? "?", out var n) ? n : 0;
    motionPerObject[primary.Name ?? "?"] = seen + 1;
    if (seen % 180 == 0)
    {
        var vals = string.Join(" ", primary.Channels.Select(c =>
            c.which == CapnpGen.ChannelValue.WHICH.Boolean
                ? $"{c.Id}={c.Boolean}"
                : $"{c.Id}={c.Number:F2}"));
        var (yaw, pitch, roll) = ToEulerDegrees(q);
        Console.WriteLine($"Motion: t={m.Timestamp:F2}s '{primary.Name}' " +
                          $"pos=({primary.Position.X:F2},{primary.Position.Y:F2},{primary.Position.Z:F2}) " +
                          $"|q|={norm:F3} yaw={Signed(yaw)}deg pitch={Signed(pitch)}deg roll={Signed(roll)}deg" +
                          (vals.Length > 0 ? " " + vals : ""));
    }
};

// Orientation readout in the OGSA-TS frame (left-handed: Z forward, X
// right, Y up). ZYX Tait-Bryan angles from the rotated basis vectors:
// forward f = q·(0,0,1), right r = q·(1,0,0) → yaw = atan2(f.X, f.Z)
// (0 = +Z, turning right positive), pitch = asin(f.Y) (nose up
// positive), roll = asin(-r.Y/cos(pitch)) (right side down positive;
// exact for r.Y = -sin(roll)·cos(pitch), degenerate only at ±90° pitch).
static (float yaw, float pitch, float roll) ToEulerDegrees(
    CapnpGen.Quaternion.READER q)
{
    var rot = new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W);
    var f = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, rot);
    var r = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, rot);
    const float rad2deg = 180f / MathF.PI;
    float yaw = MathF.Atan2(f.X, f.Z) * rad2deg;
    float pitch = MathF.Asin(Clamp1(f.Y)) * rad2deg;
    float cosPitch = MathF.Cos(pitch / rad2deg);
    float roll = MathF.Abs(cosPitch) < 1e-3f ? 0f
        : MathF.Asin(Clamp1(-r.Y / cosPitch)) * rad2deg;
    return (yaw, pitch, roll);
}

static float Clamp1(float v) => v < -1f ? -1f : v > 1f ? 1f : v;

static string Signed(float v) => (v >= 0 ? "+" : "") + (v + 0f).ToString("F1"); // +0f folds -0.0

string DescribeRange(ChannelDescriptor.READER ch) =>
    ch.which == ChannelDescriptor.WHICH.Range ? $"{ch.Range.Min}..{ch.Range.Max}" : "-";
void Warn(string msg) { violations++; Console.WriteLine($"  [warn] {msg}"); }

receiver.Start();
Console.WriteLine("TelemetryReceive: listening on 239.255.17.17:40123 (Ctrl-C to stop)");
while (!printedDiscovery || motionSeen < 5 || seenEventIds.Count == 0)
    await Task.Delay(100);
Console.WriteLine($"Summary: discoverySeen={printedDiscovery} motionFrames={motionSeen} " +
                  $"events={seenEventIds.Count} dedupedReplays={deduped} " +
                  $"warnings={violations} foreignSkipped={receiver.ForeignSkipped}");
