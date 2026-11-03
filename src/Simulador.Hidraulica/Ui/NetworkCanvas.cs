using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.Ui;

/// <summary>Herramienta activa del lienzo.</summary>
public enum Tool
{
    Select, Reservoir, Tank, Junction, Pipe, Pump, ManualValve, ControlValve, SolenoidValve, CheckValve,
    PressureSensor, LevelSensor, FlowSensor, Delete,
}

/// <summary>
/// Lienzo de diseño y animación: dibuja la red, permite colocar, unir, mover y borrar elementos, y durante la
/// simulación colorea las tuberías según el caudal, anima el sentido del flujo y llena los tanques.
/// </summary>
public sealed class NetworkCanvas : Control
{
    private const double Grid = 20, NodeRadius = 9, TankW = 64, TankH = 84, HitTolerance = 7;

    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#F6F8FA"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#E3E8EE")), 1);
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#24292F"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#57606A"));
    private static readonly IBrush Water = new SolidColorBrush(Color.Parse("#4A9FE0"));
    private static readonly IBrush WaterLight = new SolidColorBrush(Color.Parse("#D6EBFA"));
    private static readonly IBrush Steel = new SolidColorBrush(Color.Parse("#8C959F"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#E8590C"));
    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#2DA44E"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#CF222E"));
    private static readonly IBrush SensorFill = new SolidColorBrush(Color.Parse("#FFF8C5"));

    private HydraulicNetwork _net = new();
    private HydraulicSimulation? _sim;
    private object? _selected;
    private string? _pendingLinkStart;
    private Point _mouse;
    private object? _dragging;
    private Point _dragOffset;
    private double _flowPhase;

    public Tool Tool { get; set; } = Tool.Select;
    public bool DesignLocked { get; set; }

    public HydraulicNetwork Network
    {
        get => _net;
        set { _net = value; _selected = null; _pendingLinkStart = null; InvalidateVisual(); }
    }

    /// <summary>Simulación en curso (null en modo diseño).</summary>
    public HydraulicSimulation? Simulation
    {
        get => _sim;
        set { _sim = value; InvalidateVisual(); }
    }

    public object? Selected
    {
        get => _selected;
        set { _selected = value; SelectionChanged?.Invoke(this, value); InvalidateVisual(); }
    }

    public event EventHandler<object?>? SelectionChanged;
    /// <summary>El diseño cambió (para marcar el proyecto como modificado).</summary>
    public event EventHandler? DesignChanged;
    public event EventHandler<string>? Hint;

    public NetworkCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 1600, h = 1000;
        foreach (var n in _net.Nodes) { w = Math.Max(w, n.X + 200); h = Math.Max(h, n.Y + 200); }
        return new Size(w, h);
    }

    /// <summary>Avanza la animación del sentido del flujo (llamar en cada fotograma).</summary>
    public void Animate(double seconds)
    {
        _flowPhase = (_flowPhase + seconds * 40) % 1000;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- Dibujo

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Paper, bounds);
        for (double x = 0; x < bounds.Width; x += Grid) ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (double y = 0; y < bounds.Height; y += Grid) ctx.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));

        foreach (var link in _net.Links) DrawLink(ctx, link);
        foreach (var node in _net.Nodes) DrawNode(ctx, node);
        foreach (var s in _net.Sensors) DrawSensor(ctx, s);

        if (_pendingLinkStart is not null && _net.FindNode(_pendingLinkStart) is { } start)
            ctx.DrawLine(new Pen(Accent, 2, new DashStyle([4, 4], 0)), Anchor(start), _mouse);
    }

    private void DrawLink(DrawingContext ctx, HydraulicLink link)
    {
        if (_net.FindNode(link.From) is not { } a || _net.FindNode(link.To) is not { } b) return;
        var pts = Polyline(link, a, b);
        double q = _sim is null ? 0 : SafeFlow(link.Id);
        bool flowing = Math.Abs(q) > 1e-7;
        double thickness = link is Pipe p ? Math.Clamp(p.Diameter * 1000 / 12, 3, 10) : 5;
        bool selected = ReferenceEquals(_selected, link);

        if (selected)
            DrawPolyline(ctx, new Pen(Accent, thickness + 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), pts);
        DrawPolyline(ctx, new Pen(_sim is null ? Steel : flowing ? Water : Steel, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), pts);
        if (_sim is not null && flowing)
        {
            // Rayas que avanzan en el sentido del caudal; más rápido cuanto mayor es la velocidad.
            double speed = Math.Clamp(Math.Abs(_sim.VelocityIn(link.Id)), 0.2, 3);
            double offset = -Math.Sign(q) * _flowPhase * speed;
            DrawPolyline(ctx, new Pen(Brushes.White, Math.Max(thickness / 3, 1.5), new DashStyle([2, 3], offset)), pts);
        }

        var mid = Midpoint(pts);
        switch (link)
        {
            case Pump pump: DrawPump(ctx, mid, Direction(a, b), pump, selected); break;
            case Valve v: DrawValve(ctx, mid, Direction(a, b), v, selected); break;
        }

        string label = link.Id;
        if (_sim is not null) label += $"  {q * 1000:0.00} L/s";
        DrawText(ctx, label, mid + new Point(8, link is Pipe ? 4 : 18), 11, Muted);
    }

    private void DrawPump(DrawingContext ctx, Point c, Vector dir, Pump pump, bool selected)
    {
        bool on = _sim is null ? pump.InitiallyOn : pump.RelativeSpeed > 0.01;
        ctx.DrawEllipse(Brushes.White, new Pen(selected ? Accent : Ink, 2.5), c, 18, 18);
        // Triángulo apuntando en el sentido de impulsión (de From a To).
        var n = new Vector(-dir.Y, dir.X);
        var tip = c + dir * 14;
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(c - dir * 10 + n * 12, true);
            gc.LineTo(tip);
            gc.LineTo(c - dir * 10 - n * 12);
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(on ? Ok : Off, null, g);
    }

    private void DrawValve(DrawingContext ctx, Point c, Vector dir, Valve v, bool selected)
    {
        var n = new Vector(-dir.Y, dir.X);
        double open = _sim is null ? (v.Kind == ValveKind.Check ? 1 : v.InitialOpening) : v.Opening;
        IBrush fill = open > 0.99 ? Ok : open < 0.01 ? Off : Accent;
        var pen = new Pen(selected ? Accent : Ink, 2);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            // Pajarita: dos triángulos enfrentados.
            gc.BeginFigure(c - dir * 14 + n * 10, true); gc.LineTo(c); gc.LineTo(c - dir * 14 - n * 10); gc.EndFigure(true);
            if (v.Kind == ValveKind.Check)
            {
                gc.BeginFigure(c + dir * 2 + n * 10, false); gc.LineTo(c + dir * 2 - n * 10); gc.EndFigure(false);
            }
            else
            {
                gc.BeginFigure(c + dir * 14 + n * 10, true); gc.LineTo(c); gc.LineTo(c + dir * 14 - n * 10); gc.EndFigure(true);
            }
        }
        ctx.DrawGeometry(fill, pen, g);
        if (v.Kind is ValveKind.Control or ValveKind.Solenoid)
        {
            // Actuador: vástago y cabeza (círculo = control, cuadrado = bobina).
            var top = c - n * 20;
            ctx.DrawLine(pen, c, top);
            if (v.Kind == ValveKind.Control) ctx.DrawEllipse(Brushes.White, pen, top - n * 6, 7, 7);
            else ctx.DrawRectangle(Brushes.White, pen, new Rect(top.X - 7, top.Y - 7, 14, 14).Translate(-n * 6));
            if (_sim is not null) DrawText(ctx, $"{open * 100:0} %", top - n * 6 + new Point(10, -8), 10, Muted);
        }
    }

    private void DrawNode(DrawingContext ctx, HydraulicNode node)
    {
        bool selected = ReferenceEquals(_selected, node);
        var pen = new Pen(selected ? Accent : Ink, selected ? 3 : 2);
        var p = new Point(node.X, node.Y);
        switch (node)
        {
            case Tank t:
            {
                var r = new Rect(p.X - TankW / 2, p.Y - TankH / 2, TankW, TankH);
                double ratio = _sim is null ? t.InitialLevel / t.MaxLevel : t.Level / t.MaxLevel;
                ratio = Math.Clamp(double.IsFinite(ratio) ? ratio : 0, 0, 1);
                ctx.DrawRectangle(Brushes.White, null, r, 4, 4);
                var water = new Rect(r.X, r.Bottom - r.Height * ratio, r.Width, r.Height * ratio);
                ctx.DrawRectangle(_sim is null ? WaterLight : Water, null, water);
                ctx.DrawRectangle(null, pen, r, 4, 4);
                DrawText(ctx, t.Id, new Point(r.Right + 6, r.Top), 12, Ink);
                string info = _sim is null
                    ? $"Ø{t.Diameter:0.##} m · {t.Capacity * 1000:0} L"
                    : $"{t.Level:0.00} m ({ratio * 100:0} %)";
                DrawText(ctx, info, new Point(r.Right + 6, r.Top + 16), 11, Muted);
                if (_sim is not null && _sim.Overflow[_sim.NodeIndex(t.Id)] > 1e-7)
                    DrawText(ctx, "¡Rebosa!", new Point(r.Right + 6, r.Top + 32), 11, Off);
                break;
            }
            case Reservoir res:
            {
                var r = new Rect(p.X - 34, p.Y - 18, 68, 36);
                ctx.DrawRectangle(Water, pen, r);
                DrawWaves(ctx, r);
                DrawText(ctx, res.Id, new Point(r.X, r.Bottom + 4), 12, Ink);
                DrawText(ctx, $"H = {res.Elevation + res.Level:0.##} m", new Point(r.X, r.Bottom + 19), 11, Muted);
                break;
            }
            case Junction j:
            {
                ctx.DrawEllipse(j.Demand > 0 ? Accent : Ink, selected ? pen : null, p, NodeRadius / 1.6, NodeRadius / 1.6);
                if (_sim is not null)
                    DrawText(ctx, $"{_sim.PressureAt(_sim.NodeIndex(j.Id)) / 1e5:0.00} bar", p + new Point(8, -20), 11, Muted);
                else
                    DrawText(ctx, j.Id, p + new Point(8, -18), 11, Muted);
                if (j.Demand > 0) DrawText(ctx, $"↓ {j.Demand * 1000:0.##} L/s", p + new Point(8, 4), 10, Accent);
                break;
            }
        }
    }

    private static void DrawWaves(DrawingContext ctx, Rect r)
    {
        var pen = new Pen(Brushes.White, 1.5);
        for (double x = r.X + 6; x < r.Right - 10; x += 14)
        {
            ctx.DrawLine(pen, new Point(x, r.Y + 12), new Point(x + 5, r.Y + 9));
            ctx.DrawLine(pen, new Point(x + 5, r.Y + 9), new Point(x + 10, r.Y + 12));
        }
    }

    private void DrawSensor(DrawingContext ctx, Sensor s)
    {
        var target = SensorTargetPoint(s);
        var c = new Point(s.X, s.Y);
        if (target is { } t) ctx.DrawLine(new Pen(Muted, 1, new DashStyle([3, 3], 0)), c, t);
        bool selected = ReferenceEquals(_selected, s);
        ctx.DrawEllipse(SensorFill, new Pen(selected ? Accent : Ink, selected ? 2.5 : 1.5), c, 15, 15);
        string letter = s.Kind switch { SensorKind.Pressure => "PT", SensorKind.Level => "LT", _ => "FT" };
        DrawText(ctx, letter, c + new Point(-8, -7), 10, Ink);
        string label = s.Id;
        if (_sim is not null)
        {
            double v = SafeSensor(s);
            label += s.Kind switch
            {
                SensorKind.Pressure => $": {v / 1e5:0.00} bar",
                SensorKind.Level => $": {v:0.00} m",
                _ => $": {v * 1000:0.00} L/s",
            };
        }
        DrawText(ctx, label, c + new Point(18, -7), 11, Ink);
    }

    private static void DrawPolyline(DrawingContext ctx, IPen pen, IReadOnlyList<Point> pts)
    {
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++) gc.LineTo(pts[i]);
            gc.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    private static void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush) =>
        ctx.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush), at);

    // ---------------------------------------------------------------- Geometría

    private static Point Anchor(HydraulicNode n) => new(n.X, n.Y);

    private List<Point> Polyline(HydraulicLink link, HydraulicNode a, HydraulicNode b)
    {
        var pts = new List<Point> { Anchor(a) };
        pts.AddRange(link.Vertices.Select(v => new Point(v.X, v.Y)));
        pts.Add(Anchor(b));
        return pts;
    }

    private static Point Midpoint(IReadOnlyList<Point> pts)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += Dist(pts[i - 1], pts[i]);
        double half = total / 2;
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = Dist(pts[i - 1], pts[i]);
            if (half <= seg && seg > 0) return pts[i - 1] + (pts[i] - pts[i - 1]) * (half / seg);
            half -= seg;
        }
        return pts[0];
    }

    private static Vector Direction(HydraulicNode a, HydraulicNode b)
    {
        var v = new Vector(b.X - a.X, b.Y - a.Y);
        double len = v.Length;
        return len < 1e-6 ? new Vector(1, 0) : v / len;
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return Dist(p, a + ab * t);
    }

    private Point? SensorTargetPoint(Sensor s)
    {
        if (_net.FindNode(s.Target) is { } n) return Anchor(n);
        if (_net.FindLink(s.Target) is { } l && _net.FindNode(l.From) is { } a && _net.FindNode(l.To) is { } b)
            return Midpoint(Polyline(l, a, b));
        return null;
    }

    private double SafeFlow(string id) { try { return _sim!.FlowIn(id); } catch (KeyNotFoundException) { return 0; } }
    private double SafeSensor(Sensor s) { try { return _sim!.SensorValue(s); } catch (KeyNotFoundException) { return 0; } }

    // ---------------------------------------------------------------- Selección

    public object? HitTest(Point p)
    {
        foreach (var s in _net.Sensors) if (Dist(p, new Point(s.X, s.Y)) < 16) return s;
        foreach (var n in Enumerable.Reverse(_net.Nodes))
        {
            bool hit = n switch
            {
                Tank => Math.Abs(p.X - n.X) <= TankW / 2 + 2 && Math.Abs(p.Y - n.Y) <= TankH / 2 + 2,
                Reservoir => Math.Abs(p.X - n.X) <= 36 && Math.Abs(p.Y - n.Y) <= 20,
                _ => Dist(p, Anchor(n)) <= NodeRadius + 3,
            };
            if (hit) return n;
        }
        foreach (var l in _net.Links)
        {
            if (_net.FindNode(l.From) is not { } a || _net.FindNode(l.To) is not { } b) continue;
            var pts = Polyline(l, a, b);
            if (l is not Pipe && Dist(p, Midpoint(pts)) < 20) return l;
            for (int i = 1; i < pts.Count; i++)
                if (DistToSegment(p, pts[i - 1], pts[i]) < HitTolerance) return l;
        }
        return null;
    }

    private static Point Snap(Point p) => new(Math.Round(p.X / Grid) * Grid, Math.Round(p.Y / Grid) * Grid);

    // ---------------------------------------------------------------- Ratón y teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed) { CancelPending(); Tool = Tool.Select; Hint?.Invoke(this, "Herramienta: seleccionar"); return; }
        if (!props.IsLeftButtonPressed) return;

        var hit = HitTest(pos);
        if (DesignLocked || Tool == Tool.Select)
        {
            Selected = hit;
            if (!DesignLocked && hit is HydraulicNode n) { _dragging = n; _dragOffset = new Point(n.X, n.Y) - pos; }
            else if (!DesignLocked && hit is Sensor s) { _dragging = s; _dragOffset = new Point(s.X, s.Y) - pos; }
            return;
        }

        switch (Tool)
        {
            case Tool.Delete:
                if (hit is not null) DeleteElement(hit);
                break;
            case Tool.Reservoir or Tool.Tank or Tool.Junction:
                PlaceNode(Snap(pos));
                break;
            case Tool.PressureSensor or Tool.LevelSensor or Tool.FlowSensor:
                PlaceSensor(pos, hit);
                break;
            default:
                LinkClick(pos, hit);
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mouse = e.GetPosition(this);
        if (_dragging is not null)
        {
            var p = Snap(_mouse + _dragOffset);
            if (_dragging is HydraulicNode n) { n.X = p.X; n.Y = p.Y; }
            else if (_dragging is Sensor s) { s.X = p.X; s.Y = p.Y; }
            InvalidateVisual();
        }
        else if (_pendingLinkStart is not null) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging is not null) { _dragging = null; DesignChanged?.Invoke(this, EventArgs.Empty); InvalidateMeasure(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { CancelPending(); Selected = null; }
        else if (e.Key == Key.Delete && !DesignLocked && _selected is not null) DeleteElement(_selected);
    }

    private void CancelPending() { _pendingLinkStart = null; InvalidateVisual(); }

    // ---------------------------------------------------------------- Edición

    private void PlaceNode(Point p)
    {
        HydraulicNode node = Tool switch
        {
            Tool.Reservoir => new Reservoir { Id = _net.NextId("R"), Level = 1 },
            Tool.Tank => new Tank { Id = _net.NextId("T"), Elevation = 5 },
            _ => new Junction { Id = _net.NextId("N") },
        };
        node.X = p.X;
        node.Y = p.Y;
        _net.Nodes.Add(node);
        Changed(node);
    }

    private void LinkClick(Point pos, object? hit)
    {
        // Clic en vacío: crea una unión allí para poder trazar tramos libremente.
        if (hit is not HydraulicNode node)
        {
            node = new Junction { Id = _net.NextId("N"), X = Snap(pos).X, Y = Snap(pos).Y };
            _net.Nodes.Add(node);
        }

        if (_pendingLinkStart is null)
        {
            _pendingLinkStart = node.Id;
            Hint?.Invoke(this, $"Desde {node.Id}: haz clic en el destino (Esc para cancelar).");
            InvalidateVisual();
            return;
        }
        if (_pendingLinkStart == node.Id) return;

        HydraulicLink link = Tool switch
        {
            Tool.Pump => new Pump { Id = _net.NextId("B") },
            Tool.ManualValve => new Valve { Id = _net.NextId("V") },
            Tool.ControlValve => new Valve { Id = _net.NextId("V"), Kind = ValveKind.Control, StrokeTime = 8 },
            Tool.SolenoidValve => new Valve { Id = _net.NextId("EV"), Kind = ValveKind.Solenoid },
            Tool.CheckValve => new Valve { Id = _net.NextId("VR"), Kind = ValveKind.Check },
            _ => new Pipe { Id = _net.NextId("P") },
        };
        if (link is Pipe pipe && _net.FindNode(_pendingLinkStart) is { } a)
            pipe.Length = Math.Max(1, Math.Round(Dist(Anchor(a), Anchor(node)) / Grid, 1)); // 1 cuadro = 1 m
        _net.Connect(link, _pendingLinkStart, node.Id);
        // Las tuberías siguen encadenándose desde el último punto; bombas y válvulas terminan.
        _pendingLinkStart = link is Pipe ? node.Id : null;
        Changed(link);
    }

    private void PlaceSensor(Point pos, object? hit)
    {
        var kind = Tool switch { Tool.PressureSensor => SensorKind.Pressure, Tool.LevelSensor => SensorKind.Level, _ => SensorKind.Flow };
        string? target = (kind, hit) switch
        {
            (SensorKind.Level, Tank t) => t.Id,
            (SensorKind.Pressure, HydraulicNode n) => n.Id,
            (SensorKind.Flow, HydraulicLink l) => l.Id,
            _ => null,
        };
        if (target is null)
        {
            Hint?.Invoke(this, kind switch
            {
                SensorKind.Level => "El transmisor de nivel va sobre un tanque.",
                SensorKind.Pressure => "El transmisor de presión va sobre un nudo, tanque o depósito.",
                _ => "El caudalímetro va sobre una tubería, bomba o válvula.",
            });
            return;
        }
        string prefix = kind switch { SensorKind.Pressure => "PT", SensorKind.Level => "LT", _ => "FT" };
        var s = new Sensor { Id = _net.NextId(prefix), Kind = kind, Target = target, X = Snap(pos).X + 40, Y = Snap(pos).Y - 40 };
        _net.Sensors.Add(s);
        Changed(s);
    }

    public void DeleteElement(object element)
    {
        switch (element)
        {
            case HydraulicNode n: _net.RemoveNode(n.Id); break;
            case HydraulicLink l: _net.RemoveLink(l.Id); break;
            case Sensor s: _net.Sensors.Remove(s); break;
        }
        if (ReferenceEquals(_selected, element)) Selected = null;
        Changed(null);
    }

    private void Changed(object? select)
    {
        if (select is not null) Selected = select;
        DesignChanged?.Invoke(this, EventArgs.Empty);
        InvalidateMeasure();
        InvalidateVisual();
    }
}
