using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Simulador.Circuitos.Simulation;

public sealed record PlotTrace(string Name, double[] X, double[] Y, Color Color);

/// <summary>Gráfica tipo osciloscopio: cuadrícula de 10×8 divisiones, escala automática y leyenda.</summary>
public sealed class PlotControl : Control
{
    private static readonly IBrush Screen = new SolidColorBrush(Color.Parse("#0F1A17"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#2C4A40")), 1);
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.Parse("#4F7D6E")), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#A9C7BC"));
    private static readonly Typeface Font = new("Inter");

    public static readonly Color[] Palette =
    [
        Color.Parse("#F2D13D"), Color.Parse("#3DD6F2"), Color.Parse("#F25C9A"), Color.Parse("#7CF23D"),
        Color.Parse("#F2963D"), Color.Parse("#B07CF2"), Color.Parse("#FFFFFF"), Color.Parse("#3DF2B4"),
    ];

    private List<PlotTrace> _traces = [];

    public string XUnit { get; set; } = "s";
    public string YUnit { get; set; } = "V";
    public bool LogX { get; set; }
    public string Title { get; set; } = "";

    public void SetTraces(IEnumerable<PlotTrace> traces)
    {
        _traces = traces.Where(t => t.X.Length > 1 && t.X.Length == t.Y.Length).ToList();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(Screen, b);
        const double left = 70, right = 16, top = 30, bottom = 36;
        var area = new Rect(left, top, Math.Max(10, b.Width - left - right), Math.Max(10, b.Height - top - bottom));

        if (Title.Length > 0) DrawText(ctx, Title, new Point(left, 8), 13, LabelBrush);

        if (_traces.Count == 0)
        {
            DrawGrid(ctx, area, 10, 8);
            DrawText(ctx, "Sin datos: pulsa Simular.", new Point(area.X + 12, area.Y + 12), 13, LabelBrush);
            return;
        }

        double fx(double x) => LogX ? Math.Log10(Math.Max(x, 1e-30)) : x;
        double xmin = _traces.Min(t => fx(t.X.Where(v => !LogX || v > 0).DefaultIfEmpty(1).Min()));
        double xmax = _traces.Max(t => fx(t.X.Max()));
        var ys = _traces.SelectMany(t => t.Y).Where(double.IsFinite).ToList();
        double ymin = ys.Count > 0 ? ys.Min() : 0, ymax = ys.Count > 0 ? ys.Max() : 1;
        if (xmax - xmin < 1e-30) xmax = xmin + 1;
        (ymin, ymax) = NiceRange(ymin, ymax);

        int xDivs = 10, yDivs = 8;
        DrawGrid(ctx, area, xDivs, yDivs);

        // Etiquetas de los ejes
        for (int i = 0; i <= yDivs; i++)
        {
            double v = ymax - (ymax - ymin) * i / yDivs;
            double y = area.Y + area.Height * i / yDivs;
            DrawText(ctx, EngineeringFormat.Format(v, YUnit, 3), new Point(4, y - 7), 11, LabelBrush);
        }
        for (int i = 0; i <= xDivs; i += 2)
        {
            double v = xmin + (xmax - xmin) * i / xDivs;
            if (LogX) v = Math.Pow(10, v);
            double x = area.X + area.Width * i / xDivs;
            DrawText(ctx, EngineeringFormat.Format(v, XUnit, 3), new Point(x - 20, area.Bottom + 6), 11, LabelBrush);
        }

        using (ctx.PushClip(area))
        {
            foreach (var t in _traces)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    bool started = false;
                    for (int i = 0; i < t.X.Length; i++)
                    {
                        if (!double.IsFinite(t.Y[i]) || LogX && t.X[i] <= 0) continue;
                        var p = new Point(
                            area.X + (fx(t.X[i]) - xmin) / (xmax - xmin) * area.Width,
                            area.Bottom - (t.Y[i] - ymin) / (ymax - ymin) * area.Height);
                        if (!started) { g.BeginFigure(p, false); started = true; }
                        else g.LineTo(p);
                    }
                    if (started) g.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(t.Color), 1.6), geo);
            }
        }

        // Leyenda
        double lx = area.Right - 8;
        foreach (var t in _traces.AsEnumerable().Reverse())
        {
            var ft = Text(t.Name, 12, new SolidColorBrush(t.Color));
            lx -= ft.Width + 18;
            ctx.FillRectangle(new SolidColorBrush(t.Color), new Rect(lx, 14, 10, 3));
            ctx.DrawText(ft, new Point(lx + 13, 7));
        }
    }

    private static void DrawGrid(DrawingContext ctx, Rect area, int xDivs, int yDivs)
    {
        for (int i = 0; i <= xDivs; i++)
        {
            double x = area.X + area.Width * i / xDivs;
            ctx.DrawLine(i == xDivs / 2 ? AxisPen : GridPen, new Point(x, area.Y), new Point(x, area.Bottom));
        }
        for (int i = 0; i <= yDivs; i++)
        {
            double y = area.Y + area.Height * i / yDivs;
            ctx.DrawLine(i == yDivs / 2 ? AxisPen : GridPen, new Point(area.X, y), new Point(area.Right, y));
        }
    }

    /// <summary>Redondea el rango vertical a valores cómodos y deja un margen.</summary>
    internal static (double Min, double Max) NiceRange(double min, double max)
    {
        if (Math.Abs(max - min) < 1e-12)
        {
            double pad = Math.Abs(max) > 1e-12 ? Math.Abs(max) * 0.5 : 1;
            return (min - pad, max + pad);
        }
        double span = max - min;
        double step = Math.Pow(10, Math.Floor(Math.Log10(span / 8)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (span / (step * m) <= 8) { step *= m; break; }
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step);
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush);

    private static void DrawText(DrawingContext ctx, string s, Point p, double size, IBrush brush) =>
        ctx.DrawText(Text(s, size, brush), p);
}
