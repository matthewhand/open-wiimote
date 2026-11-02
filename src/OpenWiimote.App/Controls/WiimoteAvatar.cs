using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OpenWiimote.Core.Models;

namespace OpenWiimote.App.Controls;

public sealed class WiimoteAvatar : Control
{
    public const string WiimoteTheme = "wiimote";
    public const string ScrewdriverTheme = "screwdriver";

    private const float CameraDistance = 22f;
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.45f, 0.8f, 1.05f));
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromArgb(80, 6, 10, 20)), 0.8);

    private readonly List<Face> _faces = [];
    private readonly List<Label> _labels = [];
    private readonly List<Led> _leds = [];
    private readonly List<BatterySegment> _batterySegments = [];
    private readonly DispatcherTimer _timer;

    private WiimoteState _state = new();
    private Quaternion _target = Quaternion.Identity;
    private Quaternion _current = Quaternion.Identity;
    private Vector3 _lastTiltAxis = Vector3.UnitX;
    private string _theme = WiimoteTheme;
    private byte _ledMask;
    private int _batteryPercent = -1;
    private bool _stateSeen;
    private float _manualYaw;
    private bool _dragging;
    private Point _dragStart;
    private float _dragStartYaw;

    public WiimoteAvatar()
    {
        IsHitTestVisible = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        ClipToBounds = true;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += (_, _) =>
        {
            var dot = Quaternion.Dot(_current, _target);
            var target = dot < 0 ? Quaternion.Negate(_target) : _target;
            _current = Quaternion.Normalize(Quaternion.Slerp(_current, target, 0.18f));
            InvalidateVisual();
        };
    }

    public string CurrentTheme => _theme;

    public void SetTheme(string theme)
    {
        var normalized = string.Equals(theme, ScrewdriverTheme, StringComparison.OrdinalIgnoreCase)
            ? ScrewdriverTheme
            : WiimoteTheme;

        if (_theme == normalized)
        {
            return;
        }

        _theme = normalized;
        BuildModel();
        InvalidateVisual();
    }

    public void SetState(WiimoteState state)
    {
        _state = state;
        _stateSeen = true;
        UpdateOrientation();
        InvalidateVisual();
    }

    public void SetLedMask(byte mask)
    {
        _ledMask = (byte)(mask & 0xF0);
        InvalidateVisual();
    }

    public void SetBatteryPercent(int percent)
    {
        var clamped = Math.Clamp(percent, -1, 100);
        if (_batteryPercent == clamped)
        {
            return;
        }

        _batteryPercent = clamped;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BuildModel();
        UpdateOrientation();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _dragStart = point.Position;
        _dragStartYaw = _manualYaw;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        _manualYaw = _dragStartYaw + (float)(position.X - _dragStart.X) * 0.012f;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateOrientation()
    {
        if (!_stateSeen)
        {
            _target = Quaternion.Identity;
            return;
        }

        var raw = new Vector3(_state.AccelY, _state.AccelX, _state.AccelZ);
        var length = raw.Length();
        if (length < 0.15f)
        {
            return;
        }

        var up = raw / length;
        var dot = Math.Clamp(up.Z, -1f, 1f);
        var cross = Vector3.Cross(Vector3.UnitZ, up);
        if (cross.LengthSquared() > 0.000001f)
        {
            _lastTiltAxis = Vector3.Normalize(cross);
        }

        _target = dot > 0.99999f
            ? Quaternion.Identity
            : Quaternion.CreateFromAxisAngle(_lastTiltAxis, MathF.Acos(dot));
    }

    /// <summary>
    /// The shared view transform for one rendered frame: it places a model point into
    /// camera space, reorients a model normal and projects a camera point onto the
    /// screen. Every draw pass goes through it, so the sensor rotation, presentation
    /// yaw and perspective math live in one place.
    /// </summary>
    private readonly struct Camera
    {
        private readonly Matrix4x4 _view;
        private readonly Point _center;
        private readonly float _focal;

        public Camera(Matrix4x4 view, Point center, float focal)
        {
            _view = view;
            _center = center;
            _focal = focal;
        }

        /// <summary>Places a model point in camera space.</summary>
        public Vector3 Place(Vector3 local) => Vector3.Transform(local, _view);

        /// <summary>Reorients a model normal without translating it.</summary>
        public Vector3 Direction(Vector3 normal) => Vector3.TransformNormal(normal, _view);

        /// <summary>
        /// Projects a camera space point onto the screen and reports the perspective
        /// scale at that depth, so sizes can scale with distance too. Returns false
        /// while the point is at or behind the camera.
        /// </summary>
        public bool TryProject(Vector3 world, out Point point, out float scale)
        {
            var distance = CameraDistance - world.Z;
            if (distance < 1f)
            {
                point = default;
                scale = 0f;
                return false;
            }

            scale = _focal / distance;
            point = new Point(_center.X + world.X * scale, _center.Y - world.Y * scale);
            return true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Bounds.Size;
        if (size.Width < 10 || size.Height < 10)
        {
            return;
        }

        DrawBackdrop(context, size);

        var center = new Point(size.Width * 0.5, size.Height * 0.52);
        var modelScale = Math.Min(size.Width / 12.2, size.Height / 6.8);
        var camera = new Camera(
            Matrix4x4.CreateFromQuaternion(_current) * Matrix4x4.CreateRotationY(_manualYaw - 0.36f),
            center,
            (float)(modelScale * CameraDistance));
        var projected = new List<ProjectedFace>(_faces.Count);

        foreach (var face in _faces)
        {
            var pressed = face.Button != WiimoteButton.None &&
                          (_state.Buttons & face.Button) != 0;
            var shift = pressed ? -0.16f : 0f;
            var points = new Point[face.Points.Length];
            var depth = 0f;
            var visible = true;

            for (var i = 0; i < face.Points.Length; i++)
            {
                var world = camera.Place(face.Points[i] + new Vector3(0, 0, shift));
                if (!camera.TryProject(world, out points[i], out _))
                {
                    visible = false;
                    break;
                }

                depth += world.Z;
            }

            if (!visible)
            {
                continue;
            }

            var normal = camera.Direction(face.Normal);
            var shade = 0.52f + 0.48f * MathF.Max(0, Vector3.Dot(normal, LightDirection));
            var color = Shade(face.Color, shade);
            if (pressed)
            {
                color = Shade(color, 0.72f);
            }

            projected.Add(new ProjectedFace(
                points,
                color,
                depth / points.Length));
        }

        projected.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var face in projected)
        {
            context.DrawGeometry(new SolidColorBrush(face.Color), EdgePen, CreateGeometry(face.Points));
        }

        DrawLabels(context, camera);
        DrawLeds(context, camera);
        DrawBattery(context, camera);
    }

    private void DrawBattery(DrawingContext context, in Camera camera)
    {
        if (camera.Direction(new Vector3(0, 0, 1)).Z < 0.05f)
        {
            return;
        }

        var percent = _batteryPercent;
        foreach (var segment in _batterySegments)
        {
            if (!camera.TryProject(camera.Place(segment.Center), out var point, out var scale))
            {
                continue;
            }

            var lit = percent >= 0 && percent >= segment.Threshold - 24;
            var color = lit ? BatteryColor(percent) : Color.Parse("#1A2432");
            var width = scale * segment.Size.X;
            var height = scale * segment.Size.Y;

            context.DrawRectangle(
                new SolidColorBrush(color),
                EdgePen,
                new Rect(point.X - width / 2, point.Y - height / 2, width, height));
        }
    }

    private static Color BatteryColor(int percent)
    {
        if (percent < 0)
        {
            return Color.Parse("#8FA3C8");
        }

        if (percent <= 20)
        {
            return Color.Parse("#FF7186");
        }

        if (percent <= 45)
        {
            return Color.Parse("#F8C95C");
        }

        return Color.Parse("#4DDC9A");
    }

    private static void DrawBackdrop(DrawingContext context, Size size)
    {
        var center = new Point(size.Width * 0.5, size.Height * 0.74);
        var radiusX = size.Width * 0.34;
        var radiusY = Math.Max(8, size.Height * 0.055);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(80, 3, 7, 15)),
            null,
            center,
            radiusX,
            radiusY);
    }

    private void DrawLabels(DrawingContext context, in Camera camera)
    {
        foreach (var label in _labels)
        {
            if (camera.Direction(label.Normal).Z < 0.12f)
            {
                continue;
            }

            var pressed = label.Button != WiimoteButton.None &&
                          (_state.Buttons & label.Button) != 0;
            var shift = pressed ? -0.16f : 0f;
            if (!camera.TryProject(camera.Place(label.Center + new Vector3(0, 0, shift)), out var point, out _))
            {
                continue;
            }

            var text = new FormattedText(
                label.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                label.Size,
                new SolidColorBrush(label.Color));

            context.DrawText(text, new Point(point.X - text.Width * 0.5, point.Y - text.Height * 0.5));
        }
    }

    private void DrawLeds(DrawingContext context, in Camera camera)
    {
        if (camera.Direction(new Vector3(0, 0, 1)).Z < 0.05f)
        {
            return;
        }

        foreach (var led in _leds)
        {
            if (!camera.TryProject(camera.Place(led.Center), out var point, out var scale))
            {
                continue;
            }

            var active = (_ledMask & led.Mask) != 0;
            var radius = Math.Max(3.2, scale * led.Radius * 1.25);

            // Bright socket so the bar reads clearly even when LEDs are off.
            context.DrawEllipse(
                new SolidColorBrush(Color.Parse("#101823")),
                null,
                point,
                radius * 1.7,
                radius * 1.7);
            context.DrawEllipse(
                null,
                new Pen(new SolidColorBrush(Color.FromArgb(72, 157, 198, 255)), Math.Max(1.2, radius * 0.18)),
                point,
                radius * 1.7,
                radius * 1.7);
            context.DrawEllipse(
                new SolidColorBrush(Color.Parse(active ? "#B9EEFF" : "#3A4F6B")),
                null,
                point,
                radius,
                radius);

            if (active)
            {
                context.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(97, 130, 215, 255)),
                    null,
                    point,
                    radius * 3.0,
                    radius * 3.0);
            }

            // Player number makes the lit LED unambiguous.
            var index = _leds.IndexOf(led) + 1;
            if (index >= 1 && index <= 4)
            {
                var text = new FormattedText(
                    index.ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    Math.Max(9, radius * 1.6),
                    new SolidColorBrush(Color.Parse(active ? "#06101F" : "#DCE8FF")));
                context.DrawText(text, new Point(point.X - text.Width * 0.5, point.Y - text.Height * 0.5));
            }
        }
    }

    private static StreamGeometry CreateGeometry(Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true);
            for (var i = 1; i < points.Length; i++)
            {
                context.LineTo(points[i]);
            }

            context.EndFigure(true);
        }

        return geometry;
    }

    private static Color Shade(Color color, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1.5f);
        return Color.FromRgb(
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));
    }

    private void BuildModel()
    {
        _faces.Clear();
        _labels.Clear();
        _leds.Clear();

        if (_theme == ScrewdriverTheme)
        {
            BuildScrewdriver();
        }
        else
        {
            BuildWiimote();
        }
    }

    private void BuildWiimote()
    {
        const float front = 0.86f;
        var shell = Color.Parse("#ECECEE");
        var panel = Color.Parse("#2B303A");
        var dark = Color.Parse("#20242C");
        var light = Color.Parse("#D7DCE5");
        var red = Color.Parse("#D73F4E");

        AddBox(Vector3.Zero, new Vector3(10.4f, 2.4f, 1.7f), shell);
        AddBox(new Vector3(0, 0, front + 0.01f), new Vector3(9.8f, 1.9f, 0.02f), Color.Parse("#F5F6F8"));

        AddBox(new Vector3(-3.05f, 0.2f, front + 0.05f), new Vector3(1.8f, 1.55f, 0.10f), panel);
        AddButton(new Vector3(-3.05f, 0.68f, front + 0.11f), new Vector3(0.62f, 0.36f, 0.10f), dark, WiimoteButton.Up, "▲");
        AddButton(new Vector3(-3.05f, -0.28f, front + 0.11f), new Vector3(0.62f, 0.36f, 0.10f), dark, WiimoteButton.Down, "▼");
        AddButton(new Vector3(-3.49f, 0.2f, front + 0.11f), new Vector3(0.36f, 0.62f, 0.10f), dark, WiimoteButton.Left, "◀");
        AddButton(new Vector3(-2.61f, 0.2f, front + 0.11f), new Vector3(0.36f, 0.62f, 0.10f), dark, WiimoteButton.Right, "▶");

        AddButton(new Vector3(2.75f, 0.25f, front + 0.11f), new Vector3(0.86f, 0.58f, 0.10f), Color.Parse("#C9E2FF"), WiimoteButton.A, "A", 16);
        AddButton(new Vector3(1.05f, 0.66f, front + 0.11f), new Vector3(0.34f, 0.24f, 0.09f), light, WiimoteButton.One, "1");
        AddButton(new Vector3(1.05f, -0.08f, front + 0.11f), new Vector3(0.34f, 0.24f, 0.09f), light, WiimoteButton.Two, "2");
        AddButton(new Vector3(-0.55f, 0.72f, front + 0.11f), new Vector3(0.28f, 0.22f, 0.09f), light, WiimoteButton.Plus, "+");
        AddButton(new Vector3(-0.55f, -0.22f, front + 0.11f), new Vector3(0.28f, 0.22f, 0.09f), light, WiimoteButton.Minus, "−");
        AddButton(new Vector3(0.22f, 0.30f, front + 0.11f), new Vector3(0.36f, 0.26f, 0.09f), Color.Parse("#2E6DE2"), WiimoteButton.Home, "H");
        AddButton(new Vector3(4.32f, -0.55f, front + 0.11f), new Vector3(0.62f, 0.48f, 0.11f), red, WiimoteButton.B, "B");

        for (var i = 0; i < 5; i++)
        {
            AddBox(
                new Vector3(3.75f + i * 0.13f, -0.72f, front + 0.02f),
                new Vector3(0.055f, 0.055f, 0.02f),
                Color.Parse("#242A34"));
        }

        // The player LEDs sit in a dark window below the speaker, the way the real remote is
        // printed, so the bar reads as a bar instead of four specks on the white shell.
        AddBox(
            new Vector3(-0.99f, -0.62f, front + 0.04f),
            new Vector3(1.66f, 0.36f, 0.04f),
            Color.Parse("#0A1220"));

        var ledX = new[] { -1.62f, -1.20f, -0.78f, -0.36f };
        for (var i = 0; i < ledX.Length; i++)
        {
            _leds.Add(new Led(new Vector3(ledX[i], -0.62f, front + 0.07f), (byte)(0x10 << i), 0.11f));
        }

        // Battery segments sit on the bottom edge of the remote, below the LED window.
        AddBox(
            new Vector3(-0.99f, -1.06f, front + 0.02f),
            new Vector3(1.50f, 0.24f, 0.04f),
            Color.Parse("#0A1220"));

        var batteryX = new[] { -1.55f, -1.25f, -0.95f, -0.65f };
        for (var i = 0; i < batteryX.Length; i++)
        {
            _batterySegments.Add(new BatterySegment(
                new Vector3(batteryX[i], -1.06f, front + 0.06f),
                new Vector3(0.22f, 0.14f, 0.04f),
                (i + 1) * 25));
        }
    }

    private void BuildScrewdriver()
    {
        var body = Color.Parse("#263B59");
        var bodyDark = Color.Parse("#18253A");
        var grip = Color.Parse("#111923");
        var silver = Color.Parse("#A9B7C8");
        var glow = Color.Parse("#58C7FF");

        AddCylinderX(-0.2f, 9.3f, 0.82f, body, 18);

        AddCylinderX(-3.75f, 2.2f, 0.96f, grip, 18);
        for (var i = 0; i < 5; i++)
        {
            AddCylinderX(-4.55f + i * 0.36f, 0.10f, 1.03f, Color.Parse("#405571"), 18);
        }

        AddCylinderX(4.78f, 0.60f, 0.58f, silver, 18);
        AddCylinderX(5.20f, 0.18f, 0.34f, Color.Parse("#D5E5F4"), 18);
        AddCylinderX(4.38f, 0.16f, 0.78f, Color.Parse("#5B718E"), 18);

        AddBox(new Vector3(-0.25f, 0, 0.83f), new Vector3(2.6f, 1.10f, 0.08f), bodyDark);
        AddButton(new Vector3(0.45f, 0.22f, 0.91f), new Vector3(0.34f, 0.26f, 0.09f), glow, WiimoteButton.A, "A");
        AddButton(new Vector3(-0.08f, -0.30f, 0.91f), new Vector3(0.32f, 0.20f, 0.09f), Color.Parse("#E8485A"), WiimoteButton.B, "B");
        AddButton(new Vector3(-0.72f, 0.24f, 0.91f), new Vector3(0.24f, 0.20f, 0.09f), silver, WiimoteButton.One, "1");
        AddButton(new Vector3(-1.08f, -0.30f, 0.91f), new Vector3(0.24f, 0.20f, 0.09f), silver, WiimoteButton.Two, "2");

        // The player number is printed on a black window, so the four LEDs stay readable.
        AddBox(
            new Vector3(-0.975f, 0.62f, 0.88f),
            new Vector3(1.12f, 0.28f, 0.04f),
            Color.Parse("#0A1220"));

        var ledX = new[] { -1.35f, -1.10f, -0.85f, -0.60f };
        for (var i = 0; i < ledX.Length; i++)
        {
            _leds.Add(new Led(new Vector3(ledX[i], 0.62f, 0.91f), (byte)(0x10 << i), 0.075f));
        }

        // Battery segments sit below the LED window on the front plate.
        AddBox(
            new Vector3(-0.975f, -0.60f, 0.86f),
            new Vector3(1.50f, 0.24f, 0.04f),
            Color.Parse("#0A1220"));

        var batteryX = new[] { -1.55f, -1.25f, -0.95f, -0.65f };
        for (var i = 0; i < batteryX.Length; i++)
        {
            _batterySegments.Add(new BatterySegment(
                new Vector3(batteryX[i], -0.60f, 0.90f),
                new Vector3(0.22f, 0.14f, 0.04f),
                (i + 1) * 25));
        }
    }

    private void AddButton(
        Vector3 center,
        Vector3 size,
        Color color,
        WiimoteButton button,
        string text,
        double fontSize = 11)
    {
        AddBox(center, size, color, button);
        _labels.Add(new Label(
            center + new Vector3(0, 0, size.Z * 0.5f + 0.01f),
            new Vector3(0, 0, 1),
            text,
            button,
            fontSize,
            button is WiimoteButton.A or WiimoteButton.B ? Color.Parse("#0C1728") : Color.Parse("#E8EEF9")));
    }

    private void AddBox(Vector3 center, Vector3 size, Color color, WiimoteButton button = WiimoteButton.None)
    {
        var half = size * 0.5f;
        var left = center.X - half.X;
        var right = center.X + half.X;
        var bottom = center.Y - half.Y;
        var top = center.Y + half.Y;
        var back = center.Z - half.Z;
        var front = center.Z + half.Z;

        AddFace(
            [
                new Vector3(left, bottom, front),
                new Vector3(right, bottom, front),
                new Vector3(right, top, front),
                new Vector3(left, top, front)
            ],
            new Vector3(0, 0, 1),
            color,
            button);

        AddFace(
            [
                new Vector3(right, bottom, back),
                new Vector3(left, bottom, back),
                new Vector3(left, top, back),
                new Vector3(right, top, back)
            ],
            new Vector3(0, 0, -1),
            color,
            button);

        AddFace(
            [
                new Vector3(left, top, front),
                new Vector3(right, top, front),
                new Vector3(right, top, back),
                new Vector3(left, top, back)
            ],
            new Vector3(0, 1, 0),
            color,
            button);

        AddFace(
            [
                new Vector3(left, bottom, back),
                new Vector3(right, bottom, back),
                new Vector3(right, bottom, front),
                new Vector3(left, bottom, front)
            ],
            new Vector3(0, -1, 0),
            color,
            button);

        AddFace(
            [
                new Vector3(left, bottom, back),
                new Vector3(left, bottom, front),
                new Vector3(left, top, front),
                new Vector3(left, top, back)
            ],
            new Vector3(-1, 0, 0),
            color,
            button);

        AddFace(
            [
                new Vector3(right, bottom, front),
                new Vector3(right, bottom, back),
                new Vector3(right, top, back),
                new Vector3(right, top, front)
            ],
            new Vector3(1, 0, 0),
            color,
            button);
    }

    private void AddCylinderX(float centerX, float length, float radius, Color color, int segments)
    {
        var half = length * 0.5f;
        for (var i = 0; i < segments; i++)
        {
            var a0 = MathF.PI * 2f * i / segments;
            var a1 = MathF.PI * 2f * (i + 1) / segments;
            var y0 = MathF.Cos(a0) * radius;
            var z0 = MathF.Sin(a0) * radius;
            var y1 = MathF.Cos(a1) * radius;
            var z1 = MathF.Sin(a1) * radius;
            var mid = (a0 + a1) * 0.5f;
            var normal = Vector3.Normalize(new Vector3(0, MathF.Cos(mid), MathF.Sin(mid)));

            AddFace(
                [
                    new Vector3(centerX - half, y0, z0),
                    new Vector3(centerX + half, y0, z0),
                    new Vector3(centerX + half, y1, z1),
                    new Vector3(centerX - half, y1, z1)
                ],
                normal,
                color);
        }

        var cap = new Point3[segments];
        for (var i = 0; i < segments; i++)
        {
            var angle = MathF.PI * 2f * i / segments;
            cap[i] = new Point3(centerX + half, MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }

        AddFace(cap.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray(), new Vector3(1, 0, 0), color);

        cap = new Point3[segments];
        for (var i = 0; i < segments; i++)
        {
            var angle = MathF.PI * 2f * i / segments;
            cap[i] = new Point3(centerX - half, MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }

        AddFace(cap.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray(), new Vector3(-1, 0, 0), color);
    }

    private void AddFace(Vector3[] points, Vector3 normal, Color color, WiimoteButton button = WiimoteButton.None)
    {
        _faces.Add(new Face(points, normal, color, button));
    }

    private sealed record Face(Vector3[] Points, Vector3 Normal, Color Color, WiimoteButton Button);

    private sealed record Label(
        Vector3 Center,
        Vector3 Normal,
        string Text,
        WiimoteButton Button,
        double Size,
        Color Color);

    private sealed record Led(Vector3 Center, byte Mask, float Radius);

    private sealed record BatterySegment(Vector3 Center, Vector3 Size, int Threshold);

    private sealed record ProjectedFace(Point[] Points, Color Color, float Depth);

    private readonly record struct Point3(float X, float Y, float Z);
}

