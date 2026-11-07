using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>Utilidades de geometría plana (metros).</summary>
public static class Geo
{
    public const double Eps = 1e-6;

    /// <summary>Área con signo (positiva si el polígono va en sentido antihorario).</summary>
    public static double SignedArea(IReadOnlyList<Point2> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    public static double Perimeter(IReadOnlyList<Point2> poly)
    {
        double s = 0;
        for (int i = 0; i < poly.Count; i++) s += poly[i].DistanceTo(poly[(i + 1) % poly.Count]);
        return s;
    }

    public static Point2 Centroid(IReadOnlyList<Point2> poly)
    {
        double a = SignedArea(poly);
        if (Math.Abs(a) < Eps) return new(poly.Average(p => p.X), poly.Average(p => p.Y));
        double cx = 0, cy = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            double f = p.X * q.Y - q.X * p.Y;
            cx += (p.X + q.X) * f;
            cy += (p.Y + q.Y) * f;
        }
        return new(cx / (6 * a), cy / (6 * a));
    }

    /// <summary>Parte del polígono convexo a la izquierda de la recta a→b (Sutherland–Hodgman).</summary>
    public static List<Point2> ClipLeft(IReadOnlyList<Point2> poly, Point2 a, Point2 b)
    {
        var result = new List<Point2>();
        var d = b - a;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            double sp = Point2.Cross(d, p - a), sq = Point2.Cross(d, q - a);
            if (sp >= 0) result.Add(p);
            if ((sp >= 0) != (sq >= 0)) result.Add(p + (q - p) * (sp / (sp - sq)));
        }
        return result;
    }

    /// <summary>
    /// Polígono convexo menos un agujero convexo, en trozos convexos que no se solapan (por ejemplo, un forjado con
    /// el hueco de la escalera). Si no se tocan, devuelve el polígono tal cual.
    /// </summary>
    public static List<Point2[]> SubtractConvex(IReadOnlyList<Point2> poly, IReadOnlyList<Point2> hole)
    {
        var h = SignedArea(hole) < 0 ? hole.Reverse().ToArray() : hole.ToArray();
        var pieces = new List<Point2[]>();
        IReadOnlyList<Point2> rest = poly;
        for (int i = 0; i < h.Length && rest.Count >= 3; i++)
        {
            var a = h[i];
            var b = h[(i + 1) % h.Length];
            // Lo que queda fuera de este borde del agujero es un trozo; lo de dentro sigue con el siguiente borde.
            var outside = ClipLeft(rest, b, a);
            if (outside.Count >= 3 && Math.Abs(SignedArea(outside)) > 1e-6) pieces.Add(outside.ToArray());
            rest = ClipLeft(rest, a, b);
        }
        return pieces;
    }

    public static bool Contains(IReadOnlyList<Point2> poly, Point2 pt)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > pt.Y) != (b.Y > pt.Y) && pt.X < (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Parámetro t ∈ [0,1] del punto de <paramref name="a"/>→<paramref name="b"/> más cercano a <paramref name="p"/>.</summary>
    public static double ProjectT(Point2 a, Point2 b, Point2 p)
    {
        var ab = b - a;
        double len2 = Point2.Dot(ab, ab);
        if (len2 < Eps) return 0;
        return Math.Clamp(Point2.Dot(p - a, ab) / len2, 0, 1);
    }

    public static double DistanceToSegment(Point2 a, Point2 b, Point2 p)
    {
        double t = ProjectT(a, b, p);
        return (a + (b - a) * t).DistanceTo(p);
    }

    /// <summary>Intersección propia o en extremos de dos segmentos; devuelve los parámetros t y u.</summary>
    public static bool SegmentIntersection(Point2 p, Point2 p2, Point2 q, Point2 q2, out double t, out double u)
    {
        var r = p2 - p;
        var s = q2 - q;
        double denom = Point2.Cross(r, s);
        t = u = 0;
        if (Math.Abs(denom) < 1e-12) return false; // paralelos o colineales
        var qp = q - p;
        t = Point2.Cross(qp, s) / denom;
        u = Point2.Cross(qp, r) / denom;
        const double tol = 1e-9;
        return t >= -tol && t <= 1 + tol && u >= -tol && u <= 1 + tol;
    }
}
