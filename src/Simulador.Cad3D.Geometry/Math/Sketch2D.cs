namespace Simulador.Cad3D.Geometry;

/// <summary>Operaciones del editor de bocetos 2D (perfil cerrado de puntos en mm).</summary>
public static class Sketch2D
{
    /// <summary>Redondea al múltiplo más cercano de la rejilla (0 = sin ajuste).</summary>
    public static Vec2 Snap(Vec2 p, double grid) =>
        grid <= 0 ? p : new Vec2(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);

    public static double Length(Vec2 a, Vec2 b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    /// <summary>Longitud del tramo que va del punto i al siguiente (el último cierra con el primero).</summary>
    public static double SegmentLength(IReadOnlyList<Vec2> pts, int i) => Length(pts[i], pts[(i + 1) % pts.Count]);

    /// <summary>
    /// Fija la longitud del tramo i moviendo su punto final en la misma dirección.
    /// Devuelve false si el tramo tiene longitud cero (no hay dirección).
    /// </summary>
    public static bool SetSegmentLength(List<Vec2> pts, int i, double length)
    {
        int j = (i + 1) % pts.Count;
        double l = SegmentLength(pts, i);
        if (l < 1e-9 || length <= 0) return false;
        var d = pts[j] - pts[i];
        pts[j] = new Vec2(pts[i].X + d.X * length / l, pts[i].Y + d.Y * length / l);
        return true;
    }

    /// <summary>Distancia de p al segmento ab y parámetro t (0..1) del punto más cercano.</summary>
    public static (double Distance, double T) DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return (Length(p, new Vec2(a.X + ab.X * t, a.Y + ab.Y * t)), t);
    }

    /// <summary>Índice del tramo más cercano a p, o -1 si ninguno está a menos de maxDistance.</summary>
    public static int NearestSegment(IReadOnlyList<Vec2> pts, Vec2 p, double maxDistance)
    {
        int best = -1;
        double bestD = maxDistance;
        for (int i = 0; i < pts.Count; i++)
        {
            var (d, _) = DistanceToSegment(p, pts[i], pts[(i + 1) % pts.Count]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Índice del punto más cercano a p, o -1 si ninguno está a menos de maxDistance.</summary>
    public static int NearestPoint(IReadOnlyList<Vec2> pts, Vec2 p, double maxDistance)
    {
        int best = -1;
        double bestD = maxDistance;
        for (int i = 0; i < pts.Count; i++)
        {
            double d = Length(pts[i], p);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Añade un punto: si cae cerca de un tramo se inserta en él; si no, se inserta en el tramo más cercano
    /// (así el perfil sigue cerrado). Devuelve el índice del punto nuevo.
    /// </summary>
    public static int AddPoint(List<Vec2> pts, Vec2 p)
    {
        if (pts.Count < 2) { pts.Add(p); return pts.Count - 1; }
        int seg = NearestSegment(pts, p, double.MaxValue);
        pts.Insert(seg + 1, p);
        return seg + 1;
    }

    /// <summary>True si dos tramos no contiguos se cruzan (el perfil no sirve para extruir).</summary>
    public static bool SelfIntersects(IReadOnlyList<Vec2> pts)
    {
        int n = pts.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue; // contiguos
                if (SegmentsCross(pts[i], pts[(i + 1) % n], pts[j], pts[(j + 1) % n])) return true;
            }
        return false;
    }

    private static bool SegmentsCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a);
        double d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
        if (((d1 > 1e-12 && d2 < -1e-12) || (d1 < -1e-12 && d2 > 1e-12)) &&
            ((d3 > 1e-12 && d4 < -1e-12) || (d3 < -1e-12 && d4 > 1e-12))) return true;
        // Colineales que se solapan, o un extremo apoyado en el otro tramo.
        return (Math.Abs(d1) <= 1e-12 && OnSegment(a, b, c)) || (Math.Abs(d2) <= 1e-12 && OnSegment(a, b, d)) ||
               (Math.Abs(d3) <= 1e-12 && OnSegment(c, d, a)) || (Math.Abs(d4) <= 1e-12 && OnSegment(c, d, b));
    }

    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 p) =>
        p.X >= Math.Min(a.X, b.X) - 1e-9 && p.X <= Math.Max(a.X, b.X) + 1e-9 &&
        p.Y >= Math.Min(a.Y, b.Y) - 1e-9 && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;

    /// <summary>Comprueba que el perfil sirve para extruir o revolucionar. Devuelve null si es válido o el motivo.</summary>
    public static string? Validate(IReadOnlyList<Vec2> pts, bool revolve)
    {
        if (pts.Count < 3) return "Hacen falta al menos 3 puntos.";
        if (Math.Abs(Polygon2D.SignedArea(pts)) < 1e-6) return "El perfil no tiene área (puntos en línea).";
        if (SelfIntersects(pts)) return "Hay tramos que se cruzan.";
        if (revolve && pts.Any(p => p.X < -1e-9)) return "En una revolución el radio (X) no puede ser negativo.";
        return null;
    }

    /// <summary>Rectángulo con la esquina inferior izquierda en el origen.</summary>
    public static List<Vec2> Rectangle(double w, double h) => new() { new(0, 0), new(w, 0), new(w, h), new(0, h) };

    /// <summary>Polígono regular centrado en c.</summary>
    public static List<Vec2> RegularPolygon(Vec2 c, double radius, int sides) =>
        Polygon2D.Circle(radius, Math.Max(3, sides)).Select(p => p + c).ToList();
}
