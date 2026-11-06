using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using OpenWiimote.App.Controls;
using OpenWiimote.Core.Models;
using Xunit;
namespace OpenWiimote.App.Tests;
public sealed class SensorLayoutTests
{
    [AvaloniaFact]
    public void DiagnosticsStartCollapsedAndSensorsAreTheDefaultTab()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            Assert.False(window.FindControl<Expander>("TroubleshootingExpander")!.IsExpanded);
            var tabs = window.FindControl<TabControl>("ControllerTabs")!;
            Assert.Equal("Sensors", ((TabItem)tabs.SelectedItem!).Header);
            Assert.NotNull(window.FindControl<IrSensorView>("IrSensor"));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void MouseToggleWithoutAReadyControllerStaysOffAndOpensSetup()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            var toggle = window.FindControl<ToggleButton>("MouseModeToggle")!;
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(toggle.IsChecked);
            Assert.Equal("Mouse mode: Off", toggle.Content);
            Assert.Single(window.OwnedWindows.OfType<PointerSetupWindow>());
        }
        finally { window.Close(); }
    }
    [Fact]
    public void SensorPreviewExpiresIrEvenIfButtonReportsAreFresh()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new WiimoteState { LastIrReport = now, IrPoints = [new() { IsValid = true, X = 100, Y = 300 }] };
        Assert.Single(IrSensorView.FreshPoints(state, now));
        Assert.Empty(IrSensorView.FreshPoints(state with { LastReport = now.AddSeconds(1) }, now.AddSeconds(1)));
        Assert.Empty(IrSensorView.FreshPoints(state with { LastIrReport = null }, now));
        Assert.Empty(IrSensorView.FreshPoints(state with { IrPoints = [new() { IsValid = true, X = 1024 }] }, now));
    }
}
