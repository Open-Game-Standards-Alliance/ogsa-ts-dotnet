using System.Diagnostics;
using System.Numerics;
using Ogsa.Telemetry;

// Demo sender on the engine-agnostic TelemetryPublisher: one vehicle
// circling at 10 m with four wheels, rpm/headlights channels and a
// periodic body.impact event. The publisher owns cadence (discovery every
// 2 s, motion at ~60 Hz) and the C-10 event replay window; this host only
// declares, binds, fires, and ticks.

using var sender = new TelemetrySender();
using var pub = new TelemetryPublisher(sender, "TelemetryDemo", motionHz: 60);

pub.DeclareObject("player_car", "OGSA-TS demo car");

// front wheels steer, rear wheels drive (standard frame: +Z forward)
pub.AddWheel("wheel_fl", new Vector3(1.2f, 0f, 0.8f), 0.34f, driven: false, steered: true);
pub.AddWheel("wheel_fr", new Vector3(1.2f, 0f, -0.8f), 0.34f, driven: false, steered: true);
pub.AddWheel("wheel_rl", new Vector3(-1.2f, 0f, 0.8f), 0.34f, driven: true, steered: false);
pub.AddWheel("wheel_rr", new Vector3(-1.2f, 0f, -0.8f), 0.34f, driven: true, steered: false);

var rpm = pub.DeclareChannel("engine.rpm", Unit.Rpm,
    "Engine revolutions per minute", 0, 9000);
var headlights = pub.DeclareBooleanChannel("lights.headlights", "Headlights on/off");

const double angularSpeed = 0.8; // rad/s, slow left-hand yaw
var clock = Stopwatch.StartNew();
double t = 0;

pub.BindChannelRaw(rpm, () => 6500.0 * (0.5 + 0.5 * Math.Sin(t * 2.0)));
pub.BindBooleanChannel(headlights, () => true);
pub.BindPose(
    () => new Vector3(10f * MathF.Cos((float)(t * angularSpeed)), 0f,
                      10f * MathF.Sin((float)(t * angularSpeed))),
    () => new Quaternion(0f,
        MathF.Sin((float)(t * angularSpeed / 2)), 0f,
        MathF.Cos((float)(t * angularSpeed / 2))));

Console.WriteLine("TelemetryDemo: sending to 239.255.17.17:40123 (Ctrl-C to stop)");

double lastEvent = -1;
long frames = 0;
while (true)
{
    t = clock.Elapsed.TotalSeconds;
    if (t - lastEvent >= 3.0)
    {
        pub.FireEvent("body.impact", 0.8f, 0.25f, new Vector3(-1f, 0f, 0f));
        lastEvent = t;
    }
    pub.Tick(t);

    if (frames % 120 == 0)
        Console.WriteLine($"  t={t:F1}s");
    frames++;
    await Task.Delay(16);
}
