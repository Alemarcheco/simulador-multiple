using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Cad3D.Geometry;

namespace Simulador.Cad3D.Views;

/// <summary>
/// Lienzo del boceto 2D: rejilla en mm, perfil cerrado con sus cotas.
/// Clic añade un punto, arrastrar lo mueve, clic derecho lo borra, rueda acerca, botón central desplaza.
/// </summary>
internal sealed class SketchCanvas : Control
{
    private const double HitPx = 9;
    private static readonly IBrush Back = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x28));
    private static readonly IPen MinorPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
    private static readonly IPen MajorPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);
    private static readonly IPen AxisXPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A)), 1.5);
    private static readonly IPen AxisYPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0xC0, 0x6A)), 1.5);
    private static readonly IPen RevolveAxisPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x40)), 2, new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x6C, 0xB4, 0xFF)), 2);
    private static readonly IPen BadEdgePen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x50)), 2);
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(50, 0x6C, 0xB4, 0xFF));
    private static readonly IBrush Forbidden = new SolidColorBrush(Color.FromArgb(28, 255, 80, 60));
    private static readonly IBrush Handle = Brushes.White;
    private static readonly IBrush HandleSelected = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x40));
    private static readonly IBrush Label = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));

    private double _scale = 8; // píxeles por mm
    private Vec2 _center;       // mm en el centro del lienzo
    private bool _fitted;
    private int _drag = -1;
    private Point? _panFrom;
    private Vec2? _mouse;

    public List<Vec2> Points { get; } = new();
    public bool Revolve { get; init; }
    public double Snap { get; set; } = 1;
    public int Selected { get; private set; } = -1;

    /// <summary>Cambió la geometría (durante el arrastre, en cada movimiento).</summary>
    public event Action? Edited;
    /// <summary>Terminó una edición (soltar, añadir, borrar): conviene refrescar la tabla.</summary>
    public event Action? Committed;

    public SketchCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public void Select(int i) { Selected = i; InvalidateVisual(); }

    public void FitView()
    {
        if (Bounds.Width < 1) { _fitted = false; return; }
        var pts = Points.Append(new Vec2(0, 0)).ToList();
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double w = Math.Max(10, maxX - minX), h = Math.Max(10, maxY - minY);
        _scale = Math.Clamp(Math.Min(Bounds.Width / (w * 1.4), Bounds.Height / (h * 1.4)), 0.2, 200);
        _center = new Vec2((minX + maxX) / 2, (minY + maxY) / 2);
        _fitted = true;
        InvalidateVisual();
    }

    private Point ToScreen(Vec2 p) => new(Bounds.Width / 2 + (p.X - _center.X) * _scale, Bounds.Height / 2 - (p.Y - _center.Y) * _scale);
    private Vec2 ToModel(Point s) => new(_center.X + (s.X - Bounds.Width / 2) / _scale, _center.Y - (s.Y - Bounds.Height / 2) / _scale);

    private Vec2 Constrain(Vec2 p)
    {
        p = Sketch2D.Snap(p, Snap);
        return Revolve && p.X < 0 ? p with { X = 0 } : p;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        var m = ToModel(pos);
        int hit = Sketch2D.NearestPoint(Points, m, HitPx / _scale);

        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _panFrom = pos;
        }
        else if (props.IsRightButtonPressed)
        {
            if (hit >= 0 && Points.Count > 3)
            {
                Points.RemoveAt(hit);
                Selected = -1;
                Edited?.Invoke();
                Committed?.Invoke();
            }
        }
        else if (props.IsLeftButtonPressed)
        {
            if (hit < 0)
            {
                hit = Sketch2D.AddPoint(Points, Constrain(m));
                Edited?.Invoke();
                Committed?.Invoke();
            }
            Selected = hit;
            _drag = hit;
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _mouse = Constrain(ToModel(pos));
        if (_panFrom is { } from)
        {
            _center = new Vec2(_center.X - (pos.X - from.X) / _scale, _center.Y + (pos.Y - from.Y) / _scale);
            _panFrom = pos;
        }
        else if (_drag >= 0 && _drag < Points.Count)
        {
            var p = _mouse.Value;
            if (Points[_drag] != p) { Points[_drag] = p; Edited?.Invoke(); }
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag >= 0) Committed?.Invoke();
        _drag = -1;
        _panFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouse = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        var before = ToModel(pos);
        _scale = Math.Clamp(_scale * Math.Pow(1.15, e.Delta.Y), 0.2, 400);
        var after = ToModel(pos);
        _center = new Vec2(_center.X + before.X - after.X, _center.Y + before.Y - after.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && Selected >= 0 && Points.Count > 3)
        {
            Points.RemoveAt(Selected);
            Selected = -1;
            Edited?.Invoke();
            Committed?.Invoke();
            InvalidateVisual();
        }
        else if (e.Key == Key.F) FitView();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_fitted) FitView();
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        ctx.FillRectangle(Back, r);
        if (r.Width < 1) return;

        DrawGrid(ctx, r);

        if (Revolve)
        {
            double ax = ToScreen(new Vec2(0, 0)).X;
            if (ax > 0) ctx.FillRectangle(Forbidden, new Rect(0, 0, Math.Min(ax, r.Width), r.Height));
            ctx.DrawLine(RevolveAxisPen, new Point(ax, 0), new Point(ax, r.Height));
            Text(ctx, "eje de giro", new Point(ax + 6, 6), false);
        }

        if (Points.Count >= 2)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(ToScreen(Points[0]), true);
                foreach (var p in Points.Skip(1)) g.LineTo(ToScreen(p));
                g.EndFigure(true);
            }
            bool bad = Sketch2D.Validate(Points, Revolve) is not null;
            ctx.DrawGeometry(bad ? null : Fill, bad ? BadEdgePen : EdgePen, geo);

            // Cotas: longitud de cada tramo en su punto medio.
            for (int i = 0; i < Points.Count; i++)
            {
                var a = ToScreen(Points[i]);
                var b = ToScreen(Points[(i + 1) % Points.Count]);
                if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 30) continue;
                Text(ctx, Fmt(Sketch2D.SegmentLength(Points, i)), new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), true);
            }
        }

        for (int i = 0; i < Points.Count; i++)
        {
            var s = ToScreen(Points[i]);
            ctx.DrawEllipse(i == Selected ? HandleSelected : Handle, null, s, 4.5, 4.5);
            Text(ctx, (i + 1).ToString(CultureInfo.InvariantCulture), new Point(s.X + 7, s.Y - 16), false);
        }

        if (_mouse is { } m)
            Text(ctx, $"X {Fmt(m.X)}  Y {Fmt(m.Y)} mm", new Point(8, r.Height - 22), false);
    }

    private void DrawGrid(DrawingContext ctx, Rect r)
    {
        // Paso de la rejilla: el menor de 0.5/1/5/10/50/100 mm que deje al menos 10 px entre líneas.
        double step = new[] { 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500 }.First(s => s * _scale >= 10 || s == 500);
        double major = step * 5;
        var min = ToModel(new Point(0, r.Height));
        var max = ToModel(new Point(r.Width, 0));
        for (double x = Math.Floor(min.X / step) * step; x <= max.X; x += step)
        {
            double sx = ToScreen(new Vec2(x, 0)).X;
            bool isMajor = Math.Abs(x / major - Math.Round(x / major)) < 1e-6;
            ctx.DrawLine(Math.Abs(x) < 1e-9 ? AxisYPen : isMajor ? MajorPen : MinorPen, new Point(sx, 0), new Point(sx, r.Height));
        }
        for (double y = Math.Floor(min.Y / step) * step; y <= max.Y; y += step)
        {
            double sy = ToScreen(new Vec2(0, y)).Y;
            bool isMajor = Math.Abs(y / major - Math.Round(y / major)) < 1e-6;
            ctx.DrawLine(Math.Abs(y) < 1e-9 ? AxisXPen : isMajor ? MajorPen : MinorPen, new Point(0, sy), new Point(r.Width, sy));
        }
        Text(ctx, $"rejilla {Fmt(step)} mm", new Point(r.Width - 110, r.Height - 22), false);
    }

    private static void Text(DrawingContext ctx, string text, Point at, bool centered)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Label);
        var p = centered ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at;
        ctx.FillRectangle(LabelBack, new Rect(p.X - 3, p.Y - 1, ft.Width + 6, ft.Height + 2), 3);
        ctx.DrawText(ft, p);
    }

    internal static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
