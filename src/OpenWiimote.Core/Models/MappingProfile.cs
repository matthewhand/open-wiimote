using System.Text.Json.Serialization;

namespace OpenWiimote.Core.Models;

public sealed class MappingProfile
{
    /// <summary>
    /// Version 3 added the Nunchuk and Classic Controller sources of a mapping and the
    /// extension stick targets. Profiles without them are filled in when they are loaded.
    /// </summary>
    public const int CurrentVersion = 3;

    public string Name { get; set; } = "Default";
    public int Version { get; set; } = CurrentVersion;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> AssociatedGames { get; set; } = [];
    public string Author { get; set; } = "User";
    public string IconEmoji { get; set; } = "\U0001F3AE";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;
    public int UsageCount { get; set; }
    public bool IsFavorite { get; set; }
    public bool UseAccelerometer { get; set; }
    public AccelerometerMapping AccelMapping { get; set; } = new();
    public ExtensionStickMapping ExtensionStick { get; set; } = new();

    public ControlMapping A { get; set; } = new()
    {
        TargetName = "A", WiimoteButton = WiimoteButton.Two, ClassicButton = ClassicButton.A
    };
    public ControlMapping B { get; set; } = new()
    {
        TargetName = "B", WiimoteButton = WiimoteButton.One, ClassicButton = ClassicButton.B
    };
    public ControlMapping X { get; set; } = new()
    {
        TargetName = "X", WiimoteButton = WiimoteButton.A, ClassicButton = ClassicButton.X
    };
    public ControlMapping Y { get; set; } = new()
    {
        TargetName = "Y", WiimoteButton = WiimoteButton.B, ClassicButton = ClassicButton.Y
    };
    public ControlMapping LeftShoulder { get; set; } = new()
    {
        TargetName = "LB", ClassicButton = ClassicButton.L
    };
    public ControlMapping RightShoulder { get; set; } = new()
    {
        TargetName = "RB", NunchukButton = NunchukButton.C, ClassicButton = ClassicButton.R
    };
    public ControlMapping LeftTrigger { get; set; } = new()
    {
        TargetName = "LT", NunchukButton = NunchukButton.Z, ClassicButton = ClassicButton.ZL
    };
    public ControlMapping RightTrigger { get; set; } = new()
    {
        TargetName = "RT", ClassicButton = ClassicButton.ZR
    };
    public ControlMapping Start { get; set; } = new()
    {
        TargetName = "Start", WiimoteButton = WiimoteButton.Plus, ClassicButton = ClassicButton.Plus
    };
    public ControlMapping Back { get; set; } = new()
    {
        TargetName = "Back", WiimoteButton = WiimoteButton.Minus, ClassicButton = ClassicButton.Minus
    };
    public ControlMapping Guide { get; set; } = new()
    {
        TargetName = "Guide", WiimoteButton = WiimoteButton.Home, ClassicButton = ClassicButton.Home
    };
    public ControlMapping LeftThumb { get; set; } = new() { TargetName = "L-Click" };
    public ControlMapping RightThumb { get; set; } = new() { TargetName = "R-Click" };
    public ControlMapping DPadUp { get; set; } = new()
    {
        TargetName = "DPad Up", WiimoteButton = WiimoteButton.Right, ClassicButton = ClassicButton.Up
    };
    public ControlMapping DPadDown { get; set; } = new()
    {
        TargetName = "DPad Down", WiimoteButton = WiimoteButton.Left, ClassicButton = ClassicButton.Down
    };
    public ControlMapping DPadLeft { get; set; } = new()
    {
        TargetName = "DPad Left", WiimoteButton = WiimoteButton.Up, ClassicButton = ClassicButton.Left
    };
    public ControlMapping DPadRight { get; set; } = new()
    {
        TargetName = "DPad Right", WiimoteButton = WiimoteButton.Down, ClassicButton = ClassicButton.Right
    };

    [JsonIgnore]
    public IEnumerable<ControlMapping> AllMappings =>
    [
        A, B, X, Y, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, Start, Back,
        Guide, LeftThumb, RightThumb, DPadUp, DPadDown, DPadLeft, DPadRight
    ];

    public static MappingProfile CreateDefault()
    {
        return new MappingProfile
        {
            Name = "Default",
            Description = "Classic NES-style horizontal Wiimote mapping, with Nunchuk and Classic Controller",
            Tags = ["Default", "Classic"],
            Author = "OpenWiimote"
        };
    }

    public void RecordUsage()
    {
        LastUsedAt = DateTime.Now;
        UsageCount++;
    }
}
