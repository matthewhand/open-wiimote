using System.Text.Json;
using OpenWiimote.App.Headless;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

/// <summary>
/// Checks the whole way from the bytes of a remote to the virtual controller: the report
/// is parsed, the state is built from it and the state is mapped onto the gamepad.
/// </summary>
public sealed class TiltSteeringTests
{
    [Fact]
    public void FlatRemote_DoesNotSteer()
    {
        // 0x80 is the zero point of an axis and 0x9A is the value that one gravity
        // produces on the axis that points up.
        var state = State(0x31, 0x00, 0x00, 0x80, 0x80, 0x9A);

        Assert.Equal(0f, state.AccelX, 4);
        Assert.Equal(0f, state.AccelY, 4);
        Assert.Equal(1f, state.AccelZ, 4);
        Assert.Equal(0, state.TiltXDegrees, 1);
        Assert.Equal(0, state.TiltYDegrees, 1);

        var result = ProfileMapper.Map(TiltProfile(), state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void RemoteRolledToTheRight_SteersRight()
    {
        // Thirty degrees of roll: the X axis reads 52 counts above the zero point, which
        // is 0x8D, and the Z axis reads 0x96 with its leftover bit in the second button
        // byte.
        var state = State(0x31, 0x00, 0x40, 0x8D, 0x80, 0x96);

        Assert.Equal(0.5f, state.AccelX, 2);
        Assert.Equal(30f, (float)state.TiltXDegrees, 0);

        var result = ProfileMapper.Map(TiltProfile(), state);

        Assert.Equal(short.MaxValue, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void RemoteRolledToTheLeft_SteersLeft()
    {
        var state = State(0x31, 0x00, 0x40, 0x73, 0x80, 0x96);

        Assert.Equal(-0.5f, state.AccelX, 2);
        Assert.Equal(-30f, (float)state.TiltXDegrees, 0);

        var result = ProfileMapper.Map(TiltProfile(), state);

        Assert.Equal(short.MinValue + 1, result.LeftThumbX);
        Assert.Equal(0, result.LeftThumbY);
    }

    [Fact]
    public void RemotePitchedDown_MovesTheStickDown()
    {
        var state = State(0x31, 0x00, 0x40, 0x80, 0x73, 0x96);

        Assert.Equal(-0.5f, state.AccelY, 2);
        Assert.Equal(-30f, (float)state.TiltYDegrees, 0);

        var result = ProfileMapper.Map(TiltProfile(), state);

        Assert.Equal(0, result.LeftThumbX);
        Assert.Equal(short.MinValue + 1, result.LeftThumbY);
    }

    [Fact]
    public void HeadlessDevice_ReportsTheTiltAndTheAcceleration()
    {
        var state = State(0x31, 0x00, 0x40, 0x8D, 0x80, 0x96);
        var device = new ServerDevice("key", "Wii Remote", null, state, true, state.TiltXDegrees, state.TiltYDegrees, 0, 0, 0);

        var json = JsonSerializer.SerializeToElement(device, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(0.5f, json.GetProperty("state").GetProperty("accelX").GetSingle(), 4);
        Assert.Equal(0.865f, json.GetProperty("state").GetProperty("accelZ").GetSingle(), 2);
        Assert.Equal(30f, json.GetProperty("state").GetProperty("tiltXDegrees").GetDouble(), 0);
        Assert.Equal(30f, json.GetProperty("tiltXDegrees").GetDouble(), 0);
        Assert.Equal(0f, json.GetProperty("tiltYDegrees").GetDouble(), 0);
    }

    private static MappingProfile TiltProfile()
    {
        var profile = MappingProfile.CreateDefault();
        profile.UseAccelerometer = true;
        profile.AccelMapping = new AccelerometerMapping
        {
            TiltSteeringEnabled = true,
            TargetControl = "LeftStick (Steering + Pitch)",
            MaxTiltDegrees = 30f,
            Sensitivity = 1f,
            DeadZone = 0f
        };

        return profile;
    }

    /// <summary>
    /// Builds the state of a remote from the bytes of a core buttons and accelerometer
    /// report the same way the connection does.
    /// </summary>
    private static WiimoteState State(
        byte reportId,
        byte firstButtonByte,
        byte secondButtonByte,
        byte x,
        byte y,
        byte z)
    {
        var payload = new byte[WiimoteProtocol.InputReportLength];
        payload[0] = reportId;
        payload[1] = firstButtonByte;
        payload[2] = secondButtonByte;
        payload[3] = x;
        payload[4] = y;
        payload[5] = z;

        var report = WiimoteReportParser.Parse(payload);
        Assert.NotNull(report);
        Assert.True(report.HasAccelerometer);

        return new WiimoteState
        {
            Buttons = report.Buttons,
            AccelX = report.AccelX,
            AccelY = report.AccelY,
            AccelZ = report.AccelZ
        };
    }
}
