namespace OpenWiimote.Core.Models;

public sealed class AccelerometerMapping
{
    /// <summary>
    /// Enables the accelerometer mapping. The remote is expected to be held horizontally
    /// like a NES controller, with the buttons facing up.
    /// </summary>
    public bool TiltSteeringEnabled { get; set; }

    /// <summary>
    /// Enables gyro-based mapping using the Wii Motion Plus.
    /// </summary>
    public bool GyroEnabled { get; set; }

    /// <summary>
    /// The control the accelerometer drives. "LeftStick (Steering + Pitch)" uses the roll
    /// and the pitch of the remote, every other value maps a single tilt axis onto one
    /// target.
    /// </summary>
    public string TargetControl { get; set; } = "LeftStick (Steering + Pitch)";

    /// <summary>
    /// The axis that the single axis modes read: X is the roll, Y the pitch and Z the
    /// angle between the face of the remote and the horizontal.
    /// </summary>
    public string TiltAxis { get; set; } = "X";

    /// <summary>
    /// The gyro axis to use: X (pitch), Y (roll), or Z (yaw).
    /// </summary>
    public string GyroAxis { get; set; } = "Z";

    /// <summary>
    /// The tilt in degrees that produces the full deflection of an axis. Twenty five
    /// degrees is about as much as a wrist can turn the remote while playing.
    /// </summary>
    public float MaxTiltDegrees { get; set; } = 25f;

    /// <summary>
    /// Maximum gyro rate in degrees per second for full deflection.
    /// </summary>
    public float MaxGyroDegreesPerSecond { get; set; } = 200f;

    /// <summary>
    /// Multiplies the deflection after the dead zone, so a value above one saturates.
    /// </summary>
    public float Sensitivity { get; set; } = 1f;

    /// <summary>
    /// The fraction of the full deflection that is ignored, so a remote that is not held
    /// perfectly still does not drift.
    /// </summary>
    public float DeadZone { get; set; } = 0.05f;

    /// <summary>Gyro dead zone in degrees per second.</summary>
    public float GyroDeadZone { get; set; } = 10f;

    /// <summary>Gyro sensitivity multiplier.</summary>
    public float GyroSensitivity { get; set; } = 1f;

    /// <summary>Inverts the single axis modes.</summary>
    public bool InvertAxis { get; set; }

    /// <summary>Inverts the steering axis of the combined tilt mode.</summary>
    public bool InvertSteering { get; set; }

    /// <summary>Inverts the pitch axis of the combined tilt mode.</summary>
    public bool InvertPitch { get; set; }

    /// <summary>Inverts gyro input.</summary>
    public bool InvertGyro { get; set; }
}
