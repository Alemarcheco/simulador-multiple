using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;
using static Simulador.Electrico.Editor.SymbolPainter;
using Vector = Avalonia.Vector;

namespace Simulador.Electrico.Editor;

/// <summary>
/// Hoja de esquema: coloca símbolos, cablea bornes y, en simulación, deja accionar pulsadores y protecciones.
/// </summary>
internal sealed class SchematicCanvas : Control
{
    private const double Margin0 = 2 * G;
    public const int SheetWidth = 110;
    public const int SheetHeight = 70;
    private const double BaseWidth = SheetWidth * G + 2 * Margin0;
    private const double BaseHeight = SheetHeight * G + 2 * Margin0;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 6;
    private const double WheelStep = 1.15;

    private readonly ElementCatalog _catalog;
    private Point _mouse;
    private (PlacedElement el, string term)? _wireStart;
    private PlacedElement? _drag;
    private (double dx, double dy) _dragOffset;
    private bool _dragMoved;
    private Guid? _simPressed;
    private string? _placing;
    private double _zoom = 1;
    private Point? _panStart;
    private Vector _panScroll;

    public Schematic Schematic { get; private set; } = new();
    public Simulator? Sim { get; set; }

    /// <summary>Puntos que recorren los cables con la corriente (como Falstad): en continua avanzan, en alterna van y vienen.</summary>
    public bool ShowCurrent { get; set; } = true;
    /// <summary>Color de los cables según su tensión instantánea: verde positiva, rojo negativa, gris sin tensión.</summary>
    public bool ShowVoltage { get; set; } = true;
    public PlacedElement? Selected { get; private set; }
    public Wire? SelectedWire { get; private set; }

    public event Action? SelectionChanged;
    public event Action? Modified;
    public event Action<string>? Hint;

    public SchematicCanvas(ElementCatalog catalog)
    {
        _catalog = catalog;
        Focusable = true;
        Width = BaseWidth;
        Height = BaseHeight;
        // Arriba a la izquierda dentro del ScrollViewer, para que posición en la hoja = posición en pantalla + desplazamiento.
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ClipToBounds = true;
    }

    public string? Placing
    {
        get => _placing;
        set
        {
            _placing = value;
            _wireStart = null;
            if (value is not null) Hint?.Invoke($"Clic en la hoja para colocar «{_catalog.Get(value).Name}». Clic derecho o Esc para terminar.");
            InvalidateVisual();
        }
    }

    public void Load(Schematic s)
    {
        Schematic = s;
        Selected = null;
        SelectedWire = null;
        _wireStart = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ zoom y desplazamiento

    /// <summary>Escala de la hoja (1 = 100 %).</summary>
    public double Zoom => _zoom;

    private ScrollViewer? Scroller => this.FindAncestorOfType<ScrollViewer>();

    /// <summary>Posición del ratón en la hoja sin escalar (la que usan el dibujo y la detección de clics).</summary>
    private Point SheetPosition(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(p.X / _zoom, p.Y / _zoom);
    }

    /// <summary>
    /// Cambia el zoom manteniendo quieto bajo el ratón (o en el centro de la vista) el punto de la hoja que había allí.
    /// <paramref name="anchor"/> va en coordenadas de este control, ya escaladas.
    /// </summary>
    public void SetZoom(double zoom, Point? anchor = null)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(zoom - _zoom) < 1e-9) return;
        var sv = Scroller;
        var offset = sv?.Offset ?? default;
        var a = anchor ?? (sv is null
            ? new Point(Bounds.Width / 2, Bounds.Height / 2)
            : new Point(offset.X + sv.Viewport.Width / 2, offset.Y + sv.Viewport.Height / 2));
        var sheet = new Point(a.X / _zoom, a.Y / _zoom);
        var inView = new Vector(a.X - offset.X, a.Y - offset.Y);

        _zoom = zoom;
        Width = BaseWidth * zoom;
        Height = BaseHeight * zoom;
        if (sv is not null)
        {
            sv.UpdateLayout();
            sv.Offset = new Vector(Math.Max(0, sheet.X * zoom - inView.X), Math.Max(0, sheet.Y * zoom - inView.Y));
        }
        InvalidateVisual();
        Hint?.Invoke($"Zoom {zoom * 100:0} %  ·  Rueda: acercar/alejar  ·  Arrastrar el fondo o botón central: desplazar  ·  Ctrl+0: 100 %  ·  F: ajustar");
    }

    /// <summary>Ajusta el zoom para ver la hoja entera en la ventana.</summary>
    public void ZoomToFit()
    {
        var sv = Scroller;
        if (sv is null || sv.Viewport.Width < 10 || sv.Viewport.Height < 10) return;
        SetZoom(Math.Min(sv.Viewport.Width / BaseWidth, sv.Viewport.Height / BaseHeight));
        sv.Offset = default;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Mayús + rueda: la deja pasar al ScrollViewer para desplazar en horizontal.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.Y == 0)
        {
            base.OnPointerWheelChanged(e);
            return;
        }
        SetZoom(_zoom * Math.Pow(WheelStep, e.Delta.Y), e.GetPosition(this));
        _mouse = SheetPosition(e);
        e.Handled = true;
    }

    private void StartPan(PointerPressedEventArgs e)
    {
        if (Scroller is not { } sv) return;
        _panStart = e.GetPosition(sv);
        _panScroll = sv.Offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
    }

    // ------------------------------------------------------------------ coordenadas

    private static Point ToPx(double gx, double gy) => new(Margin0 + gx * G, Margin0 + gy * G);
    private static Point ToPx(Layout.GridPoint p) => ToPx(p.X, p.Y);
    private static (double x, double y) ToGrid(Point p) => ((p.X - Margin0) / G, (p.Y - Margin0) / G);

    private (PlacedElement el, string term)? HitTerminal(Point p)
    {
        foreach (var e in Schematic.Elements)
        {
            if (!_catalog.TryGet(e.Type, out var t)) continue;
            foreach (var term in t.Terminals)
            {
                var tp = ToPx(Layout.TerminalPosition(e, term));
                if (Math.Abs(tp.X - p.X) <= 6 && Math.Abs(tp.Y - p.Y) <= 6) return (e, term.Id);
            }
        }
        return null;
    }

    private PlacedElement? HitElement(Point p)
    {
        var (gx, gy) = ToGrid(p);
        for (var i = Schematic.Elements.Count - 1; i >= 0; i--)
        {
            var e = Schematic.Elements[i];
            if (!_catalog.TryGet(e.Type, out var t)) continue;
            var b = Layout.WorldBounds(e, t);
            if (gx >= b.X0 - 0.2 && gx <= b.X1 + 0.2 && gy >= b.Y0 - 0.2 && gy <= b.Y1 + 0.2) return e;
        }
        return null;
    }

    private Wire? HitWire(Point p)
    {
        var routes = RouteAll();
        foreach (var w in Schematic.Wires)
        {
            if (!routes.TryGetValue(w.Id, out var pts)) continue;
            for (var i = 0; i + 1 < pts.Count; i++)
                if (DistanceToSegment(p, ToPx(pts[i]), ToPx(pts[i + 1])) < 4) return w;
        }
        return null;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var d = b - a;
        var len2 = d.X * d.X + d.Y * d.Y;
        var t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / len2, 0, 1);
        var q = a + d * t;
        return Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2));
    }

    private Layout.GridPoint? TerminalPos(TerminalRef r)
    {
        var e = Schematic.Find(r.Element);
        if (e is null || !_catalog.TryGet(e.Type, out var t) || t.Terminal(r.Terminal) is not { } term) return null;
        return Layout.TerminalPosition(e, term);
    }

    /// <summary>
    /// Traza todos los conductores evitando símbolos y sin pisar cables de otra red
    /// (los de la misma red sí pueden compartir tramo, porque están unidos).
    /// </summary>
    private Dictionary<Guid, IReadOnlyList<Layout.GridPoint>> RouteAll()
    {
        var obstacles = new List<Layout.Box>();
        foreach (var e in Schematic.Elements)
            if (_catalog.TryGet(e.Type, out var t) && t.Behavior != Behavior.Junction)
                obstacles.Add(Layout.WorldBounds(e, t));

        // Redes en edición: unión de bornes por conductores.
        var parent = new Dictionary<string, string>();
        string Find(string k)
        {
            if (!parent.TryGetValue(k, out var p)) { parent[k] = k; return k; }
            return p == k ? k : parent[k] = Find(p);
        }
        foreach (var w in Schematic.Wires)
        {
            var ra = Find(w.A.Key);
            var rb = Find(w.B.Key);
            if (ra != rb) parent[ra] = rb;
        }

        var routed = new Dictionary<Guid, IReadOnlyList<Layout.GridPoint>>();
        var segments = new List<(string net, Layout.GridPoint p, Layout.GridPoint q)>();
        foreach (var w in Schematic.Wires)
        {
            var a = TerminalPos(w.A);
            var b = TerminalPos(w.B);
            if (a is null || b is null) continue;
            var net = Find(w.A.Key);
            var foreign = segments.Where(s => s.net != net).Select(s => (s.p, s.q)).ToList();
            var pts = Layout.Route(a.Value, b.Value, obstacles, foreign);
            routed[w.Id] = pts;
            for (var i = 0; i + 1 < pts.Count; i++) segments.Add((net, pts[i], pts[i + 1]));
        }
        return routed;
    }

    // ------------------------------------------------------------------ animación de corriente y tensión

    /// <summary>Frecuencia de la animación para 50 Hz: la alterna real se ve ralentizada para poder seguirla.</summary>
    private const double AnimationHz = 0.6;
    private const double DotSpacing = 22;

    private Simulator? _animSim;
    private readonly Dictionary<Guid, double> _dotOffset = [];
    private readonly Dictionary<int, double> _netTheta = [];
    private Dictionary<Guid, (Complex ac, double dc)> _wireCurrent = [];
    private double _vpeak = 1;

    /// <summary>Avanza la animación <paramref name="dt"/> segundos de reloj (lo llama la vista en cada fotograma).</summary>
    public void Animate(double dt)
    {
        if (Sim is null) { _animSim = null; return; }
        if (!ReferenceEquals(Sim, _animSim))
        {
            _animSim = Sim;
            _dotOffset.Clear();
            _netTheta.Clear();
        }
        _wireCurrent = ComputeWireCurrents();
        var nets = new HashSet<int>();
        double vmax = 1;
        foreach (var w in Schematic.Wires)
        {
            var net = Sim.NetOf(w.A.Element, w.A.Terminal);
            if (net < 0) continue;
            if (nets.Add(net))
            {
                var f = Math.Abs(Sim.NetFrequency(net));
                _netTheta[net] = _netTheta.GetValueOrDefault(net) + 2 * Math.PI * AnimationHz * f / 50 * dt;
                var (vac, vdc) = Sim.NetVoltage(net);
                vmax = Math.Max(vmax, Math.Abs(vdc) + Math.Sqrt(2) * vac.Magnitude);
            }
            var i = Instant(_wireCurrent.GetValueOrDefault(w.Id), _netTheta[net]);
            var speed = Math.Sign(i) * 40 * Math.Log10(1 + Math.Abs(i) / 0.01);
            _dotOffset[w.Id] = (_dotOffset.GetValueOrDefault(w.Id) + speed * dt) % (DotSpacing * 1000);
        }
        _vpeak = vmax;
    }

    private static double Instant((Complex ac, double dc) x, double theta) =>
        x.dc + Math.Sqrt(2) * (x.ac * Complex.FromPolarCoordinates(1, theta)).Real;

    /// <summary>
    /// Corriente de cada conductor (positiva de A hacia B). En cada red, los cables forman un árbol entre bornes:
    /// la corriente de un cable es la suma de lo que inyectan los bornes que quedan a un lado. Si hay lazos, los
    /// cables que cierran el lazo se dejan a cero (la corriente real se repartiría).
    /// </summary>
    private Dictionary<Guid, (Complex ac, double dc)> ComputeWireCurrents()
    {
        var result = new Dictionary<Guid, (Complex, double)>();
        if (Sim is null) return result;
        var adj = new Dictionary<string, List<(Wire w, string other)>>();
        foreach (var w in Schematic.Wires)
        {
            if (w.A.Key == w.B.Key) continue;
            (adj.TryGetValue(w.A.Key, out var la) ? la : adj[w.A.Key] = []).Add((w, w.B.Key));
            (adj.TryGetValue(w.B.Key, out var lb) ? lb : adj[w.B.Key] = []).Add((w, w.A.Key));
        }
        var refs = new Dictionary<string, TerminalRef>();
        foreach (var w in Schematic.Wires) { refs[w.A.Key] = w.A; refs[w.B.Key] = w.B; }
        var visited = new HashSet<string>();
        foreach (var start in adj.Keys)
        {
            if (!visited.Add(start)) continue;
            var order = new List<string> { start };
            var parentWire = new Dictionary<string, (Wire w, string parent)>();
            for (var k = 0; k < order.Count; k++)
            {
                foreach (var (w, other) in adj[order[k]])
                {
                    if (visited.Contains(other)) continue;
                    visited.Add(other);
                    parentWire[other] = (w, order[k]);
                    order.Add(other);
                }
            }
            var sub = new Dictionary<string, (Complex ac, double dc)>();
            for (var k = order.Count - 1; k >= 0; k--)
            {
                var node = order[k];
                var r = refs[node];
                var (iac, idc) = Sim.TerminalCurrent(r.Element, r.Terminal);
                var acc = sub.GetValueOrDefault(node);
                // Lo que entra en el elemento sale de la red: la inyección en el árbol es la contraria.
                acc = (acc.ac - iac, acc.dc - idc);
                sub[node] = acc;
                if (!parentWire.TryGetValue(node, out var pw)) continue;
                var up = sub.GetValueOrDefault(pw.parent);
                sub[pw.parent] = (up.ac + acc.ac, up.dc + acc.dc);
                // acc fluye del nudo hijo al padre.
                result[pw.w.Id] = pw.w.A.Key == node ? (acc.ac, acc.dc) : (-acc.ac, -acc.dc);
            }
        }
        return result;
    }

    private IPen WirePen(Wire w, bool live)
    {
        if (Sim is null) return Line;
        if (!ShowVoltage) return live ? new Pen(Live, 2) : Line;
        var net = Sim.NetOf(w.A.Element, w.A.Terminal);
        var (vac, vdc) = Sim.NetVoltage(net);
        var v = Instant((vac, vdc), _netTheta.GetValueOrDefault(net));
        var c = Math.Sign(v) * Math.Log10(1 + Math.Abs(v)) / Math.Log10(1 + Math.Max(_vpeak, 2));
        return new Pen(new SolidColorBrush(VoltageColor(c)), 2.2);
    }

    private static readonly Color Zero = Color.Parse("#8A949E");
    private static readonly Color Positive = Color.Parse("#1FAA3A");
    private static readonly Color Negative = Color.Parse("#E0281E");

    private static Color VoltageColor(double c)
    {
        var t = Math.Clamp(Math.Abs(c), 0, 1);
        var to = c >= 0 ? Positive : Negative;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromRgb(Mix(Zero.R, to.R), Mix(Zero.G, to.G), Mix(Zero.B, to.B));
    }

    private static readonly IBrush DotFill = new SolidColorBrush(Color.Parse("#FFD60A"));
    private static readonly IPen DotEdge = new Pen(new SolidColorBrush(Color.Parse("#5C4A00")), 0.8);

    private void DrawCurrentDots(DrawingContext dc, Wire w, IReadOnlyList<Layout.GridPoint> pts)
    {
        var x = _wireCurrent.GetValueOrDefault(w.Id);
        var amplitude = Math.Abs(x.dc) + x.ac.Magnitude;
        if (amplitude < 0.002) return;
        var offset = _dotOffset.GetValueOrDefault(w.Id) % DotSpacing;
        if (offset < 0) offset += DotSpacing;
        var travelled = 0.0;
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var a = ToPx(pts[i]);
            var b = ToPx(pts[i + 1]);
            var len = Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
            if (len < 1e-6) continue;
            // Primer punto de este tramo en la sucesión offset + k·DotSpacing.
            var first = offset - travelled;
            first -= Math.Floor(first / DotSpacing) * DotSpacing;
            for (var d = first; d <= len; d += DotSpacing)
            {
                var p = a + (b - a) * (d / len);
                dc.DrawEllipse(DotFill, DotEdge, p, 2.3, 2.3);
            }
            travelled += len;
        }
    }

    // ------------------------------------------------------------------ dibujo

    public override void Render(DrawingContext dc)
    {
        using (dc.PushTransform(Matrix.CreateScale(_zoom, _zoom)))
            RenderSheet(dc);
    }

    private void RenderSheet(DrawingContext dc)
    {
        dc.DrawRectangle(Paper, null, new Rect(0, 0, BaseWidth, BaseHeight));
        DrawGrid(dc);

        // Conductores (en simulación, en rojo los que tienen tensión).
        var connections = new Dictionary<string, int>();
        var routes = RouteAll();
        foreach (var w in Schematic.Wires)
        {
            connections[w.A.Key] = connections.GetValueOrDefault(w.A.Key) + 1;
            connections[w.B.Key] = connections.GetValueOrDefault(w.B.Key) + 1;
            if (!routes.TryGetValue(w.Id, out var pts)) continue;
            var live = Sim is not null && Sim.TerminalVoltage(w.A.Element, w.A.Terminal) > 12;
            var pen = w == SelectedWire ? new Pen(Select, 2.4) : WirePen(w, live);
            for (var i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, ToPx(pts[i]), ToPx(pts[i + 1]));
        }
        if (Sim is not null && ShowCurrent)
            foreach (var w in Schematic.Wires)
                if (routes.TryGetValue(w.Id, out var pts)) DrawCurrentDots(dc, w, pts);

        foreach (var e in Schematic.Elements)
        {
            if (!_catalog.TryGet(e.Type, out var t)) continue;
            var st = Sim?.Status(e.Id);
            using (dc.PushTransform(Matrix.CreateRotation(Math.PI / 2 * e.Rotation) * Matrix.CreateTranslation(ToPx(e.X, e.Y).X, ToPx(e.X, e.Y).Y)))
            {
                SymbolPainter.Draw(dc, e, t, st);
            }
            DrawLabels(dc, e, t, st);
            foreach (var term in t.Terminals)
            {
                var p = ToPx(Layout.TerminalPosition(e, term));
                var key = new TerminalRef(e.Id, term.Id).Key;
                var n = connections.GetValueOrDefault(key);
                if (n >= 2 || t.Behavior == Behavior.Junction) dc.DrawEllipse(Ink, null, p, 3, 3);
                else if (n == 0 && Sim is null) dc.DrawEllipse(Paper, new Pen(Muted, 1), p, 2.5, 2.5);
            }
            if (e == Selected)
            {
                var b = Layout.WorldBounds(e, t);
                dc.DrawRectangle(null, new Pen(Select, 1.2, new DashStyle([4, 3], 0)),
                    new Rect(ToPx(b.X0 - 0.3, b.Y0 - 0.3), ToPx(b.X1 + 0.3, b.Y1 + 0.3)));
            }
        }

        // Borne bajo el ratón y cable en curso.
        if (Sim is null)
        {
            if (HitTerminal(_mouse) is { } h)
            {
                var p = ToPx(Layout.TerminalPosition(h.el, _catalog.Get(h.el.Type).Terminal(h.term)!));
                dc.DrawEllipse(null, new Pen(Select, 2), p, 6, 6);
            }
            if (_wireStart is { } ws && TerminalPos(new TerminalRef(ws.el.Id, ws.term)) is { } a)
            {
                var (gx, gy) = ToGrid(_mouse);
                var pts = Layout.Route(a, new Layout.GridPoint(Math.Round(gx), Math.Round(gy)));
                var pen = new Pen(Select, 1.6, new DashStyle([4, 3], 0));
                for (var i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, ToPx(pts[i]), ToPx(pts[i + 1]));
            }
            if (_placing is not null && _catalog.TryGet(_placing, out var pt))
            {
                var (gx, gy) = ToGrid(_mouse);
                using (dc.PushOpacity(0.45))
                using (dc.PushTransform(Matrix.CreateTranslation(ToPx(Math.Round(gx), Math.Round(gy)).X, ToPx(Math.Round(gx), Math.Round(gy)).Y)))
                    SymbolPainter.Draw(dc, new PlacedElement { Type = _placing }, pt, null);
            }
        }
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(new SolidColorBrush(Color.Parse("#EEF1F5")), 1);
        var major = new Pen(new SolidColorBrush(Color.Parse("#DDE3EA")), 1);
        for (var x = 0; x <= SheetWidth; x += 2)
            dc.DrawLine(x % 10 == 0 ? major : minor, ToPx(x, 0), ToPx(x, SheetHeight));
        for (var y = 0; y <= SheetHeight; y += 2)
            dc.DrawLine(y % 10 == 0 ? major : minor, ToPx(0, y), ToPx(SheetWidth, y));
    }

    private void DrawLabels(DrawingContext dc, PlacedElement e, ElementType t, ElementStatus? st)
    {
        var b = Layout.WorldBounds(e, t);
        var isSource = t.IsSource;
        var x = isSource ? b.X1 + 0.3 : b.X1 + 0.1;
        var y = isSource ? b.Y0 + 0.3 : b.Y0 + (b.Y1 - b.Y0) / 2 - 0.9;
        // El variador tiene bornes de mando a la derecha: su rótulo va debajo de ellos.
        if (t.Behavior == Behavior.Vfd && e.Rotation == 0) y = b.Y0 + 6.5;
        var tag = Text(string.IsNullOrWhiteSpace(e.Tag) ? "?" : e.Tag, 11, Ink, FontWeight.SemiBold);
        dc.DrawText(tag, ToPx(x, y));
        var lines = new List<(string text, IBrush brush)>();
        var reference = e.Text("Referencia");
        if (reference.Length > 0 && st is null) lines.Add((reference, Muted));
        if (t.IsCoil) lines.Add(($"{e.Param(t, "V"):0.#} V {(e.Param(t, "ac") >= 0.5 ? "AC" : "DC")}", Muted));
        if (t.Behavior is Behavior.CoilTimerOn or Behavior.CoilTimerOff) lines.Add(($"t = {e.Param(t, "t"):0.##} s", Muted));
        if (t.Behavior is Behavior.Breaker) lines.Add(($"{"BCD"[(int)Math.Clamp(e.Param(t, "curva"), 0, 2)]}{e.Param(t, "In"):0.#}", Muted));
        if (t.Behavior is Behavior.MotorBreaker or Behavior.ThermalRelay) lines.Add(($"Ir {e.Param(t, "Ir"):0.#} A", Muted));
        if (t.Behavior == Behavior.Fuse) lines.Add(($"{e.Param(t, "In"):0.#} A {(e.Param(t, "tipo") >= 0.5 ? "aM" : "gG")}", Muted));
        if (t.Behavior == Behavior.Rcd) lines.Add(($"{e.Param(t, "In"):0} A {e.Param(t, "Idn"):0} mA", Muted));
        if (t.IsMotor) lines.Add(($"{e.Param(t, "Pn"):0.##} kW", Muted));
        if (t.Behavior == Behavior.Transformer1) lines.Add(($"{e.Param(t, "V1"):0}/{e.Param(t, "V2"):0} V {e.Param(t, "S"):0} VA", Muted));
        if (t.Behavior == Behavior.Transformer3) lines.Add(($"{e.Param(t, "V1"):0}/{e.Param(t, "V2"):0} V {e.Param(t, "S"):0.#} kVA", Muted));
        if (t.Behavior == Behavior.Vfd && st is null) lines.Add(($"{e.Param(t, "Pn"):0.##} kW → {e.Param(t, "fref"):0.#} Hz", Muted));
        if (t.Behavior is Behavior.Lamp or Behavior.Pilot) lines.Add(($"{e.Param(t, "P"):0.#} W", Muted));

        if (st is not null)
        {
            if (t.Behavior == Behavior.Vfd)
            {
                lines.Add((st.Fault is not null ? "FALLO" : st.State, st.Fault is not null ? Fault : Select));
                if (st.Current > 0.05) lines.Add((FormatAmps(st.Current), Select));
            }
            else if (t.Behavior is Behavior.Transformer1 or Behavior.Transformer3)
            {
                lines.Add(($"{st.Voltage:0.#} V", Select));
                if (st.Current > 0.005) lines.Add((FormatAmps(st.Current), Select));
            }
            else if (t.Behavior == Behavior.Plc)
            {
                lines.Add((st.Powered ? "RUN" : "sin 24 V", st.Powered ? Select : Fault));
                for (var i = 0; i < st.AnalogInputs.Length; i++)
                    if (st.AnalogInputs[i] > 0.01) lines.Add(($"AI{i + 1} {st.AnalogInputs[i]:0.00} V", Select));
            }
            else if (t.Behavior == Behavior.Transmitter)
            {
                var kind = ElementCatalog.TransmitterKinds[(int)Math.Clamp(e.Param(t, "magnitud"), 0, ElementCatalog.TransmitterKinds.Length - 1)];
                lines.Add(($"{st.Percent / 100 * kind.Full:0.#} {kind.Unit}", Select));
                lines.Add((st.Powered ? $"{st.Voltage:0.00} V" : "sin 24 V", st.Powered ? Select : Fault));
            }
            else if (t.IsMotor)
            {
                lines.Add(($"{Math.Abs(st.Rpm):0} rpm {(st.Rpm > 5 ? "↻" : st.Rpm < -5 ? "↺" : "")}", Select));
                lines.Add((FormatAmps(st.Current), Select));
            }
            else if (t.PoleCount > 0 && st.Current > 0.005) lines.Add((FormatAmps(st.Current), Select));
            if (t.Behavior is Behavior.CoilTimerOn or Behavior.CoilTimerOff && st.CoilOn != st.Actuated)
                lines.Add(($"{st.TimerElapsed:0.0} s", Select));
            if (t.Behavior is Behavior.ThermalRelay or Behavior.MotorBreaker or Behavior.Breaker && st.Thermal > 0.05)
                lines.Add(($"calor {Math.Min(st.Thermal / (t.Behavior == Behavior.Breaker ? 1.69 : 1.44), 9.99):P0}", st.Thermal > 1 ? Fault : Muted));
            if (t.Behavior == Behavior.Rcd && st.Residual > 0.0005) lines.Add(($"fuga {st.Residual * 1000:0} mA", Fault));
            if (st.Tripped && t.Behavior != Behavior.Vfd) lines.Add((t.Behavior switch
            {
                Behavior.Fuse => "FUNDIDO",
                Behavior.Lamp or Behavior.Pilot => "FUNDIDA",
                _ => "DISPARADO",
            }, Fault));
        }
        var ly = y + 1.0;
        foreach (var (text, brush) in lines)
        {
            dc.DrawText(Text(text, 9.5, brush), ToPx(x, ly));
            ly += 0.8;
        }

        // Numeración de bornes (13-14, 95-96, A1-A2…).
        var custom = e.Text("Bornes", t.DefaultTerminalText);
        var parts = custom.Split('-', 2);
        for (var i = 0; i < t.Terminals.Count; i++)
        {
            var term = t.Terminals[i];
            var label = t.Terminals.Count == 2 && parts.Length == 2 ? parts[i] : term.Label;
            if (string.IsNullOrEmpty(label)) continue;
            var p = ToPx(Layout.TerminalPosition(e, term));
            var tx = Text(label, 8, Muted);
            // Junto al borne, por dentro del símbolo, para que no choquen los números de dos aparatos seguidos.
            var maxY = t.Terminals.Max(x => x.Y);
            var offsetY = isSource ? 2 : term.Y == maxY && maxY > 0 ? -tx.Height - 1 : 1;
            if (e.Rotation % 2 == 1) offsetY = -tx.Height - 1;
            dc.DrawText(tx, new Point(p.X - tx.Width - 3, p.Y + offsetY));
        }
    }

    // ------------------------------------------------------------------ ratón y teclado

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_panStart is { } start && Scroller is { } sv)
        {
            var d = e.GetPosition(sv) - start;
            sv.Offset = new Vector(Math.Max(0, _panScroll.X - d.X), Math.Max(0, _panScroll.Y - d.Y));
            _mouse = SheetPosition(e);
            return;
        }
        _mouse = SheetPosition(e);
        if (_drag is not null && Sim is null)
        {
            var (gx, gy) = ToGrid(_mouse);
            var nx = (int)Math.Round(gx - _dragOffset.dx);
            var ny = (int)Math.Round(gy - _dragOffset.dy);
            if (nx != _drag.X || ny != _drag.Y) { _drag.X = nx; _drag.Y = ny; _dragMoved = true; }
        }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        _mouse = SheetPosition(e);
        var props = e.GetCurrentPoint(this).Properties;

        // Botón central: desplazar la hoja, tanto editando como simulando.
        if (props.IsMiddleButtonPressed)
        {
            StartPan(e);
            return;
        }

        if (Sim is not null)
        {
            if (!props.IsLeftButtonPressed) return;
            if (HitElement(_mouse) is { } el)
            {
                _simPressed = el.Id;
                Sim.PointerDown(el.Id, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Pointer.Capture(this);
                SelectElement(el);
            }
            else StartPan(e);
            return;
        }

        if (props.IsRightButtonPressed)
        {
            if (_placing is not null) { Placing = null; Hint?.Invoke("Listo."); }
            _wireStart = null;
            InvalidateVisual();
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        if (_placing is not null)
        {
            var (gx, gy) = ToGrid(_mouse);
            var type = _catalog.Get(_placing);
            var placed = new PlacedElement { Type = _placing, X = (int)Math.Round(gx), Y = (int)Math.Round(gy), Tag = SuggestTag(type) };
            Schematic.Elements.Add(placed);
            SelectElement(placed);
            Modified?.Invoke();
            return;
        }

        if (HitTerminal(_mouse) is { } term)
        {
            if (_wireStart is null)
            {
                _wireStart = term;
                Hint?.Invoke("Clic en otro borne para terminar el conductor. Esc para cancelar.");
            }
            else
            {
                var (a, ta) = _wireStart.Value;
                if (a.Id != term.el.Id || ta != term.term)
                {
                    var exists = Schematic.Wires.Any(w =>
                        (w.A.Element == a.Id && w.A.Terminal == ta && w.B.Element == term.el.Id && w.B.Terminal == term.term) ||
                        (w.B.Element == a.Id && w.B.Terminal == ta && w.A.Element == term.el.Id && w.A.Terminal == term.term));
                    if (!exists)
                    {
                        Schematic.Wires.Add(new Wire { A = new TerminalRef(a.Id, ta), B = new TerminalRef(term.el.Id, term.term) });
                        Modified?.Invoke();
                    }
                }
                // Con Mayús se sigue cableando desde el borne final.
                _wireStart = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? term : null;
                Hint?.Invoke(_wireStart is null ? "Conductor añadido." : "Sigue cableando desde este borne.");
            }
            InvalidateVisual();
            return;
        }

        _wireStart = null;
        if (HitElement(_mouse) is { } hit)
        {
            SelectElement(hit);
            var (gx, gy) = ToGrid(_mouse);
            _drag = hit;
            _dragOffset = (gx - hit.X, gy - hit.Y);
            _dragMoved = false;
            e.Pointer.Capture(this);
            return;
        }
        if (HitWire(_mouse) is { } wire)
        {
            Selected = null;
            SelectedWire = wire;
            SelectionChanged?.Invoke();
            InvalidateVisual();
            return;
        }
        // Clic en el fondo: quita la selección y, si se arrastra, desplaza la hoja.
        SelectElement(null);
        StartPan(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_panStart is not null)
        {
            _panStart = null;
            Cursor = null;
        }
        if (_simPressed is { } id && Sim is not null)
        {
            Sim.PointerUp(id);
            _simPressed = null;
        }
        if (_drag is not null && _dragMoved) Modified?.Invoke();
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.D0 or Key.NumPad0 when ctrl:
                SetZoom(1);
                e.Handled = true;
                return;
            case Key.OemPlus or Key.Add when ctrl:
                SetZoom(_zoom * WheelStep * WheelStep);
                e.Handled = true;
                return;
            case Key.OemMinus or Key.Subtract when ctrl:
                SetZoom(_zoom / (WheelStep * WheelStep));
                e.Handled = true;
                return;
            case Key.F when !ctrl:
                ZoomToFit();
                e.Handled = true;
                return;
        }
        if (Sim is not null) return;
        switch (e.Key)
        {
            case Key.Escape:
                _wireStart = null;
                Placing = null;
                Hint?.Invoke("Listo.");
                break;
            case Key.Delete or Key.Back:
                DeleteSelection();
                break;
            case Key.R when Selected is not null:
                Selected.Rotation = (Selected.Rotation + 1) % 4;
                Modified?.Invoke();
                break;
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        if (Selected is not null)
        {
            var id = Selected.Id;
            Schematic.Elements.Remove(Selected);
            Schematic.Wires.RemoveAll(w => w.A.Element == id || w.B.Element == id);
            SelectElement(null);
            Modified?.Invoke();
        }
        else if (SelectedWire is not null)
        {
            Schematic.Wires.Remove(SelectedWire);
            SelectedWire = null;
            SelectionChanged?.Invoke();
            Modified?.Invoke();
        }
        InvalidateVisual();
    }

    private void SelectElement(PlacedElement? e)
    {
        Selected = e;
        SelectedWire = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>
    /// Propone una designación: los contactos toman la del último aparato con el mismo prefijo (KM1),
    /// y los aparatos nuevos el siguiente número libre (KM2).
    /// </summary>
    private string SuggestTag(ElementType type)
    {
        var prefix = type.TagPrefix;
        var used = Schematic.Elements.Select(x => x.Tag).Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (type.IsFollowerContact)
        {
            if (Selected is not null && _catalog.TryGet(Selected.Type, out var st) && (st.IsCoil || st.PoleCount > 0 || st.IsManual)) return Selected.Tag;
            var last = Schematic.Elements.LastOrDefault(x => _catalog.TryGet(x.Type, out var t) && t.IsCoil && x.Tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (last is not null) return last.Tag;
        }
        var n = 1;
        while (used.Any(u => u.Equals(prefix + n, StringComparison.OrdinalIgnoreCase))) n++;
        return prefix + n;
    }
}
