namespace Simulador.Cad3D.Geometry.Csg;

/// <summary>
/// Repara las uniones en T que dejan las operaciones booleanas: un vértice que cae en mitad de la arista
/// de un triángulo vecino. Sin esto la malla no es estanca y algunos laminadores avisan de errores.
/// </summary>
internal static class MeshRepair
{
    public static Mesh FixTJunctions(Mesh mesh, int maxPasses = 4, double tolerance = 1e-4)
    {
        for (int pass = 0; pass < maxPasses; pass++)
        {
            var openEdges = OpenEdges(mesh);
            if (openEdges.Count == 0) break;

            var candidates = new HashSet<int>();
            foreach (var (a, b) in openEdges) { candidates.Add(a); candidates.Add(b); }
            var (min, max) = mesh.Bounds();
            var grid = new SpatialGrid(mesh, candidates, cell: Math.Max((max - min).Length / 64, 0.5));

            var tris = new List<(Vec3, Vec3, Vec3)>(mesh.TriangleCount + 16);
            bool changed = false;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                int[] idx = { mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2] };
                var ring = new List<Vec3>(6);
                bool split = false;
                for (int k = 0; k < 3; k++)
                {
                    int ia = idx[k], ib = idx[(k + 1) % 3];
                    ring.Add(mesh.Vertices[ia]);
                    if (!openEdges.Contains((ia, ib))) continue;
                    var onEdge = grid.PointsOnSegment(mesh.Vertices[ia], mesh.Vertices[ib], ia, ib, tolerance);
                    if (onEdge.Count == 0) continue;
                    ring.AddRange(onEdge);
                    split = true;
                }
                if (!split)
                {
                    tris.Add((mesh.Vertices[idx[0]], mesh.Vertices[idx[1]], mesh.Vertices[idx[2]]));
                    continue;
                }
                changed = true;
                // Abanico desde el centroide: nunca genera triángulos degenerados con puntos alineados en las aristas.
                var c = (mesh.Vertices[idx[0]] + mesh.Vertices[idx[1]] + mesh.Vertices[idx[2]]) / 3.0;
                for (int k = 0; k < ring.Count; k++) tris.Add((c, ring[k], ring[(k + 1) % ring.Count]));
            }
            if (!changed) break;
            mesh = Mesh.FromTriangles(tris);
        }
        return mesh;
    }

    private static HashSet<(int, int)> OpenEdges(Mesh mesh)
    {
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            for (int k = 0; k < 3; k++)
                edges.Add((mesh.Indices[i + k], mesh.Indices[i + (k + 1) % 3]));
        var open = new HashSet<(int, int)>();
        foreach (var (a, b) in edges)
            if (!edges.Contains((b, a))) open.Add((a, b));
        return open;
    }

    private sealed class SpatialGrid
    {
        private readonly Dictionary<(int, int, int), List<int>> _cells = new();
        private readonly Mesh _mesh;
        private readonly double _cell;

        public SpatialGrid(Mesh mesh, IEnumerable<int> vertices, double cell)
        {
            _mesh = mesh;
            _cell = cell;
            foreach (var v in vertices)
            {
                var key = Key(mesh.Vertices[v]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
                list.Add(v);
            }
        }

        private (int, int, int) Key(Vec3 p) => ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));

        /// <summary>Vértices que están estrictamente dentro del segmento a-b, ordenados de a hacia b.</summary>
        public List<Vec3> PointsOnSegment(Vec3 a, Vec3 b, int ia, int ib, double tol)
        {
            var found = new List<(double T, Vec3 P)>();
            var d = b - a;
            double len2 = d.Dot(d);
            if (len2 < 1e-18) return new List<Vec3>();
            var (k0, k1) = (Key(Vec3.Min(a, b)), Key(Vec3.Max(a, b)));
            var seen = new HashSet<int>();
            for (int x = k0.Item1; x <= k1.Item1; x++)
            for (int y = k0.Item2; y <= k1.Item2; y++)
            for (int z = k0.Item3; z <= k1.Item3; z++)
            {
                if (!_cells.TryGetValue((x, y, z), out var list)) continue;
                foreach (var v in list)
                {
                    if (v == ia || v == ib || !seen.Add(v)) continue;
                    var p = _mesh.Vertices[v];
                    double t = (p - a).Dot(d) / len2;
                    if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                    var closest = a + d * t;
                    if ((p - closest).Length <= tol) found.Add((t, p));
                }
            }
            return found.OrderBy(f => f.T).Select(f => f.P).ToList();
        }
    }
}
