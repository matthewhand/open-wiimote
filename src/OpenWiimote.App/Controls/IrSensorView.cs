using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenWiimote.Core.Models;
namespace OpenWiimote.App.Controls;

/// <summary>Raw IR camera coordinates, expired independently of button/status reports.</summary>
public sealed class IrSensorView : Control
{
    private WiimoteState _state = new();
    private readonly DispatcherTimer _timer;
    public IrSensorView()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => InvalidateVisual();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }
    public void SetState(WiimoteState state) { _state = state; InvalidateVisual(); }
    internal static IrPoint[] FreshPoints(WiimoteState state, DateTimeOffset now) =>
        state.LastIrReport is { } report && now >= report && now - report <= TimeSpan.FromMilliseconds(250)
            ? state.IrPoints.Where(p => p.IsValid && p.X is >= 0 and <= 1023 && p.Y is >= 0 and <= 767).Take(4).ToArray() : [];
    public override void Render(DrawingContext context)
    {
        var width = Math.Min(Bounds.Width, Bounds.Height * 4 / 3);
        var height = width * 3 / 4;
        var area = new Rect((Bounds.Width - width) / 2, 0, width, height);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#101A2D")), new Pen(Brushes.SlateGray, 1), area);
        var points = FreshPoints(_state, DateTimeOffset.UtcNow);
        foreach (var point in points)
            context.DrawEllipse(Brushes.Cyan, null, new Point(area.X + 6 + point.X / 1023d * Math.Max(0, width - 12), 6 + point.Y / 767d * Math.Max(0, height - 12)), 5, 5);
        if (points.Length == 0)
        {
            var label = new FormattedText("No fresh IR dots", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, Brushes.LightSlateGray);
            context.DrawText(label, new Point(area.X + Math.Max(0, (width - label.Width) / 2), Math.Max(0, (height - label.Height) / 2)));
        }
    }
}
