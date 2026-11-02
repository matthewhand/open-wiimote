using System.Runtime.InteropServices;
using OpenWiimote.Core.Mapping;

namespace OpenWiimote.Platform.Linux;

/// <summary>
/// One event of the Linux input protocol, laid out like the <c>struct input_event</c> that
/// <c>/dev/uinput</c> reads: the two time fields that the kernel fills in, then the type, the
/// code and the value.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct InputEvent
{
    public readonly long Seconds;
    public readonly long Microseconds;
    public readonly ushort Type;
    public readonly ushort Code;
    public readonly int Value;

    private InputEvent(ushort type, ushort code, int value)
    {
        Seconds = 0;
        Microseconds = 0;
        Type = type;
        Code = code;
        Value = value;
    }

    /// <summary>One of the buttons, pressed when <paramref name="pressed"/> is set.</summary>
    public static InputEvent Key(ushort code, bool pressed) => new(LinuxInput.EvKey, code, pressed ? 1 : 0);

    /// <summary>One stick, trigger or d-pad axis.</summary>
    public static InputEvent Abs(ushort code, int value) => new(LinuxInput.EvAbs, code, value);

    /// <summary>Hands the events before it to the client of the device.</summary>
    public static InputEvent Sync() => new(LinuxInput.EvSyn, LinuxInput.SynReport, 0);
}

/// <summary>
/// Turns the states of the mapped remote into the events that move the virtual pad from one
/// state to the next.
/// </summary>
/// <remarks>
/// A device that uinput creates reports every button released and every axis at zero, and a
/// client of evdev learns about a change only from the events that are written. The builder
/// therefore starts at <see cref="XboxGamepadState.Neutral"/> instead of at the first state it
/// is handed: a button that is already held while the pad is created has to arrive as a press,
/// because a press that is never written stays invisible until the player lets go and presses
/// the button again.
/// </remarks>
internal sealed class GamepadEventBuilder
{
    internal static readonly (XboxGamepadButtons Flag, ushort Code)[] KeyMap =
    [
        (XboxGamepadButtons.A, LinuxInput.BtnSouth),
        (XboxGamepadButtons.B, LinuxInput.BtnEast),
        (XboxGamepadButtons.X, LinuxInput.BtnWest),
        (XboxGamepadButtons.Y, LinuxInput.BtnNorth),
        (XboxGamepadButtons.LeftShoulder, LinuxInput.BtnTL),
        (XboxGamepadButtons.RightShoulder, LinuxInput.BtnTR),
        (XboxGamepadButtons.Back, LinuxInput.BtnSelect),
        (XboxGamepadButtons.Start, LinuxInput.BtnStart),
        (XboxGamepadButtons.Guide, LinuxInput.BtnMode),
        (XboxGamepadButtons.LeftThumb, LinuxInput.BtnThumbL),
        (XboxGamepadButtons.RightThumb, LinuxInput.BtnThumbR)
    ];

    private XboxGamepadState _last = XboxGamepadState.Neutral;

    /// <summary>
    /// The events that take the device from the state of the previous call to
    /// <paramref name="next"/>, or an empty array when nothing changed. The result always ends
    /// with a synchronisation event, which is what makes the events before it visible.
    /// </summary>
    public InputEvent[] Advance(XboxGamepadState next)
    {
        if (next == _last)
        {
            return [];
        }

        var events = new List<InputEvent>(24);

        foreach (var (flag, code) in KeyMap)
        {
            var wasPressed = (_last.Buttons & flag) != 0;
            var isPressed = (next.Buttons & flag) != 0;
            if (wasPressed != isPressed)
            {
                // Pressing is one, releasing is zero; any other value repeats the event.
                events.Add(InputEvent.Key(code, isPressed));
            }
        }

        AddAxis(events, LinuxInput.AbsX, _last.LeftThumbX, next.LeftThumbX);
        AddAxis(events, LinuxInput.AbsY, _last.LeftThumbY, next.LeftThumbY);
        AddAxis(events, LinuxInput.AbsRx, _last.RightThumbX, next.RightThumbX);
        AddAxis(events, LinuxInput.AbsRy, _last.RightThumbY, next.RightThumbY);
        AddAxis(events, LinuxInput.AbsZ, _last.LeftTrigger, next.LeftTrigger);
        AddAxis(events, LinuxInput.AbsRz, _last.RightTrigger, next.RightTrigger);

        // The d-pad is a pair of axes rather than keys, so a press moves the hat from the
        // centre to -1 or 1 instead of writing a key event.
        AddAxis(events, LinuxInput.AbsHat0X, HatX(_last.Buttons), HatX(next.Buttons));
        AddAxis(events, LinuxInput.AbsHat0Y, HatY(_last.Buttons), HatY(next.Buttons));

        events.Add(InputEvent.Sync());
        _last = next;
        return events.ToArray();
    }

    private static int HatX(XboxGamepadButtons buttons)
    {
        var value = 0;
        if ((buttons & XboxGamepadButtons.DPadLeft) != 0)
        {
            value--;
        }

        if ((buttons & XboxGamepadButtons.DPadRight) != 0)
        {
            value++;
        }

        return value;
    }

    private static int HatY(XboxGamepadButtons buttons)
    {
        var value = 0;
        if ((buttons & XboxGamepadButtons.DPadUp) != 0)
        {
            value--;
        }

        if ((buttons & XboxGamepadButtons.DPadDown) != 0)
        {
            value++;
        }

        return value;
    }

    private static void AddAxis(List<InputEvent> events, ushort code, int previous, int current)
    {
        if (previous != current)
        {
            events.Add(InputEvent.Abs(code, current));
        }
    }
}
