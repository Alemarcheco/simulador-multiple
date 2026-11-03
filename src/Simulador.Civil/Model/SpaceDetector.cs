using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Detecta los recintos cerrados que forman los ejes de los muros de un nivel: parte los muros en sus cruces y
/// uniones en T, construye el grafo plano y recorre sus caras. La cara exterior (área negativa) se descarta.
/// </summary>
public static class SpaceDetector
{
    /// <summary>Área mínima para considerar un recinto (m²); evita triángulos espurios en uniones.</summary>
    public const double MinArea = 0.5;

    private const double SnapTol = 0.01; // 1 cm

    public static IReadOnlyList<IReadOnlyList<Point2>> FindRooms(IEnumerable<(Point2 A, Point2 B)> segments)
    {
        var segs = segments.Where(s => s.A.DistanceTo(s.B) > SnapTol).ToList();

        // 1. Puntos de corte de cada segmento (extremos + intersecciones con los demás).
        var cuts = segs.Select(s => new List<double> { 0, 1 }).ToList();
        for (int i = 0; i < segs.Count; i++)
        {
            for (int j = i + 1; j < segs.Count; j++)
            {
                if (Geo.SegmentIntersection(segs[i].A, segs[i].B, segs[j].A, segs[j].B, out var t, out var u))
                {
                    cuts[i].Add(Math.Clamp(t, 0, 1));
                    cuts[j].Add(Math.Clamp(u, 0, 1));
                }
                else
                {
                    // Uniones en T casi exactas: extremo de uno sobre el otro (tolerancia).
                    AddTouch(segs[i], segs[j], cuts[j]);
                    AddTouch(segs[j], segs[i], cuts[i]);
                }
            }
        }

        // 2. Nodos (con imán de 1 cm) y aristas no dirigidas.
        var nodes = new List<Point2>();
        int NodeOf(Point2 p)
        {
            for (int k = 0; k < nodes.Count; k++)
                if (nodes[k].DistanceTo(p) < SnapTol) return k;
            nodes.Add(p);
            return nodes.Count - 1;
        }

        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < segs.Count; i++)
        {
            var ts = cuts[i].Distinct().OrderBy(t => t).ToList();
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                var a = segs[i].A + (segs[i].B - segs[i].A) * ts[k];
                var b = segs[i].A + (segs[i].B - segs[i].A) * ts[k + 1];
                int na = NodeOf(a), nb = NodeOf(b);
                if (na == nb) continue;
                edges.Add(na < nb ? (na, nb) : (nb, na));
            }
        }

        // 3. Vecinos de cada nodo ordenados por ángulo (antihorario).
        var adj = new Dictionary<int, List<int>>();
        foreach (var (a, b) in edges)
        {
            (adj.TryGetValue(a, out var la) ? la : adj[a] = []).Add(b);
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = []).Add(a);
        }
        foreach (var (n, list) in adj)
            list.Sort((p, q) => Angle(nodes[n], nodes[p]).CompareTo(Angle(nodes[n], nodes[q])));

        // 4. Recorrido de caras: desde la semiarista u→v, la siguiente es v→w con w el vecino anterior a u en el
        //    orden antihorario alrededor de v (la cara queda a la izquierda).
        var visited = new HashSet<(int, int)>();
        var rooms = new List<IReadOnlyList<Point2>>();
        foreach (var (a0, b0) in edges)
        {
            foreach (var start in new[] { (a0, b0), (b0, a0) })
            {
                if (visited.Contains(start)) continue;
                var face = new List<int>();
                var cur = start;
                int guard = 0;
                while (visited.Add(cur) && guard++ < 10_000)
                {
                    face.Add(cur.Item1);
                    var around = adj[cur.Item2];
                    int idx = around.IndexOf(cur.Item1);
                    int w = around[(idx - 1 + around.Count) % around.Count];
                    cur = (cur.Item2, w);
                }
                var poly = Simplify(face.Select(i => nodes[i]).ToList());
                if (poly.Count >= 3 && Geo.SignedArea(poly) > MinArea)
                    rooms.Add(poly);
            }
        }
        return rooms;
    }

    private static void AddTouch((Point2 A, Point2 B) from, (Point2 A, Point2 B) onto, List<double> ontoCuts)
    {
        foreach (var p in new[] { from.A, from.B })
        {
            if (Geo.DistanceToSegment(onto.A, onto.B, p) < SnapTol)
                ontoCuts.Add(Geo.ProjectT(onto.A, onto.B, p));
        }
    }

    private static double Angle(Point2 from, Point2 to) => Math.Atan2(to.Y - from.Y, to.X - from.X);

    /// <summary>Quita vértices repetidos, colineales y "pelos" (ida y vuelta por una arista suelta).</summary>
    private static List<Point2> Simplify(List<Point2> pts)
    {
        bool changed = true;
        while (changed && pts.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < pts.Count && pts.Count >= 3; i++)
            {
                var prev = pts[(i - 1 + pts.Count) % pts.Count];
                var cur = pts[i];
                var next = pts[(i + 1) % pts.Count];
                bool dup = prev.DistanceTo(cur) < SnapTol;
                bool spike = prev.DistanceTo(next) < SnapTol;
                bool collinear = Math.Abs(Point2.Cross(cur - prev, next - cur)) < 1e-9 && Point2.Dot(cur - prev, next - cur) > 0;
                if (dup || collinear)
                {
                    pts.RemoveAt(i);
                    changed = true;
                    i--;
                }
                else if (spike)
                {
                    // Eliminar el pico y el punto repetido que deja.
                    int nextIdx = (i + 1) % pts.Count;
                    pts.RemoveAt(Math.Max(i, nextIdx));
                    pts.RemoveAt(Math.Min(i, nextIdx));
                    changed = true;
                    i = -1;
                }
            }
        }
        return pts;
    }
}
