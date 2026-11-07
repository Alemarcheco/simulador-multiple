using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Simulador.Hidraulica.Neumatica.Ui;

/// <summary>Herramienta activa del lienzo neumático.</summary>
public enum PneuTool
{
    Select, Delete, Tube,
    Supply, Compressor, Receiver,
    Valve32Push, Valve32Roller, Valve52Lever, Valve52Pilot, Valve52Solenoid,
    FlowControl, Check, OrValve, AndValve,
    CylinderSingle, CylinderDouble, Gauge,
}

/// <summary>
/// Lienzo del esquema neumático con símbolos ISO 1219. En diseño se colocan, mueven, giran y unen componentes;
/// en simulación las mangueras con presión se ven en azul, las válvulas cambian de casilla y el vástago se mueve.
/// Durante la simulación se acciona un pulsador manteniendo el clic sobre él (Ctrl+clic lo deja enclavado), una
/// palanca o una bobina con un clic.
/// </summary>
public sealed class PneumaticCanvas : Control
{
    private const double Grid = 10, PortHit = 8;
    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#F6F8FA"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#E6EAEE")), 1);
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#24292F"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#57606A"));
    private static readonly IBrush Air = new SolidColorBrush(Color.Parse("#1F6FEB"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#E8590C"));
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#F2A93B"));
    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#2DA44E"));
    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush RodBrush = new SolidColorBrush(Color.Parse("#AEB6BF"));

    private PneumaticCircuit _circuit = new();
    private PneumaticSimulation? _sim;
    private object? _selected;
    private (string Comp, string Port)? _tubeStart;
    private readonly List<PortPoint> _tubePoints = [];
    private Point _mouse;
    private PneumaticComponent? _dragging;
    private Point _dragOffset;
    private string? _heldValve;
    private string? _heldCoil;

    public PneuTool Tool { get; set; } = PneuTool.Select;
    public bool DesignLocked { get; set; }

    public PneumaticCircuit Circuit
    {
        get => _circuit;
        set { _circuit = value; _selected = null; CancelTube(); InvalidateMeasure(); InvalidateVisual(); }
    }

    public PneumaticSimulation? Simulation
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
    public event EventHandler? DesignChanged;
    /// <summary>Se accionó un mando durante la simulación.</summary>
    public event EventHandler? ControlUsed;
    public event EventHandler<string>? Hint;

    public PneumaticCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 1400, h = 900;
        foreach (var c in _circuit.Components) { w = Math.Max(w, c.X + 300); h = Math.Max(h, c.Y + 200); }
        return new Size(w, h);
    }

    // ---------------------------------------------------------------- Dibujo

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Paper, bounds);
        for (double x = 0; x < bounds.Width; x += Grid * 2) ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (double y = 0; y < bounds.Height; y += Grid * 2) ctx.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));

        for (int i = 0; i < _circuit.Tubes.Count; i++) DrawTube(ctx, _circuit.Tubes[i], i);
        foreach (var c in _circuit.Components) DrawComponent(ctx, c);

        if (_tubeStart is { } start && _circuit.Find(start.Comp) is { } sc)
        {
            var pen = new Pen(Accent, 2, new DashStyle([4, 4], 0));
            var (sx, sy) = sc.PortPosition(start.Port);
            var prev = new Point(sx, sy);
            foreach (var p in _tubePoints) { var q = new Point(p.X, p.Y); ctx.DrawLine(pen, prev, q); prev = q; }
            ctx.DrawLine(pen, prev, Orthogonal(prev, _mouse));
        }
    }

    private static Color PressureColor(double pa)
    {
        double t = Math.Clamp(pa / 8e5, 0, 1);
        static byte L(double a, double b, double t) => (byte)(a + (b - a) * t);
        return Color.FromRgb(L(0xEE, 0x5A, t), L(0xF2, 0x9B, t), L(0xF6, 0xF0, t));
    }

    private void DrawTube(DrawingContext ctx, Tube t, int index)
    {
        if (_circuit.Find(t.FromComponent) is not { } a || _circuit.Find(t.ToComponent) is not { } b || !a.HasPort(t.FromPort) || !b.HasPort(t.ToPort)) return;
        var pts = TubePolyline(t, a, b);
        bool selected = ReferenceEquals(_selected, t);
        bool pilot = t.FromPort is "12" or "14" || t.ToPort is "12" or "14";
        double p = _sim?.TubePressure(index) ?? 0;
        IBrush brush = selected ? Accent : _sim is not null && p > 0.3e5 ? Air : Ink;
        double width = selected ? 3 : _sim is not null && p > 0.3e5 ? 2.6 : 1.6;
        var pen = new Pen(brush, width, pilot ? new DashStyle([5, 3], 0) : null);
        for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
        // Punto de derivación donde coinciden varias mangueras en una toma.
        foreach (var (comp, port, pt) in new[] { (a, t.FromPort, pts[0]), (b, t.ToPort, pts[^1]) })
            if (_circuit.Tubes.Count(x => (x.FromComponent == comp.Id && x.FromPort == port) || (x.ToComponent == comp.Id && x.ToPort == port)) > 1)
                ctx.DrawEllipse(brush, null, pt, 3.2, 3.2);
    }

    public static List<Point> TubePolyline(Tube t, PneumaticComponent a, PneumaticComponent b)
    {
        var (ax, ay) = a.PortPosition(t.FromPort);
        var (bx, by) = b.PortPosition(t.ToPort);
        var list = new List<Point> { new(ax, ay) };
        list.AddRange(t.Points.Select(p => new Point(p.X, p.Y)));
        list.Add(new Point(bx, by));
        return list;
    }

    private void DrawComponent(DrawingContext ctx, PneumaticComponent c)
    {
        bool selected = ReferenceEquals(_selected, c);
        var pen = new Pen(selected ? Accent : Ink, selected ? 2.2 : 1.5);
        using (ctx.PushTransform(Matrix.CreateRotation(c.Rotation * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            switch (c)
            {
                case Cylinder cy: DrawCylinder(ctx, cy, pen); break;
                case DirectionalValve v: DrawValve(ctx, v, pen); break;
                case FlowControl f: DrawFlowControl(ctx, f, pen); break;
                case CheckValve k: DrawCheckSymbol(ctx, pen, 0, 0, 24); break;
                case LogicValve l: DrawLogic(ctx, l, pen); break;
                case AirSupply: DrawSupply(ctx, pen); break;
                case Compressor k: DrawCompressor(ctx, k, pen); break;
                case AirReceiver r: DrawReceiver(ctx, r, pen); break;
                case Gauge g: DrawGauge(ctx, g, pen); break;
            }
            foreach (var p in c.Ports) ctx.DrawEllipse(Body, new Pen(Muted, 1), new Point(p.Dx, p.Dy), 2.5, 2.5);
        }
        DrawLabels(ctx, c);
    }

    private void DrawLabels(DrawingContext ctx, PneumaticComponent c)
    {
        var box = LocalBounds(c);
        var r = RotatedBounds(c, box);
        string? value = null;
        if (_sim is not null)
            value = c switch
            {
                Gauge g => $"{_sim.PressureAt(g.Id, "1") / 1e5:0.00} bar",
                AirReceiver r2 => $"{_sim.PressureAt(r2.Id, "1") / 1e5:0.00} bar",
                Cylinder cy => $"{_sim.Position(cy.Id) * 1000:0} mm",
                Compressor k => _sim.IsCompressorRunning(k.Id) ? "en marcha" : "parado",
                _ => null,
            };
        else
            value = c switch
            {
                AirSupply s => $"{s.Pressure / 1e5:0.#} bar",
                Cylinder cy => $"Ø{cy.Bore * 1000:0} × {cy.Stroke * 1000:0}",
                FlowControl f => $"{f.Opening * 100:0} %",
                AirReceiver r2 => $"{r2.Volume * 1000:0} L",
                _ => null,
            };
        if (c is FlowControl fc && _sim is not null) value = $"{fc.Opening * 100:0} %";
        double y = c is Cylinder ? r.Top - 16 : r.Top - 14;
        Text(ctx, c.Id, new Point(r.Left, y), 11, ReferenceEquals(_selected, c) ? Accent : Ink, bold: true);
        if (value is not null) Text(ctx, value, new Point(r.Left + MeasureText(c.Id, 11) + 6, y), 11, Muted);
    }

    // ------------------------------------------------ símbolos (coordenadas locales, sin girar)

    private void DrawCylinder(DrawingContext ctx, Cylinder c, IPen pen)
    {
        double ratio = _sim is null ? c.InitialPosition : _sim.PositionRatio(c.Id);
        double xp = -60 + Math.Clamp(ratio, 0, 1) * Cylinder.RodTravel;
        var body = new Rect(-70, -20, 140, 40);
        if (_sim is not null)
        {
            ctx.FillRectangle(new SolidColorBrush(PressureColor(_sim.PressureAt(c.Id, "A"))), new Rect(-70, -20, xp + 70, 40));
            var front = c.Kind == CylinderKind.DoubleActing ? PressureColor(_sim.PressureAt(c.Id, "B")) : Colors.White;
            ctx.FillRectangle(new SolidColorBrush(front), new Rect(xp, -20, 70 - xp, 40));
        }
        else ctx.FillRectangle(Body, body);
        ctx.DrawRectangle(null, pen, body);
        // Vástago y émbolo.
        ctx.FillRectangle(RodBrush, new Rect(xp, -4, 150, 8));
        ctx.DrawRectangle(null, new Pen(Ink, 1), new Rect(xp, -4, 150, 8));
        ctx.FillRectangle(Ink, new Rect(xp - 3, -19, 6, 38));
        ctx.FillRectangle(Ink, new Rect(xp + 145, -9, 6, 18)); // cabeza del vástago (leva)
        if (c.Kind == CylinderKind.SingleActing)
        {
            var spring = new Pen(Ink, 1.1);
            double x0 = xp + 6, x1 = 66, step = (x1 - x0) / 8;
            for (int i = 0; i < 8; i++)
                ctx.DrawLine(spring, new Point(x0 + i * step, i % 2 == 0 ? -14 : 14), new Point(x0 + (i + 1) * step, i % 2 == 0 ? 14 : -14));
            ctx.DrawLine(new Pen(Ink, 1), new Point(50, 20), new Point(50, 26));   // escape del lado del vástago
        }
        // Detectores magnéticos.
        DrawSensor(ctx, c.SensorRetracted, -58, _sim?.SensorActive(c.SensorRetracted) == true);
        DrawSensor(ctx, c.SensorExtended, 40, _sim?.SensorActive(c.SensorExtended) == true);
        foreach (var v in _circuit.Components.OfType<DirectionalValve>().Where(v => v.Actuation == ValveActuation.Roller && v.RollerCylinder == c.Id))
        {
            double xr = -60 + Math.Clamp(v.RollerPosition, 0, 1) * Cylinder.RodTravel + 148;
            bool on = _sim?.IsActuated(v.Id) == true;
            ctx.DrawLine(new Pen(on ? Accent : Muted, 1.2), new Point(xr, 12), new Point(xr, 30));
            ctx.DrawEllipse(on ? Active : Body, new Pen(on ? Accent : Muted, 1.2), new Point(xr, 14), 4, 4);
            Text(ctx, v.Id, new Point(xr - 10, 32), 10, on ? Accent : Muted);
        }
    }

    private void DrawSensor(DrawingContext ctx, string name, double x, bool on)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var r = new Rect(x, -30, 18, 10);
        ctx.DrawRectangle(on ? Active : Body, new Pen(on ? Accent : Muted, 1), r);
        Text(ctx, name, new Point(x - 2, -44), 10, on ? Accent : Muted);
    }

    private void DrawValve(DrawingContext ctx, DirectionalValve v, IPen pen)
    {
        bool on = _sim?.IsActuated(v.Id) ?? (v.IsBistable && v.InitiallyActuated);
        double shift = on ? 40 : 0;
        // Casillas: la izquierda es la posición accionada; la derecha, la de reposo (bajo las tomas).
        var left = new Rect(-40 + shift, -20, 40, 40);
        var right = new Rect(0 + shift, -20, 40, 40);
        ctx.FillRectangle(Body, left);
        ctx.FillRectangle(Body, right);
        ctx.DrawRectangle(null, pen, left);
        ctx.DrawRectangle(null, pen, right);
        var flow = new Pen(on ? Air : Ink, 1.3);
        var rest = new Pen(on ? Ink : Air, 1.3);
        if (_sim is null) flow = rest = new Pen(Ink, 1.3);
        if (v.Ways == ValveWays.ThreeTwo)
        {
            bool no = v.NormallyOpen;
            // Reposo (casilla derecha).
            if (!no) { Arrow(ctx, rest, shift + 20, -20, shift + 28, 20); Block(ctx, shift + 12, 20, false); }
            else { Arrow(ctx, rest, shift + 12, 20, shift + 20, -20); Block(ctx, shift + 28, 20, false); }
            // Accionada (casilla izquierda).
            if (!no) { Arrow(ctx, flow, shift - 28, 20, shift - 20, -20); Block(ctx, shift - 12, 20, false); }
            else { Arrow(ctx, flow, shift - 20, -20, shift - 12, 20); Block(ctx, shift - 28, 20, false); }
            Exhaust(ctx, 28, 20);
        }
        else
        {
            Arrow(ctx, rest, shift + 20, 20, shift + 30, -20);
            Arrow(ctx, rest, shift + 10, -20, shift + 6, 20);
            Arrow(ctx, flow, shift - 20, 20, shift - 30, -20);
            Arrow(ctx, flow, shift - 10, -20, shift - 6, 20);
            Exhaust(ctx, 6, 20);
            Exhaust(ctx, 34, 20);
        }

        // Accionamiento izquierdo (lado 14) y derecho (muelle o lado 12).
        bool leftActive = _sim is not null && v.Actuation switch
        {
            ValveActuation.PushButton => _sim.IsPressed(v.Id),
            ValveActuation.Solenoid or ValveActuation.DoubleSolenoid => _sim.IsCoilEnergized(PneumaticSimulation.CoilKey(v, true)),
            ValveActuation.Pilot or ValveActuation.DoublePilot => _sim.PressureAt(v.Id, "14") >= v.PilotPressure,
            _ => on,
        };
        bool rightActive = _sim is not null && v.Actuation switch
        {
            ValveActuation.DoubleSolenoid => _sim.IsCoilEnergized(PneumaticSimulation.CoilKey(v, false)),
            ValveActuation.DoublePilot => _sim.PressureAt(v.Id, "12") >= v.PilotPressure,
            _ => false,
        };
        double lx = -40 + shift, rx = 40 + shift;
        var apen = new Pen(leftActive ? Accent : Ink, 1.3);
        IBrush afill = leftActive ? Active : Body;
        switch (v.Actuation)
        {
            case ValveActuation.PushButton:
                ctx.DrawLine(apen, new Point(lx, 0), new Point(lx - 8, 0));
                ctx.DrawGeometry(afill, apen, new EllipseGeometry(new Rect(lx - 16, -8, 10, 16)));
                break;
            case ValveActuation.Lever:
                ctx.DrawLine(apen, new Point(lx, 0), new Point(lx - 8, 0));
                ctx.DrawLine(apen, new Point(lx - 8, 8), new Point(lx - 16, -10));
                ctx.DrawEllipse(afill, apen, new Point(lx - 16, -10), 2.5, 2.5);
                ctx.DrawLine(apen, new Point(lx - 4, -6), new Point(lx - 4, 6)); // enclavamiento
                break;
            case ValveActuation.Solenoid or ValveActuation.DoubleSolenoid:
                Solenoid(ctx, apen, afill, lx - 14, PneumaticSimulation.CoilKey(v, true), left: true);
                break;
            case ValveActuation.Roller:
                ctx.DrawLine(apen, new Point(lx, 0), new Point(lx - 10, 0));
                ctx.DrawEllipse(afill, apen, new Point(lx - 15, 0), 5, 5);
                break;
            case ValveActuation.Pilot or ValveActuation.DoublePilot:
                Pilot(ctx, apen, afill, lx, -60, left: true);
                break;
        }
        var rpen = new Pen(rightActive ? Accent : Ink, 1.3);
        IBrush rfill = rightActive ? Active : Body;
        if (v.HasSpring)
        {
            var spring = new Pen(Ink, 1.1);
            for (int i = 0; i < 4; i++)
                ctx.DrawLine(spring, new Point(rx + i * 4, i % 2 == 0 ? -8 : 8), new Point(rx + (i + 1) * 4, i % 2 == 0 ? 8 : -8));
        }
        else if (v.Actuation == ValveActuation.DoubleSolenoid) Solenoid(ctx, rpen, rfill, rx, PneumaticSimulation.CoilKey(v, false), left: false);
        else if (v.Actuation == ValveActuation.DoublePilot) Pilot(ctx, rpen, rfill, rx, 60, left: false);
        else if (v.Actuation == ValveActuation.Lever) { }
    }

    private void Solenoid(DrawingContext ctx, IPen pen, IBrush fill, double x, string coil, bool left)
    {
        var r = new Rect(x, -8, 14, 16);
        ctx.DrawRectangle(fill, pen, r);
        ctx.DrawLine(pen, new Point(x + 3, 8), new Point(x + 11, -8));
        Text(ctx, coil, new Point(left ? x - 4 : x, 10), 10, Muted);
    }

    private static void Pilot(DrawingContext ctx, IPen pen, IBrush fill, double x, double portX, bool left)
    {
        double dir = left ? -1 : 1;
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(x + dir * 2, 0), true);
            g.LineTo(new Point(x + dir * 10, -5));
            g.LineTo(new Point(x + dir * 10, 5));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(fill, pen, tri);
        ctx.DrawLine(new Pen(pen.Brush, 1, new DashStyle([3, 2], 0)), new Point(x + dir * 10, 0), new Point(portX, 0));
    }

    private static void Arrow(DrawingContext ctx, IPen pen, double x1, double y1, double x2, double y2)
    {
        ctx.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        double ang = Math.Atan2(y2 - y1, x2 - x1);
        var tip = new Point(x2 - Math.Cos(ang) * 2, y2 - Math.Sin(ang) * 2);
        var a = new Point(tip.X - Math.Cos(ang - 0.4) * 7, tip.Y - Math.Sin(ang - 0.4) * 7);
        var b = new Point(tip.X - Math.Cos(ang + 0.4) * 7, tip.Y - Math.Sin(ang + 0.4) * 7);
        var g = new StreamGeometry();
        using (var gc = g.Open()) { gc.BeginFigure(tip, true); gc.LineTo(a); gc.LineTo(b); gc.EndFigure(true); }
        ctx.DrawGeometry(pen.Brush, null, g);
    }

    private static void Block(DrawingContext ctx, double x, double y, bool top)
    {
        var pen = new Pen(Ink, 1.3);
        double d = top ? 6 : -6;
        ctx.DrawLine(pen, new Point(x, y), new Point(x, y + d));
        ctx.DrawLine(pen, new Point(x - 4, y + d), new Point(x + 4, y + d));
    }

    private static void Exhaust(DrawingContext ctx, double x, double y)
    {
        var pen = new Pen(Ink, 1.1);
        ctx.DrawLine(pen, new Point(x, y), new Point(x, y + 5));
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(x - 5, y + 5), true);
            gc.LineTo(new Point(x + 5, y + 5));
            gc.LineTo(new Point(x, y + 12));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    private void DrawFlowControl(DrawingContext ctx, FlowControl f, IPen pen)
    {
        ctx.DrawLine(pen, new Point(0, 30), new Point(0, -30));
        // Estrangulación: dos arcos enfrentados.
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(-7, -12), false);
            gc.QuadraticBezierTo(new Point(-1, 0), new Point(-7, 12));
            gc.EndFigure(false);
            gc.BeginFigure(new Point(7, -12), false);
            gc.QuadraticBezierTo(new Point(1, 0), new Point(7, 12));
            gc.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
        Arrow(ctx, new Pen(Ink, 1.1), -12, 12, 12, -12); // ajustable
        if (f.OneWay)
        {
            ctx.DrawLine(pen, new Point(0, 22), new Point(20, 22));
            ctx.DrawLine(pen, new Point(0, -22), new Point(20, -22));
            DrawCheckSymbol(ctx, pen, 20, 0, 22, flip: true);
        }
        ctx.DrawRectangle(null, new Pen(Muted, 0.8, new DashStyle([3, 3], 0)), new Rect(-16, -26, 44, 52));
    }

    /// <summary>Antirretorno: bola sobre asiento; deja pasar de abajo (1) arriba (2), o al revés si flip.</summary>
    private static void DrawCheckSymbol(DrawingContext ctx, IPen pen, double x, double y, double half, bool flip = false)
    {
        double s = flip ? -1 : 1;
        ctx.DrawLine(pen, new Point(x, y + half), new Point(x, y + 6 * s));
        ctx.DrawLine(pen, new Point(x, y - 8 * s), new Point(x, y - half));
        ctx.DrawEllipse(Body, pen, new Point(x, y + 1 * s), 5, 5);
        ctx.DrawLine(pen, new Point(x - 7, y - 8 * s), new Point(x, y + 2 * s));
        ctx.DrawLine(pen, new Point(x + 7, y - 8 * s), new Point(x, y + 2 * s));
    }

    private void DrawLogic(DrawingContext ctx, LogicValve l, IPen pen)
    {
        var r = new Rect(-24, -10, 48, 20);
        ctx.DrawRectangle(Body, pen, r);
        ctx.DrawLine(pen, new Point(-30, 0), new Point(-24, 0));
        ctx.DrawLine(pen, new Point(24, 0), new Point(30, 0));
        ctx.DrawLine(pen, new Point(0, -10), new Point(0, -20));
        bool on = _sim is not null && _sim.PressureAt(l.Id, "2") > 0.5e5;
        Text(ctx, l.Kind == LogicKind.Or ? "≥1" : "&", new Point(l.Kind == LogicKind.Or ? -7 : -4, -8), 12, on ? Air : Ink, bold: true);
    }

    private static void DrawSupply(DrawingContext ctx, IPen pen)
    {
        ctx.DrawEllipse(Body, pen, new Point(0, 0), 10, 10);
        ctx.DrawEllipse(Ink, null, new Point(0, 0), 3, 3);
        ctx.DrawLine(pen, new Point(0, -10), new Point(0, -20));
    }

    private void DrawCompressor(DrawingContext ctx, Compressor k, IPen pen)
    {
        bool run = _sim?.IsCompressorRunning(k.Id) == true;
        ctx.DrawEllipse(run ? new SolidColorBrush(Color.Parse("#DDF4E4")) : Body, pen, new Point(0, 0), 16, 16);
        var g = new StreamGeometry();
        using (var gc = g.Open()) { gc.BeginFigure(new Point(0, -14), true); gc.LineTo(new Point(-7, -2)); gc.LineTo(new Point(7, -2)); gc.EndFigure(true); }
        ctx.DrawGeometry(null, pen, g);
        ctx.DrawLine(pen, new Point(0, -16), new Point(0, -22));
        ctx.DrawLine(pen, new Point(-16, 6), new Point(-26, 6));
        Text(ctx, "M", new Point(-36, 0), 10, run ? Ok : Muted, bold: true);
    }

    private void DrawReceiver(DrawingContext ctx, AirReceiver r, IPen pen)
    {
        var rect = new Rect(-16, -34, 32, 68);
        IBrush fill = _sim is null ? Body : new SolidColorBrush(PressureColor(_sim.PressureAt(r.Id, "1")));
        ctx.DrawRectangle(fill, pen, rect, 12, 12);
        ctx.DrawLine(pen, new Point(0, -34), new Point(0, -40));
    }

    private void DrawGauge(DrawingContext ctx, Gauge g, IPen pen)
    {
        ctx.DrawEllipse(Body, pen, new Point(0, 0), 14, 14);
        ctx.DrawLine(pen, new Point(0, 14), new Point(0, 22));
        double p = _sim is null ? 0 : _sim.PressureAt(g.Id, "1");
        double ang = Math.PI * (1.25 - 1.5 * Math.Clamp(p / 10e5, 0, 1));
        ctx.DrawLine(new Pen(Accent, 1.6), new Point(0, 0), new Point(Math.Cos(ang) * 11, -Math.Sin(ang) * 11));
        ctx.DrawEllipse(Ink, null, new Point(0, 0), 1.8, 1.8);
    }

    // ---------------------------------------------------------------- Geometría y búsqueda

    public static Rect LocalBounds(PneumaticComponent c) => c switch
    {
        Cylinder => new Rect(-72, -46, 300, 80),
        DirectionalValve v => new Rect(v.Actuation is ValveActuation.Pilot or ValveActuation.DoublePilot ? -62 : -58, -22, 120, 46),
        FlowControl => new Rect(-18, -32, 48, 64),
        CheckValve => new Rect(-12, -26, 24, 52),
        LogicValve => new Rect(-32, -22, 64, 34),
        AirSupply => new Rect(-12, -22, 24, 34),
        Compressor => new Rect(-38, -24, 56, 42),
        AirReceiver => new Rect(-18, -42, 36, 78),
        Gauge => new Rect(-16, -16, 32, 40),
        _ => new Rect(-20, -20, 40, 40),
    };

    private static Rect RotatedBounds(PneumaticComponent c, Rect local)
    {
        var corners = new[] { local.TopLeft, local.TopRight, local.BottomLeft, local.BottomRight }
            .Select(p => PneumaticComponent.Rotate(p.X, p.Y, c.Rotation)).ToList();
        double x0 = corners.Min(p => p.Item1), x1 = corners.Max(p => p.Item1), y0 = corners.Min(p => p.Item2), y1 = corners.Max(p => p.Item2);
        return new Rect(c.X + x0, c.Y + y0, x1 - x0, y1 - y0);
    }

    private (string Comp, string Port)? PortAt(Point p)
    {
        foreach (var c in _circuit.Components)
            foreach (var port in c.Ports)
            {
                var (x, y) = c.PortPosition(port.Name);
                if (Math.Abs(x - p.X) <= PortHit && Math.Abs(y - p.Y) <= PortHit) return (c.Id, port.Name);
            }
        return null;
    }

    private object? HitTest(Point p)
    {
        for (int i = _circuit.Components.Count - 1; i >= 0; i--)
        {
            var c = _circuit.Components[i];
            if (RotatedBounds(c, LocalBounds(c)).Contains(p)) return c;
        }
        foreach (var t in _circuit.Tubes)
        {
            if (_circuit.Find(t.FromComponent) is not { } a || _circuit.Find(t.ToComponent) is not { } b || !a.HasPort(t.FromPort) || !b.HasPort(t.ToPort)) continue;
            var pts = TubePolyline(t, a, b);
            for (int i = 1; i < pts.Count; i++)
                if (DistanceToSegment(p, pts[i - 1], pts[i]) < 5) return t;
        }
        return null;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>Lleva el punto a la horizontal o la vertical del anterior (tramos rectos como en un esquema real).</summary>
    private static Point Orthogonal(Point prev, Point p) =>
        Math.Abs(p.X - prev.X) >= Math.Abs(p.Y - prev.Y) ? new Point(Snap(p.X), prev.Y) : new Point(prev.X, Snap(p.Y));

    private static double Snap(double v) => Math.Round(v / Grid) * Grid;

    // ---------------------------------------------------------------- Ratón y teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (_tubeStart is not null) CancelTube(); else { Tool = PneuTool.Select; Hint?.Invoke(this, "Herramienta: seleccionar"); }
            return;
        }

        if (_sim is not null) { SimulationClick(pos, e); return; }
        if (DesignLocked) return;

        switch (Tool)
        {
            case PneuTool.Select:
            {
                var hit = HitTest(pos);
                Selected = hit;
                if (hit is PneumaticComponent c) { _dragging = c; _dragOffset = new Point(c.X - pos.X, c.Y - pos.Y); e.Pointer.Capture(this); }
                break;
            }
            case PneuTool.Delete:
                if (HitTest(pos) is { } h) Delete(h);
                break;
            case PneuTool.Tube:
                TubeClick(pos);
                break;
            default:
                Place(pos);
                break;
        }
    }

    private void SimulationClick(Point pos, PointerPressedEventArgs e)
    {
        var hit = HitTest(pos);
        Selected = hit;
        if (hit is not DirectionalValve v) return;
        var local = new Point(pos.X - v.X, pos.Y - v.Y);
        var (lx, _) = PneumaticComponent.Rotate(local.X, local.Y, -v.Rotation);
        bool latch = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (v.Actuation)
        {
            case ValveActuation.PushButton:
                if (latch) _sim!.SetPressed(v.Id, !_sim.IsPressed(v.Id));
                else { _sim!.SetPressed(v.Id, true); _heldValve = v.Id; e.Pointer.Capture(this); }
                break;
            case ValveActuation.Lever:
                _sim!.SetPressed(v.Id, true);
                break;
            case ValveActuation.Solenoid:
            {
                var coil = PneumaticSimulation.CoilKey(v, true);
                _sim!.SetCoil(coil, !_sim.IsCoilEnergized(coil));
                break;
            }
            case ValveActuation.DoubleSolenoid:
            {
                // Mitad izquierda: bobina 14; derecha: bobina 12. Se mantiene mientras dura el clic (Ctrl = enclavada).
                var coil = PneumaticSimulation.CoilKey(v, lx < 0);
                if (latch) _sim!.SetCoil(coil, !_sim.IsCoilEnergized(coil));
                else { _sim!.SetCoil(coil, true); _heldCoil = coil; e.Pointer.Capture(this); }
                break;
            }
            default:
                return;
        }
        ControlUsed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mouse = e.GetPosition(this);
        if (_dragging is not null)
        {
            _dragging.X = Snap(_mouse.X + _dragOffset.X);
            _dragging.Y = Snap(_mouse.Y + _dragOffset.Y);
            InvalidateVisual();
        }
        else if (_tubeStart is not null) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging is not null) { _dragging = null; DesignChanged?.Invoke(this, EventArgs.Empty); InvalidateMeasure(); }
        if (_heldValve is not null) { _sim?.SetPressed(_heldValve, false); _heldValve = null; ControlUsed?.Invoke(this, EventArgs.Empty); }
        if (_heldCoil is not null) { _sim?.SetCoil(_heldCoil, false); _heldCoil = null; ControlUsed?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { CancelTube(); Selected = null; }
        else if (DesignLocked || _sim is not null) return;
        else if (e.Key == Key.Delete && _selected is not null) Delete(_selected);
        else if (e.Key == Key.R && _selected is PneumaticComponent c) RotateSelected();
    }

    public void RotateSelected()
    {
        if (_selected is not PneumaticComponent c || DesignLocked) return;
        c.Rotation = (c.Rotation + 90) % 360;
        DesignChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void CancelTube() { _tubeStart = null; _tubePoints.Clear(); InvalidateVisual(); }

    private void TubeClick(Point pos)
    {
        var port = PortAt(pos);
        if (_tubeStart is null)
        {
            if (port is null) { Hint?.Invoke(this, "Empieza la manguera haciendo clic en una toma (los circulitos de los componentes)."); return; }
            _tubeStart = port;
            Hint?.Invoke(this, "Clic para cada codo y clic en la toma de destino. Clic derecho o Esc para cancelar.");
            InvalidateVisual();
            return;
        }
        if (port is null)
        {
            var (sx, sy) = _circuit.Find(_tubeStart.Value.Comp)!.PortPosition(_tubeStart.Value.Port);
            var prev = _tubePoints.Count > 0 ? new Point(_tubePoints[^1].X, _tubePoints[^1].Y) : new Point(sx, sy);
            var q = Orthogonal(prev, pos);
            _tubePoints.Add(new PortPoint(q.X, q.Y));
            InvalidateVisual();
            return;
        }
        if (port == _tubeStart) return;
        var t = new Tube
        {
            Id = _circuit.NextId("M"),
            FromComponent = _tubeStart.Value.Comp, FromPort = _tubeStart.Value.Port,
            ToComponent = port.Value.Comp, ToPort = port.Value.Port,
            Points = [.. _tubePoints],
        };
        var pts = TubePolyline(t, _circuit.Find(t.FromComponent)!, _circuit.Find(t.ToComponent)!);
        double px = 0;
        for (int i = 1; i < pts.Count; i++) px += Math.Abs(pts[i].X - pts[i - 1].X) + Math.Abs(pts[i].Y - pts[i - 1].Y);
        t.Length = Math.Round(Math.Max(0.2, px * 0.005), 2);
        _circuit.Tubes.Add(t);
        CancelTube();
        Selected = t;
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Place(Point pos)
    {
        string prefix = Tool switch
        {
            PneuTool.CylinderSingle or PneuTool.CylinderDouble => "1A",
            PneuTool.Supply => "0Z", PneuTool.Receiver => "0Z", PneuTool.Compressor => "0P",
            PneuTool.Gauge => "PI",
            PneuTool.Valve32Push or PneuTool.Valve32Roller => "1S",
            _ => "1V",
        };
        PneumaticComponent c = Tool switch
        {
            PneuTool.Supply => new AirSupply(),
            PneuTool.Compressor => new Compressor(),
            PneuTool.Receiver => new AirReceiver(),
            PneuTool.Valve32Push => new DirectionalValve { Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.PushButton },
            PneuTool.Valve32Roller => new DirectionalValve
            {
                Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.Roller,
                RollerCylinder = _circuit.Components.OfType<Cylinder>().FirstOrDefault()?.Id ?? "",
            },
            PneuTool.Valve52Lever => new DirectionalValve { Ways = ValveWays.FiveTwo, Actuation = ValveActuation.Lever },
            PneuTool.Valve52Pilot => new DirectionalValve { Ways = ValveWays.FiveTwo, Actuation = ValveActuation.DoublePilot },
            PneuTool.Valve52Solenoid => new DirectionalValve
            {
                Ways = ValveWays.FiveTwo, Actuation = ValveActuation.DoubleSolenoid,
                Coil14 = NextCoil(), Coil12 = NextCoil(1),
            },
            PneuTool.FlowControl => new FlowControl { Rotation = 180 },
            PneuTool.Check => new CheckValve(),
            PneuTool.OrValve => new LogicValve { Kind = LogicKind.Or },
            PneuTool.AndValve => new LogicValve { Kind = LogicKind.And },
            PneuTool.CylinderSingle => new Cylinder { Kind = CylinderKind.SingleActing, Bore = 0.025, RodDiameter = 0.01, Stroke = 0.1, Mass = 1 },
            PneuTool.CylinderDouble => new Cylinder(),
            _ => new Gauge(),
        };
        c.Id = _circuit.NextId(prefix);
        c.X = Snap(pos.X);
        c.Y = Snap(pos.Y);
        _circuit.Components.Add(c);
        Selected = c;
        DesignChanged?.Invoke(this, EventArgs.Empty);
        InvalidateMeasure();
    }

    private string NextCoil(int extra = 0)
    {
        var used = _circuit.Components.OfType<DirectionalValve>().SelectMany(v => new[] { v.Coil14, v.Coil12 }).ToHashSet();
        int found = 0;
        for (int i = 1; ; i++)
            if (!used.Contains("Y" + i) && found++ == extra) return "Y" + i;
    }

    private void Delete(object element)
    {
        switch (element)
        {
            case PneumaticComponent c: _circuit.Remove(c.Id); break;
            case Tube t: _circuit.Tubes.Remove(t); break;
        }
        Selected = null;
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double MeasureText(string s, double size) =>
        new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, Ink).Width;

    private static void Text(DrawingContext ctx, string text, Point at, double size, IBrush brush, bool bold = false) =>
        ctx.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? new Typeface(Font.FontFamily, FontStyle.Normal, FontWeight.SemiBold) : Font, size, brush), at);
}
