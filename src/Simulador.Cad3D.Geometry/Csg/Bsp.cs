namespace Simulador.Cad3D.Geometry.Csg;

// Operaciones booleanas por árboles BSP sobre polígonos convexos.
// Adaptación a C# del algoritmo de csg.js (Evan Wallace, licencia MIT).

internal sealed class Plane
{
    public const double Epsilon = 1e-5;
    public Vec3 Normal;
    public double W;

    public Plane(Vec3 normal, double w) { Normal = normal; W = w; }

    public static Plane? FromPoints(Vec3 a, Vec3 b, Vec3 c)
    {
        var n = (b - a).Cross(c - a);
        if (n.Length < 1e-12) return null;
        n = n.Normalized();
        return new Plane(n, n.Dot(a));
    }

    public Plane Clone() => new(Normal, W);
    public void Flip() { Normal = -Normal; W = -W; }

    private const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

    public void SplitPolygon(Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack, List<Polygon> front, List<Polygon> back)
    {
        int polygonType = 0;
        var verts = polygon.Vertices;
        var types = new int[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            double t = Normal.Dot(verts[i]) - W;
            int type = t < -Epsilon ? Back : t > Epsilon ? Front : Coplanar;
            polygonType |= type;
            types[i] = type;
        }

        switch (polygonType)
        {
            case Coplanar:
                (Normal.Dot(polygon.Plane.Normal) > 0 ? coplanarFront : coplanarBack).Add(polygon);
                break;
            case Front:
                front.Add(polygon);
                break;
            case Back:
                back.Add(polygon);
                break;
            default:
                var f = new List<Vec3>(verts.Count + 1);
                var b = new List<Vec3>(verts.Count + 1);
                for (int i = 0; i < verts.Count; i++)
                {
                    int j = (i + 1) % verts.Count;
                    int ti = types[i], tj = types[j];
                    Vec3 vi = verts[i], vj = verts[j];
                    if (ti != Back) f.Add(vi);
                    if (ti != Front) b.Add(vi);
                    if ((ti | tj) == Spanning)
                    {
                        double t = (W - Normal.Dot(vi)) / Normal.Dot(vj - vi);
                        var v = Vec3.Lerp(vi, vj, t);
                        f.Add(v);
                        b.Add(v);
                    }
                }
                if (f.Count >= 3) front.Add(new Polygon(f, polygon.Plane.Clone()));
                if (b.Count >= 3) back.Add(new Polygon(b, polygon.Plane.Clone()));
                break;
        }
    }
}

/// <summary>Polígono convexo plano, vértices en sentido antihorario vistos desde el lado de la normal.</summary>
internal sealed class Polygon
{
    public List<Vec3> Vertices;
    public Plane Plane;

    public Polygon(List<Vec3> vertices, Plane plane) { Vertices = vertices; Plane = plane; }

    public static Polygon? Create(IReadOnlyList<Vec3> vertices)
    {
        if (vertices.Count < 3) return null;
        // Plano a partir de la normal de Newell, robusta frente a vértices casi alineados.
        var n = Vec3.Zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        if (n.Length < 1e-12) return null;
        n = n.Normalized();
        return new Polygon(new List<Vec3>(vertices), new Plane(n, n.Dot(vertices[0])));
    }

    public Polygon Clone() => new(new List<Vec3>(Vertices), Plane.Clone());

    public void Flip()
    {
        Vertices.Reverse();
        Plane.Flip();
    }
}

internal sealed class Node
{
    private Plane? _plane;
    private Node? _front;
    private Node? _back;
    private List<Polygon> _polygons = new();

    public Node(List<Polygon>? polygons = null)
    {
        if (polygons is not null) Build(polygons);
    }

    public void Invert()
    {
        foreach (var p in _polygons) p.Flip();
        _plane?.Flip();
        _front?.Invert();
        _back?.Invert();
        (_front, _back) = (_back, _front);
    }

    public List<Polygon> ClipPolygons(List<Polygon> polygons)
    {
        if (_plane is null) return new List<Polygon>(polygons);
        var front = new List<Polygon>();
        var back = new List<Polygon>();
        foreach (var p in polygons) _plane.SplitPolygon(p, front, back, front, back);
        if (_front is not null) front = _front.ClipPolygons(front);
        back = _back is not null ? _back.ClipPolygons(back) : new List<Polygon>();
        front.AddRange(back);
        return front;
    }

    public void ClipTo(Node bsp)
    {
        _polygons = bsp.ClipPolygons(_polygons);
        _front?.ClipTo(bsp);
        _back?.ClipTo(bsp);
    }

    public List<Polygon> AllPolygons()
    {
        var result = new List<Polygon>();
        var stack = new Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            result.AddRange(n._polygons);
            if (n._front is not null) stack.Push(n._front);
            if (n._back is not null) stack.Push(n._back);
        }
        return result;
    }

    public void Build(List<Polygon> polygons)
    {
        if (polygons.Count == 0) return;
        _plane ??= polygons[0].Plane.Clone();
        var front = new List<Polygon>();
        var back = new List<Polygon>();
        foreach (var p in polygons) _plane.SplitPolygon(p, _polygons, _polygons, front, back);
        if (front.Count > 0) (_front ??= new Node()).Build(front);
        if (back.Count > 0) (_back ??= new Node()).Build(back);
    }
}

/// <summary>Sólido como conjunto de polígonos convexos que cierran un volumen.</summary>
internal sealed class CsgSolid
{
    public List<Polygon> Polygons { get; }

    public CsgSolid(List<Polygon> polygons) { Polygons = polygons; }

    public CsgSolid Clone() => new(Polygons.Select(p => p.Clone()).ToList());

    public CsgSolid Union(CsgSolid other)
    {
        var a = new Node(Clone().Polygons);
        var b = new Node(other.Clone().Polygons);
        a.ClipTo(b);
        b.ClipTo(a);
        b.Invert();
        b.ClipTo(a);
        b.Invert();
        a.Build(b.AllPolygons());
        return new CsgSolid(a.AllPolygons());
    }

    public CsgSolid Subtract(CsgSolid other)
    {
        var a = new Node(Clone().Polygons);
        var b = new Node(other.Clone().Polygons);
        a.Invert();
        a.ClipTo(b);
        b.ClipTo(a);
        b.Invert();
        b.ClipTo(a);
        b.Invert();
        a.Build(b.AllPolygons());
        a.Invert();
        return new CsgSolid(a.AllPolygons());
    }

    public CsgSolid Intersect(CsgSolid other)
    {
        var a = new Node(Clone().Polygons);
        var b = new Node(other.Clone().Polygons);
        a.Invert();
        b.ClipTo(a);
        b.Invert();
        a.ClipTo(b);
        b.ClipTo(a);
        a.Build(b.AllPolygons());
        a.Invert();
        return new CsgSolid(a.AllPolygons());
    }

    public CsgSolid Transformed(Transform3 t)
    {
        bool mirror = t.Determinant < 0;
        var result = new List<Polygon>(Polygons.Count);
        foreach (var p in Polygons)
        {
            var verts = p.Vertices.Select(t.Apply).ToList();
            if (mirror) verts.Reverse();
            if (Polygon.Create(verts) is { } np) result.Add(np);
        }
        return new CsgSolid(result);
    }
}
