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
    public static BuildingMesh Build(BuildingDocument doc, string? upToLevelId = null, bool showRoof = true)
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
                m.AddBox(s.Outline, lvl.Elevation - s.Thickness, lvl.Elevation, FloorColor);
            else if (s.Kind == SlabKind.Roof && showRoof && lvl == topShown && lvl == topAll)
                m.AddBox(s.Outline, lvl.Elevation + lvl.Height - s.Thickness, lvl.Elevation + lvl.Height, RoofColor);
        }

        foreach (var st in doc.Stairs.Where(s => shown.Contains(s.LevelId)))
        {
            var d = st.Design;
            double z0 = doc.GetLevel(st.LevelId).Elevation;
            var dir = st.Direction;
            var n = dir.Perp() * (st.Width / 2);
            for (int i = 0; i < d.Risers - 1; i++)
            {
                var a = st.Start + dir * (i * d.Tread);
                var b = st.Start + dir * ((i + 1) * d.Tread);
                m.AddBox([a - n, b - n, b + n, a + n], z0, z0 + (i + 1) * d.Riser, StairColor);
            }
        }

        foreach (var f in doc.Fixtures.Where(f => shown.Contains(f.LevelId)))
        {
            uint c = f.ModuleId == "hidraulica" ? HydroColor : ElecColor;
            bool tank = f.Kind == "tanque";
            double half = tank ? f.Width / 2 : 0.06;
            double h = tank ? 1.2 : 0.12;
            var p = f.Position.Xy;
            double z = tank ? f.Position.Z : f.Position.Z - h / 2;
            m.AddBox([new(p.X - half, p.Y - half), new(p.X + half, p.Y - half), new(p.X + half, p.Y + half), new(p.X - half, p.Y + half)], z, z + h, c);
        }

        foreach (var r in doc.Routes)
        {
            var from = doc.Fixtures.FirstOrDefault(f => f.Id == r.FromFixtureId);
            if (from is not null && !shown.Contains(from.LevelId)) continue;
            uint c = r.ModuleId == "hidraulica" ? HydroColor : ElecColor;
            foreach (var s in r.Segments) m.Lines.Add(new(s.Start, s.End, c));
        }
        return m;
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
