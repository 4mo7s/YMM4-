using System.Windows;

namespace YmmTextAlign;

internal static class RenderedPlacement
{
    private const double Tolerance = 0.75;
    private const double Probe = 32;

    internal static Point Solve(Func<double, double, Rect> measure, double currentX, double currentY,
        int? horizontal, int? vertical, double width, double height, double marginX, double marginY, bool anchor)
    {
        double Target(int direction, double size, double margin) => direction switch
        { 0 => -size / 2 + margin, 1 => 0, _ => size / 2 - margin };
        var x = anchor && horizontal.HasValue ? Target(horizontal.Value, width, marginX) : currentX;
        var y = anchor && vertical.HasValue ? Target(vertical.Value, height, marginY) : currentY;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var bounds = measure(x, y);
            var error = Error(bounds);
            if (Norm(error) <= Tolerance) return new Point(x, y);

            // Ordinary drawing responds one-to-one to item position. Verify that
            // inexpensive move first; cameras or coordinate effects may not.
            if (TryMove(x + error.X, y + error.Y, Norm(error), out var direct, 0.2))
            { x = direct.X; y = direct.Y; continue; }

            var hFeature = anchor ? error.X > 0 ? 0 : error.X < 0 ? 2 : 1 : horizontal ?? 1;
            var vFeature = anchor ? error.Y > 0 ? 0 : error.Y < 0 ? 2 : 1 : vertical ?? 1;
            var fx = Feature(bounds.Left, bounds.Right, hFeature);
            var fy = Feature(bounds.Top, bounds.Bottom, vFeature);
            var bx = horizontal.HasValue ? measure(x + Probe, y) : bounds;
            var by = vertical.HasValue ? measure(x, y + Probe) : bounds;
            var a = (Feature(bx.Left, bx.Right, hFeature) - fx) / Probe;
            var b = (Feature(by.Left, by.Right, hFeature) - fx) / Probe;
            var c = (Feature(bx.Top, bx.Bottom, vFeature) - fy) / Probe;
            var d = (Feature(by.Top, by.Bottom, vFeature) - fy) / Probe;
            double dx, dy;
            if (horizontal.HasValue && vertical.HasValue)
            {
                var determinant = a * d - b * c;
                if (Math.Abs(determinant) < 0.000001) throw Unmovable();
                dx = (error.X * d - b * error.Y) / determinant;
                dy = (a * error.Y - c * error.X) / determinant;
            }
            else if (horizontal.HasValue)
            {
                if (Math.Abs(a) < 0.000001) throw Unmovable();
                dx = error.X / a; dy = 0;
            }
            else
            {
                if (Math.Abs(d) < 0.000001) throw Unmovable();
                dx = 0; dy = error.Y / d;
            }
            var improved = false;
            foreach (var fraction in new[] { 1d, 0.5, 0.25 })
                if (TryMove(x + dx * fraction, y + dy * fraction, Norm(error), out var next))
                { x = next.X; y = next.Y; improved = true; break; }
            if (!improved) throw new InvalidOperationException("エフェクト適用後の描画位置が安定せず、整列位置を確定できません");
        }
        throw new InvalidOperationException("エフェクト適用後の整列位置を確定できません");

        Point Error(Rect bounds)
        {
            return new Point(horizontal.HasValue ? AxisError(bounds.Left, bounds.Right, horizontal.Value, width, marginX) : 0,
                vertical.HasValue ? AxisError(bounds.Top, bounds.Bottom, vertical.Value, height, marginY) : 0);
        }
        double AxisError(double start, double end, int direction, double size, double margin)
        {
            var low = -size / 2 + margin;
            var high = size / 2 - margin;
            if (!double.IsFinite(start) || !double.IsFinite(end))
                throw new InvalidOperationException("エフェクト適用後の描画範囲を取得できません");
            if (end - start > high - low)
                throw new InvalidOperationException("エフェクトを含む描画全体が余白内に収まりません");
            return anchor ? Math.Clamp(0, low - start, high - end)
                : Target(direction, size, margin) - Feature(start, end, direction);
        }
        bool TryMove(double nx, double ny, double previousError, out Point next, double improvement = 0.8)
        {
            next = new Point(nx, ny);
            if (!double.IsFinite(nx) || !double.IsFinite(ny) || Math.Abs(nx) > 100000 || Math.Abs(ny) > 100000) return false;
            try
            {
                var error = Norm(Error(measure(nx, ny)));
                return error <= Tolerance || error < previousError * improvement;
            }
            catch (Exception) { return false; }
        }
    }
    private static double Feature(double start, double end, int direction) => direction switch
    { 0 => start, 1 => (start + end) / 2, _ => end };
    private static double Norm(Point error) => Math.Max(Math.Abs(error.X), Math.Abs(error.Y));
    private static Exception Unmovable() => new InvalidOperationException("このエフェクトでは指定したX/Yを変えても描画位置を整列できません");
}
