namespace Simulador.Tableros.Import.Step;

/// <summary>
/// Triangulación de polígonos con agujeros por recorte de orejas (versión reducida del algoritmo «earcut» de Mapbox,
/// licencia ISC). Entrada: coordenadas 2D seguidas (x0, y0, x1, y1…) e índices donde empieza cada agujero.
/// </summary>
public static class Earcut
{
    private sealed class Node(int i, double x, double y)
    {
        public readonly int I = i;
        public readonly double X = x, Y = y;
        public Node Prev = null!, Next = null!;
        public bool Steiner;
    }

    public static List<int> Triangulate(IReadOnlyList<double> data, IReadOnlyList<int>? holes = null)
    {
        var tris = new List<int>();
        var hasHoles = holes is { Count: > 0 };
        var outerLen = hasHoles ? holes![0] * 2 : data.Count;
        var outer = LinkedList(data, 0, outerLen, true);
        if (outer is null || outer.Next == outer.Prev) return tris;
        if (hasHoles) outer = EliminateHoles(data, holes!, outer);
        EarcutLinked(outer, tris, 0);
        return tris;
    }

    private static Node? LinkedList(IReadOnlyList<double> d, int start, int end, bool clockwise)
    {
        Node? last = null;
        if (clockwise == (SignedArea(d, start, end) > 0))
            for (var i = start; i < end; i += 2) last = Insert(i / 2, d[i], d[i + 1], last);
        else
            for (var i = end - 2; i >= start; i -= 2) last = Insert(i / 2, d[i], d[i + 1], last);
        if (last is not null && Equals(last, last.Next)) { Remove(last); last = last.Next; }
        return last;
    }

    private static Node? Filter(Node? start, Node? end = null)
    {
        if (start is null) return start;
        end ??= start;
        var p = start;
        bool again;
        do
        {
            again = false;
            if (!p.Steiner && (Equals(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
            {
                Remove(p);
                p = end = p.Prev;
                if (p == p.Next) break;
                again = true;
            }
            else p = p.Next;
        } while (again || p != end);
        return end;
    }

    private static void EarcutLinked(Node? ear, List<int> tris, int pass)
    {
        if (ear is null) return;
        var stop = ear;
        var guard = 0;
        while (ear.Prev != ear.Next)
        {
            if (++guard > 10_000_000) return;
            var prev = ear.Prev; var next = ear.Next;
            if (IsEar(ear))
            {
                tris.Add(prev.I); tris.Add(ear.I); tris.Add(next.I);
                Remove(ear);
                ear = next.Next; stop = next.Next;
                continue;
            }
            ear = next;
            if (ear == stop)
            {
                if (pass == 0) EarcutLinked(Filter(ear), tris, 1);
                else if (pass == 1) { ear = CureLocalIntersections(Filter(ear)!, tris); EarcutLinked(ear, tris, 2); }
                else if (pass == 2) SplitEarcut(ear, tris);
                break;
            }
        }
    }

    private static bool IsEar(Node ear)
    {
        Node a = ear.Prev, b = ear, c = ear.Next;
        if (Area(a, b, c) >= 0) return false;
        var p = ear.Next.Next;
        while (p != ear.Prev)
        {
            if (PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0) return false;
            p = p.Next;
        }
        return true;
    }

    private static Node CureLocalIntersections(Node start, List<int> tris)
    {
        var p = start;
        do
        {
            Node a = p.Prev, b = p.Next.Next;
            if (!Equals(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
            {
                tris.Add(a.I); tris.Add(p.I); tris.Add(b.I);
                Remove(p); Remove(p.Next);
                p = start = b;
            }
            p = p.Next;
        } while (p != start);
        return Filter(p)!;
    }

    private static void SplitEarcut(Node start, List<int> tris)
    {
        var a = start;
        do
        {
            var b = a.Next.Next;
            while (b != a.Prev)
            {
                if (a.I != b.I && IsValidDiagonal(a, b))
                {
                    var c = SplitPolygon(a, b);
                    a = Filter(a, a.Next)!;
                    c = Filter(c, c.Next)!;
                    EarcutLinked(a, tris, 0);
                    EarcutLinked(c, tris, 0);
                    return;
                }
                b = b.Next;
            }
            a = a.Next;
        } while (a != start);
    }

    private static Node EliminateHoles(IReadOnlyList<double> d, IReadOnlyList<int> holes, Node outer)
    {
        var queue = new List<Node>();
        for (var i = 0; i < holes.Count; i++)
        {
            var start = holes[i] * 2;
            var end = i < holes.Count - 1 ? holes[i + 1] * 2 : d.Count;
            var list = LinkedList(d, start, end, false);
            if (list is null) continue;
            if (list == list.Next) list.Steiner = true;
            queue.Add(LeftMost(list));
        }
        queue.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        foreach (var h in queue) outer = EliminateHole(h, outer);
        return outer;
    }

    private static Node EliminateHole(Node hole, Node outer)
    {
        var bridge = FindHoleBridge(hole, outer);
        if (bridge is null) return outer;
        var bridgeReverse = SplitPolygon(bridge, hole);
        Filter(bridgeReverse, bridgeReverse.Next);
        return Filter(bridge, bridge.Next)!;
    }

    private static Node? FindHoleBridge(Node hole, Node outer)
    {
        var p = outer;
        double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
        Node? m = null;
        do
        {
            if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
            {
                var x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                if (x <= hx && x > qx)
                {
                    qx = x;
                    m = p.X < p.Next.X ? p : p.Next;
                    if (x == hx) return m;
                }
            }
            p = p.Next;
        } while (p != outer);
        if (m is null) return null;
        var stop = m;
        double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
        p = m;
        do
        {
            if (hx >= p.X && p.X >= mx && hx != p.X &&
                PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
            {
                var tan = Math.Abs(hy - p.Y) / (hx - p.X);
                if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                {
                    m = p; tanMin = tan;
                }
            }
            p = p.Next;
        } while (p != stop);
        return m;
    }

    private static bool SectorContainsSector(Node m, Node p) => Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;

    private static Node LeftMost(Node start)
    {
        Node p = start, left = start;
        do
        {
            if (p.X < left.X || (p.X == left.X && p.Y < left.Y)) left = p;
            p = p.Next;
        } while (p != start);
        return left;
    }

    private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
        (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
        (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
        (bx - px) * (cy - py) >= (cx - px) * (by - py);

    private static bool IsValidDiagonal(Node a, Node b) =>
        a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b) &&
        (LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) && (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0) ||
         Equals(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0);

    private static double Area(Node p, Node q, Node r) => (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

    private static bool Equals(Node a, Node b) => a.X == b.X && a.Y == b.Y;

    private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
    {
        int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
        if (o1 != o2 && o3 != o4) return true;
        if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
        if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
        if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
        if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
        return false;
    }

    private static bool OnSegment(Node p, Node q, Node r) =>
        q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);

    private static int Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : 0;

    private static bool IntersectsPolygon(Node a, Node b)
    {
        var p = a;
        do
        {
            if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b)) return true;
            p = p.Next;
        } while (p != a);
        return false;
    }

    private static bool LocallyInside(Node a, Node b) =>
        Area(a.Prev, a, a.Next) < 0 ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0 : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;

    private static bool MiddleInside(Node a, Node b)
    {
        var p = a;
        var inside = false;
        double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
        do
        {
            if (((p.Y > py) != (p.Next.Y > py)) && p.Next.Y != p.Y && (px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X))
                inside = !inside;
            p = p.Next;
        } while (p != a);
        return inside;
    }

    private static Node SplitPolygon(Node a, Node b)
    {
        var a2 = new Node(a.I, a.X, a.Y);
        var b2 = new Node(b.I, b.X, b.Y);
        Node an = a.Next, bp = b.Prev;
        a.Next = b; b.Prev = a;
        a2.Next = an; an.Prev = a2;
        b2.Next = a2; a2.Prev = b2;
        bp.Next = b2; b2.Prev = bp;
        return b2;
    }

    private static Node Insert(int i, double x, double y, Node? last)
    {
        var p = new Node(i, x, y);
        if (last is null) { p.Prev = p; p.Next = p; }
        else { p.Next = last.Next; p.Prev = last; last.Next.Prev = p; last.Next = p; }
        return p;
    }

    private static void Remove(Node p) { p.Next.Prev = p.Prev; p.Prev.Next = p.Next; }

    private static double SignedArea(IReadOnlyList<double> d, int start, int end)
    {
        double sum = 0;
        for (int i = start, j = end - 2; i < end; i += 2) { sum += (d[j] - d[i]) * (d[i + 1] + d[j + 1]); j = i; }
        return sum;
    }
}
