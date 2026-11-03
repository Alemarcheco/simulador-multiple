using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;
using static Simulador.Electrico.Editor.SymbolPainter;

namespace Simulador.Electrico.Editor;

/// <summary>
/// Hoja de esquema: coloca símbolos, cablea bornes y, en simulación, deja accionar pulsadores y protecciones.
/// </summary>
internal sealed class SchematicCanvas : Control
{
    private const double Margin0 = 2 * G;
    public const int SheetWidth = 110;
    public const int SheetHeight = 70;

    private readonly ElementCatalog _catalog;
    private Point _mouse;
    private (PlacedElement el, string term)? _wireStart;
    private PlacedElement? _drag;
    private (double dx, double dy) _dragOffset;
    private bool _dragMoved;
    private Guid? _simPressed;
    private string? _placing;

    public Schematic Schematic { get; private set; } = new();
    public Simulator? Sim { get; set; }
    public PlacedElement? Selected { get; private set; }
    public Wire? SelectedWire { get; private set; }

    public event Action? SelectionChanged;
    public event Action? Modified;
    public event Action<string>? Hint;

    public SchematicCanvas(ElementCatalog catalog)
    {
        _catalog = catalog;
        Focusable = true;
        Width = SheetWidth * G + 2 * Margin0;
        Height = SheetHeight * G + 2 * Margin0;
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

    // ------------------------------------------------------------------ dibujo

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Paper, null, new Rect(Bounds.Size));
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
            var pen = w == SelectedWire ? new Pen(Select, 2.4) : live ? new Pen(Live, 2) : Line;
            for (var i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, ToPx(pts[i]), ToPx(pts[i + 1]));
        }

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
        if (t.Behavior is Behavior.Motor3 or Behavior.Motor6) lines.Add(($"{e.Param(t, "Pn"):0.##} kW", Muted));
        if (t.Behavior is Behavior.Lamp or Behavior.Pilot) lines.Add(($"{e.Param(t, "P"):0.#} W", Muted));

        if (st is not null)
        {
            if (t.Behavior is Behavior.Motor3 or Behavior.Motor6)
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
            if (st.Tripped) lines.Add((t.Behavior switch
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
        _mouse = e.GetPosition(this);
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
        _mouse = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

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
        SelectElement(null);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
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
