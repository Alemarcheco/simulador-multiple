using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Civil.Model;
using Simulador.Civil.Model.Electrical;
using Simulador.Core.Building;

namespace Simulador.Civil.Views;

/// <summary>Estructura (columnas y vigas) e instalación eléctrica en la planta: símbolos, tubería, enlaces y simulación.</summary>
public sealed partial class PlanCanvas
{
    private string? _linkSwitch;

    public double ColumnWidth { get; set; } = 0.30;
    public double ColumnDepth { get; set; } = 0.30;
    public double BeamWidth { get; set; } = 0.25;
    public double BeamDepth { get; set; } = 0.45;
    public string StructureMaterial { get; set; } = "concreto";
    public RouteStyle RouteStyle { get; set; } = RouteStyle.Ceiling;
    public ConduitType ConduitType { get; set; } = ConduitType.PvcLigero;
    public int ConduitDiameter { get; set; } = 16;

    /// <summary>Simulación en marcha: los clics en apagadores, contactos y equipos se pasan a <see cref="SimClick"/>.</summary>
    public InstallationSimulation? Simulation { get; set; }

    /// <summary>Clic sobre un elemento con la simulación en marcha (id del elemento y teclas pulsadas).</summary>
    public event EventHandler<(string FixtureId, KeyModifiers Keys)>? SimClick;

    private static readonly IBrush ColumnFill = new SolidColorBrush(Color.FromRgb(105, 105, 112));
    private static readonly IBrush SteelFill = new SolidColorBrush(Color.FromRgb(70, 92, 120));
    private static readonly IPen BeamPen = new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 100)), 1.2, new DashStyle([8, 4], 0));
    private static readonly IPen LinkPen = new Pen(new SolidColorBrush(Color.FromArgb(170, 150, 60, 200)), 1.2, new DashStyle([2, 3], 0));
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(200, 60, 30));
    private static readonly IBrush LitFill = new SolidColorBrush(Color.FromRgb(255, 214, 60));

    private void DrawStructure(DrawingContext ctx)
    {
        foreach (var b in _doc.Beams.Where(b => b.LevelId == LevelId))
        {
            bool sel = b.Id == SelectedId;
            var o = b.Outline;
            ctx.DrawGeometry(sel ? SelBrush : new SolidColorBrush(Color.FromArgb(30, 90, 90, 100)), sel ? SelPen : BeamPen, Poly(o));
            var mid = (b.Start + b.End) * 0.5;
            if (_scale > 12) DrawLabel(ctx, $"{b.Id} {b.Width * 100:0}×{b.Depth * 100:0}", ToScreen(mid) + new Point(4, -14), 9, Brushes.DimGray, center: false);
        }
        foreach (var c in _doc.Columns.Where(c => c.LevelId == LevelId))
        {
            bool sel = c.Id == SelectedId;
            ctx.DrawGeometry(sel ? Brushes.DarkOrange : c.Material == "acero" ? SteelFill : ColumnFill, null, Poly(c.Outline));
            if (c.Material == "acero")
            {
                // Perfil I dibujado encima.
                var o = c.Outline;
                ctx.DrawLine(new Pen(Brushes.White, 1), ToScreen((o[0] + o[3]) * 0.5), ToScreen((o[1] + o[2]) * 0.5));
            }
        }
    }

    private IPen ConduitPen(Route r)
    {
        var spec = _doc.Electrical.ConduitOf(r.Id);
        uint c = ElectricalTables.ConduitColor(spec.Type);
        var brush = new SolidColorBrush(Color.FromUInt32(c));
        double w = spec.Type == ConduitType.Charola ? 5 : Math.Clamp(spec.DiameterMm / 8.0, 1.5, 4);
        var dash = RouteBuilder.GuessStyle(r) == RouteStyle.Floor ? new DashStyle([2, 3], 0) : new DashStyle([6, 3], 0);
        return new Pen(brush, w, dash);
    }

    /// <summary>Líneas punteadas del apagador a las luminarias que manda (con la herramienta de enlazar o al seleccionar uno).</summary>
    private void DrawSwitchLinks(DrawingContext ctx)
    {
        string? focus = Tool == PlanTool.Link ? _linkSwitch : SelectedId;
        bool all = Tool == PlanTool.Link && _linkSwitch is null;
        if (focus is null && !all) return;
        var fixtures = _doc.Fixtures.Where(f => f.LevelId == LevelId).ToDictionary(f => f.Id);
        foreach (var d in _doc.Electrical.Fixtures)
        {
            if (d.ControlledBy is not { Count: > 0 } || !fixtures.TryGetValue(d.FixtureId, out var lamp)) continue;
            foreach (var k in d.ControlledBy)
            {
                var sid = ElectricalInstallation.SwitchBase(k);
                if (!all && sid != focus && d.FixtureId != focus) continue;
                if (!fixtures.TryGetValue(sid, out var sw)) continue;
                ctx.DrawLine(LinkPen, ToScreen(sw.Position.Xy), ToScreen(lamp.Position.Xy));
                if (ElectricalInstallation.SwitchKey(k) > 1)
                    DrawLabel(ctx, "2", ToScreen((sw.Position.Xy + lamp.Position.Xy) * 0.5), 10, LinkPen.Brush!, center: true);
            }
        }
    }

    /// <summary>Halo de luz de las luminarias encendidas durante la simulación.</summary>
    private void DrawLightPools(DrawingContext ctx)
    {
        var sim = Simulation!;
        foreach (var f in _doc.Fixtures.Where(f => f.LevelId == LevelId && FixtureCatalog.RoleOf(f) == ElecRole.Light))
        {
            double b = Math.Clamp(sim.Brightness(f.Id), 0, 1.2);
            if (b < 0.02) continue;
            double watts = Math.Max(5, LoadSchedule.PowerOf(_doc, f));
            double r = Math.Clamp(Math.Sqrt(watts) * 0.45, 1.0, 6) * _scale;
            var c = ToScreen(f.Position.Xy);
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(150 * Math.Min(1, b)), 255, 220, 90), 0),
                    new GradientStop(Color.FromArgb(0, 255, 220, 90), 1),
                },
            };
            // La luz no atraviesa los muros: se recorta a la habitación de la luminaria.
            var room = _doc.SpaceAt(LevelId, f.Position.Xy);
            if (room is not null)
                using (ctx.PushGeometryClip(Poly(room.Outline)))
                    ctx.DrawEllipse(brush, null, c, r, r);
            else ctx.DrawEllipse(brush, null, c, r, r);
        }
    }

    private void DrawFixture(DrawingContext ctx, PlacedFixture f)
    {
        var p = ToScreen(f.Position.Xy);
        var t = FixtureCatalog.ByKind(f.Kind);
        bool sel = f.Id == SelectedId || f.Id == _routeFrom || f.Id == _linkSwitch;
        var role = FixtureCatalog.RoleOf(f);
        double r = Math.Clamp(0.12 * _scale, 6, 14);
        var pen = sel ? SelPen : new Pen(Ink, 1.6);
        if (f.ModuleId == "hidraulica" || t is null)
        {
            IBrush fill = Brushes.DodgerBlue;
            if (f.Kind == "tanque")
            {
                r = Math.Max(r, f.Width / 2 * _scale);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(80, 30, 144, 255)), sel ? SelPen : new Pen(fill, 2), p, r, r);
            }
            else ctx.DrawEllipse(Brushes.White, sel ? SelPen : new Pen(fill, 2), p, r, r);
            DrawLabel(ctx, t?.Icon ?? "•", p, r * 1.1, fill, center: true);
            DrawLabel(ctx, f.ComponentId, new Point(p.X + r + 2, p.Y - r - 2), 10, fill, center: false);
            return;
        }

        var sim = Simulation;
        // Hacia dentro del recinto (normal de la cara) para que los símbolos de pared no queden sobre el muro.
        var inward = new Point(Math.Cos(f.Rotation), -Math.Sin(f.Rotation));
        if (f.HostId is not null && t.Mount == MountKind.Wall) p += inward * (r * 0.9);
        string label = f.ComponentId;

        switch (role)
        {
            case ElecRole.Light:
            {
                double b = sim?.Brightness(f.Id) ?? 0;
                IBrush fill = b > 0.02 ? LitFill : Brushes.White;
                if (f.Kind == FixtureCatalog.Lineal.Kind || f.Kind == FixtureCatalog.PanelLed.Kind)
                {
                    double w = Math.Max(f.Width * _scale, r * 2), h = f.Kind == FixtureCatalog.Lineal.Kind ? Math.Max(0.12 * _scale, 6) : w;
                    var rect = new Rect(p.X - w / 2, p.Y - h / 2, w, h);
                    using (ctx.PushTransform(Matrix.CreateTranslation(-p.X, -p.Y) * Matrix.CreateRotation(-f.Rotation) * Matrix.CreateTranslation(p.X, p.Y)))
                    {
                        ctx.DrawRectangle(fill, pen, rect);
                        ctx.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                        ctx.DrawLine(pen, rect.TopRight, rect.BottomLeft);
                    }
                }
                else
                {
                    double rr = f.Kind == FixtureCatalog.Campana.Kind || f.Kind == FixtureCatalog.Ventilador.Kind ? Math.Max(r, f.Width / 2 * _scale * 0.6) : r;
                    ctx.DrawEllipse(fill, pen, p, rr, rr);
                    double k = rr * 0.7071;
                    ctx.DrawLine(pen, p + new Point(-k, -k), p + new Point(k, k));
                    ctx.DrawLine(pen, p + new Point(k, -k), p + new Point(-k, k));
                    if (f.Kind == FixtureCatalog.Emergencia.Kind) DrawLabel(ctx, "E", p + new Point(rr + 1, rr - 6), 9, Ink, center: false);
                    if (f.Kind == FixtureCatalog.Arbotante.Kind || f.Kind == FixtureCatalog.Reflector.Kind)
                        ctx.DrawLine(new Pen(Ink, 2.5), p - inward * rr + new Point(-inward.Y, inward.X) * rr, p - inward * rr - new Point(-inward.Y, inward.X) * rr);
                }
                if (sim is not null && sim.IsBurnt(f.Id)) label += " fundida";
                break;
            }
            case ElecRole.Outlet:
            {
                bool live = sim is not null && sim.VoltageAt(f.Id) > 60;
                IBrush fill = sim is null ? Brushes.White : live ? new SolidColorBrush(Color.FromRgb(200, 240, 200)) : new SolidColorBrush(Color.FromRgb(240, 200, 200));
                if (f.Kind == FixtureCatalog.ContactoTrifasico.Kind)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(p + new Point(0, -r), true);
                        c.LineTo(p + new Point(r * 0.95, r * 0.7));
                        c.LineTo(p + new Point(-r * 0.95, r * 0.7));
                        c.EndFigure(true);
                    }
                    ctx.DrawGeometry(fill, pen, g);
                }
                else ctx.DrawEllipse(fill, pen, p, r, r);
                // Dos rayas: contacto doble.
                var side = new Point(-inward.Y, inward.X);
                ctx.DrawLine(pen, p - inward * (r * 0.5) - side * r * 1.4, p - inward * (r * 0.5) + side * r * 1.4);
                if (f.Kind != FixtureCatalog.ContactoPiso.Kind) ctx.DrawLine(pen, p + inward * (r * 0.1) - side * r * 1.4, p + inward * (r * 0.1) + side * r * 1.4);
                string tag = f.Kind switch
                {
                    "contacto_gfci" => "GFCI",
                    "contacto_220" => "220",
                    "contacto_trifasico" => "3F",
                    "contacto_tierra_aislada" => "TA",
                    _ => "",
                };
                if (tag.Length > 0) DrawLabel(ctx, tag, p, Math.Max(7, r * 0.6), Ink, center: true);
                if (sim is not null && sim.OutletLoad(f.Id).Watts > 0) label += $" · {sim.OutletLoad(f.Id).Name} {sim.PowerAt(f.Id):0} W";
                break;
            }
            case ElecRole.Switch:
            {
                bool on = sim is not null && sim.SwitchPosition(f.Id);
                IBrush fill = on ? new SolidColorBrush(Color.FromRgb(190, 240, 190)) : Brushes.White;
                ctx.DrawEllipse(fill, pen, p, r * 0.85, r * 0.85);
                string txt = t.Switch switch
                {
                    SwitchKind.ThreeWay => "S3", SwitchKind.FourWay => "S4", SwitchKind.Double => "SS",
                    SwitchKind.Dimmer => "SD", SwitchKind.Motion => "SM", _ => "S",
                };
                DrawLabel(ctx, txt, p, Math.Max(7, r * 0.75), Ink, center: true);
                if (t.Switch == SwitchKind.Double && sim is not null && sim.SwitchPosition(f.Id, 2))
                    ctx.DrawEllipse(null, new Pen(Brushes.SeaGreen, 2), p, r * 1.1, r * 1.1);
                break;
            }
            case ElecRole.Box:
            {
                double h = Math.Max(r * 0.8, f.Width / 2 * _scale);
                if (f.Kind == FixtureCatalog.CajaOctagonal.Kind)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI / 8 + i * Math.PI / 4;
                            var q = p + new Point(Math.Cos(a) * h, Math.Sin(a) * h);
                            if (i == 0) c.BeginFigure(q, true); else c.LineTo(q);
                        }
                        c.EndFigure(true);
                    }
                    ctx.DrawGeometry(Brushes.White, pen, g);
                }
                else
                {
                    ctx.DrawRectangle(Brushes.White, pen, new Rect(p.X - h, p.Y - h, 2 * h, 2 * h));
                    if (f.Kind is "caja_paso" or "registro_piso") { ctx.DrawLine(pen, p + new Point(-h, -h), p + new Point(h, h)); ctx.DrawLine(pen, p + new Point(h, -h), p + new Point(-h, h)); }
                    if (f.Kind == "interruptor_seguridad") DrawLabel(ctx, "DS", p, Math.Max(7, h), Ink, center: true);
                    if (f.Kind == "condulet") DrawLabel(ctx, "C", p, Math.Max(7, h), Ink, center: true);
                }
                break;
            }
            case ElecRole.Panel:
            {
                double w = Math.Max(r * 2.2, f.Width * _scale), h = Math.Max(r * 0.9, 0.12 * _scale);
                using (ctx.PushTransform(Matrix.CreateTranslation(-p.X, -p.Y) * Matrix.CreateRotation(-f.Rotation + Math.PI / 2) * Matrix.CreateTranslation(p.X, p.Y)))
                {
                    var rect = new Rect(p.X - w / 2, p.Y - h / 2, w, h);
                    ctx.DrawRectangle(Brushes.White, pen, rect);
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(rect.TopLeft, true); c.LineTo(rect.TopRight); c.LineTo(rect.BottomLeft); c.EndFigure(true);
                    }
                    ctx.DrawGeometry(Ink, null, g);
                }
                break;
            }
            case ElecRole.Meter:
                ctx.DrawEllipse(Brushes.White, pen, p, r, r);
                DrawLabel(ctx, "M", p, r, Ink, center: true);
                break;
            case ElecRole.Load:
            {
                double h = Math.Max(r, f.Width / 2 * _scale * 0.7);
                bool on = sim is not null && sim.LoadOn(f.Id) && sim.VoltageAt(f.Id) > 60;
                ctx.DrawRectangle(on ? new SolidColorBrush(Color.FromRgb(200, 240, 200)) : Brushes.White, pen, new Rect(p.X - h, p.Y - h, 2 * h, 2 * h));
                DrawLabel(ctx, t.Icon, p, Math.Max(8, h), Ink, center: true);
                if (sim is not null) label += sim.LoadOn(f.Id) ? $" · {sim.PowerAt(f.Id):0} W" : " · apagado";
                break;
            }
        }
        if (sim is not null && !sim.IsSimulated(f.Id) && role is ElecRole.Light or ElecRole.Outlet or ElecRole.Load) label += " (sin circuito)";
        if (_scale > 14 || sel || sim is not null)
            DrawLabel(ctx, label, new Point(p.X + r + 2, p.Y - r - 2), 10, Ink, center: false);
    }

    private void DrawElectricalPreview(DrawingContext ctx, Point2 c, Point cs)
    {
        switch (Tool)
        {
            case PlanTool.Fixture:
            {
                var (ghost, _) = FixturePlacer.Locate(_doc, FixtureType, LevelId, c, grid: GridStep);
                if (ghost is null) return;
                using var _ = ctx.PushOpacity(0.5);
                DrawFixture(ctx, ghost);
                var lvl = _doc.GetLevel(LevelId);
                DrawLabel(ctx, $"h = {ghost.Position.Z - lvl.Elevation:0.00} m{(ghost.HostId is { } h ? " en " + h : "")}", ToScreen(ghost.Position.Xy) + new Point(12, 6), 11, Brushes.DarkOrange, center: false);
                break;
            }
            case PlanTool.Column:
                ctx.DrawGeometry(SelBrush, SelPen, Poly(new Column("_", LevelId, c, ColumnWidth, ColumnDepth).Outline));
                break;
            case PlanTool.Beam when _drawStart is { } s:
            {
                var end = SnapToColumn(c) ?? c;
                var b = new Beam("_", LevelId, s, end, BeamWidth, BeamDepth);
                if (b.Length > 1e-6) ctx.DrawGeometry(SelBrush, SelPen, Poly(b.Outline));
                DrawLabel(ctx, $"{b.Length:0.00} m", ToScreen((s + end) * 0.5), 12, Brushes.DarkOrange, center: true);
                break;
            }
            case PlanTool.Link when _linkSwitch is not null:
            {
                var sw = _doc.Fixtures.FirstOrDefault(x => x.Id == _linkSwitch);
                if (sw is not null) ctx.DrawLine(LinkPen, ToScreen(sw.Position.Xy), cs);
                break;
            }
        }
    }

    /// <summary>Elemento colocado bajo el cursor (el más cercano dentro de su símbolo).</summary>
    private string? HitFixture(Point2 p)
    {
        double tol = 8 / _scale;
        return _doc.Fixtures.Where(f => f.LevelId == LevelId)
            .Select(f => (f, d: f.Position.Xy.DistanceTo(p)))
            .Where(x => x.d < Math.Max(tol * 1.6, Math.Min(0.6, x.f.Width / 2)))
            .OrderBy(x => x.d).Select(x => x.f.Id).FirstOrDefault();
    }

    private static string MountHelp(FixtureType t) => t.Mount switch
    {
        MountKind.Wall => $"Junto a un muro o una columna, a {t.DefaultHeight:0.00} m del piso.",
        MountKind.Ceiling => "En el techo; sobre una viga queda debajo de ella.",
        MountKind.Hung => $"Colgada {t.DefaultHeight:0.00} m bajo la viga más cercana o del techo.",
        MountKind.Floor => "En el piso.",
        _ => "",
    };

    private void PlaceColumn(Point2 p)
    {
        _doc.Edit(ed => SelectedId = ed.AddColumn(LevelId, p, ColumnWidth, ColumnDepth, 0, StructureMaterial).Id);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeamClick(Point2 p)
    {
        if (_drawStart is not { } s) { _drawStart = p; return; }
        if (s.DistanceTo(p) > 0.2)
            _doc.Edit(ed => SelectedId = ed.AddBeam(LevelId, s, p, BeamWidth, BeamDepth, StructureMaterial).Id);
        _drawStart = p; // encadenar vigas de columna en columna
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private Point2? SnapToColumn(Point2 p)
    {
        var c = FixturePlacer.NearestColumn(_doc, LevelId, p);
        return c is not null && c.Center.DistanceTo(p) < Math.Max(0.4, 10 / _scale) ? c.Center : null;
    }

    /// <summary>Herramienta de enlazar: primero el apagador, luego las luminarias que manda.</summary>
    private void LinkClick(Point2 raw, bool secondKey)
    {
        var id = HitFixture(raw);
        var f = id is null ? null : _doc.Fixtures.First(x => x.Id == id);
        if (f is null) { Status?.Invoke(this, "Haz clic sobre un apagador o una luminaria."); return; }
        var role = FixtureCatalog.RoleOf(f);
        if (role == ElecRole.Switch) { _linkSwitch = f.Id; Select(f.Id); return; }
        if (_linkSwitch is null || role != ElecRole.Light) { Status?.Invoke(this, "Primero haz clic en un apagador y luego en sus luminarias."); return; }
        var sw = _doc.Fixtures.First(x => x.Id == _linkSwitch);
        string key = secondKey && FixtureCatalog.ByKind(sw.Kind)?.Switch == SwitchKind.Double ? sw.Id + "#2" : sw.Id;
        _doc.Edit(ed =>
        {
            var data = ed.Electrical.Of(f.Id) ?? new FixtureElectrical(f.Id);
            var list = (data.ControlledBy ?? []).ToList();
            if (list.Remove(key)) { }
            else
            {
                // Un apagador sencillo sustituye al que hubiera; los de escalera y 4 vías se suman.
                var kind = FixtureCatalog.ByKind(sw.Kind)?.Switch;
                if (kind is not (SwitchKind.ThreeWay or SwitchKind.FourWay)) list.Clear();
                else list.RemoveAll(k => _doc.Fixtures.FirstOrDefault(x => x.Id == ElectricalInstallation.SwitchBase(k)) is { } o
                    && FixtureCatalog.ByKind(o.Kind)?.Switch is not (SwitchKind.ThreeWay or SwitchKind.FourWay));
                if (kind == SwitchKind.FourWay && list.Count >= 1) list.Insert(list.Count - (list.Count >= 2 ? 1 : 0), key);
                else list.Add(key);
                // Ordenar: escalera, 4 vías…, escalera.
                var three = list.Where(k => KindOf(k) == SwitchKind.ThreeWay).ToList();
                var four = list.Where(k => KindOf(k) == SwitchKind.FourWay).ToList();
                if (three.Count == 2) list = [three[0], .. four, three[1]];
            }
            ed.Electrical.Set(data with { ControlledBy = list });
            // Si la luminaria tiene circuito y el apagador no, se lo pone (y al revés).
            var lampC = ed.Electrical.Of(f.Id)?.CircuitId;
            var swData = ed.Electrical.Of(sw.Id) ?? new FixtureElectrical(sw.Id);
            if (swData.CircuitId is null && lampC is not null) ed.Electrical.Set(swData with { CircuitId = lampC });
            else if (lampC is null && swData.CircuitId is not null) ed.Electrical.Set(ed.Electrical.Of(f.Id)! with { CircuitId = swData.CircuitId });
        });
        Status?.Invoke(this, $"{f.ComponentId} se enciende con: {string.Join(", ", (_doc.Electrical.Of(f.Id)?.ControlledBy ?? []).Select(k => _doc.Fixtures.FirstOrDefault(x => x.Id == ElectricalInstallation.SwitchBase(k))?.ComponentId + (ElectricalInstallation.SwitchKey(k) > 1 ? " (tecla 2)" : "")))}");
    }

    private SwitchKind KindOf(string key) =>
        _doc.Fixtures.FirstOrDefault(x => x.Id == ElectricalInstallation.SwitchBase(key)) is { } f ? FixtureCatalog.ByKind(f.Kind)?.Switch ?? SwitchKind.None : SwitchKind.None;
}
