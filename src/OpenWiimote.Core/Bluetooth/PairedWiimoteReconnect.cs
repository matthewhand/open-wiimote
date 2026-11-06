using System.Text.RegularExpressions;
using OpenWiimote.Core.Protocol;

namespace OpenWiimote.Core.Bluetooth;

/// <summary>Connect-only recovery for known, trusted Wii Remotes. Never discovers or pairs.</summary>
internal sealed partial class PairedWiimoteReconnect
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<BluetoothCommandResult>> _run;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private CancellationTokenSource? _iteration;
    private bool _enabled;

    internal PairedWiimoteReconnect(
        Func<IReadOnlyList<string>, CancellationToken, Task<BluetoothCommandResult>>? run = null,
        TimeProvider? clock = null)
    {
        _run = run ?? BluetoothCommand.RunAsync;
        _clock = clock ?? TimeProvider.System;
    }

    internal void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;
            if (!enabled) _iteration?.Cancel();
        }
    }

    internal async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await PollAsync(token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        }
    }

    // One iteration at a time. The watcher owns this worker and awaits it at shutdown.
    internal async Task PollAsync(CancellationToken token)
    {
        CancellationTokenSource iteration;
        lock (_gate)
        {
            if (!_enabled) return;
            iteration = CancellationTokenSource.CreateLinkedTokenSource(token);
            _iteration = iteration;
        }
        try
        {
            var ct = iteration.Token;
            var devices = await _run(["devices", "Paired"], ct).ConfigureAwait(false);
            if (!devices.Success)
                devices = await _run(["paired-devices"], ct).ConfigureAwait(false);
            if (!devices.Success) return;
            var candidates = devices.Output.Split('\n').Select(line => DeviceLine().Match(line.Trim()))
                .Where(match => match.Success)
                .Select(match => HidDeviceIdentity.TryFormatBluetoothAddress(match.Groups[1].Value))
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            // Inspect in small batches; an unreachable remote cannot starve another one.
            foreach (var batch in candidates.Chunk(4))
                await Task.WhenAll(batch.Select(address => TryReconnectAsync(address, ct))).ConfigureAwait(false);
            foreach (var forgotten in _retryAfter.Keys.Except(candidates, StringComparer.OrdinalIgnoreCase).ToArray())
                _retryAfter.Remove(forgotten);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception) { }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_iteration, iteration)) _iteration = null;
                iteration.Dispose();
            }
        }
    }

    private async Task TryReconnectAsync(string address, CancellationToken token)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
            if (_retryAfter.TryGetValue(address, out var next) && now < next) return;
        var info = await _run(["info", address], token).ConfigureAwait(false);
        if (!info.Success || !CanReconnect(info.Output)) return;
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_enabled) return;
            _retryAfter[address] = now.AddSeconds(10);
        }
        await _run(["--timeout", "8", "connect", address], token).ConfigureAwait(false);
        // A command exit status is not proof of connection. Only HID enumeration in the
        // watcher announces a controller or attaches a virtual gamepad.
    }

    internal static bool CanReconnect(string info)
    {
        var properties = info.Split('\n').Select(line => line.Trim())
            .Where(line => line.Contains(':'))
            .Select(line => (Key: line[..line.IndexOf(':')], Value: line[(line.IndexOf(':') + 1)..].Trim()))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        string Value(string key) => properties.GetValueOrDefault(key, "");
        return Value("Paired") == "yes" && Value("Trusted") == "yes" && Value("Blocked") == "no"
            && Value("Connected") == "no" && RemoteName().IsMatch(Value("Name"))
            && (Value("Modalias").Length > 0 ? NintendoId().IsMatch(Value("Modalias"))
                : info.Contains("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"^Device ([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5}) Nintendo RVL-CNT-01(?:-TR)?$")]
    private static partial Regex DeviceLine();
    [GeneratedRegex(@"^Nintendo RVL-CNT-01(?:-TR)?$")]
    private static partial Regex RemoteName();
    [GeneratedRegex(@"^(?:usb|bluetooth):v057Ep(?:0306|0330)d[0-9a-f]+$", RegexOptions.IgnoreCase)]
    private static partial Regex NintendoId();
}
