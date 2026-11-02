using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Models;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class ProfileMapperTests
{
    [Fact]
    public void Map_DefaultProfile_UsesHorizontalLayout()
    {
        var profile = MappingProfile.CreateDefault();

        Assert.Equal(XboxGamepadButtons.A, ProfileMapper.Map(profile, State(WiimoteButton.Two)).Buttons);
        Assert.Equal(XboxGamepadButtons.B, ProfileMapper.Map(profile, State(WiimoteButton.One)).Buttons);
        Assert.Equal(XboxGamepadButtons.X, ProfileMapper.Map(profile, State(WiimoteButton.A)).Buttons);
        Assert.Equal(XboxGamepadButtons.Y, ProfileMapper.Map(profile, State(WiimoteButton.B)).Buttons);
        Assert.Equal(XboxGamepadButtons.Start, ProfileMapper.Map(profile, State(WiimoteButton.Plus)).Buttons);
        Assert.Equal(XboxGamepadButtons.Back, ProfileMapper.Map(profile, State(WiimoteButton.Minus)).Buttons);
        Assert.Equal(XboxGamepadButtons.Guide, ProfileMapper.Map(profile, State(WiimoteButton.Home)).Buttons);
    }

    [Fact]
    public void Map_DefaultProfile_RotatesDpadForHorizontalHold()
    {
        var profile = MappingProfile.CreateDefault();

        Assert.Equal(XboxGamepadButtons.DPadUp, ProfileMapper.Map(profile, State(WiimoteButton.Right)).Buttons);
        Assert.Equal(XboxGamepadButtons.DPadDown, ProfileMapper.Map(profile, State(WiimoteButton.Left)).Buttons);
        Assert.Equal(XboxGamepadButtons.DPadLeft, ProfileMapper.Map(profile, State(WiimoteButton.Up)).Buttons);
        Assert.Equal(XboxGamepadButtons.DPadRight, ProfileMapper.Map(profile, State(WiimoteButton.Down)).Buttons);
    }

    [Fact]
    public void Map_CombinesButtonsAndTriggers()
    {
        var profile = MappingProfile.CreateDefault();
        profile.LeftTrigger.WiimoteButton = WiimoteButton.B;
        profile.RightTrigger.WiimoteButton = WiimoteButton.A;

        var result = ProfileMapper.Map(profile, State(WiimoteButton.Two | WiimoteButton.A | WiimoteButton.B));

        Assert.Equal(XboxGamepadButtons.A | XboxGamepadButtons.X | XboxGamepadButtons.Y, result.Buttons);
        Assert.Equal(255, result.LeftTrigger);
        Assert.Equal(255, result.RightTrigger);
    }

    [Fact]
    public void Map_UnmappedControlsStayNeutral()
    {
        var result = ProfileMapper.Map(MappingProfile.CreateDefault(), State(WiimoteButton.None));

        Assert.Equal(XboxGamepadState.Neutral, result);
    }

    [Fact]
    public void Map_TiltSteering_FlatRemoteKeepsTheStickNeutral()
    {
        var profile = TiltProfile();

        var result = ProfileMapper.Map(profile, State(WiimoteButton.None) with { AccelZ = 1f });

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_TiltSteering_TiltToTheRightSteersRight()
    {
        var profile = TiltProfile();

        // Thirty degrees to the right, the remote lies flat otherwise.
        var state = State(WiimoteButton.None) with { AccelX = 0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(short.MaxValue, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_TiltSteering_TiltToTheLeftSteersLeft()
    {
        var profile = TiltProfile();

        var state = State(WiimoteButton.None) with { AccelX = -0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(short.MinValue + 1, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_TiltSteering_PitchingTheNoseDownMovesTheStickDown()
    {
        var profile = TiltProfile();

        var state = State(WiimoteButton.None) with { AccelY = -0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(short.MinValue + 1, result.LeftThumbY);
    }

    [Fact]
    public void Map_TiltSteering_PartialTiltIsProportional()
    {
        var profile = TiltProfile();

        // Half of the maximum tilt has to give a smaller deflection than the maximum.
        var state = State(WiimoteButton.None) with { AccelX = 0.259f, AccelZ = 0.966f };
        var result = ProfileMapper.Map(profile, state);

        Assert.InRange(result.LeftThumbX, 1, short.MaxValue - 1);
        Assert.True(result.LeftThumbX < short.MaxValue);
    }

    [Fact]
    public void Map_TiltSteering_AppliesDeadZone()
    {
        var profile = TiltProfile(deadZone: 0.2f);

        // A tilt of about three degrees is inside the dead zone.
        var state = State(WiimoteButton.None) with { AccelX = 0.05f, AccelZ = 0.999f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_TiltSteering_InvertsTheAxes()
    {
        var profile = TiltProfile(invertSteering: true, invertPitch: true);

        var state = State(WiimoteButton.None) with { AccelX = 0.5f, AccelY = -0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(short.MinValue + 1, result.LeftThumbX);
        Assert.Equal(short.MaxValue, result.LeftThumbY);
    }

    [Fact]
    public void Map_TiltSteering_StaysOffWhenTheMappingIsDisabled()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = false,
            TargetControl = "LeftStick (Steering + Pitch)"
        };

        var state = State(WiimoteButton.None) with { AccelX = 0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_SingleAxisMode_MapsTheTiltAngleOntoTheTarget()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "X",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f
        };

        // Fifteen degrees of roll are half of the maximum tilt.
        var state = State(WiimoteButton.None) with { AccelX = 0.259f, AccelZ = 0.966f };
        var result = ProfileMapper.Map(profile, state);

        Assert.True(result.RightThumbX > 0);
        Assert.True(result.RightThumbX < short.MaxValue);
        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_SingleAxisMode_UsesTheTotalTiltForTheZAxis()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "Z",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f
        };

        // Fifteen degrees of roll are also a tilt of fifteen degrees.
        var state = State(WiimoteButton.None) with { AccelX = 0.259f, AccelZ = 0.966f };
        var result = ProfileMapper.Map(profile, state);

        Assert.True(result.RightThumbX > 0);
        Assert.True(result.RightThumbX < short.MaxValue);
    }

    [Fact]
    public void Map_SingleAxisMode_StaysNeutralWhileTheRemoteLiesFlat()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "Z",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f
        };

        var state = State(WiimoteButton.None) with { AccelX = 0f, AccelY = 0f, AccelZ = 1f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.RightThumbX);
    }

    [Fact]
    public void Map_SingleAxisMode_WithDeadZone_IgnoresSmallTilt()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "X",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0.2f
        };

        // Small tilt that should be within the dead zone
        var state = State(WiimoteButton.None) with { AccelX = 0.05f, AccelZ = 0.999f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.RightThumbX);
    }

    [Fact]
    public void Map_SingleAxisMode_WithSensitivity_ScalesOutput()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "X",
            MaxTiltDegrees = 30f,
            Sensitivity = 0.5f,
            DeadZone = 0f
        };

        var state = State(WiimoteButton.None) with { AccelX = 0.259f, AccelZ = 0.966f };
        var result = ProfileMapper.Map(profile, state);

        // With 50% sensitivity, the output should be half of maximum
        Assert.True(result.RightThumbX > 0);
        Assert.True(result.RightThumbX < short.MaxValue / 2);
    }

    [Fact]
    public void Map_SingleAxisMode_InvertedAxis_InvertsOutput()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "X",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f,
            InvertAxis = true
        };

        var state = State(WiimoteButton.None) with { AccelX = 0.259f, AccelZ = 0.966f };
        var result = ProfileMapper.Map(profile, state);

        // Inverted axis should give negative output
        Assert.True(result.RightThumbX < 0);
    }

    [Fact]
    public void Map_SingleAxisMode_ZAxis_ReadsTheTotalTiltAsAFullDeflection()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "Z",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f
        };

        // Thirty degrees away from the horizontal: the Z axis carries no direction, so the
        // angle is the same whichever way the remote is tilted and it drives the full range.
        var state = State(WiimoteButton.None) with { AccelX = 0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(short.MaxValue, result.RightThumbX);
    }

    [Fact]
    public void Map_SingleAxisMode_ZAxis_Inverted_ShouldReverseOutput()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "Z",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f,
            InvertAxis = true
        };

        // The same thirty degree tilt has to come out as the opposite end of the axis, which
        // a flat remote (no tilt at all) would not show.
        var state = State(WiimoteButton.None) with { AccelX = 0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(short.MinValue + 1, result.RightThumbX);
    }

    [Fact]
    public void Map_CombinedSteeringAndPitch_WithInvertedPitch()
    {
        var profile = TiltProfile(invertPitch: true);

        // Pitching nose down with inverted pitch should move stick up
        var state = State(WiimoteButton.None) with { AccelY = -0.5f, AccelZ = 0.866f };
        var result = ProfileMapper.Map(profile, state);

        // Thirty degrees is the full deflection of this profile, so the inverted pitch has to
        // reach the positive end of the axis and leave the steering alone.
        Assert.Equal(short.MaxValue, result.LeftThumbY);
        Assert.Equal(0, result.LeftThumbX);
    }

    

    [Fact]
    public void Map_SingleAxisMode_WithMaxTilt_ScalesAppropriately()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "RightStickX",
            TiltAxis = "X",
            MaxTiltDegrees = 45f,
            Sensitivity = 1f,
            DeadZone = 0f
        };

        // Full 45 degrees tilt should map to max output
        var state = State(WiimoteButton.None) with { AccelX = 0.707f, AccelZ = 0.707f }; // 45 degrees
        var result = ProfileMapper.Map(profile, state);

        Assert.True(result.RightThumbX > 0);
        Assert.True(result.RightThumbX <= short.MaxValue);
    }

    [Fact]
    public void Map_DefaultProfile_WithoutAccelerometer_ReturnsNeutral()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = false;

        var result = ProfileMapper.Map(profile, State(WiimoteButton.None) with { AccelZ = 1f });

        Assert.Equal(XboxGamepadState.Neutral, result);
    }

    [Fact]
    public void Map_Profile_WithNoAccelMapping_ReturnsNeutral()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping();

        var result = ProfileMapper.Map(profile, State(WiimoteButton.None) with { AccelZ = 1f });

        Assert.Equal(XboxGamepadState.Neutral, result);
    }

    [Fact]
    public void Map_NunchukButtonsDriveTheMappedControls()
    {
        var profile = MappingProfile.CreateDefault();
        var state = State(WiimoteButton.None) with
        {
            Nunchuk = new NunchukState { ButtonC = true, ButtonZ = true }
        };

        var result = ProfileMapper.Map(profile, state);

        // Z is the trigger of the left hand, C sits next to the right shoulder.
        Assert.Equal(255, result.LeftTrigger);
        Assert.Equal(XboxGamepadButtons.RightShoulder, result.Buttons);
    }

    [Fact]
    public void Map_ClassicControllerButtonsDriveTheMappedControls()
    {
        var profile = MappingProfile.CreateDefault();
        var state = State(WiimoteButton.None) with
        {
            ClassicController = new ClassicControllerState
            {
                ButtonA = true,
                ButtonB = true,
                ButtonZL = true,
                ButtonZR = true,
                DPadUp = true
            }
        };

        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(
            XboxGamepadButtons.A | XboxGamepadButtons.B | XboxGamepadButtons.DPadUp,
            result.Buttons);
        Assert.Equal(255, result.LeftTrigger);
        Assert.Equal(255, result.RightTrigger);
    }

    [Fact]
    public void Map_ClassicControllerSticksUseTheirTargets()
    {
        var profile = MappingProfile.CreateDefault();
        profile.ExtensionStick.DeadZone = 0f;
        var state = State(WiimoteButton.None) with
        {
            ClassicController = new ClassicControllerState
            {
                LeftStickX = 0.5f,
                LeftStickY = -1f,
                RightStickX = 1f
            }
        };

        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(16384, result.LeftThumbX);
        Assert.Equal(short.MinValue + 1, result.LeftThumbY);
        Assert.Equal(short.MaxValue, result.RightThumbX);
    }

    [Fact]
    public void Map_NunchukStickTakesOverTheAxesOfItsTarget()
    {
        var profile = TiltProfile();
        profile.ExtensionStick.DeadZone = 0f;

        // The remote is rolled to the right, but the centred Nunchuk stick wins.
        var state = State(WiimoteButton.None) with
        {
            AccelX = 0.5f,
            AccelZ = 0.866f,
            Nunchuk = new NunchukState()
        };

        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void Map_NunchukStickHonoursTheDeadZone()
    {
        var profile = MappingProfile.CreateDefault();
        profile.ExtensionStick.DeadZone = 0.2f;
        var state = State(WiimoteButton.None) with
        {
            Nunchuk = new NunchukState { StickX = 0.1f, StickY = 0.9f }
        };

        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.True(result.LeftThumbY > 0);
    }

    [Fact]
    public void Map_UnmappedExtensionStickStaysNeutral()
    {
        var profile = MappingProfile.CreateDefault();
        profile.ExtensionStick.NunchukTarget = ExtensionStickMapping.UnmappedTarget;
        profile.ExtensionStick.DeadZone = 0f;
        var state = State(WiimoteButton.None) with
        {
            Nunchuk = new NunchukState { StickX = 1f, StickY = 1f }
        };

        var result = ProfileMapper.Map(profile, state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    private static MappingProfile TiltProfile(
        float deadZone = 0f,
        float sensitivity = 1f,
        bool invertSteering = false,
        bool invertPitch = false)
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "LeftStick (Steering + Pitch)",
            MaxTiltDegrees = 30f,
            Sensitivity = sensitivity,
            DeadZone = deadZone,
            InvertSteering = invertSteering,
            InvertPitch = invertPitch
        };

        return profile;
    }

    private static WiimoteState State(WiimoteButton buttons)
    {
        return new WiimoteState { Buttons = buttons };
    }
}