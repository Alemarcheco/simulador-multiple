using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Core.Signals;

namespace Simulador.Banco.Mesa.Ui;

/// <summary>
/// La mesa: dibuja las piezas como bloques con sus bornes y los cables entre ellos. En edición se ponen piezas, se
/// mueven y se cablean arrastrando de borne a borne; en marcha se ven sus valores, se pulsan los pulsadores y los
/// cables se iluminan cuando llevan señal o potencia.
/// </summary>
public sealed class BenchCanvas : Control
{
    public const double PartWidth = 190, Header = 34, Row = 18, Grid = 10, LineH = 14;
    private const double PortR = 5, PortHit = 10;

    private BenchProject _project = new();
    private Point _pan = new(20, 20);
    private double _zoom = 1;
    private double _phase;

    private enum Drag { None, Move, Pan, Wire, Press }
    private Drag _drag;
    private Point _dragStart, _dragWorld, _partStart, _panStart;
    private (string Part, string Port)? _wireFrom;
    private Point _wireEnd;
    private string? _pressedPart;
    private (string Part, string Port)? _hoverPort;

    public BenchCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public BenchProject Project
    {
        get => _project;
        set { _project = value; Selected = null; SelectedWire = null; InvalidateVisual(); }
    }

    /// <summary>true mientras se simula: no se puede añadir, borrar ni cablear (sí mover y accionar).</summary>
    public bool Running { get; set; }

    public BenchPart? Selected { get; private set; }
    public BenchWire? SelectedWire { get; private set; }

    /// <summary>Tipo elegido en la lista de piezas, pendiente de poner con un clic.</summary>
    public PartType? Pending { get; set; }

    public Func<string, PartLook?>? LookOf { get; set; }
    public Func<string, IssueLevel?>? IssueOf { get; set; }
    /// <summary>Si el cable lleva algo ahora mismo (señal a 1, corriente, caudal).</summary>
    public Func<BenchWire, bool>? WireActive { get; set; }

    public event EventHandler? SelectionChanged;
    /// <summary>El montaje ha cambiado (pieza puesta, movida, borrada o cable nuevo).</summary>
    public event EventHandler? ProjectChanged;
    public event EventHandler<string>? Message;
    /// <summary>Se pulsa (true) o suelta (false) una pieza en marcha: pulsadores, selectores.</summary>
    public event EventHandler<(string PartId, bool Down)>? PartPressed;
    public event EventHandler? PlacementDone;

    public void Select(BenchPart? part)
    {
        Selected = part;
        SelectedWire = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void Animate(double wallSeconds)
    {
        _phase = (_phase + wallSeconds * 24) % 1000;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- geometría

    public sealed record PortSlot(PortDef Def, Point Pos);

    public sealed record PartLayout(Rect Box, IReadOnlyList<PortSlot> Ports, double DisplayTop);

    public static PartLayout Layout(BenchPart part)
    {
        var type = BenchCatalog.Get(part.Type);
        var ports = type?.PortsFor(part) ?? [];
        var left = ports.Where(p => p.EffectiveSide == PortSide.Left).ToList();
        var right = ports.Where(p => p.EffectiveSide == PortSide.Right).ToList();
        var top = ports.Where(p => p.EffectiveSide == PortSide.Top).ToList();
        var bottom = ports.Where(p => p.EffectiveSide == PortSide.Bottom).ToList();
        int rows = Math.Max(left.Count, right.Count);
        double body = Header + 6 + rows * Row;
        double display = 3 * LineH + 10;
        double h = body + display + (bottom.Count > 0 ? 4 : 0);
        double w = Math.Max(PartWidth, Math.Max(top.Count, bottom.Count) * 52);
        var box = new Rect(part.X, part.Y, w, h);
        var slots = new List<PortSlot>();
        for (int i = 0; i < left.Count; i++) slots.Add(new(left[i], new Point(box.X, box.Y + Header + 6 + Row * i + Row / 2)));
        for (int i = 0; i < right.Count; i++) slots.Add(new(right[i], new Point(box.Right, box.Y + Header + 6 + Row * i + Row / 2)));
        for (int i = 0; i < top.Count; i++) slots.Add(new(top[i], new Point(box.X + w * (i + 1) / (top.Count + 1), box.Y)));
        for (int i = 0; i < bottom.Count; i++) slots.Add(new(bottom[i], new Point(box.X + w * (i + 1) / (bottom.Count + 1), box.Bottom)));
        return new(box, slots, box.Y + body);
    }

    private Point ToWorld(Point screen) => new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    private (string Part, string Port, PortSlot Slot)? PortAt(Point world)
    {
        for (int i = _project.Parts.Count - 1; i >= 0; i--)
        {
            var p = _project.Parts[i];
            foreach (var s in Layout(p).Ports)
                if (Dist(s.Pos, world) <= PortHit / Math.Min(1, _zoom)) return (p.Id, s.Def.Name, s);
        }
        return null;
    }

    private BenchPart? PartAt(Point world)
    {
        for (int i = _project.Parts.Count - 1; i >= 0; i--)
            if (Layout(_project.Parts[i]).Box.Contains(world)) return _project.Parts[i];
        return null;
    }

    private BenchWire? WireAt(Point world)
    {
        foreach (var w in _project.Wires)
        {
            var pts = WirePoints(w);
            if (pts is null) continue;
            for (int i = 1; i < pts.Count; i++)
                if (DistToSegment(world, pts[i - 1], pts[i]) < 5 / _zoom) return w;
        }
        return null;
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
        return Dist(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private PortSlot? Slot(string partId, string port)
    {
        var p = _project.Part(partId);
        return p is null ? null : Layout(p).Ports.FirstOrDefault(s => s.Def.Name == port);
    }

    private static Vector Out(PortSide side) => side switch
    {
        PortSide.Left => new Vector(-1, 0),
        PortSide.Right => new Vector(1, 0),
        PortSide.Top => new Vector(0, -1),
        _ => new Vector(0, 1),
    };

    private static List<Point> Bezier(Point a, Vector da, Point b, Vector db)
    {
        double k = Math.Clamp(Dist(a, b) * 0.45, 30, 140);
        var c1 = a + da * k;
        var c2 = b + db * k;
        var pts = new List<Point>(25);
        for (int i = 0; i <= 24; i++)
        {
            double t = i / 24.0, u = 1 - t;
            pts.Add(new Point(
                u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
                u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y));
        }
        return pts;
    }

    private List<Point>? WirePoints(BenchWire w)
    {
        var a = Slot(w.FromPart, w.FromPort);
        var b = Slot(w.ToPart, w.ToPort);
        if (a is null || b is null) return null;
        return Bezier(a.Pos, Out(a.Def.EffectiveSide), b.Pos, Out(b.Def.EffectiveSide));
    }

    // ---------------------------------------------------------------- colores

    public static Color PortColor(PortDef d) => d.Kind switch
    {
        PortKind.SignalIn or PortKind.SignalOut => d.Analog ? Color.Parse("#E07A00") : Color.Parse("#2E7D32"),
        PortKind.Pipe or PortKind.Tap => Color.Parse("#1565C0"),
        PortKind.Probe => Color.Parse("#7B1FA2"),
        _ => d.Domain switch
        {
            PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC => Color.Parse("#C62828"),
            PowerDomain.MechanicalRotational or PowerDomain.MechanicalLinear => Color.Parse("#546E7A"),
            PowerDomain.Pneumatic => Color.Parse("#00838F"),
            _ => Color.Parse("#5D4037"),
        },
    };

    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#F4F6F8"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#E1E5EA")), 1);
    private static readonly IBrush Card = Brushes.White;
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1F2933"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#66727F"));
    private static readonly IBrush DisplayBg = new SolidColorBrush(Color.Parse("#EEF2F5"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#1E6FD9"));
    private static readonly Typeface Face = new("Inter, Segoe UI, sans-serif");
    private static readonly Typeface Bold = new("Inter, Segoe UI, sans-serif", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Mono = new("Cascadia Mono, Consolas, monospace");

    private static IBrush StateBrush(PartState s) => s switch
    {
        PartState.On => new SolidColorBrush(Color.Parse("#2E9E4F")),
        PartState.Warning => new SolidColorBrush(Color.Parse("#E0A100")),
        PartState.Fault => new SolidColorBrush(Color.Parse("#D32F2F")),
        _ => new SolidColorBrush(Color.Parse("#B0B8C1")),
    };

    private static Color LampColor(string? name) => name switch
    {
        "rojo" => Color.Parse("#E53935"),
        "ámbar" => Color.Parse("#FFB300"),
        "azul" => Color.Parse("#1E88E5"),
        "blanco" => Color.Parse("#FAFAFA"),
        _ => Color.Parse("#43A047"),
    };

    // ---------------------------------------------------------------- dibujo

    private static void Text(DrawingContext ctx, string s, Point at, double size, IBrush brush, Typeface? face = null, double maxWidth = 0, bool right = false, bool center = false)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face ?? Face, size, brush);
        if (maxWidth > 0) { ft.MaxTextWidth = maxWidth; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis; }
        double x = right ? at.X - ft.Width : center ? at.X - ft.Width / 2 : at.X;
        ctx.DrawText(ft, new Point(x, at.Y));
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Paper, new Rect(Bounds.Size));
        using (ctx.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y)))
        {
            DrawGrid(ctx);
            foreach (var w in _project.Wires) DrawWire(ctx, w);
            foreach (var p in _project.Parts) DrawPart(ctx, p);
            if (_drag == Drag.Wire && _wireFrom is { } from && Slot(from.Part, from.Port) is { } s)
            {
                var pen = new Pen(new SolidColorBrush(PortColor(s.Def)), 2, new DashStyle([4, 3], 0));
                var end = _hoverPort is { } hp && Slot(hp.Part, hp.Port) is { } hs ? hs.Pos : _wireEnd;
                var pts = Bezier(s.Pos, Out(s.Def.EffectiveSide), end, -Out(s.Def.EffectiveSide));
                for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
            }
        }
        if (_project.Parts.Count == 0)
        {
            Text(ctx, "Mesa vacía", new Point(Bounds.Width / 2, Bounds.Height / 2 - 40), 22, Muted, Bold, center: true);
            Text(ctx, "Elige una pieza en la lista de la izquierda y haz clic aquí para ponerla,", new Point(Bounds.Width / 2, Bounds.Height / 2 - 6), 14, Muted, center: true);
            Text(ctx, "o abre un ejemplo con el botón «Ejemplos».", new Point(Bounds.Width / 2, Bounds.Height / 2 + 14), 14, Muted, center: true);
        }
        if (Pending is not null)
            Text(ctx, $"Haz clic en la mesa para poner: {Pending.Name}   (Esc para cancelar)", new Point(12, Bounds.Height - 26), 13, Accent, Bold);
    }

    private void DrawGrid(DrawingContext ctx)
    {
        var tl = ToWorld(new Point(0, 0));
        var br = ToWorld(new Point(Bounds.Width, Bounds.Height));
        double step = 40;
        for (double x = Math.Floor(tl.X / step) * step; x < br.X; x += step) ctx.DrawLine(GridPen, new Point(x, tl.Y), new Point(x, br.Y));
        for (double y = Math.Floor(tl.Y / step) * step; y < br.Y; y += step) ctx.DrawLine(GridPen, new Point(tl.X, y), new Point(br.X, y));
    }

    private void DrawWire(DrawingContext ctx, BenchWire w)
    {
        var a = Slot(w.FromPart, w.FromPort);
        var b = Slot(w.ToPart, w.ToPort);
        if (a is null || b is null) return;
        var def = a.Def.Kind == PortKind.Probe ? a.Def : b.Def.Kind == PortKind.Probe ? b.Def : a.Def.Kind == PortKind.Tap ? b.Def : a.Def;
        var color = PortColor(def);
        bool probe = def.Kind == PortKind.Probe;
        bool active = Running && WireActive?.Invoke(w) == true;
        double width = probe ? 1.4 : def.Kind == PortKind.Pipe ? 4.5 : def.Kind == PortKind.Power ? 3 : 1.8;
        if (w == SelectedWire) width += 2.5;
        var baseColor = Running && !active && !probe ? Color.FromArgb(150, color.R, color.G, color.B) : color;
        var pen = new Pen(new SolidColorBrush(baseColor), width, probe ? new DashStyle([3, 3], 0) : null, PenLineCap.Round);
        var pts = Bezier(a.Pos, Out(a.Def.EffectiveSide), b.Pos, Out(b.Def.EffectiveSide));
        if (w == SelectedWire)
        {
            var halo = new Pen(new SolidColorBrush(Color.FromArgb(70, 30, 111, 217)), width + 6, lineCap: PenLineCap.Round);
            for (int i = 1; i < pts.Count; i++) ctx.DrawLine(halo, pts[i - 1], pts[i]);
        }
        for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
        if (active && !probe)
        {
            // Lo que circula: puntos claros que avanzan desde la salida hacia la entrada.
            var flow = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), Math.Max(1.2, width * 0.45),
                new DashStyle([1.5, 5], -_phase / Math.Max(1.2, width * 0.45)), PenLineCap.Round);
            bool reverse = a.Def.Kind == PortKind.SignalIn || (a.Def.Kind == PortKind.Power && a.Def.Causality == Causality.ImposesFlow && def.Domain is PowerDomain.Electrical3Ph);
            var seq = reverse ? Enumerable.Reverse(pts).ToList() : pts;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(seq[0], false);
                foreach (var p in seq.Skip(1)) g.LineTo(p);
            }
            ctx.DrawGeometry(null, flow, geo);
        }
    }

    private void DrawPart(DrawingContext ctx, BenchPart part)
    {
        var type = BenchCatalog.Get(part.Type);
        var lay = Layout(part);
        var box = lay.Box;
        var look = Running ? LookOf?.Invoke(part.Id) : null;
        var issue = IssueOf?.Invoke(part.Id);
        bool sel = part == Selected;

        // sombra y tarjeta
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), null, box.Translate(new Vector(2, 3)), 8, 8);
        var border = issue switch
        {
            IssueLevel.Error => new Pen(new SolidColorBrush(Color.Parse("#D32F2F")), 2.2),
            IssueLevel.Warning => new Pen(new SolidColorBrush(Color.Parse("#E0A100")), 2.2),
            _ => sel ? new Pen(Accent, 2.4) : new Pen(new SolidColorBrush(Color.Parse("#C5CDD6")), 1.2),
        };
        ctx.DrawRectangle(Card, border, box, 8, 8);
        if (sel && issue is not null) ctx.DrawRectangle(null, new Pen(Accent, 1.5, new DashStyle([3, 2], 0)), box.Inflate(4), 10, 10);

        // cabecera
        var head = new Rect(box.X, box.Y, box.Width, Header);
        var headColor = type is null ? Color.Parse("#9E9E9E") : CategoryColor(type.Category);
        using (ctx.PushClip(new RoundedRect(box, 8)))
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(38, headColor.R, headColor.G, headColor.B)), head);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, headColor.R, headColor.G, headColor.B)), 1), head.BottomLeft, head.BottomRight);
        Text(ctx, type?.Icon ?? "?", new Point(box.X + 8, box.Y + 7), 15, new SolidColorBrush(headColor), Bold);
        Text(ctx, part.Id, new Point(box.X + 30, box.Y + 3), 13, Ink, Bold, box.Width - 66);
        Text(ctx, type?.Name ?? part.Type, new Point(box.X + 30, box.Y + 18), 10.5, Muted, null, box.Width - 66);

        // indicador de estado y giro
        if (look is not null)
        {
            var c = new Point(box.Right - 16, box.Y + Header / 2);
            if (look.Color is not null)
            {
                var lc = LampColor(look.Color);
                bool lit = look.State == PartState.On;
                if (lit) ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, lc.R, lc.G, lc.B)), null, c, 13, 13);
                ctx.DrawEllipse(new SolidColorBrush(lit ? lc : Color.FromArgb(70, lc.R, lc.G, lc.B)), new Pen(Muted, 1), c, 8, 8);
            }
            else if (look.Spin is { } spin)
            {
                double ang = _phase * 0.25 * Math.Clamp(spin, 0, 1.3);
                ctx.DrawEllipse(null, new Pen(StateBrush(look.State), 2), c, 9, 9);
                for (int k = 0; k < 3; k++)
                {
                    double a = ang + k * 2 * Math.PI / 3;
                    ctx.DrawLine(new Pen(StateBrush(look.State), 2), c, c + new Vector(Math.Cos(a) * 8, Math.Sin(a) * 8));
                }
            }
            else ctx.DrawEllipse(StateBrush(look.State), null, c, 6, 6);
        }

        // bornes
        foreach (var s in lay.Ports)
        {
            var col = PortColor(s.Def);
            bool connected = _project.WiresAt(part.Id, s.Def.Name).Any();
            bool hover = _hoverPort is { } h && h.Part == part.Id && h.Port == s.Def.Name;
            var brush = new SolidColorBrush(col);
            if (s.Def.Kind == PortKind.Pipe || s.Def.Kind == PortKind.Power)
                ctx.DrawRectangle(connected ? brush : Brushes.White, new Pen(brush, 1.6), new Rect(s.Pos.X - PortR, s.Pos.Y - PortR, PortR * 2, PortR * 2), 2, 2);
            else
                ctx.DrawEllipse(connected ? brush : Brushes.White, new Pen(brush, 1.6), s.Pos, PortR, PortR);
            if (hover) ctx.DrawEllipse(null, new Pen(brush, 2), s.Pos, PortR + 4, PortR + 4);
            switch (s.Def.EffectiveSide)
            {
                case PortSide.Left: Text(ctx, s.Def.Text, new Point(s.Pos.X + 9, s.Pos.Y - 7), 10.5, Ink, Mono, box.Width / 2 - 12); break;
                case PortSide.Right: Text(ctx, s.Def.Text, new Point(s.Pos.X - 9, s.Pos.Y - 7), 10.5, Ink, Mono, 0, right: true); break;
                case PortSide.Top: Text(ctx, s.Def.Text, new Point(s.Pos.X, s.Pos.Y - 19), 10, Muted, Mono, center: true); break;
                default: Text(ctx, s.Def.Text, new Point(s.Pos.X, s.Pos.Y + 7), 10, Muted, Mono, center: true); break;
            }
        }

        // pantalla de valores
        var disp = new Rect(box.X + 8, lay.DisplayTop + 2, box.Width - 16, 3 * LineH + 4);
        ctx.DrawRectangle(DisplayBg, null, disp, 4, 4);
        if (look is not null)
        {
            double wText = disp.Width - (look.Fill is not null ? 22 : 8);
            int n = 0;
            foreach (var line in look.Lines.Where(l => !string.IsNullOrEmpty(l)).Take(3))
                Text(ctx, line, new Point(disp.X + 5, disp.Y + 2 + LineH * n++), 11, look.State == PartState.Fault ? StateBrush(PartState.Fault) : Ink, Mono, wText);
            if (look.Fill is { } fill)
            {
                var bar = new Rect(disp.Right - 14, disp.Y + 3, 9, disp.Height - 6);
                ctx.DrawRectangle(Brushes.White, new Pen(Muted, 0.8), bar, 2, 2);
                double hgt = bar.Height * Math.Clamp(fill, 0, 1);
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#4A90D9")), null, new Rect(bar.X, bar.Bottom - hgt, bar.Width, hgt), 2, 2);
            }
        }
        else
        {
            var lines = Summary(part, type).Take(3).ToList();
            for (int n = 0; n < lines.Count; n++)
                Text(ctx, lines[n], new Point(disp.X + 5, disp.Y + 2 + LineH * n), 10.5, Muted, null, disp.Width - 10);
        }
    }

    /// <summary>En edición, la pantalla de la pieza enseña sus datos principales (o qué es, si no tiene).</summary>
    private static IEnumerable<string> Summary(BenchPart part, PartType? type)
    {
        if (type is null) yield break;
        int shown = 0;
        foreach (var d in type.Params)
        {
            string v = part.Text(d.Key, d.Default);
            if (d.Kind == ParamKind.Program)
            {
                int n = string.IsNullOrWhiteSpace(v) ? 0 : v.Split('\n').Length;
                yield return n == 0 ? "programa de ejemplo" : $"programa: {n} líneas";
            }
            else yield return $"{d.Label}: {v.Replace('.', ',')} {d.Unit}".TrimEnd();
            if (++shown == 3) yield break;
        }
        if (shown > 0) yield break;
        string hint = type.Description;
        yield return hint;
        if (hint.Length > 34) yield return Wrap(hint, 34);
        if (hint.Length > 68) yield return Wrap(Wrap(hint, 34), 34);
    }

    /// <summary>Resto del texto tras la primera línea de ~n caracteres (corta por un espacio).</summary>
    private static string Wrap(string s, int n)
    {
        if (s.Length <= n) return "";
        int cut = s.LastIndexOf(' ', Math.Min(n, s.Length - 1));
        return s[(cut > 0 ? cut + 1 : n)..];
    }

    public static Color CategoryColor(string category) => category switch
    {
        BenchCatalog.CatControl => Color.Parse("#2E7D32"),
        BenchCatalog.CatProgrammable => Color.Parse("#1E6FD9"),
        BenchCatalog.CatElectric => Color.Parse("#C62828"),
        BenchCatalog.CatMechanical => Color.Parse("#546E7A"),
        BenchCatalog.CatHydraulic => Color.Parse("#1565C0"),
        BenchCatalog.CatInstruments => Color.Parse("#7B1FA2"),
        _ => Color.Parse("#616161"),
    };

    // ---------------------------------------------------------------- ratón y teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        var world = ToWorld(pt.Position);
        _dragStart = pt.Position;
        _dragWorld = world;

        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            _drag = Drag.Pan; _panStart = _pan; e.Pointer.Capture(this); return;
        }

        if (Pending is not null)
        {
            if (Running) { Message?.Invoke(this, "Detén la simulación (⏹) para añadir piezas."); return; }
            var t = Pending;
            var part = t.NewPart(_project.NextId(t.Prefix), Snap(world.X - PartWidth / 2), Snap(world.Y - Header / 2));
            _project.Parts.Add(part);
            if ((e.KeyModifiers & KeyModifiers.Shift) == 0) { Pending = null; PlacementDone?.Invoke(this, EventArgs.Empty); }
            Select(part);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (PortAt(world) is { } port)
        {
            if (Running) { Message?.Invoke(this, "Detén la simulación (⏹) para cambiar el cableado."); return; }
            _drag = Drag.Wire;
            _wireFrom = (port.Part, port.Port);
            _wireEnd = world;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (PartAt(world) is { } hit)
        {
            if (Selected != hit) Select(hit);
            // Mover la pieza al frente.
            _project.Parts.Remove(hit);
            _project.Parts.Add(hit);
            if (Running && LookOf is not null)
            {
                _drag = Drag.Press; _pressedPart = hit.Id;
                PartPressed?.Invoke(this, (hit.Id, true));
            }
            else _drag = Drag.Move;
            _partStart = new Point(hit.X, hit.Y);
            e.Pointer.Capture(this);
            return;
        }

        if (WireAt(world) is { } wire)
        {
            Selected = null;
            SelectedWire = wire;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (Selected is not null || SelectedWire is not null) { Selected = null; SelectedWire = null; SelectionChanged?.Invoke(this, EventArgs.Empty); }
        _drag = Drag.Pan;
        _panStart = _pan;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var world = ToWorld(pos);
        switch (_drag)
        {
            case Drag.Pan:
                _pan = _panStart + (pos - _dragStart);
                break;
            case Drag.Move when Selected is not null:
                Selected.X = Snap(_partStart.X + world.X - _dragWorld.X);
                Selected.Y = Snap(_partStart.Y + world.Y - _dragWorld.Y);
                break;
            case Drag.Wire:
                _wireEnd = world;
                var target = PortAt(world);
                _hoverPort = target is { } t && (t.Part, t.Port) != _wireFrom ? (t.Part, t.Port) : null;
                break;
            default:
                var hp = PortAt(world);
                var newHover = hp is { } h ? (h.Part, h.Port) : ((string, string)?)null;
                if (newHover != _hoverPort)
                {
                    _hoverPort = newHover;
                    ToolTip.SetTip(this, hp is { } x ? PortTip(x.Part, x.Slot.Def) : null);
                    Cursor = hp is not null && !Running ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
                }
                else return;
                break;
        }
        InvalidateVisual();
    }

    private string PortTip(string part, PortDef d)
    {
        string what = BenchRules.KindName(d);
        var links = _project.WiresAt(part, d.Name).Select(w => w.Other(part, d.Name)).Select(o => $"{o.Part}.{o.Port}").ToList();
        return $"{part}.{d.Text}: {what}" + (links.Count > 0 ? $"\nunido a {string.Join(", ", links)}" : "\nlibre: arrastra desde aquí hasta otro borne");
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var world = ToWorld(e.GetPosition(this));
        var drag = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        if (drag == Drag.Press && _pressedPart is not null)
        {
            PartPressed?.Invoke(this, (_pressedPart, false));
            _pressedPart = null;
        }
        if (drag == Drag.Move && Selected is not null && (Selected.X != _partStart.X || Selected.Y != _partStart.Y))
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        if (drag == Drag.Wire && _wireFrom is { } from)
        {
            _hoverPort = null;
            if (PortAt(world) is { } to && (to.Part, to.Port) != from)
            {
                var why = BenchRules.CanConnect(_project, from.Part, from.Port, to.Part, to.Port);
                if (why is not null) Message?.Invoke(this, "No se puede unir: " + why);
                else
                {
                    var w = new BenchWire(from.Part, from.Port, to.Part, to.Port);
                    _project.Wires.Add(w);
                    SelectedWire = w;
                    Selected = null;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    ProjectChanged?.Invoke(this, EventArgs.Empty);
                    Message?.Invoke(this, $"Unido {from.Part}.{from.Port} con {to.Part}.{to.Port}.");
                }
            }
            _wireFrom = null;
        }
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        var before = ToWorld(pos);
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.3, 2.5);
        _pan = new Point(pos.X - before.X * _zoom, pos.Y - before.Y * _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Pending = null; PlacementDone?.Invoke(this, EventArgs.Empty); _drag = Drag.None; _wireFrom = null; InvalidateVisual(); e.Handled = true; }
        if (e.Key is Key.Delete or Key.Back) { DeleteSelection(); e.Handled = true; }
    }

    public void DeleteSelection()
    {
        if (Running) { Message?.Invoke(this, "Detén la simulación (⏹) para borrar."); return; }
        if (SelectedWire is not null) { _project.Wires.Remove(SelectedWire); SelectedWire = null; }
        else if (Selected is not null) { _project.Remove(Selected); Selected = null; }
        else return;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Encaja todo el montaje en la vista.</summary>
    public void ZoomToFit()
    {
        if (_project.Parts.Count == 0 || Bounds.Width < 10) { _zoom = 1; _pan = new Point(20, 20); InvalidateVisual(); return; }
        var boxes = _project.Parts.Select(p => Layout(p).Box).ToList();
        double x0 = boxes.Min(b => b.X) - 40, y0 = boxes.Min(b => b.Y) - 40, x1 = boxes.Max(b => b.Right) + 40, y1 = boxes.Max(b => b.Bottom) + 40;
        _zoom = Math.Clamp(Math.Min(Bounds.Width / (x1 - x0), Bounds.Height / (y1 - y0)), 0.3, 1.3);
        _pan = new Point((Bounds.Width - (x1 - x0) * _zoom) / 2 - x0 * _zoom, (Bounds.Height - (y1 - y0) * _zoom) / 2 - y0 * _zoom);
        InvalidateVisual();
    }

    private static double Snap(double v) => Math.Round(v / Grid) * Grid;
}
