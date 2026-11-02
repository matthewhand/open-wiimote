using System.Collections.Concurrent;
using HidSharp;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

/// <summary>
/// Runs the real watcher, the real connection and the real parser against a remote that only
/// exists in this process, so the path a user's Wiimote takes can be tested without hardware.
/// </summary>
public sealed class FakeWiimoteWatcherTests
{
    private const string DeviceKey = "02:00:00:00:00:01";

    [Fact]
    public async Task TheWatcherConnectsToARemoteAndDecodesTheReportsItSends()
    {
        var remote = new FakeWiimoteDevice();
        await using var harness = new FakeWatcherHarness(remote);

        await harness.StartAsync();
        var connection = await harness.WaitForConnectionAsync(DeviceKey);

        Assert.Equal(DeviceKey, connection.DeviceKey);
        Assert.Equal(DeviceKey, connection.BluetoothAddress);
        Assert.Equal("Nintendo RVL-CNT-01", connection.ProductName);
        Assert.Null(connection.KernelDriver);
        Assert.False(connection.IsKernelOwned);
        Assert.Equal(connection, Assert.Single(harness.Connected));

        // The initialisation of a user space remote reaches the device: the player LEDs, the
        // data reporting mode and a status request.
        await WaitForAsync(() => remote.WrittenReportIds.Contains(WiimoteProtocol.SetLedRumbleReport));
        Assert.Contains(WiimoteProtocol.SetDataReportingReport, remote.WrittenReportIds);
        Assert.Contains(WiimoteProtocol.RequestStatusReport, remote.WrittenReportIds);
        Assert.Equal(
            WiimoteProtocol.Led1,
            remote.WrittenReports.First(report => report[0] == WiimoteProtocol.SetLedRumbleReport)[1]);

        // Observe the report as it arrives: a later acknowledgement may legitimately
        // replace the button state before a polling assertion sees it.
        var received = new TaskCompletionSource<WiimoteState>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<WiimoteState> handler = (_, state) =>
        {
            if (state.Buttons == (WiimoteButton.A | WiimoteButton.B)) received.TrySetResult(state);
        };
        connection.SnapshotUpdated += handler;
        try
        {
            remote.QueueStatusReport(extensionConnected: false);
            remote.QueueButtonsAndAccelerometerReport(secondButtonByte: 0x0C);
            var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1f, snapshot.AccelZ, 3);
            Assert.Equal(100, snapshot.BatteryPercent);
        }
        finally
        {
            connection.SnapshotUpdated -= handler;
        }
    }

    [Fact]
    public async Task TheWatcherDecodesTheNunchukBehindAMotionPlus()
    {
        var remote = new FakeWiimoteDevice
        {
            ExtensionIdentifier = [0x00, 0x00, 0xA4, 0x20, 0x05, 0x05]
        };
        await using var harness = new FakeWatcherHarness(remote);

        await harness.StartAsync();
        var connection = await harness.WaitForConnectionAsync(DeviceKey);

        // Plugging in an extension arrives as a status report, and the connection then turns
        // the encryption off and reads the identifier. The fake answers both.
        remote.QueueStatusReport(extensionConnected: true);
        await WaitForAsync(() => connection.Snapshot.ExtensionType == WiimoteExtensionType.Nunchuk);

        Assert.True(connection.Snapshot.ExtensionConnected);
        Assert.Contains(
            WiimoteProtocol.WriteMemoryReport,
            remote.WrittenReportIds);
        Assert.Contains(WiimoteProtocol.ReadMemoryReport, remote.WrittenReportIds);

        // A gyro frame of the Motion Plus carries no controller data: bit 1 of the last byte
        // is the flag that says "this frame is the gyro".
        var gyroFrame = WaitForNextReportAsync(connection);
        remote.QueueExtensionReport([0x00, 0x00, 0x00, 0x00, 0x00, 0x02]);
        await gyroFrame;

        Assert.Null(connection.Snapshot.Nunchuk);

        // The next frame is the Nunchuk one, in the modified passthrough layout: stick hard
        // left and up, C held and Z released, both of which are active low.
        remote.QueueExtensionReport([0x00, 0xFF, 0x80, 0x80, 0x80, 0x04]);
        await WaitForAsync(() => connection.Snapshot.Nunchuk is not null);

        var nunchuk = connection.Snapshot.Nunchuk;
        Assert.NotNull(nunchuk);
        Assert.Equal(-1f, nunchuk.StickX, 3);
        Assert.Equal(1f, nunchuk.StickY, 3);
        Assert.True(nunchuk.ButtonC);
        Assert.False(nunchuk.ButtonZ);
        Assert.Equal(WiimoteExtensionType.Nunchuk, connection.Snapshot.ExtensionType);
    }

    [Fact]
    public async Task ExperimentalMotionPlusDoesNotReadUnverifiedGyroRegisters()
    {
        var remote = new FakeWiimoteDevice
        {
            ExtensionIdentifier = [0x00, 0x00, 0xA4, 0x20, 0x04, 0x05],
            Gyro = new GyroReading(Yaw: 90, Roll: -45, Pitch: 12)
        };
        await using var harness = new FakeWatcherHarness(remote);

        await harness.StartAsync();
        var connection = await harness.WaitForConnectionAsync(DeviceKey);

        remote.QueueStatusReport(extensionConnected: true);
        await WaitForAsync(() => connection.Snapshot.ExtensionType == WiimoteExtensionType.MotionPlus);

        Assert.Equal(0, connection.Snapshot.GyroX);
        Assert.Equal(0, connection.Snapshot.GyroY);
        Assert.Equal(0, connection.Snapshot.GyroZ);
        Assert.DoesNotContain(remote.WrittenReports, report =>
            report[0] == WiimoteProtocol.ReadMemoryReport &&
            ((report[2] << 16) | (report[3] << 8) | report[4]) == WiimoteProtocol.GyroBlockRegister);
    }

    [Fact]
    public async Task ARemoteThatStopsAnsweringIsDroppedAsStale()
    {
        var remote = new FakeWiimoteDevice();
        await using var harness = new FakeWatcherHarness(remote);

        // A long status interval keeps the connection from writing, so the remote goes quiet
        // after the initialisation and only the stale timeout can notice.
        await harness.StartAsync(new WiimoteWatcherOptions
        {
            PollIntervalMs = 250,
            StaleTimeoutMs = 2000,
            StatusRequestIntervalMs = 10000
        });
        var connection = await harness.WaitForConnectionAsync(DeviceKey);

        await WaitForAsync(
            () => !harness.Watcher.Connections.Contains(connection),
            TimeSpan.FromSeconds(15));

        Assert.False(connection.IsAlive);
        Assert.Contains(connection.DeviceKey, harness.Disconnected.Select(entry => entry.DeviceKey));
    }

    [Fact]
    public async Task ARemoteThatDisappearsIsDroppedAndTheFailureIsLogged()
    {
        var remote = new FakeWiimoteDevice();
        await using var harness = new FakeWatcherHarness(remote);

        await harness.StartAsync();
        var connection = await harness.WaitForConnectionAsync(DeviceKey);

        // Switching the remote off makes the read fail, the way a hidraw node that is gone does.
        remote.Detach();
        await WaitForAsync(() => harness.Disconnected.Any(entry => entry.DeviceKey == DeviceKey));

        Assert.False(connection.IsAlive);
        Assert.Contains(harness.Logs, message => message.Contains("Read loop stopped"));
    }

    [Fact]
    public async Task ARemoteThatCannotBeOpenedIsReportedAndNotConnected()
    {
        var remote = new FakeWiimoteDevice { OpenFails = true };
        await using var harness = new FakeWatcherHarness(remote);

        await harness.StartAsync();

        await WaitForAsync(() => harness.Logs.Any(message => message.Contains("Could not open")));
        Assert.Empty(harness.Connected);
        Assert.Empty(harness.Watcher.Connections);
        Assert.Contains(harness.Logs, message => message.Contains("access to the hidraw device was denied"));
    }

    /// <summary>Waits until the connection has processed one more report.</summary>
    private static async Task WaitForNextReportAsync(WiimoteConnection connection)
    {
        var reports = 0;
        EventHandler<WiimoteReport> handler = (_, _) => Interlocked.Increment(ref reports);
        connection.ReportReceived += handler;

        try
        {
            await WaitForAsync(() => Volatile.Read(ref reports) > 0);
        }
        finally
        {
            connection.ReportReceived -= handler;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("The condition was not met in time.");
    }

    /// <summary>
    /// The watcher plus the remote list it should enumerate, with the events it raises
    /// collected so a test can wait for them.
    /// </summary>
    private sealed class FakeWatcherHarness : IAsyncDisposable
    {
        private readonly List<FakeWiimoteDevice> _devices;

        public FakeWatcherHarness(params FakeWiimoteDevice[] devices)
        {
            _devices = [.. devices];
            Watcher = new WiimoteWatcher(() => _devices.Cast<HidDevice>().ToArray());
            Watcher.DeviceConnected += (_, connection) => Connected.Enqueue(connection);
            Watcher.DeviceDisconnected += (_, connection) => Disconnected.Enqueue(connection);
            Watcher.Log += (_, message) => Logs.Enqueue(message);
        }

        public WiimoteWatcher Watcher { get; }

        public ConcurrentQueue<WiimoteConnection> Connected { get; } = new();

        public ConcurrentQueue<WiimoteConnection> Disconnected { get; } = new();

        public ConcurrentQueue<string> Logs { get; } = new();

        public Task StartAsync(WiimoteWatcherOptions? options = null)
        {
            return Watcher.StartAsync(options ?? new WiimoteWatcherOptions
            {
                PollIntervalMs = 250,
                ReconnectDelayMs = 500,
                StaleTimeoutMs = 4000,
                StatusRequestIntervalMs = 250
            });
        }

        /// <summary>Waits until the watcher reported a connection for that remote.</summary>
        public async Task<WiimoteConnection> WaitForConnectionAsync(string deviceKey)
        {
            await WaitForAsync(() => Watcher.Connections.Any(connection => connection.DeviceKey == deviceKey));
            return Watcher.Connections.First(connection => connection.DeviceKey == deviceKey);
        }

        public async ValueTask DisposeAsync() => await Watcher.DisposeAsync();
    }
}
