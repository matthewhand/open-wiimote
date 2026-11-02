using HidSharp;
using OpenWiimote.Core.Protocol;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class WatcherLifecycleTests
{
    [Fact]
    public async Task StopWaitsForAnInFlightManualScanAndReleasesItsRemote()
    {
        var remote = new FakeWiimoteDevice();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var scanBlocked = false;
        await using var watcher = new WiimoteWatcher(() =>
        {
            if (!scanBlocked) return Array.Empty<HidDevice>();
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test scan was not released");
            return new HidDevice[] { remote };
        });
        await watcher.StartAsync(new WiimoteWatcherOptions { AutoConnect = false, PollIntervalMs = 10000 });
        scanBlocked = true;
        var scanning = Task.Run(() => watcher.ScanOnceAsync());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var stopping = watcher.StopAsync();
        try { Assert.False(stopping.IsCompleted); }
        finally { release.Set(); }
        await Task.WhenAll(scanning, stopping).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(watcher.Connections);
        Assert.True(remote.LastOpenedStream?.IsClosed);
        await watcher.ScanOnceAsync();
        Assert.Empty(watcher.Connections);
    }

    [Fact]
    public async Task ACancelledStartCanBeStartedAgainAndDisposeIsIdempotent()
    {
        var watcher = new WiimoteWatcher(() => Array.Empty<HidDevice>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            watcher.StartAsync(new WiimoteWatcherOptions(), cancelled.Token));
        await watcher.StartAsync(new WiimoteWatcherOptions { AutoConnect = false });
        await watcher.DisposeAsync();
        await watcher.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => watcher.ScanOnceAsync());
    }

    [Fact]
    public void ADisposedConnectionCannotBeStarted()
    {
        var connection = WiimoteConnection.Open(new FakeWiimoteDevice(), new WiimoteWatcherOptions());
        connection.Dispose();
        Assert.Throws<ObjectDisposedException>(connection.Start);
    }

    [Fact]
    public void FailedConnectionInitializationClosesTheOpenedStream()
    {
        var remote = new FakeWiimoteDevice { ReportLengthsFail = true };
        Assert.Throws<IOException>(() => WiimoteConnection.Open(remote, new WiimoteWatcherOptions()));
        Assert.NotNull(remote.LastOpenedStream);
        Assert.True(remote.LastOpenedStream.IsClosed);
    }
}
