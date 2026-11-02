using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class ExtensionDecoderTests
{
    [Fact]
    public void DetectType_ReadsTheNunchukIdentifier()
    {
        Assert.Equal(
            WiimoteExtensionType.Nunchuk,
            ExtensionDecoder.DetectType([0x00, 0x00, 0xA4, 0x20, 0x00, 0x00]));
    }

    [Fact]
    public void DetectType_ReadsTheClassicControllerIdentifier()
    {
        Assert.Equal(
            WiimoteExtensionType.ClassicController,
            ExtensionDecoder.DetectType([0x00, 0x00, 0xA4, 0x20, 0x01, 0x01]));
    }

    [Theory]
    // 01 00 A4 20 01 01 is what the WiiBrew table documents; the second value is the one this
    // decoder accepted before, and it is kept so a controller reporting it still works.
    [InlineData(new byte[] { 0x01, 0x00, 0xA4, 0x20, 0x01, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x01, 0x20 })]
    public void DetectType_ReadsTheClassicControllerProIdentifier(byte[] identifier)
    {
        Assert.Equal(WiimoteExtensionType.ClassicControllerPro, ExtensionDecoder.DetectType(identifier));
    }

    [Theory]
    // The activated Motion Plus, the inactive one a Wii Remote Plus reports until a game
    // switches it on, and the two passthrough modes with a Nunchuk or a Classic behind it.
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x04, 0x05 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x00, 0x05 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x05, 0x05 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x07, 0x05 })]
    public void DetectType_ReadsTheMotionPlusIdentifiers(byte[] identifier)
    {
        Assert.Equal(WiimoteExtensionType.MotionPlus, ExtensionDecoder.DetectType(identifier));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4 })]
    // A Guitar, a drum kit and a turntable are not recognised yet.
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x01, 0x03 })]
    [InlineData(new byte[] { 0x03, 0x00, 0xA4, 0x20, 0x01, 0x03 })]
    public void DetectType_MarksEverythingElseAsUnknown(byte[] identifier)
    {
        Assert.Equal(WiimoteExtensionType.Unknown, ExtensionDecoder.DetectType(identifier));
    }

    [Fact]
    public void DecodeNunchuk_ReadsACenteredStickWithoutButtons()
    {
        var state = ExtensionDecoder.DecodeNunchuk([0x80, 0x80, 0x80, 0x80, 0x80, 0x03]);

        Assert.NotNull(state);
        Assert.Equal(0f, state.StickX, 4);
        Assert.Equal(0f, state.StickY, 4);
        Assert.False(state.ButtonC);
        Assert.False(state.ButtonZ);
        Assert.Equal(0f, state.AccelX, 4);
        Assert.Equal(0f, state.AccelY, 4);
        Assert.Equal(0f, state.AccelZ, 4);
    }

    [Fact]
    public void DecodeNunchuk_ReadsTheActiveLowButtons()
    {
        // Clearing bit 0 presses Z, clearing bit 1 presses C.
        var state = ExtensionDecoder.DecodeNunchuk([0x80, 0x80, 0x80, 0x80, 0x80, 0x02]);

        Assert.NotNull(state);
        Assert.True(state.ButtonZ);
        Assert.False(state.ButtonC);

        state = ExtensionDecoder.DecodeNunchuk([0x80, 0x80, 0x80, 0x80, 0x80, 0x01]);

        Assert.NotNull(state);
        Assert.False(state.ButtonZ);
        Assert.True(state.ButtonC);
    }

    [Fact]
    public void DecodeNunchuk_ReadsTheStickEnds()
    {
        var state = ExtensionDecoder.DecodeNunchuk([0x00, 0xFF, 0x80, 0x80, 0x80, 0x03]);

        Assert.NotNull(state);
        Assert.Equal(-1f, state.StickX, 4);
        Assert.Equal(1f, state.StickY, 4);
    }

    [Fact]
    public void DecodeNunchuk_UsesTheTwoLowAxisBitsFromTheButtonByte()
    {
        // One gravity is (0xB3 - 0x80) * 4 = 204 counts, so 0xB3 with a clear low bit
        // reads exactly one gravity.
        var state = ExtensionDecoder.DecodeNunchuk([0x80, 0x80, 0xB3, 0x80, 0x80, 0x03]);

        Assert.NotNull(state);
        Assert.Equal(1f, state.AccelX, 4);

        // The same high byte with the low bits all set sits three counts higher.
        state = ExtensionDecoder.DecodeNunchuk([0x80, 0x80, 0xB3, 0x80, 0x80, 0xFF]);

        Assert.NotNull(state);
        Assert.Equal(1 + (3 / 204f), state.AccelX, 4);
    }

    [Fact]
    public void DecodeClassicController_ReadsNeutralInput()
    {
        var state = ExtensionDecoder.DecodeClassicController([0xA0, 0x20, 0x10, 0x00, 0xFF, 0xFF]);

        Assert.NotNull(state);
        Assert.Equal(0f, state.LeftStickX, 4);
        Assert.Equal(0f, state.LeftStickY, 4);
        Assert.Equal(0f, state.RightStickX, 4);
        Assert.Equal(0f, state.RightStickY, 4);
        Assert.Equal(0f, state.LeftTrigger, 4);
        Assert.Equal(0f, state.RightTrigger, 4);
        Assert.False(state.ButtonA);
        Assert.False(state.ButtonB);
        Assert.False(state.DPadUp);
        Assert.False(state.DPadDown);
        Assert.False(state.DPadLeft);
        Assert.False(state.DPadRight);
    }

    [Fact]
    public void DecodeClassicController_ReadsTheActiveLowButtons()
    {
        // The buttons live in the last two bytes, active low: only A is cleared here.
        var state = ExtensionDecoder.DecodeClassicController([0xA0, 0x20, 0x10, 0x00, 0xFF, 0xEF]);

        Assert.NotNull(state);
        Assert.True(state.ButtonA);
        Assert.False(state.ButtonB);
        Assert.False(state.ButtonX);
        Assert.False(state.ButtonY);
        Assert.False(state.ButtonL);
        Assert.False(state.ButtonR);
        Assert.False(state.ButtonZL);
        Assert.False(state.ButtonZR);
        Assert.False(state.ButtonHome);
    }

    [Fact]
    public void DecodeClassicController_ReadsTheDPad()
    {
        var state = ExtensionDecoder.DecodeClassicController([0xA0, 0x20, 0x10, 0x00, 0xBF, 0xFF]);

        Assert.NotNull(state);
        Assert.True(state.DPadDown);
        Assert.False(state.DPadUp);
        Assert.False(state.DPadRight);
    }

    [Fact]
    public void DecodeClassicController_ReadsTheTriggersFromBothBytes()
    {
        // A full left trigger is 31: the low three bits live in byte 3 and the upper two
        // bits in byte 2. The right trigger occupies the low five bits of byte 3.
        var state = ExtensionDecoder.DecodeClassicController([0xA0, 0x20, 0x70, 0xFF, 0xFF, 0xFF]);

        Assert.NotNull(state);
        Assert.Equal(1f, state.LeftTrigger, 4);
        Assert.Equal(1f, state.RightTrigger, 4);
    }

    [Fact]
    public void DecodeClassicController_ReadsTheStickEnds()
    {
        // The left stick has six bit axes with 32 as the centre, the right stick five bit
        // axes with 16 as the centre spread over the upper bits of the first three bytes.
        var state = ExtensionDecoder.DecodeClassicController([0xFF, 0xC0, 0x90, 0x00, 0xFF, 0xFF]);

        Assert.NotNull(state);
        Assert.Equal(0.96875f, state.LeftStickX, 4);
        Assert.Equal(-1f, state.LeftStickY, 4);
        Assert.Equal(0.9375f, state.RightStickX, 4);
        Assert.Equal(0f, state.RightStickY, 4);
    }

    [Fact]
    public void DecodeClassicController_RejectsShortPayloads()
    {
        Assert.Null(ExtensionDecoder.DecodeClassicController([0x00, 0x00]));
        Assert.Null(ExtensionDecoder.DecodeNunchuk([0x00, 0x00]));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x05, 0x05 }, WiimoteExtensionType.Nunchuk)]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x07, 0x05 }, WiimoteExtensionType.ClassicController)]
    // A Motion Plus on its own passes nothing through, and neither does anything else.
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x04, 0x05 }, WiimoteExtensionType.None)]
    [InlineData(new byte[] { 0x00, 0x00, 0xA4, 0x20, 0x00, 0x00 }, WiimoteExtensionType.None)]
    public void DetectPassthroughType_ReadsTheInnerController(byte[] identifier, WiimoteExtensionType expected)
    {
        Assert.Equal(expected, ExtensionDecoder.DetectPassthroughType(identifier));
    }

    [Fact]
    public void DetectPassthroughType_RejectsShortIdentifiers()
    {
        Assert.Equal(WiimoteExtensionType.None, ExtensionDecoder.DetectPassthroughType([0x00, 0x00, 0xA4]));
    }

    [Fact]
    public void IsMotionPlusFrame_ReadsTheFlagInTheLastByte()
    {
        // Bit 1 of the last byte is set on the frames that carry the gyro instead of the
        // controller plugged into the Motion Plus.
        Assert.True(ExtensionDecoder.IsMotionPlusFrame([0x00, 0x00, 0x00, 0x00, 0x00, 0x02]));
        Assert.False(ExtensionDecoder.IsMotionPlusFrame([0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        Assert.False(ExtensionDecoder.IsMotionPlusFrame([0x00, 0x00]));
    }

    [Fact]
    public void DecodePassthroughNunchuk_ReadsACenteredStickWithBothButtonsHeld()
    {
        // Byte 0 and 1 are the stick, byte 2 to 4 the axes and byte 5 the moved low bits with
        // the two active low buttons at bits 3 and 2.
        var state = ExtensionDecoder.DecodePassthroughNunchuk([0x80, 0x80, 0xB3, 0x80, 0x80, 0x00]);

        Assert.NotNull(state);
        Assert.Equal(0f, state.StickX, 4);
        Assert.Equal(0f, state.StickY, 4);
        Assert.True(state.ButtonC);
        Assert.True(state.ButtonZ);
        // The Nunchuk sensor keeps its own scale, so 0xB3 is one gravity here too.
        Assert.Equal(1f, state.AccelX, 4);
        Assert.Equal(0f, state.AccelY, 4);
        Assert.Equal(0f, state.AccelZ, 4);
    }

    [Fact]
    public void DecodePassthroughNunchuk_ReadsTheHighAccelerometerBits()
    {
        // Bit 4 of byte 5 is AX<1>, two counts above the dropped AX<0>.
        var x = ExtensionDecoder.DecodePassthroughNunchuk([0x80, 0x80, 0xB3, 0x80, 0x80, 0x10]);
        Assert.NotNull(x);
        Assert.Equal(206 / 204f, x.AccelX, 4);

        // Bit 5 is AY<1>.
        var y = ExtensionDecoder.DecodePassthroughNunchuk([0x80, 0x80, 0x80, 0xB3, 0x80, 0x20]);
        Assert.NotNull(y);
        Assert.Equal(206 / 204f, y.AccelY, 4);

        // AZ is split across the two bytes: bits 7:6 of byte 5 hold AZ<2:1> with AZ<1> in bit
        // 7, and byte 4 keeps AZ<9:3> in its upper bits, its lowest bit being the flag.
        var z = ExtensionDecoder.DecodePassthroughNunchuk([0x80, 0x80, 0x80, 0x80, 0x80, 0x80]);
        Assert.NotNull(z);
        Assert.Equal(2 / 204f, z.AccelZ, 4);

        // Setting bit 6 instead of bit 7 writes AZ<2>, which sits one count higher.
        var zBit2 = ExtensionDecoder.DecodePassthroughNunchuk([0x80, 0x80, 0x80, 0x80, 0x80, 0x40]);
        Assert.NotNull(zBit2);
        Assert.Equal(4 / 204f, zBit2.AccelZ, 4);
    }

    [Fact]
    public void DecodePassthroughNunchuk_ReadsOneButtonAtATime()
    {
        var state = ExtensionDecoder.DecodePassthroughNunchuk([0x80, 0x80, 0x80, 0x80, 0x80, 0x08]);

        Assert.NotNull(state);
        Assert.False(state.ButtonC);
        Assert.True(state.ButtonZ);
    }

    [Fact]
    public void DecodePassthroughNunchuk_RejectsShortPayloads()
    {
        Assert.Null(ExtensionDecoder.DecodePassthroughNunchuk([0x00, 0x00]));
    }

    [Fact]
    public void DecodePassthroughClassicController_ReadsNeutralInput()
    {
        // Same readings as the plain neutral payload, in the passthrough layout: bit 0 of the
        // first two bytes carries the d-pad instead of the stick, and the last byte keeps no
        // d-pad bits of its own.
        var state = ExtensionDecoder.DecodePassthroughClassicController([0xA1, 0x21, 0x10, 0x00, 0xFF, 0xFC]);

        Assert.NotNull(state);
        Assert.Equal(0f, state.LeftStickX, 4);
        Assert.Equal(0f, state.LeftStickY, 4);
        Assert.Equal(0f, state.RightStickX, 4);
        Assert.Equal(0f, state.RightStickY, 4);
        Assert.Equal(0f, state.LeftTrigger, 4);
        Assert.Equal(0f, state.RightTrigger, 4);
        Assert.False(state.DPadUp);
        Assert.False(state.DPadLeft);
        Assert.False(state.ButtonA);
    }

    [Fact]
    public void DecodePassthroughClassicController_ReadsTheMovedDPadButtons()
    {
        // Up lives at bit 0 of the first byte and left at bit 0 of the second one, both
        // active low.
        var up = ExtensionDecoder.DecodePassthroughClassicController([0x20, 0x21, 0x10, 0x00, 0xFF, 0xFC]);
        Assert.NotNull(up);
        Assert.True(up.DPadUp);
        Assert.False(up.DPadLeft);

        var left = ExtensionDecoder.DecodePassthroughClassicController([0x21, 0x20, 0x10, 0x00, 0xFF, 0xFC]);
        Assert.NotNull(left);
        Assert.False(left.DPadUp);
        Assert.True(left.DPadLeft);
    }

    [Fact]
    public void DecodePassthroughClassicController_ReadsTheButtonsAndSticks()
    {
        // The last byte holds the same buttons as the plain layout, and the left stick keeps
        // its six bit scale with the dropped low bit reading as zero.
        var state = ExtensionDecoder.DecodePassthroughClassicController([0x00, 0x21, 0x10, 0x00, 0xFF, 0xEC]);

        Assert.NotNull(state);
        Assert.Equal(-1f, state.LeftStickX, 4);
        Assert.True(state.ButtonA);
        Assert.False(state.ButtonB);
        Assert.False(state.ButtonZL);
    }

    [Fact]
    public void DecodePassthroughClassicController_RejectsShortPayloads()
    {
        Assert.Null(ExtensionDecoder.DecodePassthroughClassicController([0x00, 0x00]));
    }
}
