namespace Simulador.Cad3D.Geometry.Scan;

/// <summary>
/// Malla de un escaneo 3D (móvil con LiDAR, fotogrametría…): triángulos indexados y, si el archivo los trae, un color
/// por vértice (ARGB). Puede estar abierta, tener islas sueltas, normales revueltas y cientos de miles de triángulos;
/// <see cref="ScanRepair"/> la deja usable.
/// </summary>
public sealed class ScanMesh
{
    public List<Vec3> Vertices { get; } = new();
    public List<int> Indices { get; } = new();
    /// <summary>Color de cada vértice (mismo orden que <see cref="Vertices"/>); null si el archivo no trae colores.</summary>
    public List<uint>? Colors { get; set; }

    /// <summary>Unidades que declara el propio archivo (USD: metersPerUnit); null si no lo dice.</summary>
    public double? MetersPerUnit { get; set; }

    public int TriangleCount => Indices.Count / 3;
    public bool HasColors => Colors is { } c && c.Count == Vertices.Count;

    public (Vec3 Min, Vec3 Max) Bounds()
    {
        if (Vertices.Count == 0) return (Vec3.Zero, Vec3.Zero);
        var min = Vertices[0];
        var max = Vertices[0];
        foreach (var v in Vertices) { min = Vec3.Min(min, v); max = Vec3.Max(max, v); }
        return (min, max);
    }

    public Vec3 Size { get { var (a, b) = Bounds(); return b - a; } }

    public ScanMesh Clone()
    {
        var m = new ScanMesh();
        m.Vertices.AddRange(Vertices);
        m.Indices.AddRange(Indices);
        if (Colors is not null) m.Colors = new List<uint>(Colors);
        m.MetersPerUnit = MetersPerUnit;
        return m;
    }

    /// <summary>Aplica una transformación a todos los vértices (en el sitio).</summary>
    public ScanMesh Transform(Func<Vec3, Vec3> f)
    {
        for (int i = 0; i < Vertices.Count; i++) Vertices[i] = f(Vertices[i]);
        return this;
    }

    /// <summary>Da la vuelta a todos los triángulos (para cuando una transformación es un espejo).</summary>
    public void FlipWinding()
    {
        for (int i = 0; i + 2 < Indices.Count; i += 3) (Indices[i + 1], Indices[i + 2]) = (Indices[i + 2], Indices[i + 1]);
    }

    /// <summary>Volumen con signo (mm³); positivo si las normales miran hacia fuera. Solo exacto en mallas cerradas.</summary>
    public double Volume()
    {
        double v = 0;
        for (int i = 0; i + 2 < Indices.Count; i += 3)
            v += Vertices[Indices[i]].Dot(Vertices[Indices[i + 1]].Cross(Vertices[Indices[i + 2]]));
        return v / 6.0;
    }

    /// <summary>Número de aristas de borde (las que solo tienen un triángulo): 0 = malla cerrada.</summary>
    public int CountBorderEdges()
    {
        var count = new Dictionary<(int, int), int>();
        for (int i = 0; i + 2 < Indices.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = Indices[i + k], b = Indices[i + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                count[key] = count.GetValueOrDefault(key) + 1;
            }
        return count.Values.Count(n => n == 1);
    }

    /// <summary>Malla del módulo 3D (sin colores).</summary>
    public Mesh ToMesh()
    {
        var m = new Mesh();
        m.Vertices.AddRange(Vertices);
        m.Indices.AddRange(Indices);
        return m;
    }

    /// <summary>Color de cada triángulo (media de sus tres vértices); null si no hay colores.</summary>
    public List<uint>? TriangleColors()
    {
        if (!HasColors) return null;
        var r = new List<uint>(TriangleCount);
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            uint a = Colors![Indices[i]], b = Colors[Indices[i + 1]], c = Colors[Indices[i + 2]];
            uint Ch(int s) => ((((a >> s) & 0xFF) + ((b >> s) & 0xFF) + ((c >> s) & 0xFF)) / 3) << s;
            r.Add(0xFF000000 | Ch(16) | Ch(8) | Ch(0));
        }
        return r;
    }

    /// <summary>Color medio de la malla (o null sin colores), útil como color de la pieza.</summary>
    public uint? AverageColor()
    {
        if (!HasColors || Colors!.Count == 0) return null;
        double r = 0, g = 0, b = 0;
        foreach (var c in Colors) { r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF; }
        int n = Colors.Count;
        return 0xFF000000 | ((uint)(r / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n);
    }

    /// <summary>Junta varias mallas en una.</summary>
    public static ScanMesh Merge(IEnumerable<ScanMesh> meshes)
    {
        var list = meshes.ToList();
        var r = new ScanMesh();
        bool colors = list.Count > 0 && list.Any(m => m.HasColors);
        if (colors) r.Colors = new List<uint>();
        foreach (var m in list)
        {
            int o = r.Vertices.Count;
            r.Vertices.AddRange(m.Vertices);
            r.Indices.AddRange(m.Indices.Select(i => i + o));
            if (colors) r.Colors!.AddRange(m.HasColors ? m.Colors! : Enumerable.Repeat(0xFFB9C0C7u, m.Vertices.Count));
        }
        return r;
    }
}
