using OpenWiimote.Core.Mapping;
using OpenWiimote.Platform.Linux;
using Xunit;

namespace OpenWiimote.Core.Tests;

/// <summary>
/// Checks the events that the Linux virtual pad writes, which are what a game reads from the
/// evdev node. A device that uinput creates starts with every button released and every axis
/// at zero, so the events of the first submit have to carry the whole state.
/// </summary>
public sealed class UinputGamepadTests
{
    // Values from linux/input-event-codes.h, spelled out so the test does not just repeat
    // whatever the production code happens to believe.
    private const ushort EvKey = 0x01;
    private const ushort EvAbs = 0x03;
    private const ushort BtnSouth = 0x130;
    private const ushort BtnSelect = 0x13A;
    private const ushort AbsX = 0x00;
    private const ushort AbsZ = 0x02;
    private const ushort AbsHat0Y = 0x11;

    [Fact]
    public void TheFirstState_ReportsTheButtonThatIsAlreadyHeld()
    {
        var builder = new GamepadEventBuilder();

        var events = builder.Advance(State(XboxGamepadButtons.A));

        Assert.Contains(events, e => e.Type == EvKey && e.Code == BtnSouth && e.Value == 1);
    }

    [Fact]
    public void TheFirstState_ReportsTheAxesThatAreNotCentred()
    {
        var builder = new GamepadEventBuilder();

        var events = builder.Advance(new XboxGamepadState(
            XboxGamepadButtons.None, 128, 0, 12000, -9000, 0, 0));

        Assert.Contains(events, e => e.Type == EvAbs && e.Code == AbsX && e.Value == 12000);
        Assert.Contains(events, e => e.Type == EvAbs && e.Code == AbsZ && e.Value == 128);

        // The right stick is centred, so it does not need an event.
        Assert.DoesNotContain(events, e => e.Type == EvAbs && e.Code == 0x03);
    }

    [Fact]
    public void TheFirstState_EndsWithASyncEvent()
    {
        var builder = new GamepadEventBuilder();

        var events = builder.Advance(State(XboxGamepadButtons.A));

        Assert.NotEmpty(events);
        var last = events[^1];
        Assert.Equal((ushort)0x00, last.Type);
        Assert.Equal((ushort)0x00, last.Code);
    }

    [Fact]
    public void TheSameStateAgain_WritesNothing()
    {
        var builder = new GamepadEventBuilder();
        var state = State(XboxGamepadButtons.B | XboxGamepadButtons.DPadDown);

        Assert.NotEmpty(builder.Advance(state));

        Assert.Empty(builder.Advance(state));
    }

    [Fact]
    public void ReleasingAButton_WritesAReleaseAndNothingElse()
    {
        var builder = new GamepadEventBuilder();
        builder.Advance(State(XboxGamepadButtons.Back));

        var events = builder.Advance(State(XboxGamepadButtons.None));

        var key = Assert.Single(events.Where(e => e.Type == EvKey).ToArray());
        Assert.Equal(BtnSelect, key.Code);
        Assert.Equal(0, key.Value);
    }

    [Fact]
    public void ADPadPress_MovesTheHatAxis()
    {
        var builder = new GamepadEventBuilder();

        var events = builder.Advance(State(XboxGamepadButtons.DPadUp));

        // Up is negative on ABS_HAT0Y, the way a keyboard style hat reports it.
        Assert.Contains(events, e => e.Type == EvAbs && e.Code == AbsHat0Y && e.Value == -1);
        Assert.DoesNotContain(events, e => e.Type == EvKey);
    }

    [Fact]
    public void AStateThatReturnsToNeutral_ReleasesEverythingItPressed()
    {
        var builder = new GamepadEventBuilder();
        builder.Advance(State(XboxGamepadButtons.A | XboxGamepadButtons.Start));

        var events = builder.Advance(XboxGamepadState.Neutral);

        var releases = events.Where(e => e.Type == EvKey).ToArray();
        Assert.Equal(2, releases.Length);
        Assert.All(releases, release => Assert.Equal(0, release.Value));
    }

    private static XboxGamepadState State(XboxGamepadButtons buttons)
    {
        return new XboxGamepadState(buttons, 0, 0, 0, 0, 0, 0);
    }
}
