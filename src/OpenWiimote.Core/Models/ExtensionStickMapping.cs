namespace OpenWiimote.Core.Models;

/// <summary>
/// Maps the sticks of a Nunchuk or a Classic Controller onto the virtual controller.
/// </summary>
/// <remarks>
/// Each stick is mapped as a whole, so both axes of one source always end up on the same
/// target. A stick that is plugged in overwrites the axes of its target, while tilt
/// steering keeps working on the axes it does not target.
/// </remarks>
public sealed class ExtensionStickMapping
{
    public const string LeftStickTarget = "LeftStick";
    public const string RightStickTarget = "RightStick";
    public const string UnmappedTarget = "Unmapped";

    /// <summary>The targets that a stick can be mapped to.</summary>
    public static readonly string[] Targets = [LeftStickTarget, RightStickTarget, UnmappedTarget];

    public bool Enabled { get; set; } = true;

    public string NunchukTarget { get; set; } = LeftStickTarget;

    public string ClassicLeftTarget { get; set; } = LeftStickTarget;

    public string ClassicRightTarget { get; set; } = RightStickTarget;

    /// <summary>The deflection that is ignored around the centre of every axis.</summary>
    public float DeadZone { get; set; } = 0.15f;

    /// <summary>Scales the deflection that is left after the dead zone.</summary>
    public float Sensitivity { get; set; } = 1f;

    /// <summary>Flips the Y axis, for players who prefer the inverted direction.</summary>
    public bool InvertY { get; set; }
}
