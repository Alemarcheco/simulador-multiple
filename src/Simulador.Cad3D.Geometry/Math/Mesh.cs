namespace Simulador.Cad3D.Geometry;

/// <summary>Malla de triángulos indexada. Orientación antihoraria vista desde fuera (normal hacia fuera).</summary>
public sealed class Mesh
{
    public List<Vec3> Vertices { get; } = new();
    public List<int> Indices { get; } = new();

    public int TriangleCount => Indices.Count / 3;

    public IEnumerable<(Vec3 A, Vec3 B, Vec3 C)> Triangles()
    {
        for (int i = 0; i + 2 < Indices.Count; i += 3)
            yield return (Vertices[Indices[i]], Vertices[Indices[i + 1]], Vertices[Indices[i + 2]]);
    }

    public (Vec3 Min, Vec3 Max) Bounds()
    {
        if (Vertices.Count == 0) return (Vec3.Zero, Vec3.Zero);
        var min = Vertices[0];
        var max = Vertices[0];
        foreach (var v in Vertices) { min = Vec3.Min(min, v); max = Vec3.Max(max, v); }
        return (min, max);
    }

    /// <summary>Volumen con signo (mm³), por el teorema de la divergencia. Positivo en mallas cerradas bien orientadas.</summary>
    public double Volume()
    {
        double v = 0;
        foreach (var (a, b, c) in Triangles()) v += a.Dot(b.Cross(c));
        return v / 6.0;
    }

    /// <summary>
    /// Cuenta las aristas que no comparten exactamente dos triángulos con orientación opuesta.
    /// 0 significa malla cerrada (estanca), apta para imprimir.
    /// </summary>
    public int CountOpenEdges()
    {
        var edges = new Dictionary<(int, int), int>();
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = Indices[i + k], b = Indices[i + (k + 1) % 3];
                edges[(a, b)] = edges.GetValueOrDefault((a, b)) + 1;
            }
        }
        int open = 0;
        foreach (var ((a, b), n) in edges)
            if (n != 1 || edges.GetValueOrDefault((b, a)) != 1) open++;
        return open;
    }

    /// <summary>Construye una malla indexada a partir de triángulos sueltos, uniendo vértices coincidentes.</summary>
    public static Mesh FromTriangles(IEnumerable<(Vec3 A, Vec3 B, Vec3 C)> triangles, double weldTolerance = 1e-5)
    {
        var mesh = new Mesh();
        var lookup = new Dictionary<(long, long, long), int>();
        double inv = 1.0 / weldTolerance;
        int Index(Vec3 v)
        {
            var key = ((long)Math.Round(v.X * inv), (long)Math.Round(v.Y * inv), (long)Math.Round(v.Z * inv));
            if (!lookup.TryGetValue(key, out var idx))
            {
                idx = mesh.Vertices.Count;
                mesh.Vertices.Add(v);
                lookup[key] = idx;
            }
            return idx;
        }
        foreach (var (a, b, c) in triangles)
        {
            int ia = Index(a), ib = Index(b), ic = Index(c);
            if (ia == ib || ib == ic || ia == ic) continue; // triángulo degenerado
            mesh.Indices.Add(ia); mesh.Indices.Add(ib); mesh.Indices.Add(ic);
        }
        return mesh;
    }
}
