using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Circuitos.Catalog;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Schematic;

public enum SchematicTool { Select, Place, Wire, Label }

/// <summary>
/// Lienzo del editor de esquemas. Rueda = zoom, botón central o derecho arrastrando = desplazar.
/// Teclas: R girar, M espejo, Supr borrar, W cable, L etiqueta, Esc cancelar, Ctrl+Z / Ctrl+Y deshacer / rehacer.
/// </summary>
public sealed partial class SchematicCanvas : Control
{
    public const double Grid = 1.27; // mm (50 mil), la rejilla de KiCad

    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#FFFDF7"));
    private static readonly IBrush GridDot = new SolidColorBrush(Color.Parse("#C9C4B8"));
    private static readonly Color BodyColor = Color.Parse("#8A1C1C");
    private static readonly Color WireColor = Color.Parse("#0B6E2E");
    private static readonly Color SelectColor = Color.Parse("#1E6FD9");
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#2B2B2B"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#1F4E9E"));
    private static readonly Typeface Font = new("Inter");

    private readonly ComponentCatalog _catalog;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    private SchematicDocument _doc = new();
    private ConnectivityResult _conn = new();
    private Vector _offset = new(-40, -30); // mm en la esquina superior izquierda
    private double _zoom = 6;               // píxeles por mm

    private SchematicTool _tool = SchematicTool.Select;
    private PlacedSymbol? _ghost;
    private Vec2? _wireStart;
    private Vec2 _mouseWorld;
    private bool _verticalFirst;

    private Point? _panStart;
    private Vector _panOrigin;
    private bool _dragging;
    private Vec2 _dragStart;
    private Vec2 _dragOrigin;
    private readonly List<(Wire Wire, bool IsA)> _rubber = [];

    public SchematicCanvas(ComponentCatalog catalog)
    {
        _catalog = catalog;
        Focusable = true;
        ClipToBounds = true;
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler<string>? Status;
    public event EventHandler? ToolChanged;

    public SchematicDocument Document => _doc;
    public object? Selected { get; private set; }
    public SchematicTool Tool => _tool;
    public ConnectivityResult Connectivity => _conn;
    /// <summary>Nombre que tendrá la próxima etiqueta de red.</summary>
    public string LabelName { get; set; } = "SALIDA";
    public bool IsDirty { get; set; }

    private Dictionary<string, double>? _opVoltages;

    /// <summary>Muestra sobre el esquema la tensión de cada red (resultado del punto de trabajo). null = ocultar.</summary>
    public void SetOperatingPoint(Dictionary<string, double>? voltages)
    {
        _opVoltages = voltages;
        InvalidateVisual();
    }

    // ---------- Documento ----------

    public void Load(SchematicDocument doc)
    {
        _doc = doc;
        var missing = _doc.ResolveSymbols(_catalog.FindSymbol);
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        Select(null);
        Changed(markDirty: false);
        ZoomToFit();
        if (missing.Count > 0)
            Status?.Invoke(this, "Faltan símbolos (importa sus librerías): " + string.Join(", ", missing));
    }

    /// <summary>Guarda el estado actual para poder deshacer el siguiente cambio.</summary>
    public void Checkpoint()
    {
        _undo.Push(_doc.ToJson());
        if (_undo.Count > 200) TrimUndo();
        _redo.Clear();
    }

    private void TrimUndo()
    {
        var keep = _undo.Take(100).Reverse().ToList();
        _undo.Clear();
        foreach (var s in keep) _undo.Push(s);
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return;
        to.Push(_doc.ToJson());
        _doc = SchematicDocument.FromJson(from.Pop());
        _doc.ResolveSymbols(_catalog.FindSymbol);
        Select(null);
        Changed();
    }

    /// <summary>Llamar tras modificar el documento desde fuera (panel de propiedades).</summary>
    public void Changed(bool markDirty = true)
    {
        _conn = Schematic.Connectivity.Analyze(_doc);
        if (_anim is not null) SetAnimation(null);
        if (markDirty) IsDirty = true;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ---------- Herramientas ----------

    public void SetTool(SchematicTool tool, SymbolDefinition? symbol = null)
    {
        _tool = tool;
        _wireStart = null;
        _ghost = null;
        if (tool == SchematicTool.Place && symbol is not null)
        {
            _ghost = new PlacedSymbol
            {
                SymbolName = symbol.FullName,
                Symbol = symbol,
                Value = symbol.Value,
                Position = _mouseWorld,
            };
            Status?.Invoke(this, $"Colocando {symbol.Name}: clic para colocar, R para girar, M para espejo, Esc para terminar.");
        }
        else if (tool == SchematicTool.Wire)
            Status?.Invoke(this, "Cable: clic para empezar y en cada esquina; doble clic, clic derecho o Esc para terminar. Espacio cambia el sentido de la esquina.");
        else if (tool == SchematicTool.Label)
            Status?.Invoke(this, $"Etiqueta «{LabelName}»: clic sobre un cable o pin. Puntos con la misma etiqueta quedan unidos.");
        ToolChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void Select(object? item)
    {
        if (ReferenceEquals(Selected, item)) return;
        Selected = item;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (Selected is null) return;
        Checkpoint();
        switch (Selected)
        {
            case PlacedSymbol s: _doc.Symbols.Remove(s); break;
            case Wire w: _doc.Wires.Remove(w); break;
            case NetLabel l: _doc.Labels.Remove(l); break;
        }
        Select(null);
        Changed();
    }

    public void RotateSelected()
    {
        if (_ghost is not null) { _ghost.Rotation = (_ghost.Rotation + 90) % 360; InvalidateVisual(); return; }
        if (Selected is not PlacedSymbol s) return;
        Checkpoint();
        s.Rotation = (s.Rotation + 90) % 360;
        Changed();
    }

    public void MirrorSelected()
    {
        if (_ghost is not null) { _ghost.Mirror = !_ghost.Mirror; InvalidateVisual(); return; }
        if (Selected is not PlacedSymbol s) return;
        Checkpoint();
        s.Mirror = !s.Mirror;
        Changed();
    }

    // ---------- Vista ----------

    private Point ToScreen(Vec2 w) => new((w.X - _offset.X) * _zoom, (w.Y - _offset.Y) * _zoom);
    private Vec2 ToWorld(Point p) => new(p.X / _zoom + _offset.X, p.Y / _zoom + _offset.Y);
    private static Vec2 Snap(Vec2 v) => new(Math.Round(v.X / Grid) * Grid, Math.Round(v.Y / Grid) * Grid);

    public void ZoomToFit()
    {
        if (Bounds.Width < 10 || Bounds.Height < 10) return;
        var pts = _doc.Symbols.SelectMany(s => { var b = SymbolTransform.Bounds(s); return new[] { b.Min, b.Max }; })
            .Concat(_doc.Wires.SelectMany(w => new[] { w.A, w.B }))
            .Concat(_doc.Labels.Select(l => l.Position)).ToList();
        if (pts.Count == 0)
        {
            _zoom = 6;
            _offset = new Vector(-Bounds.Width / 2 / _zoom, -Bounds.Height / 2 / _zoom);
        }
        else
        {
            double minX = pts.Min(p => p.X) - 10, maxX = pts.Max(p => p.X) + 10;
            double minY = pts.Min(p => p.Y) - 10, maxY = pts.Max(p => p.Y) + 10;
            _zoom = Math.Clamp(Math.Min(Bounds.Width / (maxX - minX), Bounds.Height / (maxY - minY)), 1, 60);
            _offset = new Vector(
                (minX + maxX) / 2 - Bounds.Width / 2 / _zoom,
                (minY + maxY) / 2 - Bounds.Height / 2 / _zoom);
        }
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.PreviousSize.Width < 10) ZoomToFit();
    }

    // ---------- Ratón y teclado ----------

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var before = ToWorld(p);
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 1, 80);
        var after = ToWorld(p);
        _offset += new Vector(before.X - after.X, before.Y - after.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(this);
        var world = ToWorld(point.Position);
        var snapped = Snap(world);

        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsRightButtonPressed && !(_tool == SchematicTool.Wire && _wireStart is not null)))
        {
            _panStart = point.Position;
            _panOrigin = _offset;
            e.Pointer.Capture(this);
            return;
        }

        switch (_tool)
        {
            case SchematicTool.Place when _ghost is not null && point.Properties.IsLeftButtonPressed:
                Checkpoint();
                var placed = _ghost.Clone();
                placed.Id = Guid.NewGuid();
                placed.Position = snapped;
                placed.Reference = placed.Symbol!.IsPower ? _doc.NextReference("#PWR") : _doc.NextReference(placed.Symbol.Reference);
                _doc.Symbols.Add(placed);
                Changed();
                break;

            case SchematicTool.Wire:
                if (point.Properties.IsRightButtonPressed) { FinishWire(); break; }
                if (_wireStart is null) { _wireStart = snapped; break; }
                AddWireTo(snapped);
                if (e.ClickCount >= 2 || EndsOnConnection(snapped)) FinishWire();
                else _wireStart = snapped;
                break;

            case SchematicTool.Label when point.Properties.IsLeftButtonPressed:
                Checkpoint();
                _doc.Labels.Add(new NetLabel { Name = LabelName, Position = snapped });
                Changed();
                break;

            case SchematicTool.Select when point.Properties.IsLeftButtonPressed:
                var hit = HitTest(world);
                Select(hit);
                if (hit is PlacedSymbol s)
                {
                    _dragging = true;
                    _dragStart = snapped;
                    _dragOrigin = s.Position;
                    CollectRubber(s);
                    Checkpoint();
                    e.Pointer.Capture(this);
                }
                else if (hit is NetLabel l)
                {
                    _dragging = true;
                    _dragStart = snapped;
                    _dragOrigin = l.Position;
                    _rubber.Clear();
                    Checkpoint();
                    e.Pointer.Capture(this);
                }
                if (e.ClickCount >= 2 && hit is not null)
                    Status?.Invoke(this, "Edita los valores en el panel de propiedades.");
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        _mouseWorld = Snap(ToWorld(p));

        if (_panStart is { } start)
        {
            var d = (p - start) / _zoom;
            _offset = _panOrigin - new Vector(d.X, d.Y);
        }
        else if (_dragging)
        {
            var delta = _mouseWorld - _dragStart;
            if (Selected is PlacedSymbol s)
            {
                var newPos = _dragOrigin + delta;
                var move = newPos - s.Position;
                s.Position = newPos;
                foreach (var (w, isA) in _rubber)
                {
                    if (isA) w.A += move; else w.B += move;
                }
            }
            else if (Selected is NetLabel l)
                l.Position = _dragOrigin + delta;
        }
        if (_ghost is not null) _ghost.Position = _mouseWorld;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            bool moved = Selected switch
            {
                PlacedSymbol s => s.Position != _dragOrigin,
                NetLabel l => l.Position != _dragOrigin,
                _ => false,
            };
            if (moved) Changed();
            else _undo.TryPop(out _); // no hubo cambio: descartar el punto de deshacer
        }
        _panStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.R: RotateSelected(); break;
            case Key.M: MirrorSelected(); break;
            case Key.Delete or Key.Back: DeleteSelected(); break;
            case Key.W: SetTool(SchematicTool.Wire); break;
            case Key.L: SetTool(SchematicTool.Label); break;
            case Key.F: ZoomToFit(); break;
            case Key.Space when _tool == SchematicTool.Wire: _verticalFirst = !_verticalFirst; InvalidateVisual(); break;
            case Key.Escape:
                if (_tool == SchematicTool.Wire && _wireStart is not null) _wireStart = null;
                else SetTool(SchematicTool.Select);
                InvalidateVisual();
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void AddWireTo(Vec2 end)
    {
        if (_wireStart is not { } start || start == end) return;
        Checkpoint();
        var corner = _verticalFirst ? new Vec2(start.X, end.Y) : new Vec2(end.X, start.Y);
        if (start != corner) _doc.Wires.Add(new Wire(start, corner));
        if (corner != end) _doc.Wires.Add(new Wire(corner, end));
        Changed();
    }

    private void FinishWire()
    {
        _wireStart = null;
        InvalidateVisual();
    }

    private bool EndsOnConnection(Vec2 p)
    {
        const double tol = 0.01;
        bool Near(Vec2 a) => Math.Abs(a.X - p.X) < tol && Math.Abs(a.Y - p.Y) < tol;
        if (_doc.Symbols.Any(s => SymbolTransform.Pins(s).Any(x => Near(x.Position)))) return true;
        // Otro cable que ya llegaba aquí (sin contar los dos tramos recién añadidos).
        return _doc.Wires.Take(Math.Max(0, _doc.Wires.Count - 2)).Any(w => Near(w.A) || Near(w.B) || DistanceToSegment(p, w.A, w.B) < tol);
    }

    private void CollectRubber(PlacedSymbol s)
    {
        _rubber.Clear();
        var pins = SymbolTransform.Pins(s).Select(p => p.Position).ToList();
        foreach (var w in _doc.Wires)
        {
            if (pins.Any(p => p == w.A)) _rubber.Add((w, true));
            if (pins.Any(p => p == w.B)) _rubber.Add((w, false));
        }
    }

    private object? HitTest(Vec2 p)
    {
        double tol = Math.Max(0.6, 5 / _zoom);
        foreach (var l in _doc.Labels.AsEnumerable().Reverse())
        {
            double width = Math.Max(1, l.Name.Length) * 0.9 + 1;
            if (p.X >= l.Position.X - tol && p.X <= l.Position.X + width && p.Y >= l.Position.Y - 2 && p.Y <= l.Position.Y + tol)
                return l;
        }
        foreach (var w in _doc.Wires.AsEnumerable().Reverse())
            if (DistanceToSegment(p, w.A, w.B) < tol) return w;
        foreach (var s in _doc.Symbols.AsEnumerable().Reverse())
        {
            var (min, max) = SymbolTransform.Bounds(s);
            if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y) return s;
        }
        return null;
    }

    private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    // ---------- Dibujo ----------

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Paper, bounds);
        DrawGrid(ctx, bounds);

        var m = Matrix.CreateTranslation(-_offset.X, -_offset.Y) * Matrix.CreateScale(_zoom, _zoom);
        using (ctx.PushTransform(m))
        {
            double minPen = 1 / _zoom;
            var wirePen = new Pen(new SolidColorBrush(WireColor), Math.Max(0.25, minPen * 1.5), lineCap: PenLineCap.Round);
            var selPen = new Pen(new SolidColorBrush(SelectColor), Math.Max(0.35, minPen * 2), lineCap: PenLineCap.Round);

            foreach (var w in _doc.Wires)
                ctx.DrawLine(ReferenceEquals(w, Selected) ? selPen : wirePen, P(w.A), P(w.B));
            DrawAnimationUnder(ctx, minPen);
            var dotBrush = new SolidColorBrush(WireColor);
            foreach (var j in _conn.Junctions)
                ctx.DrawEllipse(dotBrush, null, P(j), 0.5, 0.5);

            foreach (var s in _doc.Symbols)
                DrawSymbol(ctx, s, ReferenceEquals(s, Selected) ? SelectColor : BodyColor, 1.0);

            foreach (var l in _doc.Labels)
                DrawLabel(ctx, l, ReferenceEquals(l, Selected));

            DrawAnimationOver(ctx);
            if (_opVoltages is not null) DrawVoltages(ctx);

            if (_ghost is not null)
                DrawSymbol(ctx, _ghost, BodyColor, 0.45);

            if (_tool == SchematicTool.Wire && _wireStart is { } ws)
            {
                var corner = _verticalFirst ? new Vec2(ws.X, _mouseWorld.Y) : new Vec2(_mouseWorld.X, ws.Y);
                var preview = new Pen(new SolidColorBrush(WireColor, 0.6), Math.Max(0.25, minPen * 1.5), dashStyle: DashStyle.Dash);
                ctx.DrawLine(preview, P(ws), P(corner));
                ctx.DrawLine(preview, P(corner), P(_mouseWorld));
            }
            if (_tool is SchematicTool.Wire or SchematicTool.Label)
            {
                var cross = new Pen(new SolidColorBrush(SelectColor, 0.7), minPen);
                ctx.DrawLine(cross, P(_mouseWorld - new Vec2(1, 0)), P(_mouseWorld + new Vec2(1, 0)));
                ctx.DrawLine(cross, P(_mouseWorld - new Vec2(0, 1)), P(_mouseWorld + new Vec2(0, 1)));
            }
        }
        DrawAnimationOverlay(ctx);
    }

    private static Point P(Vec2 v) => new(v.X, v.Y);

    private void DrawGrid(DrawingContext ctx, Rect bounds)
    {
        double step = Grid;
        while (step * _zoom < 8) step *= 2;
        var tl = ToWorld(bounds.TopLeft);
        var br = ToWorld(bounds.BottomRight);
        double x0 = Math.Floor(tl.X / step) * step, y0 = Math.Floor(tl.Y / step) * step;
        double size = Math.Max(1, _zoom * 0.12);
        for (double x = x0; x <= br.X; x += step)
            for (double y = y0; y <= br.Y; y += step)
            {
                var sp = ToScreen(new Vec2(x, y));
                ctx.FillRectangle(GridDot, new Rect(sp.X - size / 2, sp.Y - size / 2, size, size));
            }
    }

    private void DrawSymbol(DrawingContext ctx, PlacedSymbol s, Color color, double opacity)
    {
        var def = s.Symbol;
        var brush = new SolidColorBrush(color, opacity);
        var fill = new SolidColorBrush(color, opacity * 0.25);
        double minPen = 1 / _zoom;
        if (def is null)
        {
            ctx.DrawRectangle(null, new Pen(brush, minPen), new Rect(s.Position.X - 2, s.Position.Y - 2, 4, 4));
            DrawText(ctx, "?" + s.Reference, s.Position, 1.27, brush);
            return;
        }

        Vec2 W(Vec2 v) => SymbolTransform.ToWorld(s, v);
        foreach (var g in def.Graphics)
        {
            if (g.Unit is not (0 or 1)) continue;
            var pen = new Pen(brush, Math.Max(g.StrokeWidth > 0 ? g.StrokeWidth : 0.15, minPen), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var gFill = g.Filled ? fill : null;
            switch (g)
            {
                case RectangleGraphic r:
                    DrawPolygon(ctx, [W(r.Start), W(new Vec2(r.End.X, r.Start.Y)), W(r.End), W(new Vec2(r.Start.X, r.End.Y))], true, pen, gFill);
                    break;
                case CircleGraphic c:
                    ctx.DrawEllipse(gFill, pen, P(W(c.Center)), c.Radius, c.Radius);
                    break;
                case PolylineGraphic pl when pl.Points.Count >= 2:
                    DrawPolygon(ctx, pl.Points.Select(W).ToList(), false, pen, gFill);
                    break;
                case ArcGraphic a:
                    DrawPolygon(ctx, ArcPoints(W(a.Start), W(a.Mid), W(a.End)), false, pen, null);
                    break;
                case TextGraphic t:
                    DrawText(ctx, t.Text, W(t.Position), t.Size, brush, center: true);
                    break;
            }
        }

        var pinPen = new Pen(brush, Math.Max(0.15, minPen), lineCap: PenLineCap.Round);
        foreach (var pin in def.Pins)
        {
            if (pin.Unit is not (0 or 1) || pin.Hidden || pin.Length <= 0) continue;
            ctx.DrawLine(pinPen, P(W(pin.Position)), P(W(pin.BodyEnd)));
        }
        // Extremos de pines sin conectar: una pequeña marca para saber dónde conectar.
        if (opacity >= 1)
        {
            var open = new Pen(new SolidColorBrush(Color.Parse("#B03A2E")), minPen);
            foreach (var (pin, pos) in SymbolTransform.Pins(s))
            {
                var net = _conn.NetOf(s, pin.Number);
                if (net is null || net.Pins.Count < 2 && !_doc.Wires.Any(w => w.A == pos || w.B == pos))
                    ctx.DrawEllipse(null, open, P(pos), 0.35, 0.35);
            }
        }

        var (min, max) = SymbolTransform.Bounds(s);
        double cy = (min.Y + max.Y) / 2;
        if (def.IsPower)
        {
            bool up = SymbolTransform.Pins(s).Any() && SymbolTransform.Pins(s).First().Position.Y >= max.Y - 1;
            DrawText(ctx, s.Value, new Vec2((min.X + max.X) / 2, up ? min.Y - 1.2 : max.Y + 0.2), 1.27, brush, center: true);
        }
        else
        {
            DrawText(ctx, s.Reference, new Vec2(max.X + 0.4, cy - 1.6), 1.27, new SolidColorBrush(TextBrush is SolidColorBrush tb ? tb.Color : Colors.Black, opacity));
            DrawText(ctx, s.Value, new Vec2(max.X + 0.4, cy + 0.1), 1.27, new SolidColorBrush(TextBrush is SolidColorBrush tv ? tv.Color : Colors.Black, opacity));
        }

        if (ReferenceEquals(s, Selected))
        {
            var dash = new Pen(new SolidColorBrush(SelectColor, 0.8), minPen, dashStyle: DashStyle.Dash);
            ctx.DrawRectangle(null, dash, new Rect(P(min), P(max)));
        }
    }

    private static readonly IBrush ProbeBrush = new SolidColorBrush(Color.Parse("#E8F1FF"));
    private static readonly IBrush ProbeText = new SolidColorBrush(Color.Parse("#0B4FB3"));

    private void DrawVoltages(DrawingContext ctx)
    {
        foreach (var net in _conn.Nets)
        {
            if (!_opVoltages!.TryGetValue(net.Name, out var v) || net.Pins.Count == 0) continue;
            // Se coloca junto al primer pin de la red que no sea de un símbolo de alimentación.
            var (sym, pin) = net.Pins.FirstOrDefault(p => p.Symbol.Symbol?.IsPower != true);
            if (sym is null) continue;
            var pos = SymbolTransform.ToWorld(sym, pin.Position);
            var ft = Text(Simulation.EngineeringFormat.Format(v, "V", 3), 1.1, ProbeText);
            var rect = new Rect(pos.X + 0.6, pos.Y - ft.Height - 0.6, ft.Width + 0.8, ft.Height + 0.2);
            ctx.DrawRectangle(ProbeBrush, new Pen(ProbeText, 0.08), rect, 0.3, 0.3);
            ctx.DrawText(ft, new Point(rect.X + 0.4, rect.Y + 0.1));
        }
    }

    private void DrawLabel(DrawingContext ctx, NetLabel l, bool selected)
    {
        var brush = selected ? new SolidColorBrush(SelectColor) : LabelBrush;
        double minPen = 1 / _zoom;
        ctx.DrawRectangle(brush, null, new Rect(l.Position.X - 0.3, l.Position.Y - 0.3, 0.6, 0.6));
        var ft = Text(l.Name, 1.27, brush);
        ctx.DrawText(ft, new Point(l.Position.X + 0.3, l.Position.Y - ft.Height - 0.1));
        ctx.DrawLine(new Pen(brush, minPen), P(l.Position), new Point(l.Position.X + 0.3 + ft.Width, l.Position.Y));
    }

    private static void DrawPolygon(DrawingContext ctx, IReadOnlyList<Vec2> pts, bool closed, IPen pen, IBrush? fill)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(P(pts[0]), fill is not null);
            for (int i = 1; i < pts.Count; i++) g.LineTo(P(pts[i]));
            g.EndFigure(closed);
        }
        ctx.DrawGeometry(fill, pen, geo);
    }

    /// <summary>Arco por tres puntos, aproximado con segmentos.</summary>
    internal static List<Vec2> ArcPoints(Vec2 a, Vec2 m, Vec2 b)
    {
        double d = 2 * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
        if (Math.Abs(d) < 1e-9) return [a, b];
        double a2 = a.X * a.X + a.Y * a.Y, m2 = m.X * m.X + m.Y * m.Y, b2 = b.X * b.X + b.Y * b.Y;
        double cx = (a2 * (m.Y - b.Y) + m2 * (b.Y - a.Y) + b2 * (a.Y - m.Y)) / d;
        double cy = (a2 * (b.X - m.X) + m2 * (a.X - b.X) + b2 * (m.X - a.X)) / d;
        double r = Math.Sqrt((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy));
        double t0 = Math.Atan2(a.Y - cy, a.X - cx);
        double Norm(double t) { t %= 2 * Math.PI; return t < 0 ? t + 2 * Math.PI : t; }
        double dm = Norm(Math.Atan2(m.Y - cy, m.X - cx) - t0);
        double db = Norm(Math.Atan2(b.Y - cy, b.X - cx) - t0);
        double sweep = dm < db ? db : db - 2 * Math.PI;
        int n = Math.Max(6, (int)(Math.Abs(sweep) / (Math.PI / 24)));
        var pts = new List<Vec2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = t0 + sweep * i / n;
            pts.Add(new Vec2(cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return pts;
    }

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush);

    private static void DrawText(DrawingContext ctx, string text, Vec2 at, double size, IBrush brush, bool center = false)
    {
        if (string.IsNullOrEmpty(text) || text == "~") return;
        var ft = Text(text, size, brush);
        var origin = center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : P(at);
        ctx.DrawText(ft, origin);
    }
}
