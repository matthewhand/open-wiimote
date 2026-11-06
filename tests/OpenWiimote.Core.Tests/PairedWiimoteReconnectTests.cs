using HidSharp;
using OpenWiimote.Core.Bluetooth;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class PairedWiimoteReconnectTests
{
    private const string Address = "00:11:22:33:44:55";
    private const string Info = "Name: Nintendo RVL-CNT-01\nPaired: yes\nTrusted: yes\nBlocked: no\nConnected: no\nModalias: usb:v057Ep0306d0600\n";
    private const string Devices = "Device " + Address + " Nintendo RVL-CNT-01\n";

    [Fact]
    public async Task DisabledDoesNothingAndAConnectCannotClaimAHidController()
    {
        var calls = new List<string>();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new PairedWiimoteReconnect((args, _) =>
        {
            calls.Add(string.Join(" ", args));
            if (args.Contains("connect")) connected.TrySetResult();
            return Task.FromResult(new BluetoothCommandResult(true, args[0] == "devices" ? Devices : Info));
        });
        await worker.PollAsync(default);
        Assert.Empty(calls);
        worker.SetEnabled(true);
        await using var watcher = new WiimoteWatcher(() => [], worker);
        await watcher.StartAsync(new WiimoteWatcherOptions());
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await watcher.StopAsync();
        Assert.Empty(watcher.Connections);
        Assert.Contains("--timeout 8 connect " + Address, calls);
        Assert.DoesNotContain(calls, command => command.Contains("pair ") || command.Contains("remove ") || command.Contains("scan "));
    }

    [Theory]
    [InlineData("Paired: yes", "Paired: no")]
    [InlineData("Trusted: yes", "Trusted: no")]
    [InlineData("Blocked: no", "Blocked: yes")]
    [InlineData("Connected: no", "Connected: yes")]
    [InlineData("Name: Nintendo RVL-CNT-01", "Name: A keyboard")]
    [InlineData("Modalias: usb:v057Ep0306d0600", "Modalias: usb:v9999p0306d0600")]
    [InlineData("Paired: yes", "")] // Missing state fails closed.
    public async Task NeverConnectsAnIneligibleDevice(string before, string after)
    {
        var calls = new List<string>();
        var worker = new PairedWiimoteReconnect((args, _) =>
        {
            calls.Add(string.Join(" ", args));
            return Task.FromResult(new BluetoothCommandResult(true, args[0] == "devices" ? Devices : Info.Replace(before, after)));
        });
        worker.SetEnabled(true);
        await worker.PollAsync(default);
        Assert.DoesNotContain(calls, call => call.Contains("connect "));
    }

    [Fact]
    public void KnownTrRemoteAndHidUuidAreSupported()
    {
        Assert.True(PairedWiimoteReconnect.CanReconnect(Info.Replace("CNT-01", "CNT-01-TR")));
        Assert.True(PairedWiimoteReconnect.CanReconnect(Info.Replace("Modalias: usb:v057Ep0306d0600", "UUID: HID (00001124-0000-1000-8000-00805f9b34fb)")));
        Assert.False(PairedWiimoteReconnect.CanReconnect(Info.Replace("v057E", "v9999") + "UUID: HID (00001124-0000-1000-8000-00805f9b34fb)"));
        Assert.False(PairedWiimoteReconnect.CanReconnect("Alias: Nintendo RVL-CNT-01\n" + Info.Replace("Name: Nintendo RVL-CNT-01", "Name: keyboard")));
    }

    [Fact]
    public async Task BackoffPreventsHammeringAndLegacyListingStillValidatesInfo()
    {
        var time = new Clock();
        var attempts = 0;
        var worker = new PairedWiimoteReconnect((args, _) =>
        {
            if (args.Contains("connect")) attempts++;
            return Task.FromResult(new BluetoothCommandResult(args[0] != "devices", args[0] == "paired-devices" ? Devices : Info));
        }, time);
        worker.SetEnabled(true);
        await worker.PollAsync(default);
        await worker.PollAsync(default);
        Assert.Equal(1, attempts);
        time.Now = time.Now.AddSeconds(11);
        await worker.PollAsync(default);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task MalformedAddressesAndOtherDevicesNeverReachInfoOrConnect()
    {
        var calls = new List<string>();
        var worker = new PairedWiimoteReconnect((args, _) =>
        {
            calls.Add(string.Join(" ", args));
            return Task.FromResult(new BluetoothCommandResult(true, "Device bad;whoami Nintendo RVL-CNT-01\nDevice 00:11:22:33:44:55 Keyboard\n"));
        });
        worker.SetEnabled(true);
        await worker.PollAsync(default);
        Assert.Equal(new[] { "devices Paired" }, calls);
    }

    [Fact]
    public async Task MissingBluetoothctlIsHarmless()
    {
        var worker = new PairedWiimoteReconnect((_, _) => throw new System.ComponentModel.Win32Exception());
        worker.SetEnabled(true);
        await worker.PollAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingAutoConnectOrStoppingCancelsAndAwaitsThePendingCommand(bool stop)
    {
        var connecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new PairedWiimoteReconnect(async (args, ct) =>
        {
            if (args.Contains("connect"))
            {
                connecting.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { cancelled.TrySetResult(); }
            }
            return new BluetoothCommandResult(true, args[0] == "devices" ? Devices : Info);
        });
        await using var watcher = new WiimoteWatcher(() => [], worker);
        await watcher.StartAsync(new WiimoteWatcherOptions());
        await connecting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (stop) await watcher.StopAsync();
        else watcher.UpdateOptions(new WiimoteWatcherOptions { AutoConnect = false });
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await watcher.StopAsync();
        Assert.Empty(watcher.Connections);
    }

    [Fact]
    public async Task UnreachableFirstRemoteDoesNotBlockTheSecond()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var worker = new PairedWiimoteReconnect(async (args, ct) =>
        {
            if (args.Contains("connect"))
            {
                if (args[^1] == Address)
                {
                    firstEntered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
                else secondEntered.TrySetResult();
            }
            return new(true, args[0] == "devices" ? Devices + "Device 00:11:22:33:44:66 Nintendo RVL-CNT-01\n" : Info);
        });
        worker.SetEnabled(true);
        var poll = worker.PollAsync(stop.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
