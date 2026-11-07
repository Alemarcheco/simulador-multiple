using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Simulador.Banco.Ui;

/// <summary>
/// Gráfica de tendencias del banco con toda la historia del proceso. Sigue el tiempo real hasta que el usuario la
/// mueve: arrastrar con el ratón va atrás o adelante, la rueda acerca o aleja alrededor del puntero, la franja de abajo
/// salta a cualquier momento y el doble clic vuelve a seguir en vivo. Al pasar el ratón se leen los valores en ese instante.
/// </summary>
public sealed class HistoryChart : Control
{
    public const double DefaultSpan = 120;
    private const double MinSpan = 1;
    private const double Left = 48, LegendWidth = 190, Top = 22, OverviewHeight = 10;

    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly Color[] Palette =
        [Color.Parse("#2A78D6"), Color.Parse("#E8590C"), Color.Parse("#2DA44E"), Color.Parse("#8250DF"), Color.Parse("#CF222E"), Color.Parse("#0E8A8A")];
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#57606A"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2A78D6"));
    private static readonly Pen Grid = new(new SolidColorBrush(Color.Parse("#D0D7DE")), 1);
    private static readonly Pen CursorPen = new(new SolidColorBrush(Color.Parse("#8C959F")), 1, new DashStyle([4, 3], 0));

    private double _viewEnd;
    private Point? _dragStart;
    private double _dragEnd0;
    private bool _dragOverview;
    private double? _hoverX;
    private int _lastCount = -1;

    /// <param name="history">Historial compartido (para abrir la misma gráfica en otra ventana); si falta, uno propio.</param>
    public HistoryChart(TrendHistory? history = null)
    {
        History = history ?? new TrendHistory();
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Cursor ??= new Cursor(StandardCursorType.SizeWestEast);
    }

    public TrendHistory History { get; }

    /// <summary>True mientras la gráfica sigue el último instante simulado.</summary>
    public bool IsLive { get; private set; } = true;

    /// <summary>Segundos visibles en la ventana.</summary>
    public double Span { get; private set; } = DefaultSpan;

    /// <summary>Instante del borde derecho de la ventana.</summary>
    public double ViewEnd => IsLive ? (History.Range?.End ?? 0) : _viewEnd;

    public void GoLive() { IsLive = true; Span = DefaultSpan; InvalidateVisual(); }

    public void ShowAll()
    {
        if (History.Range is not { } r) return;
        Span = Math.Max(MinSpan, r.End - r.Start);
        IsLive = true;
        InvalidateVisual();
    }

    /// <summary>Llamar en cada refresco de la interfaz: redibuja solo si hay datos nuevos y se está en vivo.</summary>
    public void Refresh()
    {
        int n = History.Count;
        if (n != _lastCount && (IsLive || n < _lastCount)) { _lastCount = n; InvalidateVisual(); }
        else _lastCount = n;
    }

    private Rect Plot => new(Left, Top, Math.Max(Bounds.Width - Left - LegendWidth, 10), Math.Max(Bounds.Height - Top - 24 - OverviewHeight - 6, 10));
    private Rect Overview { get { var p = Plot; return new Rect(p.Left, p.Bottom + 22, p.Width, OverviewHeight); } }

    private (double From, double To) Window()
    {
        double end = ViewEnd, span = Span;
        if (IsLive && History.Range is { } r && r.End - r.Start < span) return (r.Start, Math.Max(r.Start + span, r.End));
        return (end - span, end);
    }

    /// <summary>Mueve la ventana de modo que termine en <paramref name="end"/>; si llega al final, vuelve a vivo.</summary>
    public void PanTo(double end)
    {
        if (History.Range is not { } r) return;
        double min = r.Start + Math.Min(Span, r.End - r.Start);
        if (end >= r.End - 1e-9) { IsLive = true; }
        else { IsLive = false; _viewEnd = Math.Max(min, end); }
        InvalidateVisual();
    }

    /// <summary>Acerca (factor &lt; 1) o aleja la ventana manteniendo <paramref name="anchor"/> en el mismo sitio de la pantalla.</summary>
    public void Zoom(double factor, double anchor)
    {
        var (from, to) = Window();
        double frac = to > from ? Math.Clamp((anchor - from) / (to - from), 0, 1) : 1;
        double total = History.Range is { } r ? Math.Max(r.End - r.Start, 10) : 10;
        double span = Math.Clamp(Span * factor, MinSpan, total);
        bool live = IsLive && frac > 0.98;
        Span = span;
        if (live) { InvalidateVisual(); return; }
        PanTo(anchor + (1 - frac) * span);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (e.ClickCount >= 2) { GoLive(); e.Handled = true; return; }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragOverview = Overview.Inflate(new Thickness(0, 4)).Contains(pt);
        if (_dragOverview) JumpFromOverview(pt.X);
        _dragStart = pt;
        _dragEnd0 = ViewEnd;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        var plot = Plot;
        _hoverX = plot.Contains(pt) ? pt.X : null;
        if (_dragStart is { } s)
        {
            if (_dragOverview) JumpFromOverview(pt.X);
            else PanTo(_dragEnd0 - (pt.X - s.X) / plot.Width * Span);
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var plot = Plot;
        var (from, to) = Window();
        double x = Math.Clamp(e.GetPosition(this).X, plot.Left, plot.Right);
        double anchor = from + (x - plot.Left) / plot.Width * (to - from);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))   // Mayús + rueda: desplazar sin zoom
            PanTo(ViewEnd - Math.Sign(e.Delta.Y + e.Delta.X) * Span * 0.2);
        else
            Zoom(e.Delta.Y > 0 ? 0.8 : 1.25, anchor);
        e.Handled = true;
    }

    private void JumpFromOverview(double x)
    {
        if (History.Range is not { } r) return;
        var o = Overview;
        double t = r.Start + Math.Clamp((x - o.Left) / o.Width, 0, 1) * (r.End - r.Start);
        PanTo(t + Span / 2);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        var plot = Plot;
        var names = History.Names;
        var range = History.Range;
        var (from, to) = Window();
        if (to - from < 1e-6) to = from + 1;

        // Rótulo de estado arriba a la izquierda
        string state = IsLive ? "● En vivo" : "◀ Historia";
        Text(ctx, state, new Point(Left, 3), IsLive ? Accent : Ink, 11, true);
        Text(ctx, "arrastra: ir atrás/adelante · rueda: zoom · doble clic: en vivo", new Point(Left + 80, 3), Ink, 10.5);

        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4;
            ctx.DrawLine(Grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(ctx, $"{i * 25} %", new Point(4, y - 7), Ink);
        }
        double step = NiceStep((to - from) / 6);
        for (double t = Math.Ceiling(from / step) * step; t <= to + 1e-9; t += step)
        {
            double x = X(t, from, to, plot);
            ctx.DrawLine(Grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            Text(ctx, FormatTime(t, step), new Point(x - 18, plot.Bottom + 4), Ink);
        }

        int buckets = Math.Max(10, (int)plot.Width);
        using (ctx.PushClip(plot.Inflate(2)))
        {
            for (int k = 0; k < names.Count; k++)
            {
                var pts = History.Window(k, from, to, buckets);
                if (pts.Count < 2) continue;
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var p = new Point(X(pts[i].T, from, to, plot), plot.Bottom - Math.Clamp(pts[i].V, 0, 100) / 100 * plot.Height);
                        if (i == 0) gc.BeginFigure(p, false); else gc.LineTo(p);
                    }
                    gc.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Palette[k % Palette.Length]), 2), g);
            }
        }

        // Cursor: lectura en el instante bajo el ratón (o el último si no hay ratón)
        (double Time, double[] Values)? read = null;
        if (_hoverX is { } hx)
        {
            double t = from + (hx - plot.Left) / plot.Width * (to - from);
            read = History.At(t);
            if (read is { } rd)
            {
                double x = X(rd.Time, from, to, plot);
                ctx.DrawLine(CursorPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                Text(ctx, $"t = {FormatTime(rd.Time, 0.1)}", new Point(Math.Min(x + 4, plot.Right - 70), plot.Top + 2), Ink, 11, true);
            }
        }
        else if (range is { } rr) read = History.At(IsLive ? rr.End : to);

        for (int k = 0; k < names.Count; k++)
        {
            var color = new SolidColorBrush(Palette[k % Palette.Length]);
            double ly = plot.Top + k * 18;
            ctx.DrawLine(new Pen(color, 3), new Point(plot.Right + 12, ly + 8), new Point(plot.Right + 28, ly + 8));
            string value = read is { } r2 && k < r2.Values.Length ? $" · {r2.Values[k]:0} %" : "";
            Text(ctx, names[k] + value, new Point(plot.Right + 34, ly), Ink);
        }

        // Franja de toda la historia con la ventana visible marcada
        var o = Overview;
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#F3F5F7")), Grid, o, 3, 3);
        if (range is { } all && all.End > all.Start)
        {
            double a = o.Left + Math.Clamp((from - all.Start) / (all.End - all.Start), 0, 1) * o.Width;
            double b = o.Left + Math.Clamp((to - all.Start) / (all.End - all.Start), 0, 1) * o.Width;
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#2A78D6"), 0.35), null, new Rect(a, o.Top, Math.Max(b - a, 3), o.Height), 3, 3);
            Text(ctx, FormatTime(all.Start, 1), new Point(o.Right + 8, o.Top - 3), Ink, 10);
            Text(ctx, "→ " + FormatTime(all.End, 1), new Point(o.Right + 60, o.Top - 3), Ink, 10);
        }
    }

    private static double X(double t, double from, double to, Rect plot) => plot.Left + (t - from) / (to - from) * plot.Width;

    private static double NiceStep(double raw)
    {
        double[] steps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 21600, 43200, 86400];
        foreach (var s in steps) if (s >= raw) return s;
        return Math.Ceiling(raw / 86400) * 86400;
    }

    /// <summary>"42 s", "3:05", "1:02:10"; con décimas si el paso es menor de un segundo.</summary>
    internal static string FormatTime(double seconds, double step)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (step < 1) return ts.TotalMinutes < 1 ? $"{seconds:0.0} s" : ts.ToString(ts.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"m\:ss\.f", CultureInfo.InvariantCulture);
        if (ts.TotalMinutes < 1) return $"{seconds:0} s";
        return ts.ToString(ts.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
    }

    private static void Text(DrawingContext ctx, string s, Point p, IBrush brush, double size = 11, bool bold = false) =>
        ctx.DrawText(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? new Typeface(Font.FontFamily, FontStyle.Normal, FontWeight.SemiBold) : Font, size, brush), p);
}
