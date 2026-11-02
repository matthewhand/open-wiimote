using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenWiimote.Core.Models;

namespace OpenWiimote.App.Controls;

/// <summary>
/// A vector drawing of the extension controller that is plugged into the selected remote.
/// </summary>
/// <remarks>
/// The drawing follows the same dark theme as <see cref="WiimoteAvatar"/>: a light body,
/// dark edges and the accent colour for every button that is currently pressed. The
/// Nunchuk and the Classic Controller are drawn top down inside a fixed 320x190 design
/// box that is scaled to the control bounds.
/// </remarks>
public sealed class ExtensionAvatar : Control
{
    private const double DesignWidth = 320;
    private const double DesignHeight = 190;

    private static readonly IBrush BodyBrush = new SolidColorBrush(Color.Parse("#E7EEF9"));
    private static readonly IBrush BodyShadeBrush = new SolidColorBrush(Color.Parse("#C9D6E8"));
    private static readonly IBrush ButtonBrush = new SolidColorBrush(Color.Parse("#2B3C5C"));
    private static readonly IBrush ButtonTextBrush = new SolidColorBrush(Color.Parse("#EAF2FF"));
    private static readonly IBrush PressedBrush = new SolidColorBrush(Color.Parse("#6C9CFF"));
    private static readonly IBrush StickWellBrush = new SolidColorBrush(Color.Parse("#B8C7DC"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8FA3C8"));
    private static readonly IPen OutlinePen =
        new Pen(new SolidColorBrush(Color.FromArgb(90, 6, 10, 20)), 1.2);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);

    private WiimoteState _state = new();

    public void SetState(WiimoteState state)
    {
        _state = state;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight);
        if (scale <= 0)
        {
            return;
        }

        var offset = new Vector(
            (Bounds.Width - (DesignWidth * scale)) / 2,
            (Bounds.Height - (DesignHeight * scale)) / 2);

        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));

        switch (_state.ExtensionType)
        {
            case WiimoteExtensionType.Nunchuk:
                DrawNunchuk(context, _state.Nunchuk);
                break;
            case WiimoteExtensionType.ClassicController:
            case WiimoteExtensionType.ClassicControllerPro:
                DrawClassicController(context, _state.ClassicController);
                break;
            default:
                // The label above the drawing names the controller, so the placeholder must
                // not claim that nothing is plugged in while something is.
                DrawPlaceholder(
                    context,
                    _state.ExtensionConnected ? "No model for this controller yet" : "No extension plugged in");
                break;
        }
    }

    private static void DrawPlaceholder(DrawingContext context, string message)
    {
        var text = new FormattedText(
            message,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            13,
            MutedBrush);

        context.DrawText(
            text,
            new Point((DesignWidth - text.Width) / 2, (DesignHeight - text.Height) / 2));
    }

    private static void DrawNunchuk(DrawingContext context, NunchukState? nunchuk)
    {
        // Two overlapping rounded halves make the peanut shape of the Nunchuk.
        var upper = new RoundedRect(new Rect(128, 20, 64, 84), 32);
        var lower = new RoundedRect(new Rect(112, 74, 96, 92), 42);
        context.DrawRectangle(BodyShadeBrush, OutlinePen, lower);
        context.DrawRectangle(BodyBrush, OutlinePen, upper);

        // Analog stick.
        var stick = new Point(160, 62);
        context.DrawEllipse(StickWellBrush, OutlinePen, stick, 21, 21);
        var stickX = nunchuk?.StickX ?? 0;
        var stickY = nunchuk?.StickY ?? 0;
        context.DrawEllipse(ButtonBrush, null, stick + new Vector(stickX * 9, -stickY * 9), 11, 11);

        DrawRoundButton(context, new Rect(146, 108, 28, 24), "C", nunchuk?.ButtonC == true);

        // The Z button is the trigger that wraps the front of the Nunchuk.
        var zPressed = nunchuk?.ButtonZ == true;
        context.DrawRectangle(
            zPressed ? PressedBrush : BodyShadeBrush,
            OutlinePen,
            new RoundedRect(new Rect(126, 138, 68, 22), 10));
        DrawCenteredText(context, "Z", new Point(160, 149), zPressed ? ButtonTextBrush : ButtonBrush);
    }

    private static void DrawClassicController(DrawingContext context, ClassicControllerState? state)
    {
        // Shoulder bars sit behind the body, the triggers behind the shoulders.
        context.DrawRectangle(
            BodyShadeBrush, OutlinePen, new RoundedRect(new Rect(84, 14, 68, 18), 8));
        context.DrawRectangle(
            BodyShadeBrush, OutlinePen, new RoundedRect(new Rect(168, 14, 68, 18), 8));
        context.DrawRectangle(
            BodyShadeBrush, OutlinePen, new RoundedRect(new Rect(96, 24, 54, 16), 7));
        context.DrawRectangle(
            BodyShadeBrush, OutlinePen, new RoundedRect(new Rect(170, 24, 54, 16), 7));
        DrawCenteredText(context, "L", new Point(118, 32), ButtonBrush);
        DrawCenteredText(context, "R", new Point(202, 32), ButtonBrush);
        DrawCenteredText(context, "ZL", new Point(123, 17), ButtonBrush, 10);
        DrawCenteredText(context, "ZR", new Point(197, 17), ButtonBrush, 10);

        context.DrawRectangle(BodyBrush, OutlinePen, new RoundedRect(new Rect(48, 36, 224, 128), 34));

        // D-pad on the left.
        var cross = new Point(96, 92);
        DrawCross(context, cross, 26, state?.DPadUp == true, state?.DPadDown == true,
            state?.DPadLeft == true, state?.DPadRight == true);

        // A, B, X and Y in the diamond of the Classic Controller.
        DrawRoundButton(context, new Rect(158, 74, 26, 26), "X", state?.ButtonX == true);
        DrawRoundButton(context, new Rect(132, 98, 26, 26), "Y", state?.ButtonY == true);
        DrawRoundButton(context, new Rect(184, 98, 26, 26), "A", state?.ButtonA == true);
        DrawRoundButton(context, new Rect(158, 122, 26, 26), "B", state?.ButtonB == true);

        // Sticks below the two clusters.
        DrawStick(context, new Point(100, 140), state?.LeftStickX ?? 0, state?.LeftStickY ?? 0);
        DrawStick(context, new Point(210, 140), state?.RightStickX ?? 0, state?.RightStickY ?? 0);

        // Minus, Home and Plus between the clusters.
        DrawRoundButton(context, new Rect(138, 62, 18, 18), "\u2212", state?.ButtonMinus == true, 12);
        DrawRoundButton(context, new Rect(162, 62, 18, 18), "+", state?.ButtonPlus == true, 12);
        DrawRoundButton(context, new Rect(150, 88, 18, 18), "H", state?.ButtonHome == true, 11);

        // The analog triggers are drawn as a fill on the shoulder bars.
        if (state?.LeftTrigger > 0.05f)
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.Parse("#6C9CFF"), state.LeftTrigger),
                null,
                new RoundedRect(new Rect(96, 24, 54, 16), 7));
        }

        if (state?.RightTrigger > 0.05f)
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.Parse("#6C9CFF"), state.RightTrigger),
                null,
                new RoundedRect(new Rect(170, 24, 54, 16), 7));
        }
    }

    private static void DrawStick(DrawingContext context, Point center, float x, float y)
    {
        context.DrawEllipse(StickWellBrush, OutlinePen, center, 19, 19);
        context.DrawEllipse(ButtonBrush, null, center + new Vector(x * 8, -y * 8), 10, 10);
    }

    private static void DrawCross(
        DrawingContext context,
        Point center,
        double arm,
        bool up,
        bool down,
        bool left,
        bool right)
    {
        var thickness = arm * 0.62;
        var horizontal = new Rect(center.X - arm, center.Y - (thickness / 2), arm * 2, thickness);
        var vertical = new Rect(center.X - (thickness / 2), center.Y - arm, thickness, arm * 2);

        context.DrawRectangle(ButtonBrush, OutlinePen, new RoundedRect(horizontal, 4));
        context.DrawRectangle(ButtonBrush, OutlinePen, new RoundedRect(vertical, 4));

        if (up)
        {
            context.DrawRectangle(PressedBrush, null, new RoundedRect(
                new Rect(center.X - (thickness / 2), center.Y - arm, thickness, arm - (thickness / 2)), 4));
        }

        if (down)
        {
            context.DrawRectangle(PressedBrush, null, new RoundedRect(
                new Rect(center.X - (thickness / 2), center.Y + (thickness / 2), thickness, arm - (thickness / 2)), 4));
        }

        if (left)
        {
            context.DrawRectangle(PressedBrush, null, new RoundedRect(
                new Rect(center.X - arm, center.Y - (thickness / 2), arm - (thickness / 2), thickness), 4));
        }

        if (right)
        {
            context.DrawRectangle(PressedBrush, null, new RoundedRect(
                new Rect(center.X + (thickness / 2), center.Y - (thickness / 2), arm - (thickness / 2), thickness), 4));
        }
    }

    private static void DrawRoundButton(
        DrawingContext context,
        Rect rect,
        string label,
        bool pressed,
        double fontSize = 12)
    {
        var radius = Math.Min(rect.Width, rect.Height) / 2;
        context.DrawEllipse(
            pressed ? PressedBrush : ButtonBrush,
            OutlinePen,
            rect.Center,
            radius,
            radius);

        DrawCenteredText(
            context,
            label,
            rect.Center,
            pressed ? ButtonTextBrush : ButtonTextBrush,
            fontSize);
    }

    private static void DrawCenteredText(
        DrawingContext context,
        string label,
        Point center,
        IBrush brush,
        double fontSize = 12)
    {
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            brush);

        context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
    }
}
