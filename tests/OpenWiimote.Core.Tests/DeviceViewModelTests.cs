using OpenWiimote.App;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

/// <summary>
/// Covers what the desktop window shows and drives for one remote, on a real connection to a
/// remote that only exists in this process.
/// </summary>
public sealed class DeviceViewModelTests
{
    [Fact]
    public void TheListSaysWhichProfileTheRemoteIsMappedWith()
    {
        var remote = new FakeWiimoteDevice();
        using var connection = WiimoteConnection.Open(remote, new WiimoteWatcherOptions());
        var viewModel = new DeviceViewModel(connection);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.Equal("02:00:00:00:00:01", viewModel.BluetoothAddress);
        Assert.Null(viewModel.PinnedProfile);
        Assert.Equal("Follows the active profile", viewModel.ProfileText);

        viewModel.PinnedProfile = "Racing";

        Assert.Equal("Profile: Racing", viewModel.ProfileText);
        Assert.Contains(nameof(DeviceViewModel.PinnedProfile), changed);
        Assert.Contains(nameof(DeviceViewModel.ProfileText), changed);

        // Pinning the same profile again is not a change, so the list is not rebuilt.
        changed.Clear();
        viewModel.PinnedProfile = "Racing";
        Assert.Empty(changed);

        viewModel.PinnedProfile = null;
        Assert.Equal("Follows the active profile", viewModel.ProfileText);
    }

    [Fact]
    public void ThePlayerLedAndRumbleControlsReachTheRemote()
    {
        var remote = new FakeWiimoteDevice();
        using var connection = WiimoteConnection.Open(remote, new WiimoteWatcherOptions());

        Assert.True(connection.SetLedMask(WiimoteProtocol.Led3));
        Assert.Equal(WiimoteProtocol.Led3, LastLedReport(remote)[1]);
        Assert.Equal(WiimoteProtocol.Led3, connection.LedMask);

        Assert.True(connection.SetRumble(true));
        Assert.Equal(WiimoteProtocol.Led3 | WiimoteProtocol.Rumble, LastLedReport(remote)[1]);
        Assert.True(connection.IsRumbling);

        Assert.True(connection.SetRumble(false));
        Assert.Equal(WiimoteProtocol.Led3, LastLedReport(remote)[1]);
        Assert.False(connection.IsRumbling);

        Assert.True(connection.RequestStatus());
        Assert.Contains(WiimoteProtocol.RequestStatusReport, remote.WrittenReportIds);
    }

    private static byte[] LastLedReport(FakeWiimoteDevice remote)
    {
        return remote.WrittenReports.Last(report => report[0] == WiimoteProtocol.SetLedRumbleReport);
    }
}
