using System.Collections.Concurrent;
using OpenWiimote.App.Headless;
using OpenWiimote.Core.Experimental;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Profiles;
using OpenWiimote.Core.Protocol;
using OpenWiimote.Core.Settings;
using OpenWiimote.Core.Virtual;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class HeadlessRecoveryTests
{
    [Fact]
    public void ExperimentalGyroSourceNeverProducesAReading()
    {
        Assert.False(ExperimentalMotionPlus.Available);
        Assert.Equal("experimental", ExperimentalMotionPlus.Status);
        Assert.False(ExperimentalMotionPlus.TryGetReading(out var reading));
        Assert.Equal(default, reading);
    }

    [Fact]
    public async Task PinnedProfileSurvivesEnablingEmulationAndChangingTheActiveProfile()
    {
        await using var fixture = new Fixture();
        var pinned = fixture.Profiles.CreateProfile("Pinned");
        pinned.A.WiimoteButton = WiimoteButton.A;
        fixture.Profiles.SaveProfile(pinned);
        fixture.Service.AssignDeviceProfile("remote", "Pinned");
        using var connection = fixture.Connect();
        fixture.Service.SetEmulation(true);
        fixture.Service.SetProfile("Default");
        Assert.Equal("Pinned", Assert.Single(fixture.Service.GetSnapshot().Devices).PreferredProfile);
        fixture.Service.OnSnapshotUpdated(connection, new WiimoteState { Buttons = WiimoteButton.A });
        Assert.True(fixture.Provider.Pads.Last().States.Last().Buttons.HasFlag(XboxGamepadButtons.A));
    }

    [Fact]
    public async Task DelayedDisconnectAndReportsCannotAffectReplacementConnection()
    {
        await using var fixture = new Fixture();
        using var first = fixture.Connect();
        fixture.Service.SetEmulation(true);
        var oldPad = fixture.Provider.Pads.Last();
        fixture.Service.OnSnapshotUpdated(first, new WiimoteState { Buttons = WiimoteButton.Two });
        using var replacement = fixture.Connect();
        var newPad = fixture.Provider.Pads.Last();
        var count = newPad.States.Count;
        fixture.Service.OnDeviceDisconnected(null, first);
        fixture.Service.OnSnapshotUpdated(first, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.Single(fixture.Service.GetSnapshot().Devices);
        Assert.Equal(count, newPad.States.Count);
        Assert.True(oldPad.Disposed);
        Assert.Equal(XboxGamepadState.Neutral, oldPad.States.Last());
        Assert.False(newPad.Disposed);
        fixture.Service.OnDeviceDisconnected(null, replacement);
        Assert.True(newPad.Disposed);
        Assert.Equal(XboxGamepadState.Neutral, newPad.States.Last());
    }

    [Fact]
    public async Task DisablingEmulationNeutralizesAndOutputFailureCanBeRetried()
    {
        await using var fixture = new Fixture();
        using var connection = fixture.Connect();
        fixture.Service.SetEmulation(true);
        var first = fixture.Provider.Pads.Last();
        fixture.Service.OnSnapshotUpdated(connection, new WiimoteState { Buttons = WiimoteButton.Two });
        fixture.Service.SetEmulation(false);
        Assert.True(first.Disposed);
        Assert.Equal(XboxGamepadState.Neutral, first.States.Last());
        fixture.Service.SetEmulation(true);
        var failed = fixture.Provider.Pads.Last();
        failed.Fail = true;
        fixture.Service.OnSnapshotUpdated(connection, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.True(failed.Disposed);
        Assert.False(Assert.Single(fixture.Service.GetSnapshot().Devices).VirtualControllerAttached);
        Assert.NotNull(fixture.Service.GetSnapshot().EmulationError);
        fixture.Service.SetEmulation(false);
        fixture.Service.SetEmulation(true);
        Assert.True(Assert.Single(fixture.Service.GetSnapshot().Devices).VirtualControllerAttached);
        Assert.Null(fixture.Service.GetSnapshot().EmulationError);
    }

    [Fact]
    public async Task FailedBackendRecheckReleasesExistingOutput()
    {
        await using var fixture = new Fixture();
        using var connection = fixture.Connect();
        fixture.Service.SetEmulation(true);
        var pad = fixture.Provider.Pads.Last();
        fixture.Service.OnSnapshotUpdated(connection, new WiimoteState { Buttons = WiimoteButton.Two });
        fixture.Provider.Available = false;
        fixture.Service.SetEmulation(true);
        Assert.True(pad.Disposed);
        Assert.Equal(XboxGamepadState.Neutral, pad.States.Last());
        Assert.False(fixture.Service.GetSnapshot().EmulationEnabled);
        Assert.NotNull(fixture.Service.GetSnapshot().EmulationError);
    }

    [Fact]
    public async Task TwoRemotesKeepSeparateProfilesAndControllers()
    {
        await using var fixture = new Fixture();
        var pinned = fixture.Profiles.CreateProfile("Second remote");
        pinned.A.WiimoteButton = WiimoteButton.A;
        fixture.Profiles.SaveProfile(pinned);
        fixture.Service.AssignDeviceProfile("second", pinned.Name);
        using var first = fixture.Connect();
        using var second = fixture.Connect("second");
        fixture.Service.SetEmulation(true);
        var devices = fixture.Service.GetSnapshot().Devices;
        Assert.Equal(2, devices.Count);
        Assert.All(devices, remote => Assert.True(remote.VirtualControllerAttached));
        Assert.NotEqual(devices[0].LedMask, devices[1].LedMask);
        fixture.Service.OnSnapshotUpdated(first, new WiimoteState { Buttons = WiimoteButton.Two });
        fixture.Service.OnSnapshotUpdated(second, new WiimoteState { Buttons = WiimoteButton.A });
        Assert.All(fixture.Provider.Pads, pad => Assert.True(pad.States.Last().Buttons.HasFlag(XboxGamepadButtons.A)));
        fixture.Service.OnDeviceDisconnected(null, first);
        Assert.Single(fixture.Service.GetSnapshot().Devices);
        Assert.False(fixture.Provider.Pads[1].Disposed);
    }

    [Fact]
    public async Task RenamingAndDeletingProfilesUpdatesPinsAndCurrentOutput()
    {
        await using var fixture = new Fixture();
        fixture.Profiles.CreateProfile("Pinned");
        fixture.Service.AssignDeviceProfile("remote", "Pinned");
        using var first = fixture.Connect();
        fixture.Service.SetEmulation(true);
        fixture.Service.RenameProfile("Pinned", "Renamed");
        Assert.Equal("Renamed", fixture.Service.GetSnapshot().Settings.DeviceProfiles["remote"]);
        Assert.Equal("Renamed", Assert.Single(fixture.Service.GetSnapshot().Devices).PreferredProfile);
        using var replacement = fixture.Connect();
        Assert.Equal("Renamed", Assert.Single(fixture.Service.GetSnapshot().Devices).PreferredProfile);
        fixture.Service.DeleteProfile("renamed");
        Assert.Empty(fixture.Service.GetSnapshot().Settings.DeviceProfiles);
        Assert.Equal("Default", Assert.Single(fixture.Service.GetSnapshot().Devices).PreferredProfile);
        Assert.Throws<ArgumentException>(() => fixture.Service.DeleteProfile("Default"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openwiimote-recovery-" + Guid.NewGuid().ToString("N"));
        public ProfileStore Profiles { get; }
        public Provider Provider { get; } = new();
        public HeadlessWiimoteService Service { get; }
        public Fixture()
        {
            var settings = new SettingsStore(_root);
            Profiles = new ProfileStore(settings.ProfilesDirectory);
            Service = new HeadlessWiimoteService(new WiimoteWatcher(() => []), Provider, settings, Profiles);
        }
        public WiimoteConnection Connect(string key = "remote")
        {
            var connection = WiimoteConnection.Open(new FakeWiimoteDevice(), new WiimoteWatcherOptions(), key);
            Service.OnDeviceConnected(null, connection);
            return connection;
        }
        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Provider : IVirtualGamepadProvider
    {
        public List<Pad> Pads { get; } = [];
        public string BackendName => "Synthetic";
        public string BackendDescription => "Offline test";
        public bool Available { get; set; } = true;
        public bool Probe(out string? error) { error = Available ? null : "Synthetic unavailable backend"; return Available; }
        public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
        {
            var pad = new Pad(); Pads.Add(pad); gamepad = pad; error = null; return true;
        }
        public void Dispose() { }
    }
    private sealed class Pad : IVirtualGamepad
    {
        public ConcurrentQueue<XboxGamepadState> States { get; } = new();
        public bool Disposed { get; private set; }
        public bool Fail { get; set; }
        public bool IsConnected => !Disposed;
        public void Submit(XboxGamepadState state)
        {
            if (Disposed || Fail) throw new IOException("Synthetic output failure");
            States.Enqueue(state);
        }
        public void Dispose() => Disposed = true;
    }
}
