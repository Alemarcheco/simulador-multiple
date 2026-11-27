namespace Simulador.Cad3D.Geometry.Scan;

/// <summary>
/// Herramientas para arreglar un escaneo: unir vértices, quitar trozos sueltos, cortar la base, orientar las caras,
/// simplificar, rellenar agujeros, suavizar, enderezar y poner a escala. Todas devuelven una malla nueva o la cambian
/// en el sitio y son deterministas (mismo archivo + mismos ajustes = misma pieza).
/// </summary>
public static class ScanRepair
{
    // ------------------------------------------------------------------ unir y limpiar

    /// <summary>Une los vértices a menos de <paramref name="tolerance"/> mm y quita triángulos degenerados o repetidos.</summary>
    public static ScanMesh Weld(ScanMesh m, double tolerance)
    {
        tolerance = Math.Max(tolerance, 1e-9);
        var r = new ScanMesh();
        bool colors = m.HasColors;
        var acc = new List<(double R, double G, double B, int N)>();
        var lookup = new Dictionary<(long, long, long), int>();
        var map = new int[m.Vertices.Count];
        double inv = 1.0 / tolerance;
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            var v = m.Vertices[i];
            var key = ((long)Math.Round(v.X * inv), (long)Math.Round(v.Y * inv), (long)Math.Round(v.Z * inv));
            if (!lookup.TryGetValue(key, out var idx))
            {
                idx = r.Vertices.Count;
                r.Vertices.Add(v);
                lookup[key] = idx;
                if (colors) acc.Add((0, 0, 0, 0));
            }
            map[i] = idx;
            if (colors)
            {
                uint c = m.Colors![i];
                var a = acc[idx];
                acc[idx] = (a.R + ((c >> 16) & 0xFF), a.G + ((c >> 8) & 0xFF), a.B + (c & 0xFF), a.N + 1);
            }
        }
        if (colors) r.Colors = acc.Select(ToColor).ToList();
        AddCleanTriangles(r, m.Indices.Select(i => map[i]));
        return Compact(r);
    }

    private static uint ToColor((double R, double G, double B, int N) a) =>
        a.N == 0 ? 0xFFB9C0C7 : 0xFF000000 | ((uint)(a.R / a.N) << 16) | ((uint)(a.G / a.N) << 8) | (uint)(a.B / a.N);

    /// <summary>Añade triángulos saltando los degenerados y los repetidos (mismos tres vértices).</summary>
    private static void AddCleanTriangles(ScanMesh r, IEnumerable<int> indices)
    {
        var seen = new HashSet<(int, int, int)>();
        var list = indices as IList<int> ?? indices.ToList();
        for (int i = 0; i + 2 < list.Count; i += 3)
        {
            int a = list[i], b = list[i + 1], c = list[i + 2];
            if (a == b || b == c || a == c) continue;
            var area = (r.Vertices[b] - r.Vertices[a]).Cross(r.Vertices[c] - r.Vertices[a]).Length;
            if (area < 1e-14) continue;
            if (!seen.Add(SortedKey(a, b, c))) continue;
            r.Indices.Add(a); r.Indices.Add(b); r.Indices.Add(c);
        }
    }

    private static (int, int, int) SortedKey(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }

    /// <summary>Quita los vértices que no usa ningún triángulo.</summary>
    public static ScanMesh Compact(ScanMesh m)
    {
        var map = Enumerable.Repeat(-1, m.Vertices.Count).ToArray();
        var r = new ScanMesh();
        bool colors = m.HasColors;
        if (colors) r.Colors = new List<uint>();
        foreach (var i in m.Indices)
        {
            if (map[i] < 0)
            {
                map[i] = r.Vertices.Count;
                r.Vertices.Add(m.Vertices[i]);
                if (colors) r.Colors!.Add(m.Colors![i]);
            }
            r.Indices.Add(map[i]);
        }
        return r;
    }

    // ------------------------------------------------------------------ trozos sueltos

    /// <summary>
    /// Quita los trozos sueltos (polvo, la mesa, objetos de alrededor) que tengan menos del <paramref name="percent"/> %
    /// de la superficie del trozo más grande. Devuelve cuántos trozos quitó.
    /// </summary>
    public static ScanMesh RemoveSmallPieces(ScanMesh m, double percent, out int removed)
    {
        removed = 0;
        int nt = m.TriangleCount;
        if (nt == 0 || percent <= 0) return m;
        var parent = Enumerable.Range(0, m.Vertices.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < nt; t++)
        {
            int a = Find(m.Indices[3 * t]), b = Find(m.Indices[3 * t + 1]), c = Find(m.Indices[3 * t + 2]);
            parent[b] = a;
            parent[Find(c)] = a;
        }
        // Tamaño de cada trozo por su superficie (una mota con muchos triángulos diminutos sigue siendo una mota).
        var area = new Dictionary<int, double>();
        for (int t = 0; t < nt; t++)
        {
            var a = m.Vertices[m.Indices[3 * t]];
            double ar = (m.Vertices[m.Indices[3 * t + 1]] - a).Cross(m.Vertices[m.Indices[3 * t + 2]] - a).Length / 2;
            int k = Find(m.Indices[3 * t]);
            area[k] = area.GetValueOrDefault(k) + ar;
        }
        double largest = area.Values.Max();
        double min = largest * percent / 100.0;
        removed = area.Values.Count(s => s < min);
        if (removed == 0) return m;
        var r = new ScanMesh();
        r.Vertices.AddRange(m.Vertices);
        if (m.HasColors) r.Colors = new List<uint>(m.Colors!);
        for (int t = 0; t < nt; t++)
            if (area[Find(m.Indices[3 * t])] >= min)
                for (int k = 0; k < 3; k++) r.Indices.Add(m.Indices[3 * t + k]);
        return Compact(r);
    }

    // ------------------------------------------------------------------ cortar por un plano horizontal

    /// <summary>
    /// Quita todo lo que queda por debajo de <paramref name="z"/> (p. ej. la mesa o el suelo donde estaba la pieza, o
    /// para dejarle una base plana). El corte queda abierto: <see cref="FillHoles"/> le pone tapa.
    /// </summary>
    public static ScanMesh CutBelow(ScanMesh m, double z)
    {
        var r = new ScanMesh();
        r.Vertices.AddRange(m.Vertices);
        bool colors = m.HasColors;
        if (colors) r.Colors = new List<uint>(m.Colors!);
        var cache = new Dictionary<(int, int), int>();
        int Cross(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (cache.TryGetValue(key, out var idx)) return idx;
            var va = m.Vertices[a];
            var vb = m.Vertices[b];
            double t = (z - va.Z) / (vb.Z - va.Z);
            idx = r.Vertices.Count;
            var p = Vec3.Lerp(va, vb, t);
            r.Vertices.Add(new Vec3(p.X, p.Y, z));
            if (colors) r.Colors!.Add(Lerp(m.Colors![a], m.Colors![b], t));
            cache[key] = idx;
            return idx;
        }
        var poly = new List<int>(4);
        for (int i = 0; i + 2 < m.Indices.Count; i += 3)
        {
            int[] tri = [m.Indices[i], m.Indices[i + 1], m.Indices[i + 2]];
            bool[] keep = tri.Select(v => m.Vertices[v].Z >= z).ToArray();
            if (keep.All(k => k)) { r.Indices.AddRange(tri); continue; }
            if (!keep.Any(k => k)) continue;
            poly.Clear();
            for (int k = 0; k < 3; k++)
            {
                int a = tri[k], b = tri[(k + 1) % 3];
                if (keep[k]) poly.Add(a);
                if (keep[k] != keep[(k + 1) % 3]) poly.Add(Cross(a, b));
            }
            for (int k = 1; k + 1 < poly.Count; k++) { r.Indices.Add(poly[0]); r.Indices.Add(poly[k]); r.Indices.Add(poly[k + 1]); }
        }
        var clean = new ScanMesh();
        clean.Vertices.AddRange(r.Vertices);
        clean.Colors = r.Colors;
        AddCleanTriangles(clean, r.Indices);
        return Compact(clean);
    }

    private static uint Lerp(uint a, uint b, double t)
    {
        uint Ch(int s) => (uint)Math.Clamp(Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t), 0, 255) << s;
        return 0xFF000000 | Ch(16) | Ch(8) | Ch(0);
    }

    // ------------------------------------------------------------------ orientación de las caras

    /// <summary>
    /// Pone todos los triángulos de cada trozo en el mismo sentido que sus vecinos y, si el trozo queda del revés
    /// (volumen negativo), le da la vuelta entero. Así las normales miran hacia fuera.
    /// </summary>
    public static void OrientOutward(ScanMesh m)
    {
        int nt = m.TriangleCount;
        if (nt == 0) return;
        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t < nt; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.Indices[3 * t + k], b = m.Indices[3 * t + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeTris.TryGetValue(key, out var l)) edgeTris[key] = l = new List<int>(2);
                l.Add(t);
            }
        bool Has(int t, int a, int b)
        {
            for (int k = 0; k < 3; k++)
                if (m.Indices[3 * t + k] == a && m.Indices[3 * t + (k + 1) % 3] == b) return true;
            return false;
        }
        void Flip(int t) => (m.Indices[3 * t + 1], m.Indices[3 * t + 2]) = (m.Indices[3 * t + 2], m.Indices[3 * t + 1]);

        var comp = Enumerable.Repeat(-1, nt).ToArray();
        var components = new List<List<int>>();
        var queue = new Queue<int>();
        for (int s = 0; s < nt; s++)
        {
            if (comp[s] >= 0) continue;
            var list = new List<int>();
            comp[s] = components.Count;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                list.Add(t);
                for (int k = 0; k < 3; k++)
                {
                    int a = m.Indices[3 * t + k], b = m.Indices[3 * t + (k + 1) % 3];
                    var l = edgeTris[a < b ? (a, b) : (b, a)];
                    if (l.Count != 2) continue; // borde o arista no variedad: no se propaga
                    int n = l[0] == t ? l[1] : l[0];
                    if (comp[n] >= 0) continue;
                    // El vecino bien orientado recorre la arista al revés (b → a).
                    if (Has(n, a, b)) Flip(n);
                    comp[n] = components.Count;
                    queue.Enqueue(n);
                }
            }
            components.Add(list);
        }
        foreach (var list in components)
        {
            // Volumen respecto al centro del trozo: el signo dice si está del revés (fiable si está casi cerrado).
            var c = Vec3.Zero;
            foreach (var t in list) c += m.Vertices[m.Indices[3 * t]];
            c /= list.Count;
            double vol = 0;
            foreach (var t in list)
            {
                var a = m.Vertices[m.Indices[3 * t]] - c;
                var b = m.Vertices[m.Indices[3 * t + 1]] - c;
                var d = m.Vertices[m.Indices[3 * t + 2]] - c;
                vol += a.Dot(b.Cross(d));
            }
            if (vol < 0) foreach (var t in list) Flip(t);
        }
    }

    // ------------------------------------------------------------------ agujeros

    /// <summary>
    /// Tapa los agujeros cuyo borde tenga como mucho <paramref name="maxEdges"/> aristas (0 = todos). Las caras deben
    /// estar ya orientadas (<see cref="OrientOutward"/>). Devuelve cuántos agujeros tapó.
    /// </summary>
    public static int FillHoles(ScanMesh m, int maxEdges)
    {
        var dirEdges = new HashSet<(int, int)>();
        for (int i = 0; i + 2 < m.Indices.Count; i += 3)
            for (int k = 0; k < 3; k++) dirEdges.Add((m.Indices[i + k], m.Indices[i + (k + 1) % 3]));
        // Arista de borde: (a, b) sin su gemela (b, a).
        var next = new Dictionary<int, List<int>>();
        foreach (var (a, b) in dirEdges)
            if (!dirEdges.Contains((b, a)))
            {
                if (!next.TryGetValue(a, out var l)) next[a] = l = new List<int>(1);
                l.Add(b);
            }
        int filled = 0;
        var used = new HashSet<(int, int)>();
        foreach (var start in next.Keys.OrderBy(k => k).ToList())
        {
            foreach (var first in next[start])
            {
                if (used.Contains((start, first))) continue;
                var loop = new List<int> { start };
                used.Add((start, first));
                int cur = first;
                bool closed = false;
                while (loop.Count < 1_000_000)
                {
                    if (cur == start) { closed = true; break; }
                    loop.Add(cur);
                    if (!next.TryGetValue(cur, out var outs)) break;
                    int nx = -1;
                    foreach (var o in outs) if (!used.Contains((cur, o))) { nx = o; break; }
                    if (nx < 0) break;
                    used.Add((cur, nx));
                    cur = nx;
                }
                if (!closed || loop.Count < 3) continue;
                if (maxEdges > 0 && loop.Count > maxEdges) continue;
                // Recorrido al revés para que la tapa mire hacia fuera.
                loop.Reverse();
                Cap(m, loop);
                filled++;
            }
        }
        return filled;
    }

    /// <summary>Tapa un contorno: por orejas sobre su plano medio y, si no puede, en abanico desde el centro.</summary>
    private static void Cap(ScanMesh m, List<int> loop)
    {
        var n = Vec3.Zero;
        var c = Vec3.Zero;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = m.Vertices[loop[i]];
            var b = m.Vertices[loop[(i + 1) % loop.Count]];
            n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            c += a;
        }
        c /= loop.Count;
        n = n.Normalized();
        if (loop.Count == 3) { m.Indices.AddRange(loop); return; }
        if (n.Length > 0.5 && loop.Count <= 5000 && EarClip(m, loop, n) is { } tris)
        {
            m.Indices.AddRange(tris);
            return;
        }
        int ci = m.Vertices.Count;
        m.Vertices.Add(c);
        if (m.HasColors || m.Colors is not null)
        {
            var avg = loop.Select(i => m.Colors![i]).ToList();
            m.Colors!.Add(Average(avg));
        }
        for (int i = 0; i < loop.Count; i++) { m.Indices.Add(ci); m.Indices.Add(loop[i]); m.Indices.Add(loop[(i + 1) % loop.Count]); }
    }

    private static uint Average(List<uint> cs)
    {
        double r = 0, g = 0, b = 0;
        foreach (var c in cs) { r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF; }
        int k = Math.Max(1, cs.Count);
        return 0xFF000000 | ((uint)(r / k) << 16) | ((uint)(g / k) << 8) | (uint)(b / k);
    }

    private static List<int>? EarClip(ScanMesh m, List<int> loop, Vec3 normal)
    {
        // Base del plano: u, v perpendiculares a la normal (contorno antihorario visto desde la normal).
        var u = (Math.Abs(normal.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY).Cross(normal).Normalized();
        var v = normal.Cross(u);
        var pts = loop.Select(i => new Vec2(m.Vertices[i].Dot(u), m.Vertices[i].Dot(v))).ToList();
        var idx = Enumerable.Range(0, loop.Count).ToList();
        var result = new List<int>((loop.Count - 2) * 3);
        int guard = 0;
        while (idx.Count > 3 && guard++ < loop.Count * loop.Count + 10)
        {
            bool cut = false;
            for (int k = 0; k < idx.Count; k++)
            {
                int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                Vec2 a = pts[ia], b = pts[ib], cc = pts[ic];
                if ((b - a).Cross(cc - b) <= 1e-12) continue; // vértice cóncavo
                bool inside = false;
                foreach (var j in idx)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    if (InTriangle(pts[j], a, b, cc)) { inside = true; break; }
                }
                if (inside) continue;
                result.Add(loop[ia]); result.Add(loop[ib]); result.Add(loop[ic]);
                idx.RemoveAt(k);
                cut = true;
                break;
            }
            if (!cut) return null; // contorno que no es plano ni simple: lo hará el abanico
        }
        if (idx.Count == 3) { result.Add(loop[idx[0]]); result.Add(loop[idx[1]]); result.Add(loop[idx[2]]); }
        return result;
    }

    private static bool InTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        double d1 = (b - a).Cross(p - a), d2 = (c - b).Cross(p - b), d3 = (a - c).Cross(p - c);
        return d1 >= 0 && d2 >= 0 && d3 >= 0;
    }

    // ------------------------------------------------------------------ simplificar

    /// <summary>
    /// Reduce la malla a unos <paramref name="targetTriangles"/> triángulos agrupando los vértices próximos en una
    /// rejilla (rápido y robusto con escaneos de millones de triángulos). Mantiene la forma general y los colores.
    /// </summary>
    public static ScanMesh Simplify(ScanMesh m, int targetTriangles)
    {
        if (targetTriangles <= 0 || m.TriangleCount <= targetTriangles) return m;
        var size = m.Size;
        double diag = Math.Max(size.Length, 1e-6);
        // Búsqueda del tamaño de celda que da el número de triángulos pedido.
        double lo = diag / 4000, hi = diag / 4;
        ScanMesh? best = null;
        for (int it = 0; it < 14; it++)
        {
            double cell = Math.Sqrt(lo * hi);
            var r = Cluster(m, cell);
            if (r.TriangleCount > targetTriangles) lo = cell;
            else { hi = cell; best = r; }
            if (best is not null && Math.Abs(best.TriangleCount - targetTriangles) < targetTriangles * 0.08) break;
        }
        return best ?? Cluster(m, hi);
    }

    private static ScanMesh Cluster(ScanMesh m, double cell)
    {
        var (min, _) = m.Bounds();
        double inv = 1.0 / cell;
        var cells = new Dictionary<(int, int, int), int>();
        var sum = new List<(Vec3 P, double R, double G, double B, int N)>();
        var map = new int[m.Vertices.Count];
        bool colors = m.HasColors;
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            var d = (m.Vertices[i] - min) * inv;
            var key = ((int)d.X, (int)d.Y, (int)d.Z);
            if (!cells.TryGetValue(key, out var idx)) { idx = sum.Count; cells[key] = idx; sum.Add((Vec3.Zero, 0, 0, 0, 0)); }
            map[i] = idx;
            var s = sum[idx];
            uint c = colors ? m.Colors![i] : 0;
            sum[idx] = (s.P + m.Vertices[i], s.R + ((c >> 16) & 0xFF), s.G + ((c >> 8) & 0xFF), s.B + (c & 0xFF), s.N + 1);
        }
        var r = new ScanMesh();
        foreach (var s in sum) r.Vertices.Add(s.P / s.N);
        if (colors) r.Colors = sum.Select(s => ToColor((s.R, s.G, s.B, s.N))).ToList();
        AddCleanTriangles(r, m.Indices.Select(i => map[i]).ToList());
        return Compact(r);
    }

    // ------------------------------------------------------------------ suavizar

    /// <summary>Suavizado de Taubin (quita el «ruido» del escaneo sin encoger la pieza).</summary>
    public static void Smooth(ScanMesh m, int iterations)
    {
        if (iterations <= 0) return;
        var nb = new HashSet<int>[m.Vertices.Count];
        for (int i = 0; i < nb.Length; i++) nb[i] = new HashSet<int>();
        for (int i = 0; i + 2 < m.Indices.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = m.Indices[i + k], b = m.Indices[i + (k + 1) % 3];
                nb[a].Add(b); nb[b].Add(a);
            }
        var tmp = new Vec3[m.Vertices.Count];
        void Step(double f)
        {
            for (int i = 0; i < tmp.Length; i++)
            {
                if (nb[i].Count == 0) { tmp[i] = m.Vertices[i]; continue; }
                var avg = Vec3.Zero;
                foreach (var j in nb[i]) avg += m.Vertices[j];
                avg /= nb[i].Count;
                tmp[i] = m.Vertices[i] + (avg - m.Vertices[i]) * f;
            }
            for (int i = 0; i < tmp.Length; i++) m.Vertices[i] = tmp[i];
        }
        for (int it = 0; it < iterations; it++) { Step(0.5); Step(-0.53); }
    }

    // ------------------------------------------------------------------ escala y posición

    /// <summary>
    /// Ángulo (grados) que hay que girar alrededor de Z para que el lado largo de la planta quede según X
    /// (análisis de componentes principales de los vértices en planta).
    /// </summary>
    public static double PrincipalAngleZ(ScanMesh m)
    {
        if (m.Vertices.Count < 3) return 0;
        double cx = m.Vertices.Average(v => v.X), cy = m.Vertices.Average(v => v.Y);
        double sxx = 0, syy = 0, sxy = 0;
        foreach (var v in m.Vertices)
        {
            double x = v.X - cx, y = v.Y - cy;
            sxx += x * x; syy += y * y; sxy += x * y;
        }
        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return -angle * 180 / Math.PI;
    }

    public static void RotateZ(ScanMesh m, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        m.Transform(v => new Vec3(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z));
    }

    /// <summary>Escala uniforme (en el sitio).</summary>
    public static void Scale(ScanMesh m, double factor)
    {
        if (factor <= 0 || factor == 1) return;
        m.Transform(v => v * factor);
    }

    /// <summary>Deja la pieza apoyada en Z = 0 y centrada en X e Y.</summary>
    public static void PlaceOnFloor(ScanMesh m)
    {
        var (min, max) = m.Bounds();
        var shift = new Vec3(-(min.X + max.X) / 2, -(min.Y + max.Y) / 2, -min.Z);
        m.Transform(v => v + shift);
    }
}
