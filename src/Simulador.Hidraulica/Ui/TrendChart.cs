using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Simulador.Hidraulica.Ui;

/// <summary>Gráfica de tendencias en vivo (eje Y en %, eje X en segundos simulados).</summary>
public sealed class TrendChart : Control
{
    private const int MaxPoints = 2000;
    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly Color[] Palette =
        [Color.Parse("#2A78D6"), Color.Parse("#E8590C"), Color.Parse("#2DA44E"), Color.Parse("#8250DF"), Color.Parse("#CF222E"), Color.Parse("#0E8A8A")];

    private readonly List<(string Name, List<Point> Points)> _series = [];

    public TrendChart() { ClipToBounds = true; }

    public void Clear() { _series.Clear(); InvalidateVisual(); }

    /// <summary>Añade un punto (t en s, valor en %).</summary>
    public void Add(string series, double t, double percent)
    {
        var s = _series.FirstOrDefault(x => x.Name == series);
        if (s.Points is null) { s = (series, new List<Point>()); _series.Add(s); }
        s.Points.Add(new Point(t, percent));
        if (s.Points.Count > MaxPoints) s.Points.RemoveRange(0, s.Points.Count - MaxPoints);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        var fg = new SolidColorBrush(Color.Parse("#57606A"));
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#FFFFFF")), b);
        var plot = new Rect(44, 10, Math.Max(b.Width - 44 - 160, 10), Math.Max(b.Height - 34, 10));
        var axis = new Pen(new SolidColorBrush(Color.Parse("#D0D7DE")), 1);

        double tMin = double.MaxValue, tMax = double.MinValue;
        foreach (var (_, pts) in _series)
            if (pts.Count > 0) { tMin = Math.Min(tMin, pts[0].X); tMax = Math.Max(tMax, pts[^1].X); }
        if (tMin == double.MaxValue) { tMin = 0; tMax = 60; }
        if (tMax - tMin < 1) tMax = tMin + 1;

        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4;
            ctx.DrawLine(axis, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(ctx, $"{i * 25} %", new Point(4, y - 7), fg);
        }
        Text(ctx, $"{tMin:0} s", new Point(plot.Left, plot.Bottom + 4), fg);
        Text(ctx, $"{tMax:0} s", new Point(plot.Right - 40, plot.Bottom + 4), fg);

        for (int k = 0; k < _series.Count; k++)
        {
            var (name, pts) = _series[k];
            var color = new SolidColorBrush(Palette[k % Palette.Length]);
            if (pts.Count >= 2)
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var p = new Point(plot.Left + (pts[i].X - tMin) / (tMax - tMin) * plot.Width,
                            plot.Bottom - Math.Clamp(pts[i].Y, 0, 100) / 100 * plot.Height);
                        if (i == 0) gc.BeginFigure(p, false); else gc.LineTo(p);
                    }
                    gc.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(color, 2), g);
            }
            double ly = plot.Top + k * 18;
            ctx.DrawLine(new Pen(color, 3), new Point(plot.Right + 12, ly + 8), new Point(plot.Right + 28, ly + 8));
            string last = pts.Count > 0 ? $" {pts[^1].Y:0} %" : "";
            Text(ctx, name + last, new Point(plot.Right + 34, ly), fg);
        }
    }

    private static void Text(DrawingContext ctx, string s, Point p, IBrush brush) =>
        ctx.DrawText(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11, brush), p);
}
