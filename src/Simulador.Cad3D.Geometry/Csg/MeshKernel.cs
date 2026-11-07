namespace Simulador.Cad3D.Geometry.Csg;

/// <summary>
/// Motor de sólidos por mallas y árboles BSP, en C# puro. Sirve para el prototipo y para las pruebas;
/// el motor exacto (OpenCASCADE) implementará la misma interfaz <see cref="IGeometryKernel"/>.
/// </summary>
public sealed class MeshKernel : IGeometryKernel
{
    public string Name => "Mallas (BSP)";
    public bool SupportsExactGeometry => false;

    private sealed class Solid : ISolid
    {
        public Solid(CsgSolid csg) { Csg = csg; }
        public CsgSolid Csg { get; }
    }

    private static CsgSolid Get(ISolid s) => s is Solid m ? m.Csg : throw new ArgumentException("Sólido de otro motor geométrico.");

    private static ISolid FromFaces(IEnumerable<IReadOnlyList<Vec3>> faces) =>
        new Solid(new CsgSolid(faces.Select(Polygon.Create).OfType<Polygon>().ToList()));

    public ISolid Box(Vec3 size)
    {
        double x = size.X, y = size.Y, z = size.Z;
        Vec3 P(double a, double b, double c) => new(a, b, c);
        return FromFaces(new[]
        {
            new[] { P(0, 0, 0), P(0, y, 0), P(x, y, 0), P(x, 0, 0) }, // abajo (normal -Z)
            new[] { P(0, 0, z), P(x, 0, z), P(x, y, z), P(0, y, z) }, // arriba
            new[] { P(0, 0, 0), P(x, 0, 0), P(x, 0, z), P(0, 0, z) }, // frente (-Y)
            new[] { P(0, y, 0), P(0, y, z), P(x, y, z), P(x, y, 0) }, // fondo (+Y)
            new[] { P(0, 0, 0), P(0, 0, z), P(0, y, z), P(0, y, 0) }, // izquierda (-X)
            new[] { P(x, 0, 0), P(x, y, 0), P(x, y, z), P(x, 0, z) }, // derecha (+X)
        });
    }

    public ISolid Cylinder(double radiusBottom, double radiusTop, double height, int segments)
    {
        segments = Math.Max(3, segments);
        var faces = new List<IReadOnlyList<Vec3>>();
        var bottom = Polygon2D.Circle(Math.Max(radiusBottom, 0), segments);
        var top = Polygon2D.Circle(Math.Max(radiusTop, 0), segments);
        bool coneBottom = radiusBottom <= 1e-9, coneTop = radiusTop <= 1e-9;
        if (!coneBottom) faces.Add(bottom.Select(p => new Vec3(p.X, p.Y, 0)).Reverse().ToArray());
        if (!coneTop) faces.Add(top.Select(p => new Vec3(p.X, p.Y, height)).ToArray());
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % segments;
            var b0 = new Vec3(bottom[i].X, bottom[i].Y, 0);
            var b1 = new Vec3(bottom[j].X, bottom[j].Y, 0);
            var t0 = new Vec3(top[i].X, top[i].Y, height);
            var t1 = new Vec3(top[j].X, top[j].Y, height);
            if (coneBottom) faces.Add(new[] { Vec3.Zero, t1, t0 });
            else if (coneTop) faces.Add(new[] { b0, b1, new Vec3(0, 0, height) });
            else faces.Add(new[] { b0, b1, t1, t0 });
        }
        return FromFaces(faces);
    }

    public ISolid Sphere(double radius, int segments)
    {
        int slices = Math.Max(6, segments), stacks = Math.Max(3, segments / 2);
        var faces = new List<IReadOnlyList<Vec3>>();
        Vec3 V(int i, int j)
        {
            double theta = 2 * Math.PI * i / slices, phi = Math.PI * j / stacks;
            return new Vec3(radius * Math.Sin(phi) * Math.Cos(theta), radius * Math.Sin(phi) * Math.Sin(theta), -radius * Math.Cos(phi));
        }
        for (int i = 0; i < slices; i++)
            for (int j = 0; j < stacks; j++)
            {
                var v = new List<Vec3> { V(i, j) };
                if (j > 0) v.Add(V(i + 1, j));
                if (j < stacks - 1) v.Add(V(i + 1, j + 1));
                v.Add(V(i, j + 1));
                faces.Add(v);
            }
        return FromFaces(faces);
    }

    public ISolid Extrude(IReadOnlyList<Vec2> profile, double height)
    {
        var pts = Polygon2D.Normalize(profile);
        if (pts.Count < 3) throw new ArgumentException("El perfil necesita al menos 3 puntos.");
        var faces = new List<IReadOnlyList<Vec3>>();
        foreach (var (a, b, c) in Polygon2D.Triangulate(pts))
        {
            faces.Add(new[] { new Vec3(pts[a].X, pts[a].Y, height), new Vec3(pts[b].X, pts[b].Y, height), new Vec3(pts[c].X, pts[c].Y, height) });
            faces.Add(new[] { new Vec3(pts[c].X, pts[c].Y, 0), new Vec3(pts[b].X, pts[b].Y, 0), new Vec3(pts[a].X, pts[a].Y, 0) });
        }
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            faces.Add(new[] { new Vec3(p.X, p.Y, 0), new Vec3(q.X, q.Y, 0), new Vec3(q.X, q.Y, height), new Vec3(p.X, p.Y, height) });
        }
        return FromFaces(faces);
    }

    public ISolid ExtrudeRegion(IReadOnlyList<Vec2> outer, IReadOnlyList<IReadOnlyList<Vec2>> holes, double height)
    {
        var o = Polygon2D.Normalize(outer);
        var hs = holes.Select(h => { var n = Polygon2D.Normalize(h); n.Reverse(); return n; }).Where(h => h.Count >= 3).ToList();
        if (hs.Count == 0) return Extrude(o, height);
        // Tapas: un solo polígono con puentes hacia los huecos, triangulado por orejas.
        var merged = Polygon2D.MergeHoles(o, hs);
        var faces = new List<IReadOnlyList<Vec3>>();
        foreach (var (a, b, c) in Polygon2D.Triangulate(merged))
        {
            faces.Add(new[] { new Vec3(merged[a].X, merged[a].Y, height), new Vec3(merged[b].X, merged[b].Y, height), new Vec3(merged[c].X, merged[c].Y, height) });
            faces.Add(new[] { new Vec3(merged[c].X, merged[c].Y, 0), new Vec3(merged[b].X, merged[b].Y, 0), new Vec3(merged[a].X, merged[a].Y, 0) });
        }
        // Paredes: exterior antihoraria (hacia fuera) y huecos en sentido horario (hacia dentro del hueco).
        foreach (var ring in hs.Prepend(o))
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                faces.Add(new[] { new Vec3(p.X, p.Y, 0), new Vec3(q.X, q.Y, 0), new Vec3(q.X, q.Y, height), new Vec3(p.X, p.Y, height) });
            }
        return FromFaces(faces);
    }

    public ISolid Transform(ISolid solid, Transform3 transform) => new Solid(Get(solid).Transformed(transform));

    public ISolid Union(ISolid a, ISolid b)
    {
        var ca = Get(a); var cb = Get(b);
        // Sólidos separados (p. ej. letras o copias de una repetición): basta con juntar sus caras,
        // sin pasar por los árboles BSP (más rápido y sin cortes que dejen uniones en T).
        if (!BoundsOverlap(ca, cb)) return new Solid(new CsgSolid(ca.Clone().Polygons.Concat(cb.Clone().Polygons).ToList()));
        return new Solid(ca.Union(cb));
    }


    private static bool BoundsOverlap(CsgSolid a, CsgSolid b)
    {
        static (Vec3 Min, Vec3 Max) B(CsgSolid s)
        {
            var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var p in s.Polygons) foreach (var v in p.Vertices) { min = Vec3.Min(min, v); max = Vec3.Max(max, v); }
            return (min, max);
        }
        if (a.Polygons.Count == 0 || b.Polygons.Count == 0) return false;
        var (amin, amax) = B(a); var (bmin, bmax) = B(b);
        const double gap = 1e-6; // si se tocan, mejor la unión de verdad (comparten caras)
        return amin.X <= bmax.X + gap && bmin.X <= amax.X + gap && amin.Y <= bmax.Y + gap && bmin.Y <= amax.Y + gap
            && amin.Z <= bmax.Z + gap && bmin.Z <= amax.Z + gap;
    }
    public ISolid Subtract(ISolid a, ISolid b) => new Solid(Get(a).Subtract(Get(b)));
    public ISolid Intersect(ISolid a, ISolid b) => new Solid(Get(a).Intersect(Get(b)));

    public Mesh ToMesh(ISolid solid)
    {
        var tris = new List<(Vec3, Vec3, Vec3)>();
        foreach (var p in Get(solid).Polygons)
            for (int i = 1; i + 1 < p.Vertices.Count; i++)
                tris.Add((p.Vertices[0], p.Vertices[i], p.Vertices[i + 1]));
        return MeshRepair.FixTJunctions(Mesh.FromTriangles(tris));
    }
}
