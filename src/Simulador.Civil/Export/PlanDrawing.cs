using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Export;

/// <summary>Capas de un plano exportado (se usan como capas DXF y para elegir color y grosor en PDF).</summary>
public static class PlanLayers
{
    public const string Walls = "MUROS";
    public const string Openings = "HUECOS";
    public const string Stairs = "ESCALERAS";
    public const string Rooms = "HABITACIONES";
    public const string Text = "TEXTOS";
    public const string Dimensions = "COTAS";
    public const string Electric = "ELECTRICIDAD";
    public const string Plumbing = "HIDRAULICA";
    public const string Frame = "CAJETIN";

    public static readonly string[] All = [Walls, Openings, Stairs, Rooms, Text, Dimensions, Electric, Plumbing, Frame];
}

public enum TextAlign { Left, Center, Right }

public abstract record PlanItem(string Layer);
/// <summary>Polígono cerrado; <paramref name="Filled"/> = relleno (muros) o solo contorno.</summary>
public sealed record PlanPolygon(string Layer, IReadOnlyList<Point2> Points, bool Filled) : PlanItem(Layer);
public sealed record PlanLine(string Layer, Point2 A, Point2 B, bool Dashed = false) : PlanItem(Layer);
/// <summary>Texto con la altura en metros de planta (a escala 1:100, 0,25 m = 2,5 mm en papel).</summary>
public sealed record PlanText(string Layer, Point2 At, string Text, double Height, TextAlign Align = TextAlign.Center, double Rotation = 0) : PlanItem(Layer);

/// <summary>Qué incluir en el plano.</summary>
public sealed record PlanOptions(bool Dimensions = true, bool RoomLabels = true, bool Installations = true, bool Stairs = true);

/// <summary>
/// Plano 2D de un nivel como lista de primitivas en metros (Y hacia arriba): muros con sus huecos, puertas con su
/// barrido, ventanas, escaleras, nombres y áreas de habitaciones, instalaciones y cotas exteriores. Lo usan la
/// exportación a DXF y a PDF, así que los dos salen iguales.
/// </summary>
public sealed class PlanDrawing
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public List<PlanItem> Items { get; } = [];

    public (Point2 Min, Point2 Max) Extents
    {
        get
        {
            var pts = Items.SelectMany(Points).ToList();
            if (pts.Count == 0) return (new(0, 0), new(1, 1));
            return (new(pts.Min(p => p.X), pts.Min(p => p.Y)), new(pts.Max(p => p.X), pts.Max(p => p.Y)));
        }
    }

    private static IEnumerable<Point2> Points(PlanItem i) => i switch
    {
        PlanPolygon p => p.Points,
        PlanLine l => [l.A, l.B],
        // El texto ocupa más o menos su altura × 0,6 por letra.
        PlanText t => [t.At + new Point2(-t.Text.Length * t.Height * 0.3, 0), t.At + new Point2(t.Text.Length * t.Height * 0.3, t.Height)],
        _ => [],
    };

    public static PlanDrawing Build(BuildingDocument doc, string levelId, PlanOptions? options = null)
    {
        options ??= new PlanOptions();
        var level = doc.GetLevel(levelId);
        var d = new PlanDrawing
        {
            Title = doc.Name,
            Subtitle = $"{level.Name} (cota {level.Elevation:+0.00;-0.00;0.00} m)",
        };
        var walls = doc.Walls.Where(w => w.LevelId == levelId).ToList();

        foreach (var w in walls)
        {
            foreach (var piece in WallPieces(doc, w)) d.Items.Add(new PlanPolygon(PlanLayers.Walls, piece, true));
            foreach (var o in doc.Openings.Where(o => o.HostWallId == w.Id)) AddOpening(d, w, o);
        }

        if (options.Stairs)
            foreach (var s in doc.Stairs.Where(s => s.LevelId == levelId)) AddStair(d, s);

        if (options.RoomLabels)
            foreach (var sp in doc.Spaces.Where(s => s.LevelId == levelId))
            {
                var c = Geo.Centroid(sp.Outline);
                d.Items.Add(new PlanText(PlanLayers.Rooms, c + new Point2(0, 0.05), sp.Name, 0.25));
                d.Items.Add(new PlanText(PlanLayers.Rooms, c - new Point2(0, 0.35), $"{sp.Area:0.00} m²", 0.18));
            }

        if (options.Installations)
        {
            foreach (var f in doc.Fixtures.Where(f => f.LevelId == levelId)) AddFixture(d, f);
            double z0 = level.Elevation, z1 = level.Elevation + level.Height;
            foreach (var r in doc.Routes)
            foreach (var s in r.Segments)
            {
                if (s.Start.Z < z0 - 1e-6 || s.Start.Z > z1 + 1e-6) continue;
                if (s.Start.Xy.DistanceTo(s.End.Xy) < 1e-6) continue; // tramos verticales: no se ven en planta
                d.Items.Add(new PlanLine(r.ModuleId == "hidraulica" ? PlanLayers.Plumbing : PlanLayers.Electric, s.Start.Xy, s.End.Xy, Dashed: true));
            }
        }

        if (options.Dimensions && walls.Count > 0) AddDimensions(d, doc, levelId, walls);
        return d;
    }

    // ------------------------------------------------------------------ muros y huecos

    /// <summary>Tramos macizos del muro (sin los huecos), alargados medio espesor en los extremos para cerrar esquinas.</summary>
    public static IEnumerable<Point2[]> WallPieces(BuildingDocument doc, Wall w)
    {
        var gaps = doc.Openings.Where(o => o.HostWallId == w.Id)
            .Select(o => (Lo: Math.Max(0, o.Offset - o.Width / 2), Hi: Math.Min(w.Length, o.Offset + o.Width / 2)))
            .OrderBy(g => g.Lo).ToList();
        double pos = -w.Thickness / 2;
        foreach (var (lo, hi) in gaps)
        {
            if (lo > pos + 1e-6) yield return Quad(w, pos, lo);
            pos = Math.Max(pos, hi);
        }
        if (w.Length + w.Thickness / 2 > pos + 1e-6) yield return Quad(w, pos, w.Length + w.Thickness / 2);
    }

    private static Point2[] Quad(Wall w, double from, double to)
    {
        var dir = w.Direction;
        var n = dir.Perp() * (w.Thickness / 2);
        var a = w.Start + dir * from;
        var b = w.Start + dir * to;
        return [a - n, b - n, b + n, a + n];
    }

    private static void AddOpening(PlanDrawing d, Wall w, Opening o)
    {
        var dir = w.Direction;
        var n = dir.Perp();
        var a = w.Start + dir * (o.Offset - o.Width / 2);
        var b = w.Start + dir * (o.Offset + o.Width / 2);
        // Jambas: cierran el muro a los lados del hueco.
        d.Items.Add(new PlanLine(PlanLayers.Openings, a - n * (w.Thickness / 2), a + n * (w.Thickness / 2)));
        d.Items.Add(new PlanLine(PlanLayers.Openings, b - n * (w.Thickness / 2), b + n * (w.Thickness / 2)));
        if (o.Kind == OpeningKind.Window)
        {
            foreach (var k in new[] { -0.5, 0, 0.5 })
                d.Items.Add(new PlanLine(PlanLayers.Openings, a + n * (w.Thickness * k), b + n * (w.Thickness * k)));
            return;
        }
        var hinge = o.HingeAtStart ? a : b;
        var closed = o.HingeAtStart ? b : a;
        var side = n * (o.SwingLeft ? 1 : -1);
        var face = side * (w.Thickness / 2);
        var open = hinge + face + side * o.Width;
        d.Items.Add(new PlanLine(PlanLayers.Openings, hinge + face, open));
        // Barrido de la hoja: cuarto de círculo de la hoja abierta a la cerrada.
        double a0 = Math.Atan2(open.Y - (hinge + face).Y, open.X - (hinge + face).X);
        double a1 = Math.Atan2(closed.Y - hinge.Y, closed.X - hinge.X);
        double delta = Math.IEEERemainder(a1 - a0, 2 * Math.PI);
        const int n0 = 12;
        var c = hinge + face;
        for (int i = 0; i < n0; i++)
        {
            double t0 = a0 + delta * i / n0, t1 = a0 + delta * (i + 1) / n0;
            d.Items.Add(new PlanLine(PlanLayers.Openings,
                c + new Point2(Math.Cos(t0), Math.Sin(t0)) * o.Width, c + new Point2(Math.Cos(t1), Math.Sin(t1)) * o.Width, Dashed: true));
        }
    }

    // ------------------------------------------------------------------ escaleras e instalaciones

    private static void AddStair(PlanDrawing d, Stair s)
    {
        foreach (var part in s.Parts) d.Items.Add(new PlanPolygon(PlanLayers.Stairs, part, false));
        var steps = s.Steps;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].IsLanding || i == 0 || steps[i - 1].IsLanding) continue;
            var o = steps[i].Outline;
            d.Items.Add(new PlanLine(PlanLayers.Stairs, o[0], o[3]));
        }
        // Línea de huella con flecha.
        var path = new List<Point2> { s.Start };
        path.AddRange(steps.Select(st => new Point2(st.Outline.Average(p => p.X), st.Outline.Average(p => p.Y))));
        path.Add(s.Arrival);
        for (int i = 0; i + 1 < path.Count; i++) d.Items.Add(new PlanLine(PlanLayers.Stairs, path[i], path[i + 1]));
        var end = path[^1];
        var dir = end - path[^2];
        if (dir.Length > 1e-9)
        {
            dir = dir * (1 / dir.Length);
            d.Items.Add(new PlanLine(PlanLayers.Stairs, end, end - dir * 0.25 + dir.Perp() * 0.12));
            d.Items.Add(new PlanLine(PlanLayers.Stairs, end, end - dir * 0.25 - dir.Perp() * 0.12));
        }
        d.Items.Add(new PlanText(PlanLayers.Stairs, s.Start - s.Direction * 0.3, "SUBE", 0.15));
    }

    private static void AddFixture(PlanDrawing d, PlacedFixture f)
    {
        string layer = f.ModuleId == "hidraulica" ? PlanLayers.Plumbing : PlanLayers.Electric;
        var p = f.Position.Xy;
        double r = f.Kind == "tanque" ? Math.Max(0.3, f.Width / 2) : 0.12;
        // Círculo de 12 lados con la designación al lado.
        const int n = 12;
        var pts = Enumerable.Range(0, n).Select(i => p + new Point2(Math.Cos(2 * Math.PI * i / n), Math.Sin(2 * Math.PI * i / n)) * r).ToArray();
        d.Items.Add(new PlanPolygon(layer, pts, false));
        d.Items.Add(new PlanText(layer, p + new Point2(r + 0.05, -0.07), f.ComponentId, 0.15, TextAlign.Left));
    }

    // ------------------------------------------------------------------ cotas

    /// <summary>Distancia a la que van las cotas por fuera del edificio.</summary>
    public const double DimOffset = 0.8;

    private static void AddDimensions(PlanDrawing d, BuildingDocument doc, string levelId, List<Wall> walls)
    {
        var spaces = doc.Spaces.Where(s => s.LevelId == levelId).ToList();
        // Cotas totales: ancho abajo y fondo a la izquierda, sobre la caja exterior de los muros.
        var pieces = walls.SelectMany(w => WallPieces(doc, w)).SelectMany(p => p).ToList();
        double x0 = pieces.Min(p => p.X), x1 = pieces.Max(p => p.X), y0 = pieces.Min(p => p.Y), y1 = pieces.Max(p => p.Y);
        Dimension(d, new(x0, y0), new(x1, y0), new Point2(0, -1), DimOffset * 1.8);
        Dimension(d, new(x0, y0), new(x0, y1), new Point2(-1, 0), DimOffset * 1.8);

        // Cada muro de fachada: su longitud por la cara exterior (el lado que no da a ninguna habitación).
        foreach (var w in walls)
        {
            var n = w.Direction.Perp();
            var mid = (w.Start + w.End) * 0.5;
            bool leftInside = spaces.Any(s => Geo.Contains(s.Outline, mid + n * (w.Thickness / 2 + 0.2)));
            bool rightInside = spaces.Any(s => Geo.Contains(s.Outline, mid - n * (w.Thickness / 2 + 0.2)));
            if (leftInside == rightInside) continue; // interior (o suelto)
            var outward = leftInside ? n * -1 : n;
            var a = w.Start - w.Direction * (w.Thickness / 2) + outward * (w.Thickness / 2);
            var b = w.End + w.Direction * (w.Thickness / 2) + outward * (w.Thickness / 2);
            // Abajo y a la izquierda ya está la cota total: no se repite si el muro ocupa todo el lado.
            double len = a.DistanceTo(b);
            if (outward.Y < -0.99 && Math.Abs(len - (x1 - x0)) < 0.01) continue;
            if (outward.X < -0.99 && Math.Abs(len - (y1 - y0)) < 0.01) continue;
            Dimension(d, a, b, outward, DimOffset);
        }
    }

    /// <summary>Cota de a a b, desplazada <paramref name="offset"/> metros hacia <paramref name="outward"/>.</summary>
    public static void Dimension(PlanDrawing d, Point2 a, Point2 b, Point2 outward, double offset)
    {
        double len = a.DistanceTo(b);
        if (len < 0.05) return;
        var dir = (b - a) * (1 / len);
        var off = outward * offset;
        var da = a + off;
        var db = b + off;
        const string L = PlanLayers.Dimensions;
        d.Items.Add(new PlanLine(L, a + outward * 0.1, da + outward * 0.1)); // líneas de referencia
        d.Items.Add(new PlanLine(L, b + outward * 0.1, db + outward * 0.1));
        d.Items.Add(new PlanLine(L, da, db));
        // Marcas oblicuas a 45° en los extremos.
        var tick = (dir + outward) * (0.08 / Math.Sqrt(2));
        d.Items.Add(new PlanLine(L, da - tick, da + tick));
        d.Items.Add(new PlanLine(L, db - tick, db + tick));
        double angle = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI;
        // Texto legible: nunca boca abajo.
        if (angle > 90.01 || angle < -89.99) angle += angle > 0 ? -180 : 180;
        // El texto va del lado de fuera: si su "arriba" mira hacia el edificio, la línea base se aleja una altura.
        const double h = 0.18;
        double rad = angle * Math.PI / 180;
        var up = new Point2(-Math.Sin(rad), Math.Cos(rad));
        var textAt = (da + db) * 0.5 + outward * (Point2.Dot(outward, up) > 0 ? 0.05 : 0.05 + h);
        d.Items.Add(new PlanText(L, textAt, $"{len:0.00}", h, TextAlign.Center, angle));
    }
}
