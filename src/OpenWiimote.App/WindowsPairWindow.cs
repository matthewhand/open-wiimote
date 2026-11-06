using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenWiimote.Platform.Windows;
namespace OpenWiimote.App;

public sealed class WindowsPairWindow : Window
{
    private readonly ListBox _devices = new() { Height = 170 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _find = new() { Content = "Find Wiimotes" };
    private readonly Button _pair = new() { Content = "Pair selected remote", IsEnabled = false };
    private readonly Button _windowsWizard = new() { Content = "Open Windows pairing wizard" };
    private readonly CancellationTokenSource _closed = new();
    private CancellationTokenSource? _searchCancellation;
    private Task _searchTask = Task.CompletedTask;
    private bool _pairing, _openingWizard;
    private readonly Action _openWizard;
    private bool _busy;
    private readonly Func<CancellationToken, Task<WindowsBluetoothRemote[]>> _scan;
    private readonly Func<WindowsBluetoothRemote, CancellationToken, Task<WindowsPairResult>> _connect;
    private readonly TimeSpan _searchDuration, _retryDelay;
    public WindowsPairWindow() : this(
        token => RunHelper<WindowsBluetoothRemote[]>(["--windows-scan"], token),
        (remote, token) => RunHelper<WindowsPairResult>(["--windows-pair", remote.Address, remote.RadioAddress], token)) { }
    internal WindowsPairWindow(Func<CancellationToken, Task<WindowsBluetoothRemote[]>> scan,
        Func<WindowsBluetoothRemote, CancellationToken, Task<WindowsPairResult>> connect,
        TimeSpan? searchDuration = null, TimeSpan? retryDelay = null, Action? openWizard = null)
    {
        _scan = scan; _connect = connect;
        _openWizard = openWizard ?? LaunchWindowsWizard;
        _searchDuration = searchDuration ?? TimeSpan.FromSeconds(30);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(750);
        Title = "Pair a Wii Remote"; Width = 660; Height = 640; MinWidth = 540; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Press the red SYNC button", FontSize = 24 });
        panel.Children.Add(new TextBlock { Text = "Cancel any Windows PIN dialog first. Press and release the red button under the battery cover, then select your remote when it appears. OpenWiimote supplies its special Bluetooth PIN automatically. Use red SYNC for a saved pairing; this flow is not for 1 + 2 guest mode.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_devices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(_find); buttons.Children.Add(_pair); panel.Children.Add(buttons);
        panel.Children.Add(_windowsWizard);
        panel.Children.Add(new TextBlock { Text = "Prefer the built-in Windows wizard? The button above pauses our search and opens it. If Windows offers Skip or pairing without a code, use that option. A PIN prompt is still possible on some Windows versions.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_status); Content = new ScrollViewer { Content = panel };
        _devices.SelectionChanged += (_, _) => _pair.IsEnabled = !_busy && !_openingWizard && _devices.SelectedItem is WindowsBluetoothRemote;
        _find.Click += async (_, _) => await Find();
        _pair.Click += async (_, _) => await Pair();
        _windowsWizard.Click += async (_, _) => await OpenWindowsWizard();
        Closed += (_, _) => _closed.Cancel();
        Opened += async (_, _) => await Find();
        _status.Text = "Ready. This uses the Windows Bluetooth stack and does not install a driver or remove existing bonds.";
    }
    private void Busy(bool busy, string? message = null)
    {
        _busy = busy; _find.IsEnabled = !busy && !_openingWizard; _pair.IsEnabled = !busy && !_openingWizard && _devices.SelectedItem is WindowsBluetoothRemote;
        _windowsWizard.IsEnabled = !_pairing && !_openingWizard && !_closed.IsCancellationRequested;
        if (message is not null) _status.Text = message;
    }
    private Task Find()
    {
        if (_busy || _openingWizard || _closed.IsCancellationRequested) return Task.CompletedTask;
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
        Busy(true, "Searching automatically… press and release red SYNC while this window is open.");
        return _searchTask = Search(_searchCancellation);
    }
    private async Task Search(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var watch = Stopwatch.StartNew();
        try
        {
            do
            {
                var result = await _scan(token);
                token.ThrowIfCancellationRequested();
                _devices.ItemsSource = result; _devices.SelectedItem = result.FirstOrDefault();
                if (result.Length > 0)
                {
                    _status.Text = "Choose your remote and pair while its lights are flashing.";
                    return;
                }
                _status.Text = "Still searching… press red SYNC and keep the remote nearby.";
                await Task.Delay(_retryDelay, token);
            } while (watch.Elapsed < _searchDuration);
            _status.Text = "No Wii Remote found after 30 seconds. Press red SYNC, then Find Wiimotes to search again.";
        }
        catch (Exception ex) { _status.Text = ex is OperationCanceledException ? "Bluetooth search cancelled." : ex.Message; }
        finally { _searchCancellation = null; cancellation.Dispose(); Busy(false); }
    }
    private async Task OpenWindowsWizard()
    {
        if (_pairing || _openingWizard || _closed.IsCancellationRequested) return;
        _openingWizard = true; Busy(_busy);
        try
        {
            // Reap our discovery helper before Windows starts its own inquiry.
            _searchCancellation?.Cancel();
            await _searchTask;
            _closed.Token.ThrowIfCancellationRequested();
            _openWizard();
            _status.Text = "Windows pairing wizard opened. Complete or cancel it before searching here again. If it requires a PIN with no skip option, cancel it and try Pair selected remote here.";
        }
        catch (Exception ex) { _status.Text = ex is OperationCanceledException ? "Opening Windows pairing was cancelled." : "Could not open Windows pairing: " + ex.Message; }
        finally { _openingWizard = false; Busy(false); }
    }
    private static void LaunchWindowsWizard()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        var wizard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DevicePairingWizard.exe");
        if (!File.Exists(wizard)) throw new FileNotFoundException("The built-in Windows pairing wizard is unavailable on this PC.");
        using var process = Process.Start(new ProcessStartInfo(wizard) { UseShellExecute = true })
            ?? throw new IOException("Windows did not start its pairing wizard.");
    }
    private async Task Pair()
    {
        if (_busy || _openingWizard || _closed.IsCancellationRequested || _devices.SelectedItem is not WindowsBluetoothRemote remote) return;
        _pairing = true; Busy(true, "Pairing the selected Wii Remote…");
        try
        {
            var result = await _connect(remote, _closed.Token);
            _status.Text = result.Message;
        }
        catch (Exception ex) { _status.Text = ex is OperationCanceledException ? "Pairing timed out or was cancelled. An already completed bond is kept; press red SYNC to retry." : ex.Message; }
        finally { _pairing = false; Busy(false); }
    }
    // Inquiry/authentication are synchronous Win32 APIs. An owned child supplies a hard
    // deadline and is killed/reaped on cancellation, rather than leaking a blocked thread.
    internal static async Task<T> RunHelper<T>(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = Path.Combine(Path.GetTempPath(), "openwiimote-bt-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(WindowsPairWindow).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(result);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Could not start Bluetooth pairing.");
            using var stopOwnedProcess = deadline.Token.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            });
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None); throw;
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                if (File.Exists(result))
                {
                    using var failure = JsonDocument.Parse(await File.ReadAllTextAsync(result, token));
                    if (failure.RootElement.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
                }
                throw new IOException("Windows Bluetooth could not complete the request. Turn Bluetooth on and try again.");
            }
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(result, token)) ?? throw new IOException("Windows returned no Bluetooth result.");
        }
        finally { if (File.Exists(result)) File.Delete(result); }
    }
}
