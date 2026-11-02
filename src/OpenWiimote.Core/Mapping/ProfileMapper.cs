using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Mapping;

public static class ProfileMapper
{
    public static XboxGamepadState Map(MappingProfile profile, WiimoteState state)
    {
        var buttons = XboxGamepadButtons.None;
        byte leftTrigger = 0;
        byte rightTrigger = 0;

        AddIfPressed(ref buttons, profile.A, state, XboxGamepadButtons.A);
        AddIfPressed(ref buttons, profile.B, state, XboxGamepadButtons.B);
        AddIfPressed(ref buttons, profile.X, state, XboxGamepadButtons.X);
        AddIfPressed(ref buttons, profile.Y, state, XboxGamepadButtons.Y);
        AddIfPressed(ref buttons, profile.LeftShoulder, state, XboxGamepadButtons.LeftShoulder);
        AddIfPressed(ref buttons, profile.RightShoulder, state, XboxGamepadButtons.RightShoulder);
        AddIfPressed(ref buttons, profile.Start, state, XboxGamepadButtons.Start);
        AddIfPressed(ref buttons, profile.Back, state, XboxGamepadButtons.Back);
        AddIfPressed(ref buttons, profile.Guide, state, XboxGamepadButtons.Guide);
        AddIfPressed(ref buttons, profile.LeftThumb, state, XboxGamepadButtons.LeftThumb);
        AddIfPressed(ref buttons, profile.RightThumb, state, XboxGamepadButtons.RightThumb);
        AddIfPressed(ref buttons, profile.DPadUp, state, XboxGamepadButtons.DPadUp);
        AddIfPressed(ref buttons, profile.DPadDown, state, XboxGamepadButtons.DPadDown);
        AddIfPressed(ref buttons, profile.DPadLeft, state, XboxGamepadButtons.DPadLeft);
        AddIfPressed(ref buttons, profile.DPadRight, state, XboxGamepadButtons.DPadRight);

        if (IsPressed(profile.LeftTrigger, state))
        {
            leftTrigger = byte.MaxValue;
        }

        if (IsPressed(profile.RightTrigger, state))
        {
            rightTrigger = byte.MaxValue;
        }

        short leftX = 0;
        short leftY = 0;
        short rightX = 0;
        short rightY = 0;

        if (profile.UseAccelerometer && profile.AccelMapping is { TiltSteeringEnabled: true } accel)
        {
            ApplyAccelerometer(
                accel,
                state,
                ref leftX,
                ref leftY,
                ref rightX,
                ref rightY,
                ref leftTrigger,
                ref rightTrigger);
        }

        // An extension stick takes over the axes of its target, so a Nunchuk or Classic
        // Controller stick keeps working while tilt steering drives the other axes.
        ApplyExtensionSticks(
            profile.ExtensionStick,
            state,
            ref leftX,
            ref leftY,
            ref rightX,
            ref rightY);

        return new XboxGamepadState(
            buttons,
            leftTrigger,
            rightTrigger,
            leftX,
            leftY,
            rightX,
            rightY);
    }

    private static void ApplyAccelerometer(
        AccelerometerMapping mapping,
        WiimoteState state,
        ref short leftX,
        ref short leftY,
        ref short rightX,
        ref short rightY,
        ref byte leftTrigger,
        ref byte rightTrigger)
    {
        var target = mapping.TargetControl ?? string.Empty;
        var sensitivity = mapping.Sensitivity <= 0 ? 1f : mapping.Sensitivity;
        var deadZone = Math.Clamp(mapping.DeadZone, 0f, 0.95f);
        var maxTilt = Math.Clamp(mapping.MaxTiltDegrees <= 0 ? 25f : mapping.MaxTiltDegrees, 5f, 80f);

        if (target.StartsWith("LeftStick", StringComparison.OrdinalIgnoreCase) &&
            target.Contains("Steering", StringComparison.OrdinalIgnoreCase))
        {
            // The remote is held horizontally like a NES controller. Rolling it to the
            // left or right steers, pitching it forward or back pitches.
            var steering = Shape((float)state.TiltXDegrees / maxTilt, deadZone, sensitivity, mapping.InvertSteering);
            var pitch = Shape((float)state.TiltYDegrees / maxTilt, deadZone, sensitivity, mapping.InvertPitch);

            leftX = ToAxis(steering);
            leftY = ToAxis(pitch);
            return;
        }

        // Single axis mode: the target names the output, the tilt axis names the input.
        // Every source is an angle, so the maximum tilt scales them the same way as the
        // combined steering mode. The Z axis does not have a direction, it reports how
        // far the remote is tilted away from the horizontal.
        var axisName = (mapping.TiltAxis ?? string.Empty).Trim();
        var angle = axisName.StartsWith("Y", StringComparison.OrdinalIgnoreCase)
            ? state.TiltYDegrees
            : axisName.StartsWith("Z", StringComparison.OrdinalIgnoreCase)
                ? state.TiltDegrees
                : state.TiltXDegrees;

        var value = Shape((float)angle / maxTilt, deadZone, sensitivity, mapping.InvertAxis);

        if (target.StartsWith("LeftStickX", StringComparison.OrdinalIgnoreCase))
        {
            leftX = ToAxis(value);
        }
        else if (target.StartsWith("LeftStickY", StringComparison.OrdinalIgnoreCase))
        {
            leftY = ToAxis(value);
        }
        else if (target.StartsWith("RightStickX", StringComparison.OrdinalIgnoreCase))
        {
            rightX = ToAxis(value);
        }
        else if (target.StartsWith("RightStickY", StringComparison.OrdinalIgnoreCase))
        {
            rightY = ToAxis(value);
        }
        else if (target.StartsWith("LeftTrigger", StringComparison.OrdinalIgnoreCase))
        {
            leftTrigger = ToTrigger(value);
        }
        else if (target.StartsWith("RightTrigger", StringComparison.OrdinalIgnoreCase))
        {
            rightTrigger = ToTrigger(value);
        }
    }

    private static void ApplyExtensionSticks(
        ExtensionStickMapping? mapping,
        WiimoteState state,
        ref short leftX,
        ref short leftY,
        ref short rightX,
        ref short rightY)
    {
        if (mapping is not { Enabled: true })
        {
            return;
        }

        var deadZone = Math.Clamp(mapping.DeadZone, 0f, 0.95f);
        var sensitivity = mapping.Sensitivity <= 0 ? 1f : mapping.Sensitivity;

        if (state.Nunchuk is { } nunchuk)
        {
            ApplyStick(
                mapping.NunchukTarget,
                nunchuk.StickX,
                nunchuk.StickY,
                deadZone,
                sensitivity,
                mapping.InvertY,
                ref leftX,
                ref leftY,
                ref rightX,
                ref rightY);
        }

        if (state.ClassicController is { } classic)
        {
            ApplyStick(
                mapping.ClassicLeftTarget,
                classic.LeftStickX,
                classic.LeftStickY,
                deadZone,
                sensitivity,
                mapping.InvertY,
                ref leftX,
                ref leftY,
                ref rightX,
                ref rightY);

            ApplyStick(
                mapping.ClassicRightTarget,
                classic.RightStickX,
                classic.RightStickY,
                deadZone,
                sensitivity,
                mapping.InvertY,
                ref leftX,
                ref leftY,
                ref rightX,
                ref rightY);
        }
    }

    private static void ApplyStick(
        string? target,
        float x,
        float y,
        float deadZone,
        float sensitivity,
        bool invertY,
        ref short leftX,
        ref short leftY,
        ref short rightX,
        ref short rightY)
    {
        if (string.Equals(target, ExtensionStickMapping.LeftStickTarget, StringComparison.OrdinalIgnoreCase))
        {
            leftX = ToStickAxis(x, deadZone, sensitivity);
            leftY = ToStickAxis(y, deadZone, sensitivity, invertY);
        }
        else if (string.Equals(target, ExtensionStickMapping.RightStickTarget, StringComparison.OrdinalIgnoreCase))
        {
            rightX = ToStickAxis(x, deadZone, sensitivity);
            rightY = ToStickAxis(y, deadZone, sensitivity, invertY);
        }
    }

    private static short ToStickAxis(float value, float deadZone, float sensitivity, bool invert = false)
    {
        value = Math.Clamp(value, -1f, 1f);
        if (Math.Abs(value) <= deadZone)
        {
            return 0;
        }

        value = MathF.Sign(value) * ((MathF.Abs(value) - deadZone) / (1f - deadZone));
        value = Math.Clamp(value * sensitivity, -1f, 1f);
        return ToAxis(invert ? -value : value);
    }

    private static void AddIfPressed(
        ref XboxGamepadButtons target,
        ControlMapping? mapping,
        WiimoteState state,
        XboxGamepadButtons button)
    {
        if (IsPressed(mapping, state))
        {
            target |= button;
        }
    }

    private static bool IsPressed(ControlMapping? mapping, WiimoteState state)
    {
        if (mapping is null)
        {
            return false;
        }

        if (mapping.WiimoteButton is { } wiimoteButton && (state.Buttons & wiimoteButton) != 0)
        {
            return true;
        }

        return IsPressed(mapping.NunchukButton, state.Nunchuk)
            || IsPressed(mapping.ClassicButton, state.ClassicController);
    }

    private static bool IsPressed(NunchukButton? button, NunchukState? state)
    {
        if (state is null)
        {
            return false;
        }

        return button switch
        {
            NunchukButton.C => state.ButtonC,
            NunchukButton.Z => state.ButtonZ,
            _ => false
        };
    }

    private static bool IsPressed(ClassicButton? button, ClassicControllerState? state)
    {
        if (state is null)
        {
            return false;
        }

        return button switch
        {
            ClassicButton.A => state.ButtonA,
            ClassicButton.B => state.ButtonB,
            ClassicButton.X => state.ButtonX,
            ClassicButton.Y => state.ButtonY,
            ClassicButton.L => state.ButtonL,
            ClassicButton.R => state.ButtonR,
            ClassicButton.ZL => state.ButtonZL,
            ClassicButton.ZR => state.ButtonZR,
            ClassicButton.Plus => state.ButtonPlus,
            ClassicButton.Minus => state.ButtonMinus,
            ClassicButton.Home => state.ButtonHome,
            ClassicButton.Up => state.DPadUp,
            ClassicButton.Down => state.DPadDown,
            ClassicButton.Left => state.DPadLeft,
            ClassicButton.Right => state.DPadRight,
            _ => false
        };
    }

    private static float Shape(float value, float deadZone, float sensitivity, bool invert)
    {
        value = Math.Clamp(value, -1f, 1f);
        if (Math.Abs(value) < deadZone)
        {
            return 0f;
        }

        // Scale the value that is left after the dead zone back to the full range so the
        // dead zone does not leave a hole in the response.
        value = MathF.Sign(value) * ((MathF.Abs(value) - deadZone) / (1f - deadZone));
        value = Math.Clamp(value * sensitivity, -1f, 1f);
        value = MathF.Sign(value) * MathF.Pow(MathF.Abs(value), 1.8f);
        return invert ? -value : value;
    }

    private static short ToAxis(float value)
    {
        return (short)Math.Clamp(MathF.Round(value * 32767f), short.MinValue + 1, short.MaxValue);
    }

    private static byte ToTrigger(float value)
    {
        return (byte)Math.Clamp(MathF.Round(Math.Max(0f, value) * 255f), 0f, 255f);
    }
}
