using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

/// <summary>
/// Colocación automática compacta: prueba órdenes y giros de los componentes (recocido simulado), los coloca
/// en filas apretadas y se queda con la disposición de conexiones más cortas y con menos cruces entre sí, que es
/// lo que hace falta para poder rutear una placa de una sola cara.
/// </summary>
public static class AutoPlacer
{
    public const double Gap = 2.54;
    public const double Margin = 3.0;
    private const double Grid = 1.27;

    public static void Place(PcbDocument doc, int iterations = 4000, int seed = 1)
    {
        var parts = doc.Footprints.Where(f => f.Definition is not null).ToList();
        if (parts.Count == 0) return;
        doc.Tracks.Clear();

        var rng = new Random(seed);
        var order = Enumerable.Range(0, parts.Count).ToArray();
        var rot = parts.Select(p => (int)p.Rotation).ToArray();
        double area = parts.Sum(p => { var (a, b) = Size(p, p.Rotation); return (a + Gap) * (b + Gap); });
        double widest = parts.Max(p => Math.Min(Size(p, 0).W, Size(p, 90).W));
        double rowWidth = Math.Max(widest, Math.Sqrt(area) * 1.25);

        double current = Layout(doc, parts, order, rot, rowWidth);
        double bestCost = current;
        var bestOrder = (int[])order.Clone();
        var bestRot = (int[])rot.Clone();

        for (int it = 0; it < iterations; it++)
        {
            double temp = 20 * Math.Pow(0.002, (double)it / iterations);
            var o = (int[])order.Clone();
            var r = (int[])rot.Clone();
            if (rng.NextDouble() < 0.6 && parts.Count > 1)
            {
                int a = rng.Next(parts.Count), b = rng.Next(parts.Count);
                (o[a], o[b]) = (o[b], o[a]);
            }
            else
            {
                int a = rng.Next(parts.Count);
                r[a] = (r[a] + 90 * rng.Next(1, 4)) % 360;
            }
            double c = Layout(doc, parts, o, r, rowWidth);
            if (c < current || rng.NextDouble() < Math.Exp((current - c) / temp))
            {
                order = o; rot = r; current = c;
                if (c < bestCost) { bestCost = c; bestOrder = (int[])o.Clone(); bestRot = (int[])r.Clone(); }
            }
        }
        Layout(doc, parts, bestOrder, bestRot, rowWidth);
        AutoRouter.FitBoard(doc, Margin);
    }

    private static (double W, double H) Size(PcbFootprint f, double rotation)
    {
        double old = f.Rotation;
        var pos = f.Position;
        f.Rotation = rotation;
        var (a, b) = FootprintTransform.Bounds(f);
        f.Rotation = old;
        f.Position = pos;
        return (b.X - a.X, b.Y - a.Y);
    }

    /// <summary>Coloca en filas según el orden y los giros dados y devuelve el coste.</summary>
    private static double Layout(PcbDocument doc, List<PcbFootprint> parts, int[] order, int[] rot, double rowWidth)
    {
        double x = Margin, y = Margin, rowH = 0;
        foreach (int k in order)
        {
            var f = parts[k];
            f.Rotation = rot[k];
            f.Position = default;
            var (min, max) = FootprintTransform.Bounds(f);
            double w = max.X - min.X, h = max.Y - min.Y;
            if (x > Margin && x + w > Margin + rowWidth) { x = Margin; y += rowH + Gap; rowH = 0; }
            f.Position = new Vec2(Snap(x - min.X + 0.6), Snap(y - min.Y + 0.6));
            x += w + Gap;
            rowH = Math.Max(rowH, h);
        }
        doc.BoardWidth = Math.Max(doc.BoardWidth, Margin * 2 + rowWidth + 20);
        doc.BoardHeight = Math.Max(doc.BoardHeight, y + rowH + Margin * 2 + 20);
        return Cost(doc);
    }

    private static double Snap(double v) => Math.Ceiling(v / Grid) * Grid;

    /// <summary>Longitud de las conexiones + penalización por cada cruce y por cada línea que pisa un pad ajeno.</summary>
    public static double Cost(PcbDocument doc)
    {
        var lines = PcbAnalysis.ComputeRatsnest(doc);
        double cost = lines.Sum(l => Geometry2D.Length(l.A - l.B));
        for (int i = 0; i < lines.Count; i++)
            for (int j = i + 1; j < lines.Count; j++)
            {
                var a = lines[i]; var b = lines[j];
                if (a.Net == b.Net) continue;
                if (Geometry2D.SegmentsIntersect(a.A, a.B, b.A, b.B)) cost += 25;
            }
        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).ToList();
        foreach (var l in lines)
            foreach (var p in pads)
                if (p.Net != l.Net && Geometry2D.PadSegment(p, l.A, l.B) < 0.6) cost += 12;
        return cost;
    }
}
