using OpenWiimote.App.Headless;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class BluetoothPairingTests
{
    private const string Address = "00:11:22:33:44:55";

    [Fact]
    public async Task AlreadyConnectedRemoteDoesNotChangeItsBond()
    {
        var calls = new List<string>();
        var result = await HeadlessServer.PairBluetoothDeviceAsync(Address, (_, args) =>
        {
            calls.Add(args);
            return Task.FromResult(new ProcessResult(true, "Paired: yes\nConnected: yes\n", ""));
        });
        Assert.True(result.Success);
        Assert.Equal(new[] { "info " + Address }, calls);
    }

    [Fact]
    public async Task FailedReconnectKeepsAnExistingPairing()
    {
        var calls = new List<string>();
        var result = await HeadlessServer.PairBluetoothDeviceAsync(Address, (_, args) =>
        {
            calls.Add(args);
            return Task.FromResult(new ProcessResult(true, "Paired: yes\nConnected: no\n", ""));
        });
        Assert.False(result.Success);
        Assert.Equal(new[] { "info " + Address, "--timeout 15 connect " + Address, "info " + Address }, calls);
        Assert.Contains("kept", result.Message);
    }

    [Fact]
    public async Task SuccessfulPairCommandDoesNotClaimAConnectionWithoutVerifiedState()
    {
        var calls = new List<string>();
        var result = await HeadlessServer.PairBluetoothDeviceAsync(Address, (_, args) =>
        {
            calls.Add(args);
            return Task.FromResult(new ProcessResult(true, args.StartsWith("info ") ? "Paired: no\nConnected: no\n" : "Pairing successful", ""));
        });
        Assert.False(result.Success);
        Assert.Contains("--timeout 20 pair " + Address, calls);
        Assert.DoesNotContain(calls, command => command.StartsWith("remove "));
    }

    [Fact]
    public async Task SuccessfulReconnectRequiresConnectedInfoAndDoesNotPairAgain()
    {
        var calls = new List<string>();
        var infoCount = 0;
        var result = await HeadlessServer.PairBluetoothDeviceAsync(Address, (_, args) =>
        {
            calls.Add(args);
            var output = args.StartsWith("info ") ? "Paired: yes\nConnected: " + (++infoCount == 1 ? "no" : "yes") : "";
            return Task.FromResult(new ProcessResult(true, output, ""));
        });
        Assert.True(result.Success);
        Assert.DoesNotContain(calls, command => command.Contains(" pair ") || command.StartsWith("remove "));
    }

    [Fact]
    public async Task InvalidAddressDoesNotRunACommand()
    {
        var result = await HeadlessServer.PairBluetoothDeviceAsync("bad; address", (_, _) => throw new InvalidOperationException("Must not execute"));
        Assert.False(result.Success);
    }
}
