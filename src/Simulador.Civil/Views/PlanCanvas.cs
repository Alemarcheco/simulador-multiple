using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Views;

public enum PlanTool { Select, Wall, Room, Door, Window, Stair, Fixture, Route, Rename }

/// <summary>
/// Planta 2D editable de un nivel. Ratón: izquierdo dibuja/selecciona, derecho termina o cancela,
/// rueda acerca/aleja, botón central (o espacio + arrastrar) desplaza la vista.
/// </summary>
public sealed class PlanCanvas : Control
{
    private readonly BuildingDocument _doc;
    private double _scale = 40;           // píxeles por metro
    private Point _pan = new(60, 60);     // posición en pantalla del origen
    private Point2? _cursor;              // posición del ratón en metros (ya con imán)
    private Point2? _drawStart;           // inicio del muro/rectángulo en curso
    private Point? _panStart;
    private string? _routeFrom;
    private bool _spaceDown;

    public PlanCanvas(BuildingDocument doc)
    {
        _doc = doc;
        ClipToBounds = true;
        Focusable = true;
        doc.Changed += (_, _) => InvalidateVisual();
    }

    public string LevelId { get; set; } = "N0";
    public PlanTool Tool { get; set; } = PlanTool.Select;
    public double GridStep { get; set; } = 0.05;
    public double WallThickness { get; set; } = 0.15;
    public double WallHeight { get; set; } = 2.8;
    public double DoorWidth { get; set; } = 0.8;
    public double WindowWidth { get; set; } = 1.2;
    public double StairWidth { get; set; } = 1.0;
    public FixtureType FixtureType { get; set; } = FixtureCatalog.Tomacorriente;
    public string? SelectedId { get; private set; }

    /// <summary>Se dispara al cambiar la selección.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Mensajes para la barra de estado (medidas, ayuda de la herramienta).</summary>
    public event EventHandler<string>? Status;
    /// <summary>Pide al usuario un nombre y uso para la habitación en un punto.</summary>
    public Func<Point2, Task<(string Name, string Use)?>>? AskSpaceName { get; set; }

    public void Select(string? id)
    {
        SelectedId = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void CancelCurrent()
    {
        _drawStart = null;
        _routeFrom = null;
        InvalidateVisual();
    }

    /// <summary>Encuadra todo el nivel en la vista.</summary>
    public void ZoomToFit()
    {
        var pts = _doc.Walls.Where(w => w.LevelId == LevelId).SelectMany(w => new[] { w.Start, w.End }).ToList();
        if (pts.Count == 0 || Bounds.Width < 10) { _scale = 40; _pan = new(60, Bounds.Height - 60); InvalidateVisual(); return; }
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double w = Math.Max(maxX - minX, 1), h = Math.Max(maxY - minY, 1);
        _scale = Math.Min((Bounds.Width - 80) / w, (Bounds.Height - 80) / h);
        _pan = new Point(40 - minX * _scale + (Bounds.Width - 80 - w * _scale) / 2, Bounds.Height - 40 + minY * _scale - (Bounds.Height - 80 - h * _scale) / 2);
        InvalidateVisual();
    }

    // ------------------------------------------------------------ coordenadas (Y hacia arriba en el mundo)

    private Point ToScreen(Point2 p) => new(_pan.X + p.X * _scale, _pan.Y - p.Y * _scale);
    private Point2 ToWorld(Point s) => new((s.X - _pan.X) / _scale, (_pan.Y - s.Y) / _scale);

    private Point2 Snap(Point2 p, Point2? orthoFrom = null)
    {
        // Imán a extremos de muros existentes.
        double tol = 8 / _scale;
        foreach (var w in _doc.Walls.Where(w => w.LevelId == LevelId))
        {
            if (w.Start.DistanceTo(p) < tol) return w.Start;
            if (w.End.DistanceTo(p) < tol) return w.End;
        }
        if (orthoFrom is { } o)
        {
            // Ortogonal si el ángulo está a menos de ~7° de un eje.
            var d = p - o;
            if (Math.Abs(d.Y) < Math.Abs(d.X) * 0.12) p = new(p.X, o.Y);
            else if (Math.Abs(d.X) < Math.Abs(d.Y) * 0.12) p = new(o.X, p.Y);
        }
        return new(Math.Round(p.X / GridStep) * GridStep, Math.Round(p.Y / GridStep) * GridStep);
    }

    // ------------------------------------------------------------ dibujo

    private static readonly IBrush Bg = new SolidColorBrush(Color.FromRgb(250, 250, 247));
    private static readonly IPen GridMinor = new Pen(new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)), 1);
    private static readonly IPen GridMajor = new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 1);
    private static readonly IBrush WallFill = new SolidColorBrush(Color.FromRgb(70, 70, 75));
    private static readonly IBrush WallFillOther = new SolidColorBrush(Color.FromArgb(40, 70, 70, 75));
    private static readonly IBrush RoomFill = new SolidColorBrush(Color.FromArgb(28, 60, 130, 200));
    private static readonly IBrush SelBrush = new SolidColorBrush(Color.FromArgb(120, 255, 140, 0));
    private static readonly IPen SelPen = new Pen(Brushes.DarkOrange, 2);
    private static readonly IPen OpeningPen = new Pen(Brushes.SteelBlue, 1.5);
    private static readonly IPen DimPen = new Pen(Brushes.DimGray, 1, new DashStyle([4, 3], 0));
    private static readonly IPen StairPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 80, 40)), 1.2);
    private static readonly IPen RoutePenE = new Pen(Brushes.OrangeRed, 2, new DashStyle([6, 3], 0));
    private static readonly IPen RoutePenH = new Pen(Brushes.DodgerBlue, 2, new DashStyle([6, 3], 0));
    private static readonly Typeface Face = new(FontFamily.Default);

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Bg, new Rect(Bounds.Size));
        DrawGrid(ctx);

        var level = _doc.Levels.FirstOrDefault(l => l.Id == LevelId);
        if (level is null) return;

        // Nivel inferior como calco tenue.
        var below = _doc.Levels.Where(l => l.Elevation < level.Elevation - 1e-6).MaxBy(l => l.Elevation);
        if (below is not null)
            foreach (var w in _doc.Walls.Where(w => w.LevelId == below.Id)) DrawWallBody(ctx, w, WallFillOther, null);

        foreach (var s in _doc.Spaces.Where(s => s.LevelId == LevelId))
        {
            ctx.DrawGeometry(s.Id == SelectedId ? SelBrush : RoomFill, null, Poly(s.Outline));
        }

        foreach (var st in _doc.Stairs.Where(s => s.LevelId == LevelId)) DrawStair(ctx, st);
        foreach (var w in _doc.Walls.Where(w => w.LevelId == LevelId)) DrawWall(ctx, w);
        foreach (var r in _doc.Routes) DrawRoute(ctx, r);
        foreach (var f in _doc.Fixtures.Where(f => f.LevelId == LevelId)) DrawFixture(ctx, f);

        foreach (var s in _doc.Spaces.Where(s => s.LevelId == LevelId))
        {
            var c = ToScreen(Geo.Centroid(s.Outline));
            DrawLabel(ctx, $"{s.Name}\n{s.Area.ToString("0.0", CultureInfo.CurrentCulture)} m²", c, 12, Brushes.Black, center: true);
        }

        DrawPreview(ctx);
    }

    private void DrawGrid(DrawingContext ctx)
    {
        double step = _scale >= 30 ? 0.5 : _scale >= 8 ? 1 : 5;
        var a = ToWorld(new Point(0, Bounds.Height));
        var b = ToWorld(new Point(Bounds.Width, 0));
        for (double x = Math.Floor(a.X / step) * step; x <= b.X; x += step)
        {
            var pen = Math.Abs(x / (step * 10) - Math.Round(x / (step * 10))) < 1e-6 || Math.Abs(x - Math.Round(x)) < 1e-6 && step < 1 ? GridMajor : GridMinor;
            ctx.DrawLine(pen, ToScreen(new(x, a.Y)), ToScreen(new(x, b.Y)));
        }
        for (double y = Math.Floor(a.Y / step) * step; y <= b.Y; y += step)
        {
            var pen = Math.Abs(y / (step * 10) - Math.Round(y / (step * 10))) < 1e-6 || Math.Abs(y - Math.Round(y)) < 1e-6 && step < 1 ? GridMajor : GridMinor;
            ctx.DrawLine(pen, ToScreen(new(a.X, y)), ToScreen(new(b.X, y)));
        }
    }

    private StreamGeometry Poly(IReadOnlyList<Point2> pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(ToScreen(pts[0]), true);
        foreach (var p in pts.Skip(1)) c.LineTo(ToScreen(p));
        c.EndFigure(true);
        return g;
    }

    /// <summary>Rectángulo del muro entre dos distancias a lo largo del eje.</summary>
    private static Point2[] WallQuad(Wall w, double from, double to)
    {
        var d = w.Direction;
        var n = d.Perp() * (w.Thickness / 2);
        var a = w.Start + d * from;
        var b = w.Start + d * to;
        return [a - n, b - n, b + n, a + n];
    }

    private void DrawWallBody(DrawingContext ctx, Wall w, IBrush fill, IPen? pen)
    {
        // El muro se dibuja en tramos, dejando los huecos de puertas y ventanas.
        var gaps = _doc.Openings.Where(o => o.HostWallId == w.Id)
            .Select(o => (Lo: Math.Max(0, o.Offset - o.Width / 2), Hi: Math.Min(w.Length, o.Offset + o.Width / 2)))
            .OrderBy(g => g.Lo).ToList();
        // Prolongar medio espesor en los extremos para que las esquinas queden cerradas.
        double pos = -w.Thickness / 2;
        foreach (var (lo, hi) in gaps)
        {
            if (lo > pos) ctx.DrawGeometry(fill, pen, Poly(WallQuad(w, pos, lo)));
            pos = Math.Max(pos, hi);
        }
        if (w.Length + w.Thickness / 2 > pos) ctx.DrawGeometry(fill, pen, Poly(WallQuad(w, pos, w.Length + w.Thickness / 2)));
    }

    private void DrawWall(DrawingContext ctx, Wall w)
    {
        bool sel = w.Id == SelectedId;
        DrawWallBody(ctx, w, sel ? Brushes.DarkOrange : WallFill, null);
        foreach (var o in _doc.Openings.Where(o => o.HostWallId == w.Id)) DrawOpening(ctx, w, o);
    }

    private void DrawOpening(DrawingContext ctx, Wall w, Opening o)
    {
        bool sel = o.Id == SelectedId;
        var pen = sel ? SelPen : OpeningPen;
        var d = w.Direction;
        var n = d.Perp();
        if (o.Kind == OpeningKind.Window)
        {
            // Tres líneas paralelas (marco y vidrio).
            foreach (var k in new[] { -0.5, 0, 0.5 })
            {
                var off = n * (w.Thickness * k);
                ctx.DrawLine(pen, ToScreen(w.Start + d * (o.Offset - o.Width / 2) + off), ToScreen(w.Start + d * (o.Offset + o.Width / 2) + off));
            }
            return;
        }
        var hinge = w.Start + d * (o.HingeAtStart ? o.Offset - o.Width / 2 : o.Offset + o.Width / 2);
        var closed = w.Start + d * (o.HingeAtStart ? o.Offset + o.Width / 2 : o.Offset - o.Width / 2);
        var side = n * (o.SwingLeft ? 1 : -1);
        var face = side * (w.Thickness / 2);
        var open = hinge + face + side * o.Width;
        ctx.DrawLine(pen, ToScreen(hinge + face), ToScreen(open));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(ToScreen(open), false);
            bool cw = o.HingeAtStart == o.SwingLeft; // sentido del arco en pantalla (Y invertida)
            c.ArcTo(ToScreen(closed + face), new Size(o.Width * _scale, o.Width * _scale), 0, false, cw ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(pen.Brush, 1, new DashStyle([3, 2], 0)), g);
    }

    private void DrawStair(DrawingContext ctx, Stair s)
    {
        var design = s.Design;
        bool sel = s.Id == SelectedId;
        ctx.DrawGeometry(sel ? SelBrush : new SolidColorBrush(Color.FromArgb(40, 160, 110, 60)), StairPen, Poly(s.Outline));
        var d = s.Direction;
        var n = d.Perp() * (s.Width / 2);
        for (int i = 1; i < design.Risers - 1; i++)
        {
            var c = s.Start + d * (i * design.Tread);
            ctx.DrawLine(StairPen, ToScreen(c - n), ToScreen(c + n));
        }
        // Flecha de subida.
        var end = s.Start + d * design.RunLength;
        ctx.DrawLine(StairPen, ToScreen(s.Start), ToScreen(end));
        ctx.DrawLine(StairPen, ToScreen(end), ToScreen(end - d * 0.25 + d.Perp() * 0.12));
        ctx.DrawLine(StairPen, ToScreen(end), ToScreen(end - d * 0.25 - d.Perp() * 0.12));
        DrawLabel(ctx, "sube", ToScreen(s.Start - d * 0.2), 10, StairPen.Brush!, center: true);
    }

    private void DrawFixture(DrawingContext ctx, PlacedFixture f)
    {
        var p = ToScreen(f.Position.Xy);
        var t = FixtureCatalog.ByKind(f.Kind);
        bool sel = f.Id == SelectedId || f.Id == _routeFrom;
        double r = Math.Clamp(0.12 * _scale, 6, 14);
        IBrush fill = f.ModuleId == "hidraulica" ? Brushes.DodgerBlue : Brushes.OrangeRed;
        if (f.Kind == "tanque")
        {
            r = Math.Max(r, f.Width / 2 * _scale);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(80, 30, 144, 255)), sel ? SelPen : new Pen(fill, 2), p, r, r);
        }
        else
        {
            ctx.DrawEllipse(Brushes.White, sel ? SelPen : new Pen(fill, 2), p, r, r);
        }
        DrawLabel(ctx, t?.Icon ?? "•", p, r * 1.1, fill, center: true);
        DrawLabel(ctx, f.ComponentId, new Point(p.X + r + 2, p.Y - r - 2), 10, fill, center: false);
    }

    private void DrawRoute(DrawingContext ctx, Route r)
    {
        var pen = r.ModuleId == "hidraulica" ? RoutePenH : RoutePenE;
        if (r.Id == SelectedId) pen = SelPen;
        var level = _doc.Levels.First(l => l.Id == LevelId);
        foreach (var s in r.Segments)
        {
            // Solo los tramos de este nivel.
            double z = Math.Min(s.Start.Z, s.End.Z);
            if (z < level.Elevation - 1e-6 || z > level.Elevation + level.Height + 1e-6) continue;
            if (s.Start.Xy.DistanceTo(s.End.Xy) < 1e-6) continue;
            ctx.DrawLine(pen, ToScreen(s.Start.Xy), ToScreen(s.End.Xy));
        }
    }

    private void DrawPreview(DrawingContext ctx)
    {
        if (_cursor is not { } c) return;
        var cs = ToScreen(c);
        ctx.DrawLine(GridMajor, new Point(cs.X - 8, cs.Y), new Point(cs.X + 8, cs.Y));
        ctx.DrawLine(GridMajor, new Point(cs.X, cs.Y - 8), new Point(cs.X, cs.Y + 8));

        if (_drawStart is { } s && Tool == PlanTool.Wall)
        {
            var w = new Wall("_", LevelId, s, c, WallThickness, WallHeight);
            if (w.Length > 1e-6) ctx.DrawGeometry(SelBrush, SelPen, Poly(WallQuad(w, 0, w.Length)));
            DrawLabel(ctx, $"{w.Length:0.00} m", ToScreen((s + c) * 0.5), 12, Brushes.DarkOrange, center: true);
        }
        else if (_drawStart is { } r && Tool == PlanTool.Room)
        {
            ctx.DrawGeometry(SelBrush, SelPen, Poly([r, new(c.X, r.Y), c, new(r.X, c.Y)]));
            DrawLabel(ctx, $"{Math.Abs(c.X - r.X):0.00} × {Math.Abs(c.Y - r.Y):0.00} m", ToScreen((r + c) * 0.5), 12, Brushes.DarkOrange, center: true);
        }
        else if (_drawStart is { } st && Tool == PlanTool.Stair)
        {
            var dir = c - st;
            if (dir.Length > 1e-6)
            {
                var stair = new Stair("_", LevelId, st, Math.Atan2(dir.Y, dir.X), StairWidth, CurrentRise());
                ctx.DrawGeometry(SelBrush, SelPen, Poly(stair.Outline));
                var d = stair.Design;
                DrawLabel(ctx, $"{d.Risers} escalones de {d.Riser * 100:0.0} cm, huella {d.Tread * 100:0.0} cm, largo {d.RunLength:0.00} m",
                    ToScreen(st) + new Point(10, 10), 12, Brushes.DarkOrange, center: false);
            }
        }
        else if (Tool == PlanTool.Route && _routeFrom is not null)
        {
            var f = _doc.Fixtures.FirstOrDefault(x => x.Id == _routeFrom);
            if (f is not null) ctx.DrawLine(DimPen, ToScreen(f.Position.Xy), cs);
        }
    }

    private void DrawLabel(DrawingContext ctx, string text, Point at, double size, IBrush brush, bool center)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush);
        if (!center) { ctx.DrawText(ft, at); return; }
        double w = ft.Width;
        ft.MaxTextWidth = w + 1;
        ft.TextAlignment = TextAlignment.Center;
        ctx.DrawText(ft, new Point(at.X - (w + 1) / 2, at.Y - ft.Height / 2));
    }

    // ------------------------------------------------------------ ratón y teclado

    private double CurrentRise()
    {
        var lvl = _doc.GetLevel(LevelId);
        var above = _doc.Levels.Where(l => l.Elevation > lvl.Elevation + 1e-6).MinBy(l => l.Elevation);
        return above is null ? lvl.Height : above.Elevation - lvl.Elevation;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var m = e.GetPosition(this);
        var before = ToWorld(m);
        _scale = Math.Clamp(_scale * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 2, 400);
        var after = ToScreen(before);
        _pan = new Point(_pan.X + m.X - after.X, _pan.Y + m.Y - after.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space) _spaceDown = true;
        else if (e.Key == Key.Escape) CancelCurrent();
        else if (e.Key is Key.Delete or Key.Back && SelectedId is { } id && !id.Contains("-R"))
        {
            _doc.Edit(ed => ed.Delete(id));
            Select(null);
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) _spaceDown = false;
        base.OnKeyUp(e);
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        var pos = e.GetPosition(this);
        if (pt.Properties.IsMiddleButtonPressed || (_spaceDown && pt.Properties.IsLeftButtonPressed))
        {
            _panStart = pos;
            e.Pointer.Capture(this);
            return;
        }
        if (pt.Properties.IsRightButtonPressed)
        {
            CancelCurrent();
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;

        var raw = ToWorld(pos);
        var p = Snap(raw, _drawStart);
        switch (Tool)
        {
            case PlanTool.Select:
                Select(HitTest(raw));
                break;

            case PlanTool.Wall:
                if (_drawStart is { } s)
                {
                    if (s.DistanceTo(p) > 0.05)
                        _doc.Edit(ed => ed.AddWall(LevelId, s, p, WallThickness, WallHeight));
                    // Encadenar: el siguiente muro empieza donde acabó este, salvo que se cierre sobre un extremo existente.
                    bool closed = _doc.Walls.Count(w => w.LevelId == LevelId && (w.Start.DistanceTo(p) < 1e-6 || w.End.DistanceTo(p) < 1e-6)) > 1;
                    _drawStart = closed ? null : p;
                }
                else _drawStart = p;
                break;

            case PlanTool.Room:
                if (_drawStart is { } r)
                {
                    if (Math.Abs(p.X - r.X) > 0.3 && Math.Abs(p.Y - r.Y) > 0.3)
                        _doc.Edit(ed => ed.AddWallLoop(LevelId, [r, new(p.X, r.Y), p, new(r.X, p.Y)], WallThickness, WallHeight));
                    _drawStart = null;
                }
                else _drawStart = p;
                break;

            case PlanTool.Door:
            case PlanTool.Window:
                PlaceOpening(raw);
                break;

            case PlanTool.Stair:
                if (_drawStart is { } st)
                {
                    var dir = p - st;
                    if (dir.Length > 0.1)
                        _doc.Edit(ed => ed.AddStair(LevelId, st, Math.Atan2(dir.Y, dir.X), StairWidth, CurrentRise()));
                    _drawStart = null;
                }
                else _drawStart = p;
                break;

            case PlanTool.Fixture:
                PlaceFixture(raw);
                break;

            case PlanTool.Route:
                RouteClick(raw);
                break;

            case PlanTool.Rename:
                var space = _doc.SpaceAt(LevelId, raw);
                if (space is null) { Status?.Invoke(this, "Haz clic dentro de una habitación cerrada."); break; }
                if (AskSpaceName is not null && await AskSpaceName(raw) is { } named)
                    _doc.Edit(ed => ed.RenameSpaceAt(LevelId, raw, named.Name, named.Use));
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_panStart is { } ps)
        {
            _pan = new Point(_pan.X + pos.X - ps.X, _pan.Y + pos.Y - ps.Y);
            _panStart = pos;
        }
        _cursor = Snap(ToWorld(pos), _drawStart);
        Status?.Invoke(this, $"x = {_cursor.Value.X:0.00} m   y = {_cursor.Value.Y:0.00} m   ·   {ToolHelp()}");
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panStart is not null)
        {
            _panStart = null;
            e.Pointer.Capture(null);
        }
    }

    private string ToolHelp() => Tool switch
    {
        PlanTool.Select => "Clic para seleccionar; Supr para borrar; rueda para zoom; botón central o espacio + arrastrar para mover.",
        PlanTool.Wall => "Clic en el inicio y en cada esquina; clic derecho para terminar.",
        PlanTool.Room => "Clic en dos esquinas opuestas para crear una habitación rectangular.",
        PlanTool.Door => "Clic sobre un muro para poner la puerta (el lado del clic decide hacia dónde abre).",
        PlanTool.Window => "Clic sobre un muro para poner la ventana.",
        PlanTool.Stair => "Clic en el arranque y otro clic hacia donde sube; los peldaños se calculan solos.",
        PlanTool.Fixture => $"Clic para colocar: {FixtureType.Name}.",
        PlanTool.Route => _routeFrom is null ? "Clic en el elemento de origen." : "Clic en el elemento de destino (la ruta va por el techo).",
        PlanTool.Rename => "Clic dentro de una habitación para ponerle nombre y uso.",
        _ => "",
    };

    private void PlaceOpening(Point2 raw)
    {
        var near = _doc.NearestWall(LevelId, raw, 0.5);
        if (near is not { } n) { Status?.Invoke(this, "Haz clic más cerca de un muro."); return; }
        bool door = Tool == PlanTool.Door;
        double width = door ? DoorWidth : WindowWidth;
        if (n.Wall.Length < width + 0.1) { Status?.Invoke(this, "El muro es demasiado corto para ese hueco."); return; }
        double offset = Math.Clamp(Math.Round(n.Along / 0.05) * 0.05, width / 2 + 0.05, n.Wall.Length - width / 2 - 0.05);
        _doc.Edit(ed =>
        {
            var o = door ? ed.AddDoor(n.Wall, offset, width) : ed.AddWindow(n.Wall, offset, width);
            if (door) ed.Replace(o, o with { SwingLeft = n.Face == WallFace.A });
            SelectedId = o.Id;
        });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlaceFixture(Point2 raw)
    {
        var t = FixtureType;
        var lvl = _doc.GetLevel(LevelId);
        if (FixtureCatalog.IsWallMounted(t))
        {
            var near = _doc.NearestWall(LevelId, raw, 0.6);
            if (near is not { } n) { Status?.Invoke(this, "Los elementos de pared se colocan haciendo clic junto a un muro."); return; }
            double along = Math.Clamp(Math.Round(n.Along / 0.05) * 0.05, 0, n.Wall.Length);
            var pos = _doc.GetWallPoint(n.Wall.Id, n.Face, along, t.DefaultHeight);
            var normal = n.Wall.Direction.Perp() * (n.Face == WallFace.A ? 1 : -1);
            _doc.Edit(ed =>
            {
                var comp = ed.NewComponentId(t.ComponentPrefix);
                var f = ed.AddFixture(new PlacedFixture(ed.NewId("F"), t.ModuleId, t.Kind, comp, null, LevelId, n.Wall.Id, pos, Math.Atan2(normal.Y, normal.X)));
                SelectedId = f.Id;
            });
        }
        else
        {
            var p = Snap(raw);
            double z = t.Kind == "luminaria" ? lvl.Elevation + lvl.Height - 0.05 : lvl.Elevation;
            double size = t.Kind == "tanque" ? 1.2 : 0.3;
            _doc.Edit(ed =>
            {
                var comp = ed.NewComponentId(t.ComponentPrefix);
                var f = ed.AddFixture(new PlacedFixture(ed.NewId("F"), t.ModuleId, t.Kind, comp, null, LevelId, null, new Point3(p, z), 0, size, size));
                SelectedId = f.Id;
            });
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RouteClick(Point2 raw)
    {
        var hit = _doc.Fixtures.Where(f => f.LevelId == LevelId).MinBy(f => f.Position.Xy.DistanceTo(raw));
        if (hit is null || hit.Position.Xy.DistanceTo(raw) > Math.Max(0.4, hit.Width / 2 + 0.1))
        {
            Status?.Invoke(this, "Haz clic sobre un elemento (tomacorriente, punto de agua…).");
            return;
        }
        if (_routeFrom is null) { _routeFrom = hit.Id; return; }
        var from = _doc.Fixtures.First(f => f.Id == _routeFrom);
        _routeFrom = null;
        if (from.Id == hit.Id) return;
        string kind = from.ModuleId == "hidraulica" ? "tuberia" : "tubo_electrico";
        _doc.Edit(ed =>
        {
            var r = ed.AddRoute(new Route(ed.NewId("R"), from.ModuleId, kind, from.Id, hit.Id, RouteBuilder.ViaCeiling(_doc, from, hit)));
            SelectedId = r.Id;
        });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? HitTest(Point2 p)
    {
        double tol = 6 / _scale;
        var fx = _doc.Fixtures.Where(f => f.LevelId == LevelId).FirstOrDefault(f => f.Position.Xy.DistanceTo(p) < Math.Max(tol * 2, f.Width / 2));
        if (fx is not null) return fx.Id;
        foreach (var w in _doc.Walls.Where(w => w.LevelId == LevelId))
        {
            if (Geo.DistanceToSegment(w.Start, w.End, p) > w.Thickness / 2 + tol) continue;
            double along = Geo.ProjectT(w.Start, w.End, p) * w.Length;
            var o = _doc.Openings.FirstOrDefault(o => o.HostWallId == w.Id && Math.Abs(along - o.Offset) <= o.Width / 2);
            return o?.Id ?? w.Id;
        }
        var level = _doc.GetLevel(LevelId);
        foreach (var r in _doc.Routes)
            foreach (var s in r.Segments)
                if (s.Start.Z >= level.Elevation - 1e-6 && s.Start.Z <= level.Elevation + level.Height + 1e-6
                    && Geo.DistanceToSegment(s.Start.Xy, s.End.Xy, p) < tol)
                    return r.Id;
        var st = _doc.Stairs.FirstOrDefault(s => s.LevelId == LevelId && Geo.Contains(s.Outline, p));
        if (st is not null) return st.Id;
        return _doc.SpaceAt(LevelId, p)?.Id;
    }
}
