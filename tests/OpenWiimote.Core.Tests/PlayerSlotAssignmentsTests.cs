using OpenWiimote.Core.Protocol;
using OpenWiimote.Core.Settings;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class PlayerSlotAssignmentsTests
{
    [Fact]
    public void ReverseReconnectAndRestartKeepDistinctRememberedLights()
    {
        var slots = new PlayerSlotAssignments();
        Assert.Equal(1, slots.Assign("first"));
        Assert.Equal(2, slots.Assign("second"));
        slots.Release("first");
        slots.Release("second");
        Assert.Equal(2, slots.Assign("SECOND"));
        Assert.Equal(1, slots.Assign("first"));
        var restarted = new PlayerSlotAssignments(slots.Save());
        Assert.Equal(2, restarted.Assign("second"));
        Assert.Equal(1, restarted.Assign("first"));
    }

    [Fact]
    public void ReplacementAndMalformedSavedPreferencesCannotDuplicateSlots()
    {
        var slots = new PlayerSlotAssignments(new Dictionary<string, int>
        {
            ["first"] = 1, ["FIRST"] = 2, ["second"] = 1, ["invalid"] = 8, [" "] = 3
        });
        Assert.Equal(1, slots.Assign("FIRST"));
        Assert.Equal(1, slots.Assign("first"));
        Assert.Equal(2, slots.Assign("second"));
        Assert.Equal(2, slots.Save().Count);
        Assert.Equal(16, PlayerSlotAssignments.LedMask(1));
        Assert.Equal(32, PlayerSlotAssignments.LedMask(2));
    }

    [Fact]
    public void FifthRemoteDoesNotReuseAnActiveLightAndInactiveSlotsCanBeReused()
    {
        var slots = new PlayerSlotAssignments();
        for (var n = 1; n <= 4; n++) Assert.Equal(n, slots.Assign("remote" + n));
        Assert.Equal(0, slots.Assign("fifth"));
        Assert.Equal(0, PlayerSlotAssignments.LedMask(0));
        slots.Release("remote2");
        Assert.Equal(2, slots.Assign("fifth"));
        Assert.Equal(0, slots.Assign("remote2"));
        Assert.Equal(4, slots.Save().Count);
    }

    [Fact]
    public void AddressFormattingCannotChangeAPlayerIdentity()
    {
        var slots = new PlayerSlotAssignments();
        Assert.Equal(1, slots.Assign("aa:bb:cc:dd:ee:ff"));
        Assert.Equal(1, slots.Assign("AABBCCDDEEFF"));
        Assert.Single(slots.Save());
    }

    [Fact]
    public void KernelLedsUseEachReportedMaximumAndNeverClaimAPartialWriteSucceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = Enumerable.Range(0, 4).Select(index =>
            {
                var directory = Path.Combine(root, "p" + index);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "max_brightness"), index == 1 ? "7\n" : "1\n");
                var path = Path.Combine(directory, "brightness");
                File.WriteAllText(path, "0");
                return path;
            }).ToArray();
            var leds = new KernelHidLeds(paths);
            Assert.True(leds.SetMask(0x20));
            Assert.Equal(new[] { "0", "7", "0", "0" }, paths.Select(File.ReadAllText));
            Assert.True(leds.SetMask(0x10));
            Assert.Equal(new[] { "1", "0", "0", "0" }, paths.Select(File.ReadAllText));
            Directory.Delete(Path.GetDirectoryName(paths[3])!, true);
            Assert.False(leds.SetMask(0x10));
            Assert.False(new KernelHidLeds([paths[0], null, null, null]).SetMask(0x10));
        }
        finally { Directory.Delete(root, true); }
    }
}
