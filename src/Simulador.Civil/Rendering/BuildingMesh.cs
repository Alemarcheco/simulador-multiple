using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Rendering;

/// <summary>Polígono plano y convexo con su color (0xAARRGGBB).</summary>
public readonly record struct MeshFace(Point3[] Pts, uint Argb);

/// <summary>Segmento 3D (rutas de instalaciones) con su color.</summary>
public readonly record struct MeshLine(Point3 A, Point3 B, uint Argb);

/// <summary>Malla del edificio: caras de muros (con sus huecos), losas, cubierta, escaleras e instalaciones.</summary>
public sealed class BuildingMesh
{
    public const uint WallColor = 0xFFE8E0D0;
    public const uint FloorColor = 0xFFBEBEB9;
    public const uint RoofColor = 0xFFC46E50;
    public const uint StairColor = 0xFFAA825A;
    public const uint ElecColor = 0xFFE65028;
    public const uint HydroColor = 0xFF288CE6;
    public const uint ColumnColor = 0xFFB4B4B0;
    public const uint SteelColor = 0xFF5F7890;
    public const uint FixtureColor = 0xFFF4F2EC;
    public const uint LitColor = 0xFFFFE070;

    public List<MeshFace> Faces { get; } = [];
    public List<MeshLine> Lines { get; } = [];

    /// <summary>Esquinas de la caja que envuelve todas las caras (para encuadrar sin que cambie al girar).</summary>
    public (Point3 Min, Point3 Max) Bounds
    {
        get
        {
            var pts = Faces.SelectMany(f => f.Pts).Concat(Lines.SelectMany(l => new[] { l.A, l.B })).ToList();
            if (pts.Count == 0) return (new(0, 0, 0), new(1, 1, 1));
            return (new(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                    new(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)));
        }
    }

    /// <param name="upToLevelId">Muestra los niveles hasta este (incluido); null = todos.</param>
    /// <param name="showRoof">Dibuja la cubierta del último nivel visible si es el último del edificio.</param>
    /// <param name="glow">Brillo de cada luminaria (0..1) durante la simulación; null = sin simular.</param>
    public static BuildingMesh Build(BuildingDocument doc, string? upToLevelId = null, bool showRoof = true, Func<string, double>? glow = null)
    {
        var m = new BuildingMesh();
        var levels = doc.Levels.OrderBy(l => l.Elevation).ToList();
        if (upToLevelId is not null && levels.FirstOrDefault(l => l.Id == upToLevelId) is { } cut)
            levels = levels.Where(l => l.Elevation <= cut.Elevation + 1e-6).ToList();
        var shown = levels.Select(l => l.Id).ToHashSet();
        var topShown = levels.LastOrDefault();
        var topAll = doc.Levels.MaxBy(l => l.Elevation);

        foreach (var w in doc.Walls.Where(w => shown.Contains(w.LevelId)))
        {
            double z0 = doc.GetLevel(w.LevelId).Elevation;
            double pos = -w.Thickness / 2;
            foreach (var o in doc.Openings.Where(o => o.HostWallId == w.Id).OrderBy(o => o.Offset))
            {
                double lo = Math.Max(0, o.Offset - o.Width / 2), hi = Math.Min(w.Length, o.Offset + o.Width / 2);
                if (lo > pos) m.AddBox(WallQuad(w, pos, lo), z0, z0 + w.Height, WallColor);
                if (o.SillHeight > 0) m.AddBox(WallQuad(w, lo, hi), z0, z0 + o.SillHeight, WallColor);
                if (o.SillHeight + o.Height < w.Height) m.AddBox(WallQuad(w, lo, hi), z0 + o.SillHeight + o.Height, z0 + w.Height, WallColor);
                pos = Math.Max(pos, hi);
            }
            m.AddBox(WallQuad(w, pos, w.Length + w.Thickness / 2), z0, z0 + w.Height, WallColor);
        }

        foreach (var s in doc.Slabs.Where(s => shown.Contains(s.LevelId)))
        {
            var lvl = doc.GetLevel(s.LevelId);
            if (s.Kind == SlabKind.Floor)
                foreach (var piece in StairWellCut(doc, lvl, s.Outline))
                    m.AddBox(piece, lvl.Elevation - s.Thickness, lvl.Elevation, FloorColor);
            else if (s.Kind == SlabKind.Roof && showRoof && lvl == topShown && lvl == topAll)
                m.AddBox(s.Outline, lvl.Elevation + lvl.Height - s.Thickness, lvl.Elevation + lvl.Height, RoofColor);
        }

        foreach (var st in doc.Stairs.Where(s => shown.Contains(s.LevelId)))
        {
            double z0 = doc.GetLevel(st.LevelId).Elevation;
            // Huellas y descanso macizos hasta el suelo (escalera de obra).
            foreach (var step in st.Steps) m.AddBox(step.Outline, z0, z0 + step.Top, StairColor);
        }

        foreach (var c in doc.Columns.Where(c => shown.Contains(c.LevelId)))
        {
            var lvl = doc.GetLevel(c.LevelId);
            m.AddBox(c.Outline, lvl.Elevation, lvl.Elevation + lvl.Height, c.Material == "acero" ? SteelColor : ColumnColor);
        }
        foreach (var b in doc.Beams.Where(b => shown.Contains(b.LevelId)))
        {
            var lvl = doc.GetLevel(b.LevelId);
            double top = lvl.Elevation + lvl.Height;
            m.AddBox(b.Outline, top - b.Depth, top, b.Material == "acero" ? SteelColor : ColumnColor);
        }

        foreach (var f in doc.Fixtures.Where(f => shown.Contains(f.LevelId)))
        {
            if (f.ModuleId == "hidraulica" || FixtureCatalog.ByKind(f.Kind) is not { } t)
            {
                uint c = f.ModuleId == "hidraulica" ? HydroColor : ElecColor;
                bool tank = f.Kind == "tanque";
                double half = tank ? f.Width / 2 : 0.06;
                double h = tank ? 1.2 : 0.12;
                var p = f.Position.Xy;
                double z = tank ? f.Position.Z : f.Position.Z - h / 2;
                m.AddBox(Square(p, half), z, z + h, c);
                continue;
            }
            m.AddFixture(doc, f, t, glow?.Invoke(f.Id) ?? 0);
        }

        foreach (var r in doc.Routes)
        {
            var from = doc.Fixtures.FirstOrDefault(f => f.Id == r.FromFixtureId);
            if (from is not null && !shown.Contains(from.LevelId)) continue;
            uint c = r.ModuleId == "hidraulica" ? HydroColor : Model.Electrical.ElectricalTables.ConduitColor(doc.Electrical.ConduitOf(r.Id).Type);
            foreach (var s in r.Segments) m.Lines.Add(new(s.Start, s.End, c));
        }
        return m;
    }

    private static Point2[] Square(Point2 p, double half) =>
        [new(p.X - half, p.Y - half), new(p.X + half, p.Y - half), new(p.X + half, p.Y + half), new(p.X - half, p.Y + half)];

    /// <summary>Rectángulo girado de lados <paramref name="w"/> (a lo largo de <paramref name="u"/>) y <paramref name="d"/>.</summary>
    private static Point2[] Rect(Point2 c, Point2 u, double w, double d)
    {
        var v = u.Perp();
        return [c - u * (w / 2) - v * (d / 2), c + u * (w / 2) - v * (d / 2), c + u * (w / 2) + v * (d / 2), c - u * (w / 2) + v * (d / 2)];
    }

    private static Point2[] Disc(Point2 c, double r, int n = 10) =>
        Enumerable.Range(0, n).Select(i => new Point2(c.X + r * Math.Cos(2 * Math.PI * i / n), c.Y + r * Math.Sin(2 * Math.PI * i / n))).ToArray();

    /// <summary>Elemento eléctrico con una forma sencilla según su tipo: placa en el muro, plafón, campana, tablero…</summary>
    private void AddFixture(BuildingDocument doc, PlacedFixture f, FixtureType t, double glow)
    {
        var lvl = doc.GetLevel(f.LevelId);
        var p = f.Position.Xy;
        var n = new Point2(Math.Cos(f.Rotation), Math.Sin(f.Rotation)); // normal hacia el recinto (muro) o eje (techo)
        bool lit = glow > 0.02;
        uint lamp = lit ? LitColor : FixtureColor;
        double z = f.Position.Z;
        switch (t.Role)
        {
            case ElecRole.Light when t.Mount == MountKind.Wall:
            {
                var c = p + n * 0.06;
                AddBox(Rect(c, n.Perp(), Math.Max(0.15, t.Size), 0.12), z - 0.08, z + 0.08, lamp);
                break;
            }
            case ElecRole.Light:
            {
                double ceiling = f.HostId is not null && doc.Beams.FirstOrDefault(b => b.Id == f.HostId) is { } beam
                    ? lvl.Elevation + lvl.Height - beam.Depth : lvl.Elevation + lvl.Height;
                if (f.Kind == FixtureCatalog.Lineal.Kind || f.Kind == FixtureCatalog.PanelLed.Kind)
                {
                    var u = new Point2(Math.Cos(f.Rotation), Math.Sin(f.Rotation));
                    double w = t.Size, d = f.Kind == FixtureCatalog.PanelLed.Kind ? t.Size : 0.12;
                    AddBox(Rect(p, u, w, d), z - 0.04, z + 0.01, lamp);
                }
                else if (f.Kind == FixtureCatalog.Campana.Kind || f.Kind == FixtureCatalog.Colgante.Kind)
                {
                    // Cable o tubo de suspensión y la pantalla.
                    double r = t.Size / 2;
                    AddBox(Square(p, 0.015), z + 0.15, ceiling, 0xFF404040);
                    AddBox(Disc(p, r * 0.45), z + 0.05, z + 0.18, 0xFF606870);
                    AddBox(Disc(p, r), z - 0.02, z + 0.06, lit ? LitColor : 0xFF8A949E);
                }
                else if (f.Kind == FixtureCatalog.Ventilador.Kind)
                {
                    AddBox(Square(p, 0.02), z, ceiling, 0xFF505050);
                    AddBox(Disc(p, 0.09), z - 0.08, z + 0.02, lamp);
                    for (int i = 0; i < 4; i++)
                    {
                        var u = new Point2(Math.Cos(i * Math.PI / 2 + f.Rotation), Math.Sin(i * Math.PI / 2 + f.Rotation));
                        AddBox(Rect(p + u * 0.32, u, 0.5, 0.11), z - 0.02, z, 0xFF8B6B4A);
                    }
                }
                else
                {
                    double r = Math.Max(0.08, t.Size / 2);
                    AddBox(Disc(p, r), z - 0.05, Math.Max(z, ceiling - 0.005), lamp);
                }
                if (lit) AddLightPool(doc, f, glow);
                break;
            }
            case ElecRole.Outlet or ElecRole.Switch or ElecRole.Box or ElecRole.Meter when t.Mount == MountKind.Wall:
            {
                double w = t.Role == ElecRole.Box ? Math.Max(0.08, t.Size) : 0.08, h = t.Role == ElecRole.Box ? Math.Max(0.08, t.Size) : 0.12;
                if (t.Role == ElecRole.Meter) { w = 0.25; h = 0.35; }
                uint col = t.Role switch
                {
                    ElecRole.Box => f.Kind == "condulet" ? 0xFFA8B0B8 : 0xFF9AA0A6,
                    ElecRole.Meter => 0xFFB8C0C8,
                    _ => f.Kind == FixtureCatalog.ContactoTierraAislada.Kind ? 0xFFF08A30 : FixtureColor,
                };
                AddBox(Rect(p + n * 0.012, n.Perp(), w, 0.025), z - h / 2, z + h / 2, col);
                break;
            }
            case ElecRole.Panel or ElecRole.Load when t.Mount == MountKind.Wall:
            {
                double w = t.Role == ElecRole.Panel ? 0.35 : Math.Max(0.3, t.Size), h = t.Role == ElecRole.Panel ? 0.5 : Math.Max(0.25, t.Size * 0.35);
                double d = t.Role == ElecRole.Panel ? 0.1 : 0.22;
                if (f.Kind == FixtureCatalog.Calentador.Kind) { w = 0.4; h = 0.6; d = 0.2; }
                AddBox(Rect(p + n * (d / 2), n.Perp(), w, d), z - h / 2, z + h / 2, t.Role == ElecRole.Panel ? 0xFFC8CDD2 : FixtureColor);
                if (t.Role == ElecRole.Panel) AddBox(Rect(p + n * (d + 0.003), n.Perp(), w * 0.85, 0.006), z - h * 0.4, z + h * 0.4, 0xFF9AA2AA);
                break;
            }
            case ElecRole.Load:
            {
                double s = Math.Max(0.3, t.Size);
                double hgt = f.Kind == FixtureCatalog.Motor.Kind ? 1.1 : 0.6;
                AddBox(Square(p, s / 2), z, z + hgt, f.Kind == FixtureCatalog.Motor.Kind ? 0xFF4F7FA8 : 0xFFDDE2E6);
                break;
            }
            default:
                // Contactos y cajas de piso o de techo: placa delgada.
                AddBox(Square(p, Math.Max(0.05, t.Size / 2)), z - 0.01, z + 0.01, t.Role == ElecRole.Box ? 0xFF9AA0A6 : FixtureColor);
                break;
        }
    }

    /// <summary>Mancha de luz en el piso bajo una luminaria encendida (semitransparente).</summary>
    private void AddLightPool(BuildingDocument doc, PlacedFixture f, double glow)
    {
        var lvl = doc.GetLevel(f.LevelId);
        double watts = Math.Max(5, Model.Electrical.LoadSchedule.PowerOf(doc, f));
        double r = Math.Clamp(Math.Sqrt(watts) * 0.4, 0.9, 5);
        byte a = (byte)Math.Clamp(170 * glow, 0, 190);
        uint col = ((uint)a << 24) | 0x00FFD23C;
        IReadOnlyList<Point2> disc = Disc(f.Position.Xy, r, 16);
        // Sin atravesar muros: se recorta a la habitación (si es convexa) o se limita al muro más cercano.
        var room = doc.SpaceAt(f.LevelId, f.Position.Xy);
        if (room is not null && IsConvex(room.Outline)) disc = ClipConvex(disc, room.Outline);
        else
        {
            double wall = doc.Walls.Where(w => w.LevelId == f.LevelId).Select(w => Geo.DistanceToSegment(w.Start, w.End, f.Position.Xy) - w.Thickness / 2)
                .DefaultIfEmpty(r).Min();
            disc = Disc(f.Position.Xy, Math.Max(0.2, Math.Min(r, wall)), 16);
        }
        if (disc.Count < 3) return;
        if (Geo.SignedArea(disc) < 0) disc = disc.Reverse().ToArray();
        Faces.Add(new(disc.Select(q => new Point3(q, lvl.Elevation + 0.01)).ToArray(), col));
    }

    private static bool IsConvex(IReadOnlyList<Point2> poly)
    {
        int sign = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count]; var c = poly[(i + 2) % poly.Count];
            double cr = Point2.Cross(b - a, c - b);
            if (Math.Abs(cr) < 1e-9) continue;
            int s = Math.Sign(cr);
            if (sign != 0 && s != sign) return false;
            sign = s;
        }
        return true;
    }

    /// <summary>Recorte de Sutherland-Hodgman de <paramref name="subject"/> contra el polígono convexo <paramref name="clip"/>.</summary>
    private static IReadOnlyList<Point2> ClipConvex(IReadOnlyList<Point2> subject, IReadOnlyList<Point2> clip)
    {
        var cl = Geo.SignedArea(clip) < 0 ? clip.Reverse().ToList() : clip.ToList();
        var output = subject.ToList();
        for (int i = 0; i < cl.Count && output.Count > 0; i++)
        {
            var a = cl[i]; var b = cl[(i + 1) % cl.Count];
            bool Inside(Point2 p) => Point2.Cross(b - a, p - a) >= 0;
            Point2 Cut(Point2 p, Point2 q)
            {
                double d1 = Point2.Cross(b - a, p - a), d2 = Point2.Cross(b - a, q - a);
                double t = d1 / (d1 - d2);
                return p + (q - p) * t;
            }
            var input = output;
            output = [];
            for (int j = 0; j < input.Count; j++)
            {
                var p = input[j]; var q = input[(j + 1) % input.Count];
                if (Inside(q)) { if (!Inside(p)) output.Add(Cut(p, q)); output.Add(q); }
                else if (Inside(p)) output.Add(Cut(p, q));
            }
        }
        return output;
    }

    private static Point2[] WallQuad(Wall w, double from, double to)
    {
        var d = w.Direction;
        var n = d.Perp() * (w.Thickness / 2);
        var a = w.Start + d * from;
        var b = w.Start + d * to;
        return [a - n, b - n, b + n, a + n];
    }

    /// <summary>Prisma de base <paramref name="b"/> entre z0 y z1 con caras orientadas hacia fuera.</summary>
    /// <summary>
    /// Forjado sin el hueco de las escaleras que suben hasta él desde la planta de abajo (cada tramo y descanso se
    /// recorta por separado, así una escalera en L no deja un agujero en la esquina vacía).
    /// </summary>
    public static IEnumerable<IReadOnlyList<Point2>> StairWellCut(BuildingDocument doc, Level lvl, IReadOnlyList<Point2> outline)
    {
        var pieces = new List<IReadOnlyList<Point2>> { outline };
        foreach (var hole in StairWells(doc, lvl))
            pieces = pieces.SelectMany(p => Geo.SubtractConvex(p, hole)).Cast<IReadOnlyList<Point2>>().ToList();
        return pieces;
    }

    /// <summary>Tramos y descansos de las escaleras que llegan a esta planta desde la de abajo (huecos del forjado).</summary>
    public static IEnumerable<IReadOnlyList<Point2>> StairWells(BuildingDocument doc, Level lvl)
    {
        foreach (var st in doc.Stairs)
        {
            double z0 = doc.GetLevel(st.LevelId).Elevation;
            if (z0 >= lvl.Elevation - 1e-6 || Math.Abs(z0 + st.Rise - lvl.Elevation) > 0.3) continue;
            foreach (var part in st.Parts) yield return part;
        }
    }

    public const uint GroundColor = 0xFFB4C39A;

    /// <summary>Terreno alrededor del edificio (para ver las sombras que proyecta), un poco por debajo de la planta baja.</summary>
    public void AddGround(double margin)
    {
        if (Faces.Count == 0) return;
        var (min, max) = Bounds;
        double z = min.Z - 0.02;
        Faces.Add(new([new(min.X - margin, min.Y - margin, z), new(max.X + margin, min.Y - margin, z),
                       new(max.X + margin, max.Y + margin, z), new(min.X - margin, max.Y + margin, z)], GroundColor));
    }

    public void AddBox(IReadOnlyList<Point2> b, double z0, double z1, uint argb)
    {
        if (z1 - z0 < 1e-6 || b.Count < 3) return;
        if (Geo.SignedArea(b) < 0) b = b.Reverse().ToArray();
        Faces.Add(new(b.Select(p => new Point3(p, z1)).ToArray(), argb));
        Faces.Add(new(b.Reverse().Select(p => new Point3(p, z0)).ToArray(), argb));
        for (int i = 0; i < b.Count; i++)
        {
            var p = b[i];
            var q = b[(i + 1) % b.Count];
            Faces.Add(new([new(p, z0), new(q, z0), new(q, z1), new(p, z1)], argb));
        }
    }
}
