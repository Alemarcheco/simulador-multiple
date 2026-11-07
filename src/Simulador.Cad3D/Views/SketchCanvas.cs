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
/// El cuadradito del centro de cada tramo se arrastra para convertirlo en arco (doble clic lo vuelve recto).
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
    private static readonly IBrush MidHandle = new SolidColorBrush(Color.FromArgb(160, 0x6C, 0xB4, 0xFF));
    private static readonly IBrush MidHandleArc = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x40));
    private static readonly IPen CenterPen = new Pen(new SolidColorBrush(Color.FromArgb(110, 0xFF, 0x8C, 0x40)), 1, new DashStyle(new double[] { 3, 3 }, 0));
    private static readonly IBrush HandleSelected = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x40));
    private static readonly IBrush Label = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
    private static readonly IBrush RuleBack = new SolidColorBrush(Color.FromArgb(200, 0x2E, 0x6B, 0x3A));
    private static readonly IBrush RuleBackBad = new SolidColorBrush(Color.FromArgb(220, 0xB0, 0x3A, 0x2E));
    private static readonly IPen SelectedSegPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x40)), 4);
    private static readonly IPen FixedPen = new Pen(new SolidColorBrush(Color.FromRgb(0x7C, 0xE0, 0x8A)), 2);

    private double _scale = 8; // píxeles por mm
    private Vec2 _center;       // mm en el centro del lienzo
    private bool _fitted;
    private int _drag = -1;
    private int _dragArc = -1;
    private Point? _panFrom;
    private Vec2? _mouse;
    private bool _arcMoved;

    public List<Vec2> Points { get; } = new();
    /// <summary>Abombamiento de cada tramo (mismo tamaño que <see cref="Points"/>).</summary>
    public List<double> Bulges { get; } = new();
    public bool Revolve { get; init; }
    public double Snap { get; set; } = 1;
    public int Selected { get; private set; } = -1;
    /// <summary>Reglas del boceto (ver <see cref="SketchSolver"/>).</summary>
    public List<SketchConstraint> Constraints { get; } = new();
    /// <summary>Tramos elegidos para una regla (el último elegido al final, como mucho dos).</summary>
    public List<int> SelectedSegments { get; } = new();
    /// <summary>Error que queda al aplicar las reglas (0 = todas se cumplen).</summary>
    public double RuleError { get; private set; }

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

    public void SelectSegment(int seg)
    {
        if (SelectedSegments.Remove(seg)) { InvalidateVisual(); return; }
        SelectedSegments.Add(seg);
        while (SelectedSegments.Count > 2) SelectedSegments.RemoveAt(0);
        InvalidateVisual();
    }

    public void ClearSegmentSelection() { SelectedSegments.Clear(); InvalidateVisual(); }

    /// <summary>Aplica las reglas sin mover los puntos indicados (si se puede). Devuelve el error que queda.</summary>
    public double ApplyRules(params int[] held)
    {
        RuleError = SketchSolver.Solve(Points, Constraints, held);
        InvalidateVisual();
        return RuleError;
    }

    public void SetConstraints(IEnumerable<SketchConstraint>? rules)
    {
        Constraints.Clear();
        if (rules is not null) Constraints.AddRange(rules.Where(c => SketchSolver.IsValid(c, Points.Count)));
        SelectedSegments.Clear();
        RuleError = SketchSolver.Residual(Points, Constraints);
    }

    private void ReplaceConstraints(List<SketchConstraint> rules)
    {
        Constraints.Clear();
        Constraints.AddRange(rules);
        SelectedSegments.Clear();
    }

    /// <summary>Reemplaza el boceto.</summary>
    public void SetProfile(IEnumerable<Vec2> points, IEnumerable<double>? bulges)
    {
        Points.Clear();
        Points.AddRange(points);
        Bulges.Clear();
        Bulges.AddRange(Sketch2D.FitBulges(bulges?.ToList(), Points.Count));
    }

    private void SyncBulges()
    {
        while (Bulges.Count < Points.Count) Bulges.Add(0);
        while (Bulges.Count > Points.Count) Bulges.RemoveAt(Bulges.Count - 1);
    }

    private void RemovePoint(int i)
    {
        ReplaceConstraints(SketchSolver.AfterRemove(Constraints, i, Points.Count));
        Points.RemoveAt(i);
        Bulges.RemoveAt(i);
        // El tramo anterior ahora llega a otro punto: deja de ser arco.
        if (Points.Count > 0) Bulges[i == 0 ? Points.Count - 1 : i - 1] = 0;
        ApplyRules();
    }

    private int HitMid(Point screen)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            var m = ToScreen(Sketch2D.ArcMidpoint(Points[i], Points[(i + 1) % Points.Count], Bulges[i]));
            if (Math.Abs(m.X - screen.X) <= HitPx * 0.8 && Math.Abs(m.Y - screen.Y) <= HitPx * 0.8) return i;
        }
        return -1;
    }

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
        SyncBulges();
        int hit = Sketch2D.NearestPoint(Points, m, HitPx / _scale);
        int mid = hit < 0 ? HitMid(pos) : -1;

        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _panFrom = pos;
        }
        else if (props.IsRightButtonPressed)
        {
            if (hit >= 0 && Points.Count > 3)
            {
                RemovePoint(hit);
                Selected = -1;
                Edited?.Invoke();
                Committed?.Invoke();
            }
        }
        else if (props.IsLeftButtonPressed && mid >= 0)
        {
            if (e.ClickCount >= 2) { Bulges[mid] = 0; Edited?.Invoke(); Committed?.Invoke(); }
            else { _dragArc = mid; _arcMoved = false; }
        }
        else if (props.IsLeftButtonPressed)
        {
            if (hit < 0)
            {
                hit = Sketch2D.AddPoint(Points, Constrain(m));
                // Si el punto cae en un arco, los dos trozos quedan rectos.
                Bulges.Insert(hit, 0);
                if (hit > 0) Bulges[hit - 1] = 0;
                if (Points.Count > 3) ReplaceConstraints(SketchSolver.AfterInsert(Constraints, hit));
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
        else if (_dragArc >= 0 && _dragArc < Points.Count)
        {
            // Sin ajuste a rejilla: el arco sigue al ratón; muy cerca de la cuerda queda recto.
            var a = Points[_dragArc]; var b = Points[(_dragArc + 1) % Points.Count];
            double bulge = Sketch2D.BulgeThrough(a, b, ToModel(pos));
            if (Math.Abs(bulge) * Sketch2D.Length(a, b) / 2 * _scale < 4) bulge = 0;
            if (Math.Abs(Bulges[_dragArc] - bulge) > 1e-9) { Bulges[_dragArc] = bulge; _arcMoved = true; Edited?.Invoke(); }
        }
        else if (_drag >= 0 && _drag < Points.Count)
        {
            var p = _mouse.Value;
            if (Points[_drag] != p)
            {
                Points[_drag] = p;
                if (Constraints.Count > 0) ApplyRules(_drag);
                Edited?.Invoke();
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // Un clic en el cuadradito sin arrastrar elige el tramo para las reglas.
        if (_dragArc >= 0 && !_arcMoved) SelectSegment(_dragArc);
        if (_drag >= 0 || _dragArc >= 0) Committed?.Invoke();
        _drag = -1;
        _dragArc = -1;
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
            SyncBulges();
            RemovePoint(Selected);
            Selected = -1;
            Edited?.Invoke();
            Committed?.Invoke();
            InvalidateVisual();
        }
        else if (e.Key == Key.F) FitView();
        else if (e.Key == Key.Escape && SelectedSegments.Count > 0) { ClearSegmentSelection(); e.Handled = true; }
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

        SyncBulges();
        if (Points.Count >= 2)
        {
            var outline = Sketch2D.Tessellate(Points, Bulges, 0.5 / _scale);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(ToScreen(outline[0]), true);
                foreach (var p in outline.Skip(1)) g.LineTo(ToScreen(p));
                g.EndFigure(true);
            }
            bool bad = Sketch2D.Validate(Points, Bulges, Revolve) is not null;
            ctx.DrawGeometry(bad ? null : Fill, bad ? BadEdgePen : EdgePen, geo);
            foreach (var sg in SelectedSegments.Where(sg => sg < Points.Count))
            {
                var segPts = Sketch2D.Tessellate(new[] { Points[sg], Points[(sg + 1) % Points.Count] }, new[] { Bulges[sg], 0 }, 0.5 / _scale);
                // El perfil de dos puntos va del inicio al final por el arco; el cierre (recto) no se dibuja.
                for (int k = 0; k + 1 < segPts.Count; k++) ctx.DrawLine(SelectedSegPen, ToScreen(segPts[k]), ToScreen(segPts[k + 1]));
            }

            // Cotas: longitud de cada tramo recto o radio de cada arco, junto a su punto medio.
            for (int i = 0; i < Points.Count; i++)
            {
                var pa = Points[i]; var pb = Points[(i + 1) % Points.Count];
                var mid = ToScreen(Sketch2D.ArcMidpoint(pa, pb, Bulges[i]));
                var arc = Sketch2D.ArcGeometry(pa, pb, Bulges[i]);
                if (arc is { } g2)
                {
                    var c = ToScreen(g2.Center);
                    ctx.DrawLine(CenterPen, c, mid);
                    ctx.DrawEllipse(null, CenterPen, c, 2.5, 2.5);
                }
                var a = ToScreen(pa); var b = ToScreen(pb);
                if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) >= 30)
                    Text(ctx, arc is { } g3 ? "R" + Fmt(g3.Radius) : Fmt(Sketch2D.SegmentLength(Points, i)), new Point(mid.X, mid.Y - 14), true);
                ctx.FillRectangle(arc is null ? MidHandle : MidHandleArc, new Rect(mid.X - 3.5, mid.Y - 3.5, 7, 7));
            }
            DrawRuleMarks(ctx);
        }

        for (int i = 0; i < Points.Count; i++)
        {
            var s = ToScreen(Points[i]);
            ctx.DrawEllipse(i == Selected ? HandleSelected : Handle, null, s, 4.5, 4.5);
            if (Constraints.Any(c => c.Kind == ConstraintKind.Fijo && c.A == i))
                ctx.DrawRectangle(null, FixedPen, new Rect(s.X - 7, s.Y - 7, 14, 14));
            Text(ctx, (i + 1).ToString(CultureInfo.InvariantCulture), new Point(s.X + 7, s.Y - 16), false);
        }

        if (_mouse is { } m)
            Text(ctx, $"X {Fmt(m.X)}  Y {Fmt(m.Y)} mm", new Point(8, r.Height - 22), false);
    }

    /// <summary>Marca de cada regla junto al centro de su tramo: H, V, ∥n, ⊥n, =n o la cota fija.</summary>
    private void DrawRuleMarks(DrawingContext ctx)
    {
        var marks = new Dictionary<int, List<(string Text, bool Ok)>>();
        void Add(int seg, string text, bool ok)
        {
            if (!marks.TryGetValue(seg, out var list)) marks[seg] = list = new();
            list.Add((text, ok));
        }
        for (int k = 0; k < Constraints.Count; k++)
        {
            var c = Constraints[k];
            if (!SketchSolver.IsValid(c, Points.Count) || c.Kind == ConstraintKind.Fijo) continue;
            bool ok = SketchSolver.Error(Points, c) < 1e-3;
            string tag = (k + 1).ToString(CultureInfo.InvariantCulture);
            switch (c.Kind)
            {
                case ConstraintKind.Horizontal: Add(c.A, "H", ok); break;
                case ConstraintKind.Vertical: Add(c.A, "V", ok); break;
                case ConstraintKind.Longitud: Add(c.A, "⟷" + Fmt(c.Value), ok); break;
                case ConstraintKind.Paralelo: Add(c.A, "∥" + tag, ok); Add(c.B, "∥" + tag, ok); break;
                case ConstraintKind.Perpendicular: Add(c.A, "⊥" + tag, ok); Add(c.B, "⊥" + tag, ok); break;
                case ConstraintKind.IgualLongitud: Add(c.A, "=" + tag, ok); Add(c.B, "=" + tag, ok); break;
            }
        }
        foreach (var (seg, list) in marks)
        {
            var mid = ToScreen(Sketch2D.ArcMidpoint(Points[seg], Points[(seg + 1) % Points.Count], Bulges[seg]));
            double x = mid.X + 10, y = mid.Y + 6;
            foreach (var (text, ok) in list)
            {
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Label);
                ctx.FillRectangle(ok ? RuleBack : RuleBackBad, new Rect(x - 2, y - 1, ft.Width + 4, ft.Height + 2), 3);
                ctx.DrawText(ft, new Point(x, y));
                x += ft.Width + 7;
            }
        }
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
