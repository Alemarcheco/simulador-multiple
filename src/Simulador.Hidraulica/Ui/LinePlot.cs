using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Simulador.Hidraulica.Ui;

/// <summary>Gráfica X–Y sencilla con ejes autoescalados y líneas de referencia horizontales.</summary>
public sealed class LinePlot : Control
{
    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private readonly List<(string Name, Color Color, double[] X, double[] Y)> _series = [];
    private readonly List<(string Name, Color Color, double Y)> _refs = [];

    public string XLabel { get; set; } = "";
    public string YLabel { get; set; } = "";

    public LinePlot() { ClipToBounds = true; }

    public void Clear() { _series.Clear(); _refs.Clear(); InvalidateVisual(); }
    public void AddSeries(string name, Color color, double[] x, double[] y) { _series.Add((name, color, x, y)); InvalidateVisual(); }
    public void AddReference(string name, Color color, double y) { _refs.Add((name, color, y)); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        var fg = new SolidColorBrush(Color.Parse("#57606A"));
        ctx.FillRectangle(Brushes.White, b);
        var plot = new Rect(56, 24, Math.Max(b.Width - 56 - 16, 10), Math.Max(b.Height - 24 - 40, 10));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#E4E8EC")), 1);

        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var s in _series)
            for (int i = 0; i < s.X.Length; i++)
            {
                xMin = Math.Min(xMin, s.X[i]); xMax = Math.Max(xMax, s.X[i]);
                yMin = Math.Min(yMin, s.Y[i]); yMax = Math.Max(yMax, s.Y[i]);
            }
        foreach (var r in _refs) { yMin = Math.Min(yMin, r.Y); yMax = Math.Max(yMax, r.Y); }
        if (xMin > xMax) { xMin = 0; xMax = 1; yMin = 0; yMax = 1; }
        if (xMax - xMin < 1e-9) xMax = xMin + 1;
        double yStep = NiceStep((yMax - yMin) / 5);
        yMin = Math.Floor(yMin / yStep) * yStep;
        yMax = Math.Ceiling(yMax / yStep) * yStep;
        if (yMax - yMin < 1e-9) yMax = yMin + yStep;
        double xStep = NiceStep((xMax - xMin) / 6);

        Point Map(double x, double y) => new(plot.Left + (x - xMin) / (xMax - xMin) * plot.Width,
            plot.Bottom - (y - yMin) / (yMax - yMin) * plot.Height);

        for (double y = yMin; y <= yMax + yStep / 2; y += yStep)
        {
            var p = Map(xMin, y);
            ctx.DrawLine(grid, p, new Point(plot.Right, p.Y));
            Text(ctx, y.ToString("0.##", CultureInfo.CurrentCulture), new Point(4, p.Y - 7), fg);
        }
        for (double x = Math.Ceiling(xMin / xStep) * xStep; x <= xMax + 1e-9; x += xStep)
        {
            var p = Map(x, yMin);
            ctx.DrawLine(grid, new Point(p.X, plot.Top), p);
            Text(ctx, x.ToString("0.##", CultureInfo.CurrentCulture), new Point(p.X - 8, plot.Bottom + 4), fg);
        }
        Text(ctx, YLabel, new Point(4, 4), fg);
        Text(ctx, XLabel, new Point(plot.Right - 60, plot.Bottom + 20), fg);

        foreach (var r in _refs)
        {
            var pen = new Pen(new SolidColorBrush(r.Color), 1.2, new DashStyle([4, 4], 0));
            var p = Map(xMin, r.Y);
            ctx.DrawLine(pen, p, new Point(plot.Right, p.Y));
            Text(ctx, r.Name, new Point(plot.Right - 6 - Measure(r.Name), p.Y - 15), new SolidColorBrush(r.Color));
        }

        double lx = Math.Max(plot.Left + 8, 4 + Measure(YLabel) + 24);
        foreach (var s in _series)
        {
            var brush = new SolidColorBrush(s.Color);
            if (s.X.Length >= 2)
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(Map(s.X[0], s.Y[0]), false);
                    for (int i = 1; i < s.X.Length; i++) gc.LineTo(Map(s.X[i], s.Y[i]));
                    gc.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(brush, 1.6), g);
            }
            ctx.DrawLine(new Pen(brush, 3), new Point(lx, 12), new Point(lx + 16, 12));
            Text(ctx, s.Name, new Point(lx + 20, 4), fg);
            lx += 32 + Measure(s.Name);
        }
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0 || !double.IsFinite(raw)) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / mag;
        return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * mag;
    }

    private static FormattedText Fmt(string s, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11, brush);
    private static double Measure(string s) => Fmt(s, Brushes.Black).Width;
    private static void Text(DrawingContext ctx, string s, Point p, IBrush brush) => ctx.DrawText(Fmt(s, brush), p);
}
