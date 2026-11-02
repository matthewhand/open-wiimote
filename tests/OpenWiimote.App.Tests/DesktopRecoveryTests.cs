using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Profiles;
using OpenWiimote.Core.Protocol;
using OpenWiimote.Core.Settings;
using OpenWiimote.Core.Tests;
using OpenWiimote.Core.Virtual;
using Xunit;

namespace OpenWiimote.App.Tests;

public sealed class DesktopRecoveryTests
{
    [AvaloniaFact]
    public void AFailedBackendRecheckNeutralizesExistingDesktopOutput()
    {
        using var fixture = new Fixture();
        using var connection = fixture.Connect();
        Assert.True(fixture.Window.EnableEmulation());
        var pad = Assert.Single(fixture.Provider.Pads);
        fixture.Window.HandleDeviceSnapshot(connection, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.True(pad.States.Last().Buttons.HasFlag(XboxGamepadButtons.A));
        fixture.Provider.Available = false;
        Assert.False(fixture.Window.EnableEmulation());
        Assert.True(pad.Disposed);
        Assert.Equal(XboxGamepadState.Neutral, pad.States.Last());
    }

    [AvaloniaFact]
    public void OutputFailuresAreVisibleAndOldConnectionsCannotFeedAReplacement()
    {
        using var fixture = new Fixture();
        using var first = fixture.Connect();
        Assert.True(fixture.Window.EnableEmulation());
        var failed = fixture.Provider.Pads.Last();
        failed.Fail = true;
        fixture.Window.HandleDeviceSnapshot(first, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.True(failed.Disposed);
        Assert.Contains(fixture.Window.LogEntries, entry => entry.Message.Contains("Controller output failed"));
        fixture.Window.HandleDeviceDisconnected(first);
        using var replacement = fixture.Connect();
        var current = fixture.Provider.Pads.Last();
        var count = current.States.Count;
        fixture.Window.HandleDeviceSnapshot(first, new WiimoteState { Buttons = WiimoteButton.Two });
        fixture.Window.HandleDeviceDisconnected(first);
        Assert.Single(fixture.Window.Devices);
        Assert.False(current.Disposed);
        Assert.Equal(count, current.States.Count);
        fixture.Window.HandleDeviceSnapshot(replacement, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.True(current.States.Last().Buttons.HasFlag(XboxGamepadButtons.A));
    }

    [AvaloniaFact]
    public void ReplacementConnectBeforeOldDisconnectKeepsTheNewController()
    {
        using var fixture = new Fixture();
        using var first = fixture.Connect();
        Assert.True(fixture.Window.EnableEmulation());
        var oldPad = fixture.Provider.Pads.Last();
        fixture.Window.HandleDeviceSnapshot(first, new WiimoteState { Buttons = WiimoteButton.Two });
        using var replacement = fixture.Connect();
        var newPad = fixture.Provider.Pads.Last();
        Assert.NotSame(oldPad, newPad);
        Assert.True(oldPad.Disposed);
        Assert.Equal(XboxGamepadState.Neutral, oldPad.States.Last());
        var count = newPad.States.Count;
        fixture.Window.HandleDeviceDisconnected(first);
        fixture.Window.HandleDeviceSnapshot(first, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.Single(fixture.Window.Devices);
        Assert.False(newPad.Disposed);
        Assert.Equal(count, newPad.States.Count);
        fixture.Window.HandleDeviceSnapshot(replacement, new WiimoteState { Buttons = WiimoteButton.Two });
        Assert.True(newPad.States.Last().Buttons.HasFlag(XboxGamepadButtons.A));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openwiimote-desktop-" + Guid.NewGuid().ToString("N"));
        public Provider Provider { get; } = new();
        public MainWindow Window { get; }
        public Fixture()
        {
            var settings = new SettingsStore(_root);
            Window = new MainWindow(new WiimoteWatcher(() => []), Provider, settings, new ProfileStore(settings.ProfilesDirectory));
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        public WiimoteConnection Connect()
        {
            var connection = WiimoteConnection.Open(new FakeWiimoteDevice(), new WiimoteWatcherOptions(), "remote");
            Window.HandleDeviceConnected(connection);
            return connection;
        }
        public void Dispose()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(_root, true);
        }
    }

    private sealed class Provider : IVirtualGamepadProvider
    {
        public List<Pad> Pads { get; } = [];
        public bool Available { get; set; } = true;
        public string BackendName => "Synthetic";
        public string BackendDescription => "Offline test";
        public bool Probe(out string? error) { error = Available ? null : "Synthetic unavailable backend"; return Available; }
        public bool TryCreate(out IVirtualGamepad? gamepad, out string? error)
        {
            var pad = new Pad(); Pads.Add(pad); gamepad = pad; error = null; return true;
        }
        public void Dispose() { }
    }
    private sealed class Pad : IVirtualGamepad
    {
        public List<XboxGamepadState> States { get; } = [];
        public bool Disposed { get; private set; }
        public bool Fail { get; set; }
        public bool IsConnected => !Disposed;
        public void Submit(XboxGamepadState state)
        {
            if (Disposed || Fail) throw new IOException("Synthetic output failure");
            States.Add(state);
        }
        public void Dispose() => Disposed = true;
    }
}
