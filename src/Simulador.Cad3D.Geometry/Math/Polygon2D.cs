namespace Simulador.Cad3D.Geometry;

/// <summary>Utilidades para perfiles 2D (polígonos simples, sin auto-intersecciones).</summary>
public static class Polygon2D
{
    /// <summary>Área con signo: positiva si el polígono va en sentido antihorario.</summary>
    public static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++) a += pts[i].Cross(pts[(i + 1) % pts.Count]);
        return a / 2;
    }

    /// <summary>Devuelve el perfil en sentido antihorario, sin puntos repetidos consecutivos.</summary>
    public static List<Vec2> Normalize(IReadOnlyList<Vec2> pts)
    {
        var list = new List<Vec2>();
        foreach (var p in pts)
            if (list.Count == 0 || Math.Abs(p.X - list[^1].X) + Math.Abs(p.Y - list[^1].Y) > 1e-9)
                list.Add(p);
        while (list.Count > 1 && Math.Abs(list[0].X - list[^1].X) + Math.Abs(list[0].Y - list[^1].Y) <= 1e-9) list.RemoveAt(list.Count - 1);
        if (SignedArea(list) < 0) list.Reverse();
        return list;
    }

    /// <summary>Triangulación por recorte de orejas. Espera un perfil antihorario. Devuelve índices de triángulos.</summary>
    public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<Vec2> pts)
    {
        var result = new List<(int, int, int)>();
        var idx = Enumerable.Range(0, pts.Count).ToList();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                Vec2 a = pts[ia], b = pts[ib], c = pts[ic];
                if ((b - a).Cross(c - b) <= 1e-12) continue; // vértice reflejo o alineado
                bool inside = false;
                foreach (var j in idx)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    // Vértices repetidos de los puentes hacia los huecos (ver MergeHoles): no bloquean la oreja.
                    if (Same(pts[j], a) || Same(pts[j], b) || Same(pts[j], c)) continue;
                    if (PointInTriangle(pts[j], a, b, c)) { inside = true; break; }
                }
                if (inside) continue;
                result.Add((ia, ib, ic));
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                // Perfil degenerado (puntos alineados): quita el vértice alineado y sigue.
                idx.RemoveAt(0);
            }
        }
        if (idx.Count == 3) result.Add((idx[0], idx[1], idx[2]));
        return result;
    }

    private static bool Same(Vec2 p, Vec2 q) => Math.Abs(p.X - q.X) < 1e-12 && Math.Abs(p.Y - q.Y) < 1e-12;

    /// <summary>
    /// Une los huecos (en sentido horario) al contorno exterior (antihorario) con puentes de ida y vuelta,
    /// para triangular una región con huecos como un solo polígono. Los huecos se procesan de derecha a izquierda
    /// y cada uno se une al vértice visible más cercano.
    /// </summary>
    public static List<Vec2> MergeHoles(IReadOnlyList<Vec2> outer, IReadOnlyList<IReadOnlyList<Vec2>> holes)
    {
        var poly = outer.ToList();
        var pending = holes.Where(h => h.Count >= 3).Select(h => h.ToList()).ToList();
        foreach (var hole in pending.OrderByDescending(h => h.Max(p => p.X)).ToList())
        {
            pending.Remove(hole);
            int mi = 0;
            for (int i = 1; i < hole.Count; i++) if (hole[i].X > hole[mi].X) mi = i;
            var m = hole[mi];
            int best = -1;
            foreach (int vi in Enumerable.Range(0, poly.Count).OrderBy(i => Dist2(poly[i], m)))
            {
                if (Visible(m, poly[vi], poly, hole, pending)) { best = vi; break; }
            }
            if (best < 0) best = Enumerable.Range(0, poly.Count).OrderBy(i => Dist2(poly[i], m)).First();
            var bridged = new List<Vec2>(poly.Count + hole.Count + 2);
            bridged.AddRange(poly.Take(best + 1));
            for (int k = 0; k <= hole.Count; k++) bridged.Add(hole[(mi + k) % hole.Count]);
            bridged.Add(poly[best]);
            bridged.AddRange(poly.Skip(best + 1));
            poly = bridged;
        }
        return poly;
    }

    private static double Dist2(Vec2 a, Vec2 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static bool Visible(Vec2 m, Vec2 v, List<Vec2> poly, List<Vec2> hole, List<List<Vec2>> others)
    {
        bool Crosses(IReadOnlyList<Vec2> ring)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                if (Same(a, m) || Same(b, m) || Same(a, v) || Same(b, v)) continue;
                if (ProperCross(m, v, a, b)) return true;
            }
            return false;
        }
        return !Crosses(poly) && !Crosses(hole) && !others.Any(Crosses);
    }

    private static bool ProperCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a), d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        double d1 = (b - a).Cross(p - a), d2 = (c - b).Cross(p - b), d3 = (a - c).Cross(p - c);
        return d1 >= -1e-12 && d2 >= -1e-12 && d3 >= -1e-12;
    }

    /// <summary>Rectángulo con esquinas redondeadas, con origen en la esquina inferior izquierda.</summary>
    public static List<Vec2> RoundedRectangle(double width, double depth, double radius, int segmentsPerCorner = 8)
    {
        radius = Math.Clamp(radius, 0, Math.Min(width, depth) / 2 - 1e-6);
        if (radius <= 1e-6)
            return new List<Vec2> { new(0, 0), new(width, 0), new(width, depth), new(0, depth) };
        var pts = new List<Vec2>();
        var centers = new[] { new Vec2(width - radius, radius), new Vec2(width - radius, depth - radius), new Vec2(radius, depth - radius), new Vec2(radius, radius) };
        double[] startAngles = { -90, 0, 90, 180 };
        for (int k = 0; k < 4; k++)
            for (int s = 0; s <= segmentsPerCorner; s++)
            {
                double ang = (startAngles[k] + 90.0 * s / segmentsPerCorner) * Math.PI / 180;
                pts.Add(new Vec2(centers[k].X + radius * Math.Cos(ang), centers[k].Y + radius * Math.Sin(ang)));
            }
        return pts;
    }

    /// <summary>Polígono regular inscrito (círculo aproximado), centrado en el origen.</summary>
    public static List<Vec2> Circle(double radius, int segments) =>
        Enumerable.Range(0, segments).Select(i =>
        {
            double a = 2 * Math.PI * i / segments;
            return new Vec2(radius * Math.Cos(a), radius * Math.Sin(a));
        }).ToList();
}
