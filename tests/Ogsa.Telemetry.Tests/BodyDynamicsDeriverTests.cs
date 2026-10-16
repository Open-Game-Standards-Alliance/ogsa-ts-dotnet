using System.Numerics;
using Ogsa.Telemetry;
using Xunit;

namespace Ogsa.Telemetry.Tests;

/// <summary>
/// Pins the pose→body-dynamics derivation used by senders without native
/// accelerometer-class data (BepInEx/UE4SS plugins, and the C++ twin in
/// ogsa-ts-unreal). Two properties matter:
///   1. specific force is accelerometer-style (convention A): at rest it
///      reads +9.80665 on the axis that points away from gravity.
///   2. angular velocity uses the C-4 component sense — for pure-axis
///      attitude sweeps it EQUALS the rate the receiver's EulerC4
///      extraction would differentiate to (yaw +nose-right, pitch
///      +nose-up, roll +right-side-down), so GameConn's native-rate path
///      and its Euler fallback agree.
/// </summary>
public class BodyDynamicsDeriverTests
{
    const float G = BodyDynamicsDeriver.Gravity;
    const double Dt = 1.0 / 60.0;

    static void AssertNear(double expected, double actual, double tol,
                           string what)
        => Assert.True(System.Math.Abs(actual - expected) <= tol,
            $"{what}: expected {expected}, got {actual} (tol {tol})");

    [Fact]
    public void First_sample_reads_static_gravity_reaction_level()
    {
        var d = new BodyDynamicsDeriver();
        d.Sample(0.0, new Vector3(5f, 0f, 5f), Quaternion.Identity);
        // at rest, level: +1 g on the up axis (what an IMU reads)
        AssertNear(0, d.SpecificForce.X, 1e-4, "sf.x");
        AssertNear(G, d.SpecificForce.Y, 1e-4, "sf.y");
        AssertNear(0, d.SpecificForce.Z, 1e-4, "sf.z");
        AssertNear(0, d.AngularVelocity.X, 1e-6, "w.x");
        AssertNear(0, d.AngularVelocity.Y, 1e-6, "w.y");
        AssertNear(0, d.AngularVelocity.Z, 1e-6, "w.z");
    }

    [Fact]
    public void Nose_up_rest_reads_gravity_on_surge()
    {
        // pitched straight up (EulerC4 pitch +90 = nose up): constructed as
        // CreateFromYawPitchRoll(0, -90°, 0) — the stored-math angle is
        // negative because C-4 pitch is anti-stored-math (see class doc)
        var d = new BodyDynamicsDeriver();
        var q = Quaternion.CreateFromYawPitchRoll(0f, -FloatPi / 2f, 0f);
        d.Sample(0.0, Vector3.Zero, q);
        // no body axis points world-up except forward: accelerometer-style
        // specific force reads +1 g on +surge
        AssertNear(G, d.SpecificForce.Z, 1e-3, "sf.z (surge)");
        AssertNear(0, d.SpecificForce.X, 1e-3, "sf.x");
        AssertNear(0, d.SpecificForce.Y, 1e-3, "sf.y");
    }

    [Fact]
    public void Level_coast_keeps_gravity_reaction_and_zero_rates()
    {
        var d = new BodyDynamicsDeriver();
        double t = 0;
        Vector3 v = new(0f, 0f, 10f); // 10 m/s forward, level
        for (int i = 0; i < 120; i++) // 2 s
        {
            d.Sample(t, v * (float)t, Quaternion.Identity, hasVelocity: true, engineVelocity: v);
            t += Dt;
        }
        AssertNear(G, d.SpecificForce.Y, 0.05, "sf.y");
        AssertNear(0, d.SpecificForce.Z, 0.05, "sf.z");
        AssertNear(0, d.AngularVelocity.Y, 1e-6, "w.y");
    }

    [Fact]
    public void Forward_acceleration_reads_positive_surge()
    {
        // accelerating at 2 m/s² from rest, level: f = a − g ⇒
        // (0, +9.80665, +2) once the low-pass has converged
        var d = new BodyDynamicsDeriver();
        double t = 0, v = 0;
        for (int i = 0; i < 90; i++) // 1.5 s ≈ 15 filter time constants
        {
            v += 2.0 * Dt;
            d.Sample(t, new Vector3(0, 0, (float)(0.5 * 2.0 * t * t)),
                     Quaternion.Identity, hasVelocity: true,
                     engineVelocity: new Vector3(0, 0, (float)v));
            t += Dt;
        }
        AssertNear(2.0, d.SpecificForce.Z, 0.1, "sf.z (surge)");
        AssertNear(G, d.SpecificForce.Y, 0.1, "sf.y");
    }

    [Fact]
    public void Yaw_sweep_gives_positive_yaw_rate()
    {
        // the DemoSource yaw construction: yaw(Ωt) — nose-right for +Ω
        const double omega = 1.5; // rad/s
        var d = new BodyDynamicsDeriver();
        double t = 0;
        for (int i = 0; i < 60; i++)
        {
            d.Sample(t, Vector3.Zero,
                     Quaternion.CreateFromYawPitchRoll((float)(omega * t), 0, 0));
            t += Dt;
        }
        AssertNear(omega, d.AngularVelocity.Y, 1e-3, "w.y");
        AssertNear(0, d.AngularVelocity.X, 1e-3, "w.x");
        AssertNear(0, d.AngularVelocity.Z, 1e-3, "w.z");
    }

    [Fact]
    public void Pitch_sweep_rate_equals_euler_pitch_rate()
    {
        // stored-math pitch sweep +w tilts the nose DOWN (EulerC4 pitch
        // = −θ), so the C-4-consistent wire rate is NEGATIVE — this is the
        // case that pins the (−x, +y, −z) component mapping
        const double w = 0.8; // rad/s of the sweep parameter
        var d = new BodyDynamicsDeriver();
        double t = 0;
        for (int i = 0; i < 60; i++)
        {
            d.Sample(t, Vector3.Zero,
                     Quaternion.CreateFromYawPitchRoll(0, (float)(w * t), 0));
            t += Dt;
        }
        AssertNear(-w, d.AngularVelocity.X, 1e-3, "w.x (C-4 pitch rate)");
        AssertNear(0, d.AngularVelocity.Y, 1e-3, "w.y");
        AssertNear(0, d.AngularVelocity.Z, 1e-3, "w.z");
    }

    [Fact]
    public void Roll_sweep_rate_equals_euler_roll_rate()
    {
        // stored-math roll sweep +w rolls LEFT-side-down (EulerC4 roll
        // = −θ), so the C-4-consistent wire rate is NEGATIVE
        const double w = 1.1;
        var d = new BodyDynamicsDeriver();
        double t = 0;
        for (int i = 0; i < 60; i++)
        {
            d.Sample(t, Vector3.Zero,
                     Quaternion.CreateFromYawPitchRoll(0, 0, (float)(w * t)));
            t += Dt;
        }
        AssertNear(-w, d.AngularVelocity.Z, 1e-3, "w.z (C-4 roll rate)");
        AssertNear(0, d.AngularVelocity.X, 1e-3, "w.x");
    }

    [Fact]
    public void Composite_rotation_rate_is_body_frame()
    {
        // body pitched 30° up, spinning about its OWN body x at stored rate
        // S: the wire rate must be body-frame (C-4 flip of the stored log).
        // Catches body/world delta-order mistakes, which pure-axis sweeps
        // cannot (their world and body deltas coincide).
        const double S = 2.0; // rad/s about body x (stored sense)
        var attitude = Quaternion.CreateFromYawPitchRoll(0f, -FloatPi / 6f, 0f);
        // integrate one step analytically: q1 = q0 ⊗ exp(½·S·dt·x̂)
        double half = 0.5 * S * Dt;
        var step = new Quaternion((float)System.Math.Sin(half), 0, 0,
                                  (float)System.Math.Cos(half));
        var d = new BodyDynamicsDeriver();
        d.Sample(0.0, Vector3.Zero, attitude);
        d.Sample(Dt, Vector3.Zero, Quaternion.Concatenate(attitude, step));
        // stored body-x rate = +S ⇒ C-4 wire rate = −S on x
        AssertNear(-S, d.AngularVelocity.X, 1e-2, "w.x (body-frame pitch rate)");
        AssertNear(0, d.AngularVelocity.Y, 1e-2, "w.y");
        AssertNear(0, d.AngularVelocity.Z, 1e-2, "w.z");
    }

    [Fact]
    public void World_yaw_of_banked_body_is_body_frame()
    {
        // body rolled 90° (right side down, C-4) then world-yawed nose-right
        // at Ω: physically the spin axis is world +y, which in the rolled
        // body frame is −x_body... via C-4 mapping the wire shows +yaw
        // NEGATED? No — compute: C-4 roll right-down = stored −z rotation.
        // rolled body: body x (right) now points world DOWN, body y (up)
        // points world RIGHT. World yaw Ω (stored +y): stored body-frame
        // components = rotate world (0,Ω,0) by q⁻¹ → (0, −Ω, 0)?? — assert
        // via the formula the deriver itself must equal: flip(stored).
        const double omega = 1.0;
        // attitude: stored roll −90° (right side down per C-4)
        var rolled = Quaternion.CreateFromYawPitchRoll(0, 0, -FloatPi / 2f);
        // one-step world-frame yaw: q1 = exp(½Ωdt ŷ_world) ⊗ q0
        double half = 0.5 * omega * Dt;
        var step = new Quaternion(0, (float)System.Math.Sin(half), 0,
                                  (float)System.Math.Cos(half));
        var d = new BodyDynamicsDeriver();
        d.Sample(0.0, Vector3.Zero, rolled);
        // world-frame pre-multiply: q1 = step ⊗ q0 — Concatenate(a, b)
        // returns b ⊗ a, hence the (rolled, step) operand order here
        d.Sample(Dt, Vector3.Zero, Quaternion.Concatenate(rolled, step));
        // expected: stored log of q0⁻¹q1 ≈ body-frame of world-ŷ spin
        // expressed in the rolled body axes, then the C-4 flip
        var qinv = Quaternion.Conjugate(rolled);
        var worldOmega = new Vector3(0, (float)omega, 0);
        var storedBody = Vector3.Transform(worldOmega, qinv);
        AssertNear(-storedBody.X, d.AngularVelocity.X, 1e-2, "w.x");
        AssertNear(storedBody.Y, d.AngularVelocity.Y, 1e-2, "w.y");
        AssertNear(-storedBody.Z, d.AngularVelocity.Z, 1e-2, "w.z");
    }

    [Fact]
    public void Teleport_resets_to_static_values()
    {
        var d = new BodyDynamicsDeriver();
        double t = 0;
        for (int i = 0; i < 30; i++)
        {
            d.Sample(t, new Vector3(0, 0, (float)t), Quaternion.Identity,
                     hasVelocity: true, engineVelocity: new Vector3(0, 0, 1f));
            t += Dt;
        }
        // teleport 10 m in one tick
        d.Sample(t, new Vector3(10f, 0f, 10f), Quaternion.Identity);
        AssertNear(G, d.SpecificForce.Y, 1e-4, "sf.y after teleport");
        AssertNear(0, d.SpecificForce.Z, 1e-4, "sf.z after teleport");
        AssertNear(0, d.AngularVelocity.Y, 1e-6, "w.y after teleport");
    }

    [Fact]
    public void Engine_velocity_beats_position_deltas()
    {
        // caller-measured velocity is trusted even when the position feed
        // is coarse (interpolated camera roots): position says "not moving"
        // but engine velocity accelerates — surge must track the engine
        var d = new BodyDynamicsDeriver();
        double t = 0, v = 0;
        for (int i = 0; i < 90; i++)
        {
            v += 2.0 * Dt;
            d.Sample(t, Vector3.Zero /* stale position */,
                     Quaternion.Identity, hasVelocity: true,
                     engineVelocity: new Vector3(0, 0, (float)v));
            t += Dt;
        }
        AssertNear(2.0, d.SpecificForce.Z, 0.1, "sf.z from engine velocity");
    }

    [Fact]
    public void Angular_velocity_from_delta_handles_zero_and_small()
    {
        Assert.Equal(default, BodyDynamicsDeriver.AngularVelocityFromDelta(
            Quaternion.Identity, Quaternion.Identity, Dt));
        // tiny rotation: near-linear, no NaN
        var tiny = Quaternion.CreateFromYawPitchRoll(1e-6f, 0, 0);
        var w = BodyDynamicsDeriver.AngularVelocityFromDelta(
            Quaternion.Identity, tiny, Dt);
        Assert.False(float.IsNaN(w.X) || float.IsNaN(w.Y) || float.IsNaN(w.Z));
        AssertNear(1e-6 / Dt, w.Y, 1e-6 / Dt * 0.01, "w.y tiny");
    }

    static float FloatPi => (float)System.Math.PI;
}
