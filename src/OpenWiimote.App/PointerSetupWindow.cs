using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenWiimote.Core.Pointer;
using OpenWiimote.Core.Models;
using OpenWiimote.App.Controls;

namespace OpenWiimote.App;

public sealed record PointerRemote(string Key, string Name)
{
    public override string ToString() => Name;
}
internal sealed record DisplayChoice(PointerDisplay Display)
{
    public override string ToString() => $"{Display.Name} — {Display.Width} × {Display.Height}";
}

public sealed class PointerSetupWindow : Window
{
    private readonly PointerController _controller;
    private readonly Func<PointerRemote[]> _remotes;
    private readonly Action<string, string> _select;
    private readonly ComboBox _remote = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _display = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer;
    private string _signature = "";
    private string? _actionError;
    private readonly Dictionary<string, Button> _buttons = new();
    private PointerCalibrationWindow? _calibration;

    public PointerSetupWindow(PointerController controller, Func<PointerRemote[]> remotes, Action<string, string> select)
    {
        _controller = controller; _remotes = remotes; _select = select;
        Title = "Sensor bar pointer"; Width = 600; Height = 530; MinWidth = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Point with either Wiimote", FontSize = 24 });
        panel.Children.Add(new TextBlock { Text = "Centre the powered Wii infrared bar above or below the display. Choose one remote, calibrate from your usual position, then enable the pointer. A = left click, B = right click, Home = stop.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Remote" }); panel.Children.Add(_remote);
        panel.Children.Add(new TextBlock { Text = "Display" }); panel.Children.Add(_display);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        AddButton(buttons, "Use selected remote", Select);
        AddButton(buttons, "Calibrate screen", Calibrate);
        AddButton(buttons, "Enable pointer", () =>
        {
            var state = _controller.Snapshot();
            if (_remote.SelectedItem is not PointerRemote remote || _display.SelectedItem is not DisplayChoice display || remote.Key != state.DeviceKey || display.Display.Id != state.DisplayId)
                throw new ArgumentException("Choose Use selected remote first.");
            _controller.Enable(true);
        });
        AddButton(buttons, "Stop pointer", () => _controller.Enable(false));
        panel.Children.Add(buttons); panel.Children.Add(_status);
        Content = panel;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _calibration?.Close(); };
    }
    private void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        _buttons[text] = button;
        button.Click += (_, _) => { try { _actionError = null; action(); Refresh(); } catch (Exception ex) { _actionError = ex.Message; _status.Text = ex.Message; } };
        panel.Children.Add(button);
    }
    private void Select()
    {
        if (_remote.SelectedItem is not PointerRemote remote || _display.SelectedItem is not DisplayChoice display)
            throw new ArgumentException("Choose a connected remote and display.");
        _select(remote.Key, display.Display.Id);
    }
    private void Calibrate()
    {
        Select();
        _controller.BeginCalibration();
        var selected = ((DisplayChoice)_display.SelectedItem!).Display;
        _calibration = new PointerCalibrationWindow(_controller, selected);
        _calibration.Show();
    }
    private void Refresh()
    {
        var state = _controller.Snapshot(); var devices = _remotes();
        var signature = string.Join('|', devices.Select(d => d.Key)) + ":" + string.Join('|', state.Displays.Select(d => d.Fingerprint));
        if (signature != _signature)
        {
            _signature = signature; _actionError = null;
            _remote.ItemsSource = devices;
            _remote.SelectedItem = devices.FirstOrDefault(d => d.Key == state.DeviceKey) ?? devices.FirstOrDefault();
            var choices = state.Displays.Select(d => new DisplayChoice(d)).ToArray();
            _display.ItemsSource = choices;
            _display.SelectedItem = choices.FirstOrDefault(d => d.Display.Id == state.DisplayId) ?? choices.FirstOrDefault();
        }
        _buttons["Use selected remote"].IsEnabled = devices.Length > 0 && state.Displays.Count > 0;
        _buttons["Calibrate screen"].IsEnabled = devices.Length > 0 && state.Displays.Count > 0;
        _buttons["Enable pointer"].IsEnabled = state.Calibrated && state.IrVisible && !state.Calibrating
            && _remote.SelectedItem is PointerRemote selectedRemote && selectedRemote.Key == state.DeviceKey
            && _display.SelectedItem is DisplayChoice selectedDisplay && selectedDisplay.Display.Id == state.DisplayId;
        _status.Text = _actionError ?? state.Error ?? (devices.Length == 0 ? "Connect a Wii Remote first."
            : !state.Available ? "Pointer output is unavailable on this desktop."
            : state.Enabled ? "Pointer enabled. Home stops it."
            : state.Calibrating ? $"Target {Math.Min(state.Step + 1, 4)} of 4. Hold steady and press A."
            : state.Calibrated ? "Calibration saved for this remote and display. Aim at the bar, release A/B, then enable."
            : "Pointer is off. Calibrate this remote for the selected display first.");
    }
}

internal sealed class PointerCalibrationWindow : Window
{
    private readonly PointerController _controller;
    private readonly Canvas _canvas = new();
    private readonly Border _dot = new() { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Brushes.Lime, BorderBrush = Brushes.White, BorderThickness = new Thickness(3) };
    private readonly IrSensorView _irPreview = new() { Name = "CalibrationIrPreview", Width = 240, Height = 180 };
    private readonly TextBlock _irStatus = new() { Name = "CalibrationIrStatus", Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    private readonly StackPanel _instructions = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { Foreground = Brushes.White, FontSize = 24, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 650 };
    private readonly DispatcherTimer _timer;
    public PointerCalibrationWindow(PointerController controller, PointerDisplay display)
    {
        _controller = controller;
        Title = "Calibrate pointer · Esc or Home to cancel";
        Width = 500; Height = 400; SystemDecorations = SystemDecorations.None;
        Position = new PixelPoint(display.X + 40, display.Y + 40); Topmost = true;
        _canvas.Background = new SolidColorBrush(Color.Parse("#101820"));
        _canvas.Children.Add(_dot);
        _instructions.Children.Add(_hint);
        _instructions.Children.Add(_irPreview);
        _instructions.Children.Add(_irStatus);
        _instructions.Children.Add(new TextBlock { Text = "Aim the Wiimote at the green target. Press and release A (the large front button) when steady.\nThe mouse stays still during calibration. Esc or Home cancels.", Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var layout = new Grid { Background = _canvas.Background };
        layout.Children.Add(_canvas); layout.Children.Add(_instructions); Content = layout;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { WindowState = WindowState.FullScreen; _timer.Start(); Refresh(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => Close();
        Closed += (_, _) => { _timer.Stop(); _controller.CancelCalibration(); };
    }
    internal static string TrackingText(PointerSnapshot state) => state.IrPoints.Count switch
    {
        0 => "IR camera: no fresh dots — aim at the powered sensor bar",
        1 => "IR camera: 1 dot — both ends of the sensor bar must be visible",
        2 when state.Stable => "IR camera: 2 dots — ready to record with A",
        2 when state.IrVisible => "IR camera: 2 dots — hold still",
        2 => "IR camera: 2 dots — aim until both are clearly separated",
        _ => $"IR camera: {state.IrPoints.Count} dots — extra infrared lights; need exactly 2"
    };
    private void Refresh()
    {
        var state = _controller.Snapshot();
        if (!state.Calibrating) { Close(); return; }
        var step = Math.Clamp(state.Step, 0, 3);
        double[] x = [.05, .95, .95, .05], y = [.05, .05, .95, .95];
        Canvas.SetLeft(_dot, _canvas.Bounds.Width * x[step] - 18);
        Canvas.SetTop(_dot, _canvas.Bounds.Height * y[step] - 18);
        _instructions.Width = Math.Min(650, _canvas.Bounds.Width * .75);
        _irPreview.Width = Math.Min(240, _instructions.Width);
        _irPreview.Height = _irPreview.Width * .75;
        _irPreview.SetState(new WiimoteState { IrPoints = state.IrPoints.ToArray(), LastIrReport = state.LastIrReport });
        _irStatus.Text = TrackingText(state);
        _irStatus.Foreground = state.Stable ? Brushes.Lime : Brushes.White;
        _hint.Text = $"Target {step + 1} of 4\n" + (state.Error ?? (state.Stable ? "Steady — press A to record" : "Point at the green dot and hold still"));
    }
}
