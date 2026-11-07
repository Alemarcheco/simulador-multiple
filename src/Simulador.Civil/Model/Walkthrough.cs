using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Persona que recorre el edificio en primera persona: no atraviesa muros ni ventanas, pasa por las puertas, sube y
/// baja escaleras peldaño a peldaño y cambia de planta al llegar arriba o abajo. Todo en metros; el rumbo
/// (<see cref="Heading"/>) va en radianes, antihorario desde +X, como los ángulos de la planta.
/// </summary>
public sealed class Walker
{
    public const double EyeHeight = 1.60;
    /// <summary>Radio del cuerpo: distancia mínima a la cara de un muro.</summary>
    public const double BodyRadius = 0.25;
    /// <summary>Lo más alto que se sube de un paso (una contrahuella cómoda cabe de sobra; un murete no).</summary>
    public const double MaxStepUp = 0.45;

    private readonly BuildingDocument _doc;

    public Walker(BuildingDocument doc, Point2 position, double heading)
    {
        _doc = doc;
        Position = position;
        Heading = heading;
        FootZ = doc.Levels.Count == 0 ? 0 : doc.Levels.Min(l => l.Elevation);
        FootZ = FloorAt(position, FootZ + MaxStepUp);
    }

    public Point2 Position { get; private set; }
    /// <summary>Altura de los pies (suelo o peldaño en que se está).</summary>
    public double FootZ { get; private set; }
    public double Heading { get; set; }
    /// <summary>Mirada arriba (+) o abajo (−), radianes.</summary>
    public double Pitch { get; set; }

    public double EyeZ => FootZ + EyeHeight;

    /// <summary>Planta en la que se está (la más alta cuyo suelo queda a la altura de los pies o por debajo).</summary>
    public Level? Level => _doc.Levels.Where(l => l.Elevation <= FootZ + 0.05).MaxBy(l => l.Elevation)
                           ?? _doc.Levels.MinBy(l => l.Elevation);

    /// <summary>Dirección de la mirada (vector unitario 3D: x, y, z).</summary>
    public (double X, double Y, double Z) LookDirection =>
        (Math.Cos(Pitch) * Math.Cos(Heading), Math.Cos(Pitch) * Math.Sin(Heading), Math.Sin(Pitch));

    public void Turn(double dHeading, double dPitch)
    {
        Heading = (Heading + dHeading) % (2 * Math.PI);
        Pitch = Math.Clamp(Pitch + dPitch, -1.4, 1.4);
    }

    /// <summary>
    /// Avanza <paramref name="forward"/> metros hacia donde mira y <paramref name="strafe"/> hacia la derecha.
    /// Los pasos largos se trocean para no saltarse un muro fino.
    /// </summary>
    public void Move(double forward, double strafe)
    {
        var dir = new Point2(Math.Cos(Heading), Math.Sin(Heading));
        var right = new Point2(dir.Y, -dir.X);
        var delta = dir * forward + right * strafe;
        int n = Math.Max(1, (int)Math.Ceiling(delta.Length / 0.05));
        for (int i = 0; i < n; i++) Step(delta * (1.0 / n));
    }

    private void Step(Point2 delta)
    {
        var target = Position + delta;
        // Se miran los muros de la planta en la que se está y, si se va a subir a la de arriba, también esos.
        var p = ResolveWalls(target, FootZ);
        double floor = FloorAt(p, FootZ + MaxStepUp);
        if (floor > FootZ + 0.05) p = ResolveWalls(p, floor);
        Position = p;
        FootZ = FloorAt(p, FootZ + MaxStepUp);
    }

    // ------------------------------------------------------------------ muros

    /// <summary>Tramos macizos de los muros de la planta a la altura <paramref name="footZ"/>: el muro sin sus puertas.</summary>
    public IEnumerable<(Point2 A, Point2 B, double Radius)> SolidSpans(double footZ)
    {
        var lvl = _doc.Levels.Where(l => l.Elevation <= footZ + 0.05).MaxBy(l => l.Elevation);
        if (lvl is null) yield break;
        foreach (var w in _doc.Walls.Where(w => w.LevelId == lvl.Id))
        {
            if (w.Length < 1e-6) continue;
            var d = w.Direction;
            double pos = -w.Thickness / 2;
            foreach (var o in _doc.Openings.Where(o => o.HostWallId == w.Id && o.Kind == OpeningKind.Door && o.SillHeight < 0.05)
                         .OrderBy(o => o.Offset))
            {
                double lo = o.Offset - o.Width / 2, hi = o.Offset + o.Width / 2;
                if (lo > pos) yield return (w.Start + d * pos, w.Start + d * lo, w.Thickness / 2);
                pos = Math.Max(pos, hi);
            }
            double end = w.Length + w.Thickness / 2;
            if (end > pos) yield return (w.Start + d * pos, w.Start + d * end, w.Thickness / 2);
        }
    }

    /// <summary>Empuja el punto fuera de los muros (deslizando a lo largo de ellos).</summary>
    private Point2 ResolveWalls(Point2 p, double footZ)
    {
        var spans = SolidSpans(footZ).ToList();
        for (int iter = 0; iter < 4; iter++)
        {
            bool moved = false;
            foreach (var (a, b, r) in spans)
            {
                // Las puntas de un tramo junto a una puerta son planas en la realidad: se tratan como cápsula,
                // que al pasar por la puerta solo roza las esquinas.
                var ab = b - a;
                double len2 = Point2.Dot(ab, ab);
                double t = len2 < 1e-12 ? 0 : Math.Clamp(Point2.Dot(p - a, ab) / len2, 0, 1);
                var q = a + ab * t;
                var away = p - q;
                double dist = away.Length;
                double min = r + BodyRadius;
                if (dist >= min) continue;
                var n = dist < 1e-9 ? ab.Perp().Normalized() : away * (1 / dist);
                p = q + n * min;
                moved = true;
            }
            if (!moved) break;
        }
        return p;
    }

    // ------------------------------------------------------------------ suelos y escaleras

    /// <summary>
    /// Altura del suelo en <paramref name="p"/>: la más alta de las superficies pisables (terreno, forjado de cada
    /// planta salvo el hueco de la escalera que llega a ella, peldaños) que no pase de <paramref name="maxZ"/>.
    /// </summary>
    public double FloorAt(Point2 p, double maxZ)
    {
        var levels = _doc.Levels.OrderBy(l => l.Elevation).ToList();
        if (levels.Count == 0) return 0;
        double best = levels[0].Elevation; // terreno / planta baja, en todas partes
        void Consider(double z) { if (z <= maxZ + 1e-9 && z > best) best = z; }

        foreach (var lvl in levels.Skip(1))
        {
            if (lvl.Elevation > maxZ + 1e-9) break;
            if (HasFloor(lvl, p) && !InStairWell(lvl, p)) Consider(lvl.Elevation);
        }
        foreach (var st in _doc.Stairs)
        {
            double z0 = _doc.GetLevel(st.LevelId).Elevation;
            foreach (var step in st.Steps)
                if (Geo.Contains(step.Outline, p)) Consider(z0 + step.Top);
        }
        return best;
    }

    /// <summary>¿Hay forjado de esa planta en el punto? Se usan sus losas de suelo o, si no tiene, sus habitaciones.</summary>
    private bool HasFloor(Level lvl, Point2 p)
    {
        var slabs = _doc.Slabs.Where(s => s.LevelId == lvl.Id && s.Kind == SlabKind.Floor).ToList();
        if (slabs.Count > 0) return slabs.Any(s => Geo.Contains(s.Outline, p));
        var spaces = _doc.Spaces.Where(s => s.LevelId == lvl.Id).ToList();
        return spaces.Count == 0 || spaces.Any(s => Geo.Contains(s.Outline, p));
    }

    /// <summary>¿Cae el punto en el hueco que deja en este forjado una escalera que sube desde la planta de abajo?</summary>
    private bool InStairWell(Level lvl, Point2 p) =>
        Rendering.BuildingMesh.StairWells(_doc, lvl).Any(part => Geo.Contains(part, p));

    // ------------------------------------------------------------------ inicio

    /// <summary>
    /// Empieza fuera del edificio, dos metros delante de la puerta de entrada de la planta más baja y mirando hacia
    /// ella. Si no hay puerta de fachada, en el centro de la habitación más grande.
    /// </summary>
    public static Walker AtEntrance(BuildingDocument doc)
    {
        var lvl = doc.Levels.MinBy(l => l.Elevation);
        if (lvl is not null)
        {
            var spaces = doc.Spaces.Where(s => s.LevelId == lvl.Id).ToList();
            var doors = from o in doc.Openings
                        where o.Kind == OpeningKind.Door
                        let w = doc.Walls.FirstOrDefault(w => w.Id == o.HostWallId && w.LevelId == lvl.Id)
                        where w is not null
                        select (o, w);
            foreach (var (o, w) in doors.OrderByDescending(x => x.o.Width))
            {
                var c = w.Start + w.Direction * o.Offset;
                var n = w.Direction.Perp();
                double probe = w.Thickness / 2 + 0.3;
                bool left = spaces.Any(s => Geo.Contains(s.Outline, c + n * probe));
                bool right = spaces.Any(s => Geo.Contains(s.Outline, c - n * probe));
                if (left == right) continue;
                var outward = left ? n * -1 : n;
                var start = c + outward * 2.0;
                return new Walker(doc, start, Math.Atan2(-outward.Y, -outward.X));
            }
            if (spaces.MaxBy(s => s.Area) is { } big) return new Walker(doc, Geo.Centroid(big.Outline), 0);
        }
        return new Walker(doc, new Point2(0, 0), 0);
    }
}
