using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Circuitos.Catalog;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

public enum PcbTool { Select, Track }

/// <summary>
/// Lienzo del editor de placas. Rueda = zoom, botón derecho o central arrastrando = desplazar.
/// Teclas: R girar, X pista, Supr borrar, Espacio cambia la forma de la esquina, Esc cancelar, Ctrl+Z / Ctrl+Y.
/// </summary>
public sealed class PcbCanvas : Control
{
    public const double PlaceGrid = 1.27;
    public const double RouteGrid = 0.635;

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#1B1F23"));
    private static readonly IBrush BoardFill = new SolidColorBrush(Color.Parse("#173D2C"));
    private static readonly Color EdgeColor = Color.Parse("#E8D44D");
    private static readonly Color BottomCopper = Color.Parse("#E39A3B");
    private static readonly Color TopCopper = Color.Parse("#D9534F");
    private static readonly Color PadColor = Color.Parse("#C9A64A");
    private static readonly Color SilkColor = Color.Parse("#EDEBD7");
    private static readonly Color SelectColor = Color.Parse("#57A6FF");
    private static readonly IBrush Hole = new SolidColorBrush(Color.Parse("#0E1012"));
    private static readonly Typeface Font = new("Inter");

    private readonly ComponentCatalog _catalog;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    private PcbDocument _doc = new();
    private List<Ratsnest> _ratsnest = [];
    private List<DrcViolation>? _drc;
    private Vector _offset = new(-10, -10);
    private double _zoom = 10;

    private PcbTool _tool = PcbTool.Select;
    private Vec2? _trackStart;
    private string _trackNet = "";
    private bool _diagonalFirst;
    private Vec2 _mouse;
    private Vec2 _mouseRaw;

    private Point? _panStart;
    private Vector _panOrigin;
    private bool _dragging;
    private Vec2 _dragStart;
    private Vec2 _dragOrigin;

    public PcbCanvas(ComponentCatalog catalog)
    {
        _catalog = catalog;
        Focusable = true;
        ClipToBounds = true;
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler<string>? Status;

    public PcbDocument Document => _doc;
    public object? Selected { get; private set; }
    public PcbTool Tool => _tool;
    public IReadOnlyList<Ratsnest> RatsnestLines => _ratsnest;
    public IReadOnlyList<DrcViolation>? Violations => _drc;
    public bool IsDirty { get; set; }
    /// <summary>Cara donde se dibujan las pistas nuevas.</summary>
    public string ActiveLayer { get; set; } = Layers.Bottom;

    // ---------- Documento ----------

    public void Load(PcbDocument doc)
    {
        _doc = doc;
        var missing = _doc.ResolveFootprints(_catalog.FindFootprint);
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        Select(null);
        Changed(markDirty: false);
        ZoomToFit();
        if (missing.Count > 0) Status?.Invoke(this, "Faltan huellas (importa sus librerías): " + string.Join(", ", missing));
    }

    public void Checkpoint()
    {
        _undo.Push(_doc.ToJson());
        _redo.Clear();
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return;
        to.Push(_doc.ToJson());
        _doc = PcbDocument.FromJson(from.Pop());
        _doc.ResolveFootprints(_catalog.FindFootprint);
        Select(null);
        Changed();
    }

    public void Changed(bool markDirty = true)
    {
        _ratsnest = PcbAnalysis.ComputeRatsnest(_doc);
        _drc = null;
        if (markDirty) IsDirty = true;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public List<DrcViolation> RunDrc()
    {
        _drc = PcbAnalysis.CheckRules(_doc);
        InvalidateVisual();
        return _drc;
    }

    // ---------- Herramientas ----------

    public void SetTool(PcbTool tool)
    {
        _tool = tool;
        _trackStart = null;
        if (tool == PcbTool.Track)
            Status?.Invoke(this, "Pista: clic en un pad para empezar, clic en cada esquina, termina sobre otro pad (o doble clic / Esc). Espacio cambia la esquina.");
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
        if (Selected is PcbTrack t) _doc.Tracks.Remove(t);
        else if (Selected is PcbFootprint f) _doc.Footprints.Remove(f);
        Select(null);
        Changed();
    }

    public void RotateSelected()
    {
        if (Selected is not PcbFootprint f) return;
        Checkpoint();
        f.Rotation = (f.Rotation + 90) % 360;
        Changed();
    }

    // ---------- Vista ----------

    private Point ToScreen(Vec2 w) => new((w.X - _offset.X) * _zoom, (w.Y - _offset.Y) * _zoom);
    private Vec2 ToWorld(Point p) => new(p.X / _zoom + _offset.X, p.Y / _zoom + _offset.Y);
    private static Vec2 Snap(Vec2 v, double g) => new(Math.Round(v.X / g) * g, Math.Round(v.Y / g) * g);

    public void ZoomToFit()
    {
        if (Bounds.Width < 10 || Bounds.Height < 10) return;
        double minX = -5, minY = -5, maxX = _doc.BoardWidth + 5, maxY = _doc.BoardHeight + 5;
        foreach (var f in _doc.Footprints)
        {
            var (a, b) = FootprintTransform.Bounds(f);
            minX = Math.Min(minX, a.X - 3); minY = Math.Min(minY, a.Y - 3);
            maxX = Math.Max(maxX, b.X + 3); maxY = Math.Max(maxY, b.Y + 3);
        }
        _zoom = Math.Clamp(Math.Min(Bounds.Width / (maxX - minX), Bounds.Height / (maxY - minY)), 1, 100);
        _offset = new Vector((minX + maxX) / 2 - Bounds.Width / 2 / _zoom, (minY + maxY) / 2 - Bounds.Height / 2 / _zoom);
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
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 1, 150);
        var after = ToWorld(p);
        _offset += new Vector(before.X - after.X, before.Y - after.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    private PlacedPad? PadAt(Vec2 p) =>
        _doc.Footprints.SelectMany(FootprintTransform.Pads)
            .Where(pad => Geometry2D.PadSegment(pad, p, p) <= 0.05)
            .OrderBy(pad => Geometry2D.Length(pad.Center - p))
            .FirstOrDefault();

    private PcbTrack? TrackAt(Vec2 p, double extra = 0) =>
        _doc.Tracks.AsEnumerable().Reverse()
            .FirstOrDefault(t => Geometry2D.PointSegment(p, t.A, t.B) <= t.Width / 2 + extra);

    /// <summary>Punto de enganche: centro del pad o grid de ruteo.</summary>
    private Vec2 RouteSnap(Vec2 raw) => PadAt(raw)?.Center ?? Snap(raw, RouteGrid);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(this);
        var world = ToWorld(point.Position);

        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsRightButtonPressed && !(_tool == PcbTool.Track && _trackStart is not null)))
        {
            _panStart = point.Position;
            _panOrigin = _offset;
            e.Pointer.Capture(this);
            return;
        }

        if (_tool == PcbTool.Track)
        {
            if (point.Properties.IsRightButtonPressed) { _trackStart = null; InvalidateVisual(); return; }
            var snapped = RouteSnap(world);
            if (_trackStart is null)
            {
                _trackStart = snapped;
                _trackNet = PadAt(world)?.Net ?? TrackAt(world)?.Net ?? "";
                Status?.Invoke(this, _trackNet.Length > 0 ? $"Ruteando la red {NetName(_trackNet)}." : "Ruteando una pista sin red.");
            }
            else
            {
                AddTrackTo(snapped);
                var endPad = PadAt(world);
                if (_trackNet.Length == 0 && endPad is not null) _trackNet = endPad.Net;
                if (e.ClickCount >= 2 || endPad is not null) _trackStart = null;
                else _trackStart = snapped;
            }
            InvalidateVisual();
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            object? hit = TrackAt(world, 2 / _zoom);
            hit ??= _doc.Footprints.AsEnumerable().Reverse().FirstOrDefault(f =>
            {
                var (a, b) = FootprintTransform.Bounds(f);
                return world.X >= a.X && world.X <= b.X && world.Y >= a.Y && world.Y <= b.Y;
            });
            Select(hit);
            if (hit is PcbFootprint f)
            {
                _dragging = true;
                _dragStart = Snap(world, PlaceGrid);
                _dragOrigin = f.Position;
                Checkpoint();
                e.Pointer.Capture(this);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        _mouseRaw = ToWorld(p);
        _mouse = _tool == PcbTool.Track ? RouteSnap(_mouseRaw) : Snap(_mouseRaw, PlaceGrid);
        if (_panStart is { } start)
        {
            var d = (p - start) / _zoom;
            _offset = _panOrigin - new Vector(d.X, d.Y);
        }
        else if (_dragging && Selected is PcbFootprint f)
        {
            f.Position = _dragOrigin + (Snap(_mouseRaw, PlaceGrid) - _dragStart);
            _ratsnest = PcbAnalysis.ComputeRatsnest(_doc);
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            if (Selected is PcbFootprint f && f.Position != _dragOrigin) Changed();
            else _undo.TryPop(out _);
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
            case Key.X: SetTool(PcbTool.Track); break;
            case Key.F: ZoomToFit(); break;
            case Key.Delete or Key.Back: DeleteSelected(); break;
            case Key.Space when _tool == PcbTool.Track: _diagonalFirst = !_diagonalFirst; InvalidateVisual(); break;
            case Key.Escape:
                if (_tool == PcbTool.Track && _trackStart is not null) _trackStart = null;
                else SetTool(PcbTool.Select);
                InvalidateVisual();
                break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Camino de dos tramos con esquina a 45°, como en los programas de PCB.</summary>
    private (Vec2 Corner, Vec2 End) Route(Vec2 start, Vec2 end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double adx = Math.Abs(dx), ady = Math.Abs(dy);
        double diag = Math.Min(adx, ady);
        var diagVec = new Vec2(Math.Sign(dx) * diag, Math.Sign(dy) * diag);
        return _diagonalFirst ? (start + diagVec, end) : (end - diagVec, end);
    }

    private void AddTrackTo(Vec2 end)
    {
        if (_trackStart is not { } start || start == end) return;
        Checkpoint();
        var (corner, _) = Route(start, end);
        var layer = _doc.SingleSided ? Layers.Bottom : ActiveLayer;
        if (Geometry2D.Length(corner - start) > 1e-6)
            _doc.Tracks.Add(new PcbTrack { A = start, B = corner, Width = _doc.Rules.TrackWidth, Layer = layer, Net = _trackNet });
        if (Geometry2D.Length(end - corner) > 1e-6)
            _doc.Tracks.Add(new PcbTrack { A = corner, B = end, Width = _doc.Rules.TrackWidth, Layer = layer, Net = _trackNet });
        Changed();
    }

    // ---------- Dibujo ----------

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Background, new Rect(Bounds.Size));
        var m = Matrix.CreateTranslation(-_offset.X, -_offset.Y) * Matrix.CreateScale(_zoom, _zoom);
        double px = 1 / _zoom;
        using (ctx.PushTransform(m))
        {
            var board = new Rect(0, 0, _doc.BoardWidth, _doc.BoardHeight);
            ctx.FillRectangle(BoardFill, board);
            DrawGrid(ctx, board);
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(EdgeColor), Math.Max(0.15, px * 1.5)), board);

            // Cobre de abajo primero (se ve "a través" de la placa), luego el de arriba.
            foreach (var layer in new[] { Layers.Bottom, Layers.Top })
            {
                var color = layer == Layers.Bottom ? BottomCopper : TopCopper;
                foreach (var t in _doc.Tracks.Where(t => t.Layer == layer))
                {
                    var c = ReferenceEquals(t, Selected) ? SelectColor : color;
                    ctx.DrawLine(new Pen(new SolidColorBrush(c, 0.9), t.Width, lineCap: PenLineCap.Round), P(t.A), P(t.B));
                }
            }

            foreach (var f in _doc.Footprints) DrawFootprint(ctx, f);

            var rats = new Pen(new SolidColorBrush(Colors.White, 0.75), Math.Max(px, 0.08));
            foreach (var r in _ratsnest) ctx.DrawLine(rats, P(r.A), P(r.B));

            if (_tool == PcbTool.Track && _trackStart is { } ts)
            {
                var (corner, end) = Route(ts, _mouse);
                var pen = new Pen(new SolidColorBrush(BottomCopper, 0.55), _doc.Rules.TrackWidth, lineCap: PenLineCap.Round);
                ctx.DrawLine(pen, P(ts), P(corner));
                ctx.DrawLine(pen, P(corner), P(end));
            }
            if (_tool == PcbTool.Track)
            {
                var cross = new Pen(new SolidColorBrush(SelectColor), px);
                ctx.DrawLine(cross, P(_mouse - new Vec2(1, 0)), P(_mouse + new Vec2(1, 0)));
                ctx.DrawLine(cross, P(_mouse - new Vec2(0, 1)), P(_mouse + new Vec2(0, 1)));
            }

            if (_drc is not null)
            {
                var red = new Pen(new SolidColorBrush(Color.Parse("#FF3B3B")), Math.Max(0.2, px * 2));
                foreach (var v in _drc) ctx.DrawEllipse(null, red, P(v.Position), 1.2, 1.2);
            }
        }
    }

    private static Point P(Vec2 v) => new(v.X, v.Y);

    private void DrawGrid(DrawingContext ctx, Rect board)
    {
        double step = PlaceGrid;
        while (step * _zoom < 10) step *= 2;
        var dot = new SolidColorBrush(Colors.White, 0.12);
        double s = 1.2 / _zoom;
        for (double x = 0; x <= board.Width + 1e-9; x += step)
            for (double y = 0; y <= board.Height + 1e-9; y += step)
                ctx.FillRectangle(dot, new Rect(x - s / 2, y - s / 2, s, s));
    }

    private void DrawFootprint(DrawingContext ctx, PcbFootprint f)
    {
        double px = 1 / _zoom;
        bool selected = ReferenceEquals(f, Selected);
        var silk = new SolidColorBrush(selected ? SelectColor : SilkColor, 0.9);
        if (f.Definition is null)
        {
            ctx.DrawRectangle(null, new Pen(silk, px), new Rect(f.Position.X - 2, f.Position.Y - 2, 4, 4));
            DrawText(ctx, "?" + f.Reference, f.Position, 1.2, silk, true);
            return;
        }

        Vec2 W(Vec2 v) => FootprintTransform.ToBoard(f, v);
        foreach (var g in f.Definition.Graphics.Where(g => g.Layer == "F.SilkS"))
        {
            var pen = new Pen(silk, Math.Max(g.StrokeWidth > 0 ? g.StrokeWidth : 0.15, px), lineCap: PenLineCap.Round);
            switch (g)
            {
                case RectangleGraphic r:
                    Poly(ctx, [W(r.Start), W(new Vec2(r.End.X, r.Start.Y)), W(r.End), W(new Vec2(r.Start.X, r.End.Y))], true, pen);
                    break;
                case CircleGraphic c: ctx.DrawEllipse(null, pen, P(W(c.Center)), c.Radius, c.Radius); break;
                case PolylineGraphic p when p.Points.Count >= 2: Poly(ctx, p.Points.Select(W).ToList(), false, pen); break;
                case ArcGraphic a: Poly(ctx, Schematic.SchematicCanvas.ArcPoints(W(a.Start), W(a.Mid), W(a.End)), false, pen); break;
                case TextGraphic t when !t.Text.Contains("REF") && !t.Text.Contains("${"):
                    DrawText(ctx, t.Text, W(t.Position), t.Size, silk, true); break;
            }
        }

        foreach (var pad in FootprintTransform.Pads(f)) DrawPad(ctx, pad);

        var (min, max) = FootprintTransform.Bounds(f);
        DrawText(ctx, f.Reference, new Vec2((min.X + max.X) / 2, min.Y - 0.9), 1.1, silk, true);
        if (selected)
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(SelectColor), px, dashStyle: DashStyle.Dash), new Rect(P(min), P(max)));
    }

    private void DrawPad(DrawingContext ctx, PlacedPad pad)
    {
        var brush = new SolidColorBrush(PadColor);
        var c = pad.Center;
        double w = pad.Pad.Size.X, h = pad.Pad.Size.Y;
        double a = pad.RotationDeg * Math.PI / 180;
        Vec2 R(double x, double y) => new(c.X + x * Math.Cos(a) + y * Math.Sin(a), c.Y - x * Math.Sin(a) + y * Math.Cos(a));

        switch (pad.Pad.Shape)
        {
            case PadShape.Circle:
                ctx.DrawEllipse(brush, null, P(c), w / 2, w / 2);
                break;
            case PadShape.Oval:
            {
                double r = Math.Min(w, h) / 2;
                var p0 = w > h ? R(-(w / 2 - r), 0) : R(0, -(h / 2 - r));
                var p1 = w > h ? R(w / 2 - r, 0) : R(0, h / 2 - r);
                ctx.DrawLine(new Pen(brush, 2 * r, lineCap: PenLineCap.Round), P(p0), P(p1));
                break;
            }
            default:
                Poly(ctx, [R(-w / 2, -h / 2), R(w / 2, -h / 2), R(w / 2, h / 2), R(-w / 2, h / 2)], true, null, brush);
                break;
        }
        if (pad.Pad.Drill > 0)
        {
            if (pad.Pad.DrillSize is { } ds && Math.Abs(ds.X - ds.Y) > 1e-6)
            {
                double r = Math.Min(ds.X, ds.Y) / 2;
                var p0 = ds.X > ds.Y ? R(-(ds.X / 2 - r), 0) : R(0, -(ds.Y / 2 - r));
                var p1 = ds.X > ds.Y ? R(ds.X / 2 - r, 0) : R(0, ds.Y / 2 - r);
                ctx.DrawLine(new Pen(Hole, 2 * r, lineCap: PenLineCap.Round), P(p0), P(p1));
            }
            else ctx.DrawEllipse(Hole, null, P(c), pad.Pad.Drill / 2, pad.Pad.Drill / 2);
        }
        if (_zoom > 14 && pad.Net.Length > 0)
            DrawText(ctx, NetName(pad.Net), new Vec2(c.X, c.Y + Math.Max(w, h) / 2 + 0.35), 0.55, new SolidColorBrush(Colors.White, 0.8), true);
    }

    private static string NetName(string net) => net == "0" ? "GND" : net;

    private static void Poly(DrawingContext ctx, IReadOnlyList<Vec2> pts, bool closed, IPen? pen, IBrush? fill = null)
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

    private static void DrawText(DrawingContext ctx, string text, Vec2 at, double size, IBrush brush, bool center)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush);
        ctx.DrawText(ft, center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : P(at));
    }
}
