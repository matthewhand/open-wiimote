using OpenWiimote.Core.Models;

namespace OpenWiimote.Core.Pointer;

public sealed record PointerDisplay(string Id, string Name, int X, int Y, int Width, int Height)
{
    public string Fingerprint => $"{Id}:{X}:{Y}:{Width}:{Height}";
}
public sealed record CameraPoint(double X, double Y);
public sealed record PointerCalibration(string DisplayFingerprint, CameraPoint[] Corners);
public sealed class PointerPreferences
{
    public string? DeviceKey { get; set; }
    public string? DisplayId { get; set; }
    public Dictionary<string, PointerCalibration> Calibrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed record PointerSnapshot(bool Enabled, bool Available, string? Error, string? DeviceKey,
    string? DisplayId, bool Calibrated, bool Calibrating, int Step, bool Stable, bool IrVisible,
    double? X, double? Y, IReadOnlyList<PointerDisplay> Displays)
{
    public IReadOnlyList<IrPoint> IrPoints { get; init; } = [];
    public DateTimeOffset? LastIrReport { get; init; }
}

public interface IPointerOutput : IDisposable
{
    // No movement when aiming=false; release only buttons held by this output.
    void Move(int x, int y, bool left, bool right, bool aiming);
}
public interface IPointerBackend
{
    IReadOnlyList<PointerDisplay> GetDisplays();
    IPointerOutput Create(PointerDisplay display);
}

public static class PointerMath
{
    public const int AxisMax = 65535;
    public const double Inset = .05;
    public static bool TryMidpoint(IReadOnlyList<IrPoint>? points, out CameraPoint midpoint)
    {
        midpoint = new(0, 0);
        if (points is null) return false;
        var valid = points.Where(point => point is { IsValid: true }).ToArray();
        if (valid.Length != 2 || valid.Any(p => p.X is < 0 or > 1023 || p.Y is < 0 or > 767)) return false;
        if (Math.Pow(valid[0].X - valid[1].X, 2) + Math.Pow(valid[0].Y - valid[1].Y, 2) < 64) return false;
        midpoint = new((valid[0].X + valid[1].X) / 2.0, (valid[0].Y + valid[1].Y) / 2.0);
        return true;
    }

    // Four inset screen targets in clockwise order: TL, TR, BR, BL. A projective map
    // handles camera mirroring and perspective without assuming a 4:3 or 16:9 monitor.
    public static bool TryTransform(CameraPoint[]? corners, out double[] transform)
    {
        transform = [];
        if (corners is not { Length: 4 } || corners.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y)
            || p.X is < 0 or > 1023 || p.Y is < 0 or > 767)) return false;
        if (corners.Max(p => p.X) - corners.Min(p => p.X) < 60 || corners.Max(p => p.Y) - corners.Min(p => p.Y) < 45) return false;
        var signs = new List<double>();
        for (var n = 0; n < 4; n++)
        {
            var a = corners[n]; var b = corners[(n + 1) % 4]; var c = corners[(n + 2) % 4];
            signs.Add((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X));
        }
        if (signs.Any(x => Math.Abs(x) < 100) || signs.Any(x => Math.Sign(x) != Math.Sign(signs[0]))) return false;
        double[] u = [Inset, 1 - Inset, 1 - Inset, Inset], v = [Inset, Inset, 1 - Inset, 1 - Inset];
        var matrix = new double[8, 9];
        for (var n = 0; n < 4; n++)
        {
            var x = corners[n].X / 1023; var y = corners[n].Y / 767;
            double[] rowX = [x, y, 1, 0, 0, 0, -u[n] * x, -u[n] * y, u[n]];
            double[] rowY = [0, 0, 0, x, y, 1, -v[n] * x, -v[n] * y, v[n]];
            for (var j = 0; j < 9; j++) { matrix[n * 2, j] = rowX[j]; matrix[n * 2 + 1, j] = rowY[j]; }
        }
        for (var col = 0; col < 8; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < 8; row++) if (Math.Abs(matrix[row, col]) > Math.Abs(matrix[pivot, col])) pivot = row;
            if (Math.Abs(matrix[pivot, col]) < 1e-10) return false;
            for (var j = col; j < 9; j++) (matrix[col, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[col, j]);
            var divisor = matrix[col, col];
            for (var j = col; j < 9; j++) matrix[col, j] /= divisor;
            for (var row = 0; row < 8; row++)
            {
                if (row == col) continue;
                var factor = matrix[row, col];
                for (var j = col; j < 9; j++) matrix[row, j] -= factor * matrix[col, j];
            }
        }
        transform = Enumerable.Range(0, 8).Select(row => matrix[row, 8]).ToArray();
        // A projective pole inside the camera view could flip the cursor to the opposite
        // edge while aiming outside the calibrated quad. Reject that calibration.
        return transform.All(double.IsFinite) && new[] { 1d, 1 + transform[6], 1 + transform[7], 1 + transform[6] + transform[7] }.All(d => d > 1e-5);
    }

    public static bool TryMap(CameraPoint point, double[] transform, out double x, out double y)
    {
        x = y = 0;
        if (transform.Length != 8) return false;
        var cx = point.X / 1023; var cy = point.Y / 767;
        var denominator = transform[6] * cx + transform[7] * cy + 1;
        if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-8) return false;
        x = (transform[0] * cx + transform[1] * cy + transform[2]) / denominator;
        y = (transform[3] * cx + transform[4] * cy + transform[5]) / denominator;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        return true;
    }
}
