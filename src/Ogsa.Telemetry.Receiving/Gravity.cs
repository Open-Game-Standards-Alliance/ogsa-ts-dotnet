#nullable enable
using System.Numerics;

namespace Ogsa.Telemetry
{

/// <summary>
/// Receiver-side gravity resolution (standard CONVENTIONS, Gravity): a
/// world-frame vector in m/s², pointing the way gravity pulls. An object's
/// effective gravity in a motion frame is its <c>localGravity.vector</c>
/// when selected, else the session gravity from discovery — the
/// <c>gravityVector</c> when selected, else (0, −gravity, 0). Unknown
/// discriminants read as the default variant, per the encoding rules.
/// </summary>
public static class Gravity
{
    /// <summary>Session gravity declared in discovery.</summary>
    public static Vector3 Session(CapnpGen.Environment.READER environment)
    {
        if (environment.which == CapnpGen.Environment.WHICH.GravityVector)
        {
            var g = environment.GravityVector;
            return new Vector3(g.X, g.Y, g.Z);
        }
        return new Vector3(0f, -environment.Gravity, 0f);
    }

    /// <summary>Effective gravity of one object in one motion frame, given
    /// the session gravity from the latest discovery.</summary>
    public static Vector3 Effective(CapnpGen.MotionObject.READER obj, Vector3 sessionGravity)
    {
        var local = obj.LocalGravity;
        if (local.which == CapnpGen.MotionObject.localGravity.WHICH.Vector)
        {
            var g = local.Vector;
            return new Vector3(g.X, g.Y, g.Z);
        }
        return sessionGravity;
    }

    /// <summary>Specific force (body frame) from world coordinate
    /// acceleration: f = q⁻¹ · (a − g) — for senders that stream
    /// <c>kinematics</c> rather than <c>bodyDynamics</c>.</summary>
    public static Vector3 SpecificForce(Vector3 worldAcceleration, Quaternion orientation,
                                        Vector3 gravity)
        => Vector3.Transform(worldAcceleration - gravity, Quaternion.Conjugate(orientation));

    /// <summary>Body-frame coordinate acceleration (gravity reaction
    /// removed) from specific force: q⁻¹ · a = f + q⁻¹ · g.</summary>
    public static Vector3 BodyAcceleration(Vector3 specificForce, Quaternion orientation,
                                           Vector3 gravity)
        => specificForce + Vector3.Transform(gravity, Quaternion.Conjugate(orientation));
}
}
