using HidSharp;

namespace OpenWiimote.Core.Protocol;

public sealed class WiimoteWatcher : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _disposed;
    private readonly Dictionary<string, WiimoteConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RetryState> _retries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastFailureLog = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IEnumerable<HidDevice>> _enumerateDevices;
    private WiimoteWatcherOptions _options = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public WiimoteWatcher()
        : this(static () => DeviceList.Local.GetHidDevices())
    {
    }

    /// <summary>
    /// Watches the remotes that <paramref name="enumerateDevices"/> reports. The parameter
    /// is what lets a test run the whole watcher against a remote that only exists in the
    /// test process; the default source is the device list of the operating system.
    /// </summary>
    public WiimoteWatcher(Func<IEnumerable<HidDevice>> enumerateDevices)
    {
        _enumerateDevices = enumerateDevices;
    }

    public event EventHandler<WiimoteConnection>? DeviceConnected;
    public event EventHandler<WiimoteConnection>? DeviceDisconnected;
    public event EventHandler<string>? Log;

    public WiimoteWatcherOptions Options => _options;

    public IReadOnlyList<WiimoteConnection> Connections
    {
        get
        {
            lock (_gate)
            {
                return _connections.Values.ToArray();
            }
        }
    }

    public void UpdateOptions(WiimoteWatcherOptions options)
    {
        _options = options.Normalize();
    }

    public async Task StartAsync(WiimoteWatcherOptions options, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cts is not null)
            {
                UpdateOptions(options);
                return;
            }

            _options = options.Normalize();
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cts = source;
            try
            {
                await ScanAsync(force: false, source.Token).ConfigureAwait(false);
                _loopTask = Task.Run(() => LoopAsync(source.Token));
            }
            catch
            {
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task ScanOnceAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A stopped watcher must not acquire another remote after StopAsync returns.
            if (_cts is not null) await ScanAsync(force: true, _cts.Token).ConfigureAwait(false);
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollIntervalMs, cancellationToken).ConfigureAwait(false);
                await ScanAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"Watcher error: {ex.Message}");
            }
        }
    }

    private async Task ScanAsync(bool force, CancellationToken cancellationToken)
    {
        if (!await _scanGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            List<HidDevice> found;
            try
            {
                // A remote paired over Bluetooth is reported with the adapter's USB vendor
                // and product ids, so every HID device has to be checked rather than the
                // Nintendo vendor only.
                found = _enumerateDevices()
                    .Where(HidDeviceIdentity.IsWiimote)
                    .OrderBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"HID enumeration failed: {ex.Message}");
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var presentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in found)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = HidDeviceIdentity.GetKey(device);
                presentKeys.Add(key);

                WiimoteConnection? existing;
                lock (_gate)
                {
                    _connections.TryGetValue(key, out existing);
                }

                if (existing is not null)
                {
                    if (!existing.IsAlive)
                    {
                        RemoveConnection(key, "connection closed");
                    }

                    continue;
                }

                if (!force && !_options.AutoConnect)
                {
                    continue;
                }

                // An explicit scan retries failed devices straight away, which is how a
                // permission fix takes effect without restarting the app.
                if (!force && IsInBackoff(key, now))
                {
                    continue;
                }

                await TryOpenAsync(device, key, cancellationToken).ConfigureAwait(false);
            }

            RemoveStaleConnections(now, presentKeys);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task TryOpenAsync(HidDevice device, string key, CancellationToken cancellationToken)
    {
        WiimoteConnection? connection = null;
        try
        {
            connection = await Task.Run(
                () => WiimoteConnection.Open(device, _options, key),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            connection.Log += OnConnectionLog;
            connection.Disconnected += OnConnectionDisconnected;

            var added = false;
            lock (_gate)
            {
                if (!_connections.ContainsKey(key))
                {
                    _connections[key] = connection;
                    _retries.Remove(key);
                    added = true;
                }
            }

            if (!added)
            {
                connection.Disconnected -= OnConnectionDisconnected;
                connection.Dispose();
                return;
            }

            connection.Start();
            Log?.Invoke(this, $"Connected to {connection}");
            DeviceConnected?.Invoke(this, connection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            connection?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            if (connection is not null)
            {
                connection.Log -= OnConnectionLog;
                connection.Disconnected -= OnConnectionDisconnected;
                connection.Dispose();
            }

            RegisterFailure(key, ex);
        }
    }

    private void OnConnectionLog(object? sender, string message)
    {
        Log?.Invoke(this, message);
    }

    private void OnConnectionDisconnected(object? sender, WiimoteConnection connection)
    {
        WiimoteConnection? removed = null;
        lock (_gate)
        {
            if (_connections.TryGetValue(connection.DeviceKey, out var current) &&
                ReferenceEquals(current, connection))
            {
                _connections.Remove(connection.DeviceKey);
                removed = current;
            }
        }

        connection.Log -= OnConnectionLog;
        connection.Disconnected -= OnConnectionDisconnected;
        connection.Dispose();

        if (removed is null)
        {
            return;
        }

        UpdateRetryState(removed.DeviceKey, DateTimeOffset.UtcNow, 1);
        Log?.Invoke(this, $"Disconnected from {removed}");
        DeviceDisconnected?.Invoke(this, removed);
    }

    private void RemoveStaleConnections(DateTimeOffset now, HashSet<string> presentKeys)
    {
        List<WiimoteConnection> stale = [];

        lock (_gate)
        {
            foreach (var pair in _connections.ToArray())
            {
                var connection = pair.Value;
                var age = now - connection.Snapshot.LastReport;

                if (!connection.IsAlive)
                {
                    _connections.Remove(pair.Key);
                    stale.Add(connection);
                }
                else if (age > TimeSpan.FromMilliseconds(_options.StaleTimeoutMs))
                {
                    _connections.Remove(pair.Key);
                    stale.Add(connection);
                }
                else if (!presentKeys.Contains(pair.Key) && age > TimeSpan.FromSeconds(2))
                {
                    _connections.Remove(pair.Key);
                    stale.Add(connection);
                }
            }
        }

        foreach (var connection in stale)
        {
            connection.Log -= OnConnectionLog;
            connection.Disconnected -= OnConnectionDisconnected;
            connection.Dispose();
            UpdateRetryState(connection.DeviceKey, now, 1);
            Log?.Invoke(this, $"Removed stale connection {connection}");
            DeviceDisconnected?.Invoke(this, connection);
        }
    }

    private void RemoveConnection(string key, string reason)
    {
        WiimoteConnection? connection = null;
        lock (_gate)
        {
            if (_connections.Remove(key, out var current))
            {
                connection = current;
            }
        }

        if (connection is null)
        {
            return;
        }

        connection.Log -= OnConnectionLog;
        connection.Disconnected -= OnConnectionDisconnected;
        connection.Dispose();
        UpdateRetryState(key, DateTimeOffset.UtcNow, 1);
        Log?.Invoke(this, $"Removed connection {connection}: {reason}");
        DeviceDisconnected?.Invoke(this, connection);
    }

    private bool IsInBackoff(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _retries.TryGetValue(key, out var retry) && retry.NextAttempt > now;
        }
    }

    private void UpdateRetryState(string key, DateTimeOffset now, int failuresDelta)
    {
        lock (_gate)
        {
            var retry = _retries.GetValueOrDefault(key, new RetryState(0, now));
            var failures = retry.Failures + failuresDelta;
            var delay = Math.Min(_options.ReconnectDelayMs * Math.Min(Math.Max(failures, 1), 8), 60000);
            _retries[key] = new RetryState(failures, now.AddMilliseconds(delay));
        }
    }

    private void RegisterFailure(string key, Exception exception)
    {
        var now = DateTimeOffset.UtcNow;
        UpdateRetryState(key, now, 1);

        var lastLog = _lastFailureLog.TryGetValue(key, out var value) ? value : DateTimeOffset.MinValue;
        if (now - lastLog > TimeSpan.FromSeconds(15))
        {
            _lastFailureLog[key] = now;
            Log?.Invoke(this, $"Could not open {key}: {DescribeOpenFailure(exception)}. Retrying in {Math.Max(1, (int)(_retries[key].NextAttempt - now).TotalSeconds)}s.");
        }
    }

    private static string DescribeOpenFailure(Exception exception)
    {
        var message = exception.Message;
        if (exception is UnauthorizedAccessException ||
            message.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            return "access to the hidraw device was denied, so this user cannot read the remote; " +
                   "grant it with a udev rule for Nintendo hidraw devices";
        }

        return message;
    }



    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycleGate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        WiimoteConnection[] connections;
        lock (_gate)
        {
            connections = _connections.Values.ToArray();
            _connections.Clear();
            _retries.Clear();
        }

        foreach (var connection in connections)
        {
            connection.Log -= OnConnectionLog;
            connection.Disconnected -= OnConnectionDisconnected;
            connection.Dispose();
            DeviceDisconnected?.Invoke(this, connection);
        }

        _loopTask = null;
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await StopCoreAsync().ConfigureAwait(false);
            _disposed = true;
            _scanGate.Dispose();
        }
        finally { _lifecycleGate.Release(); }
    }

    private sealed record RetryState(int Failures, DateTimeOffset NextAttempt);
}
