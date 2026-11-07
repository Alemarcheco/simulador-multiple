using Simulador.Core.Building;

namespace Simulador.Civil.Import;

/// <summary>Muro propuesto a partir del plano: eje y espesor.</summary>
public readonly record struct WallCandidate(Point2 A, Point2 B, double Thickness)
{
    public double Length => A.DistanceTo(B);
}

/// <summary>
/// Propone muros a partir de las líneas de un plano. En los planos de arquitectura un muro se dibuja con dos líneas
/// paralelas separadas por su espesor: se buscan esas parejas, se toma el eje en el tramo en que se solapan y luego
/// se alargan o recortan los extremos para que los muros se encuentren en las esquinas y en las uniones en T.
/// Es una ayuda: conviene revisar el resultado.
/// </summary>
public static class WallDetector
{
    public static List<WallCandidate> Detect(IEnumerable<DrawingSegment> segments, double minThickness = 0.07,
        double maxThickness = 0.45, double minLength = 0.3, double angleToleranceDeg = 2)
    {
        var all = segments.ToList();
        // Si hay capas que se llaman como muros, solo esas (así no se confunden muebles con muros).
        var wallLayers = all.Select(s => s.Layer).Distinct().Where(LooksLikeWallLayer).ToHashSet();
        if (wallLayers.Count > 0) all = all.Where(s => wallLayers.Contains(s.Layer)).ToList();
        var segs = all.Where(s => s.A.DistanceTo(s.B) >= minLength * 0.5).Select(s => (s.A, s.B)).ToList();
        segs = MergeCollinear(segs);
        double cosTol = Math.Cos(angleToleranceDeg * Math.PI / 180);
        var pairs = new List<(int I, int J, WallCandidate W)>();

        // Todas las parejas de líneas paralelas a distancia de muro.
        for (int i = 0; i < segs.Count; i++)
        {
            var (a, b) = segs[i];
            var d = Unit(b - a);
            for (int j = i + 1; j < segs.Count; j++)
            {
                var (c, e) = segs[j];
                var d2 = Unit(e - c);
                if (Math.Abs(Point2.Dot(d, d2)) < cosTol) continue;
                double dist = Math.Abs(Cross(d, c - a));
                double dist2 = Math.Abs(Cross(d, e - a));
                if (Math.Abs(dist - dist2) > 0.02) continue;
                double t = (dist + dist2) / 2;
                if (t < minThickness || t > maxThickness) continue;
                // Solape de las proyecciones sobre la dirección de la primera línea.
                double a0 = 0, a1 = Point2.Dot(b - a, d);
                double c0 = Point2.Dot(c - a, d), c1 = Point2.Dot(e - a, d);
                double lo = Math.Max(Math.Min(a0, a1), Math.Min(c0, c1));
                double hi = Math.Min(Math.Max(a0, a1), Math.Max(c0, c1));
                if (hi - lo < minLength) continue;
                var n = d.Perp();
                double side = Math.Sign(Cross(d, c - a)); // hacia qué lado está la pareja
                var offset = n * (side * t / 2);
                pairs.Add((i, j, new WallCandidate(a + d * lo + offset, a + d * hi + offset, Math.Round(t, 3))));
            }
        }

        // Cada línea se empareja con su vecina más cercana: la cara interior de un muro va con la exterior, no con
        // el borde de una cama que está a 30 cm. Se admiten empates (una cara exterior continua con varias interiores).
        var best = new double[segs.Count];
        Array.Fill(best, double.MaxValue);
        foreach (var (i, j, w) in pairs)
        {
            best[i] = Math.Min(best[i], w.Thickness);
            best[j] = Math.Min(best[j], w.Thickness);
        }
        var walls = pairs.Where(p => p.W.Thickness <= best[p.I] + 0.01 && p.W.Thickness <= best[p.J] + 0.01).Select(p => p.W).ToList();

        walls = RemoveDuplicates(walls);
        return JoinEnds(walls, maxThickness);
    }

    /// <summary>Nombres de capa habituales para muros (español, inglés y la norma AIA "A-WALL").</summary>
    public static bool LooksLikeWallLayer(string layer)
    {
        var l = layer.ToUpperInvariant();
        return l.Contains("MURO") || l.Contains("WALL") || l.Contains("PARED") || l.Contains("TABIQ") || l.Contains("MUR_") || l == "MUR";
    }

    /// <summary>Une segmentos colineales que se tocan o solapan (los CAD a veces parten una línea en varias).</summary>
    private static List<(Point2 A, Point2 B)> MergeCollinear(List<(Point2 A, Point2 B)> segs)
    {
        var result = new List<(Point2 A, Point2 B)>(segs);
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < result.Count && !merged; i++)
            for (int j = i + 1; j < result.Count && !merged; j++)
            {
                var (a, b) = result[i];
                var (c, e) = result[j];
                var d = Unit(b - a);
                if (Math.Abs(Cross(d, Unit(e - c))) > 0.01) continue;
                if (Math.Abs(Cross(d, c - a)) > 0.005 || Math.Abs(Cross(d, e - a)) > 0.005) continue;
                double a1 = Point2.Dot(b - a, d), c0 = Point2.Dot(c - a, d), c1 = Point2.Dot(e - a, d);
                double lo1 = Math.Min(0, a1), hi1 = Math.Max(0, a1), lo2 = Math.Min(c0, c1), hi2 = Math.Max(c0, c1);
                if (lo2 > hi1 + 0.005 || lo1 > hi2 + 0.005) continue;
                double lo = Math.Min(lo1, lo2), hi = Math.Max(hi1, hi2);
                result[i] = (a + d * lo, a + d * hi);
                result.RemoveAt(j);
                merged = true;
            }
        }
        return result;
    }

    /// <summary>Quita ejes repetidos (una línea con dos parejas posibles da dos propuestas casi iguales).</summary>
    private static List<WallCandidate> RemoveDuplicates(List<WallCandidate> walls)
    {
        var keep = new List<WallCandidate>();
        foreach (var w in walls.OrderByDescending(w => w.Length))
        {
            var d = Unit(w.B - w.A);
            bool dup = keep.Any(k =>
            {
                var dk = Unit(k.B - k.A);
                if (Math.Abs(Cross(d, dk)) > 0.05) return false;
                if (Math.Abs(Cross(dk, w.A - k.A)) > Math.Max(k.Thickness, w.Thickness) / 2) return false;
                double p0 = Point2.Dot(w.A - k.A, dk), p1 = Point2.Dot(w.B - k.A, dk);
                double len = k.Length;
                return Math.Min(p0, p1) >= -0.05 && Math.Max(p0, p1) <= len + 0.05;
            });
            if (!dup) keep.Add(w);
        }
        return keep;
    }

    /// <summary>
    /// Alarga o recorta cada extremo hasta el eje del muro más cercano que corta su prolongación, si está a menos de
    /// un espesor y medio: así se cierran las esquinas y las uniones en T y se detectan las habitaciones.
    /// </summary>
    private static List<WallCandidate> JoinEnds(List<WallCandidate> walls, double maxThickness)
    {
        var result = walls.ToArray();
        for (int i = 0; i < result.Length; i++)
        {
            for (int end = 0; end < 2; end++)
            {
                var w = result[i];
                var p = end == 0 ? w.A : w.B;
                var q = end == 0 ? w.B : w.A;
                var d = Unit(p - q);
                double best = double.MaxValue;
                Point2? hit = null;
                for (int j = 0; j < result.Length; j++)
                {
                    if (j == i) continue;
                    var o = result[j];
                    var od = o.B - o.A;
                    double denom = Cross(d, od);
                    if (Math.Abs(denom) < 1e-6) continue;
                    // p + d·s = o.A + od·u
                    double s = Cross(o.A - p, od) / denom;
                    double u = Cross(o.A - p, d) / denom;
                    double reach = Math.Max(o.Thickness, w.Thickness) * 1.5 + 0.05;
                    double uTol = reach / Math.Max(od.Length, 1e-9);
                    if (u < -uTol || u > 1 + uTol) continue;
                    if (s < -reach || s > Math.Min(reach, maxThickness * 2)) continue;
                    if (Math.Abs(s) < best) { best = Math.Abs(s); hit = p + d * s; }
                }
                if (hit is { } h)
                    result[i] = end == 0 ? w with { A = h } : w with { B = h };
            }
        }
        return result.Where(w => w.Length > 0.05).ToList();
    }

    private static Point2 Unit(Point2 v)
    {
        double l = v.Length;
        return l < 1e-12 ? new Point2(1, 0) : v * (1 / l);
    }

    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
}
