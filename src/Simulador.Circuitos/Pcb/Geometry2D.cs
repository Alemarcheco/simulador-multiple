using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

/// <summary>Distancias entre pistas y pads para la comprobación de reglas y la conectividad.</summary>
public static class Geometry2D
{
    public static double Length(Vec2 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    public static double PointSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Length(new Vec2(a.X + t * dx - p.X, a.Y + t * dy - p.Y));
    }

    public static bool SegmentsIntersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        static double Cross(Vec2 o, Vec2 p, Vec2 q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    public static double SegmentSegment(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        if (SegmentsIntersect(a, b, c, d)) return 0;
        return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
                        Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
    }

    /// <summary>Distancia del borde de un pad a un segmento (0 si se tocan).</summary>
    public static double PadSegment(PlacedPad pad, Vec2 a, Vec2 b)
    {
        // Pasar el segmento al sistema local del pad (sin rotación).
        double ang = -pad.RotationDeg * Math.PI / 180;
        Vec2 Local(Vec2 p)
        {
            double x = p.X - pad.Center.X, y = p.Y - pad.Center.Y;
            double c = Math.Cos(ang), s = Math.Sin(ang);
            return new Vec2(x * c + y * s, -x * s + y * c);
        }
        var la = Local(a);
        var lb = Local(b);
        double w = pad.Pad.Size.X / 2, h = pad.Pad.Size.Y / 2;

        switch (pad.Pad.Shape)
        {
            case PadShape.Circle:
                return Math.Max(0, PointSegment(default, la, lb) - w);
            case PadShape.Oval:
            {
                // Óvalo = cápsula: segmento central con radio.
                double r = Math.Min(w, h);
                var c0 = w > h ? new Vec2(-(w - r), 0) : new Vec2(0, -(h - r));
                var c1 = w > h ? new Vec2(w - r, 0) : new Vec2(0, h - r);
                return Math.Max(0, SegmentSegment(la, lb, c0, c1) - r);
            }
            default:
            {
                var r0 = new Vec2(-w, -h); var r1 = new Vec2(w, -h); var r2 = new Vec2(w, h); var r3 = new Vec2(-w, h);
                if (Inside(la) || Inside(lb)) return 0;
                return Math.Min(Math.Min(SegmentSegment(la, lb, r0, r1), SegmentSegment(la, lb, r1, r2)),
                                Math.Min(SegmentSegment(la, lb, r2, r3), SegmentSegment(la, lb, r3, r0)));
            }
        }

        bool Inside(Vec2 p) => Math.Abs(p.X) <= w && Math.Abs(p.Y) <= h;
    }

    /// <summary>Distancia aproximada entre los bordes de dos pads.</summary>
    public static double PadPad(PlacedPad p, PlacedPad q)
    {
        // Se aproxima el segundo pad por su segmento central y su semiancho menor.
        double r = Math.Min(q.Pad.Size.X, q.Pad.Size.Y) / 2;
        double half = Math.Max(q.Pad.Size.X, q.Pad.Size.Y) / 2 - r;
        double a = (q.RotationDeg + (q.Pad.Size.X >= q.Pad.Size.Y ? 0 : 90)) * Math.PI / 180;
        var dir = new Vec2(Math.Cos(a) * half, -Math.Sin(a) * half);
        return Math.Max(0, PadSegment(p, q.Center - dir, q.Center + dir) - r);
    }
}
