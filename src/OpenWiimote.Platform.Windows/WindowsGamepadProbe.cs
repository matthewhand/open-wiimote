using System.Runtime.InteropServices;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Virtual;
namespace OpenWiimote.Platform.Windows;

/// <summary>Explicit local diagnostics only: briefly exercise two virtual pads through XInput.</summary>
public static class WindowsGamepadProbe
{
    public static bool Check(out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows()) { error = "Windows only."; return false; }
        var before = Read();
        using var provider = new ViGEmGamepadProvider();
        IVirtualGamepad? first = null, second = null;
        try
        {
            if (before.Count > 2) throw new IOException("Two free XInput controller slots are required for this check.");
            if (!provider.TryCreate(out first, out error) || first is null) return false;
            if (!provider.TryCreate(out second, out error) || second is null) return false;
            if (!Wait(() => Read().Keys.Except(before.Keys).Count() == 2)) throw new IOException("Windows did not expose two new XInput controllers.");
            var slots = Read().Keys.Except(before.Keys).ToArray();
            first.Submit(XboxGamepadState.Neutral with { Buttons = XboxGamepadButtons.A });
            second.Submit(XboxGamepadState.Neutral with { Buttons = XboxGamepadButtons.B });
            if (!Wait(() => Read().Where(p => slots.Contains(p.Key)).Select(p => p.Value).Order().SequenceEqual(new ushort[] { 0x1000, 0x2000 })))
                throw new IOException("The two controllers did not report independent A/B button states.");
            first.Submit(XboxGamepadState.Neutral); second.Submit(XboxGamepadState.Neutral);
            if (!Wait(() => { var states = Read().Where(p => slots.Contains(p.Key)).ToArray(); return states.Length == 2 && states.All(p => p.Value == 0); })) throw new IOException("Controller buttons did not return to neutral.");
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
        finally
        {
            foreach (var pad in new[] { first, second })
            {
                try { pad?.Submit(XboxGamepadState.Neutral); } catch { }
                try { pad?.Dispose(); } catch { }
            }
        }
    }
    private static bool Wait(Func<bool> condition)
    {
        for (var n = 0; n < 30; n++) { if (condition()) return true; Thread.Sleep(100); }
        return false;
    }
    private static Dictionary<uint, ushort> Read()
    {
        var result = new Dictionary<uint, ushort>();
        for (uint slot = 0; slot < 4; slot++) if (XInputGetState(slot, out var state) == 0) result[slot] = state.Gamepad.Buttons;
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Gamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }
    [StructLayout(LayoutKind.Sequential)] private struct State { public uint Packet; public Gamepad Gamepad; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint slot, out State state);
}
