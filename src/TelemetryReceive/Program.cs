using Ogsa.Telemetry;
using CapnpGen;

// Reference receiver: joins the multicast group, prints discovery once and
// motion throttled, and runs conformance checks on every frame.

using var receiver = new TelemetryReceiver();
var printedDiscovery = false;
long motionSeen = 0;
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
        Console.WriteLine($"  object '{o.Name}' type={o.Type} primary={o.Primary} " +
                          $"drivePoints={o.DrivePoints.Count} channels={o.Channels.Count}");
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
    if (motionSeen % 180 == 0)
        Console.WriteLine($"Motion: t={m.Timestamp:F2}s '{primary.Name}' " +
                          $"pos=({primary.Position.X:F2},{primary.Position.Y:F2},{primary.Position.Z:F2}) " +
                          $"|q|={norm:F3}");
    motionSeen++;
};

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
