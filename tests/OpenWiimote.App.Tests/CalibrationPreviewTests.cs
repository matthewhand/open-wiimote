using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using OpenWiimote.App.Controls;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Pointer;
using Xunit;
namespace OpenWiimote.App.Tests;
public sealed class CalibrationPreviewTests
{
    [AvaloniaTheory]
    [InlineData(0, "no fresh dots")]
    [InlineData(1, "1 dot")]
    [InlineData(2, "2 dots")]
    [InlineData(3, "extra infrared lights")]
    public void CalibrationShowsRawSensorFeedbackAndInstructionsWithoutEnablingMouse(int dots, string expected)
    {
        var backend = new Backend();
        using var controller = new PointerController(backend, new(), _ => { });
        var owner = new object(); controller.Connected("remote", owner); controller.Select("remote", "screen"); controller.BeginCalibration();
        controller.Update("remote", owner, new WiimoteState { IrFrameNumber = 1, LastIrReport = DateTimeOffset.UtcNow,
            IrPoints = Enumerable.Range(0, dots).Select(n => new IrPoint { IsValid = true, X = 300 + n * 40, Y = 300 }).ToArray() });
        var window = new PointerCalibrationWindow(controller, backend.Display);
        try
        {
            window.Show();
            var controls = window.GetVisualDescendants().ToArray();
            Assert.Single(controls.OfType<IrSensorView>());
            Assert.Contains(expected, controls.OfType<TextBlock>().Single(t => t.Name == "CalibrationIrStatus").Text);
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text?.Contains("A (the large front button)") == true);
            Assert.False(controller.Snapshot().Enabled);
            Assert.Equal(0, backend.OutputsCreated);
        }
        finally { window.Close(); }
    }
    private sealed class Backend : IPointerBackend
    {
        public PointerDisplay Display = new("screen", "Test screen", 0, 0, 3440, 1440);
        public int OutputsCreated;
        public IReadOnlyList<PointerDisplay> GetDisplays() => [Display];
        public IPointerOutput Create(PointerDisplay display) { OutputsCreated++; throw new InvalidOperationException("Calibration must not create mouse output"); }
    }
}
