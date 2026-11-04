using System.Runtime.InteropServices;

namespace Simulador.Cad3D.Geometry.Occt;

/// <summary>Motor de geometría exacta basado en OpenCASCADE (a través de la biblioteca nativa simcad_occt).</summary>
public sealed class OcctKernel : IExactKernel
{
    private sealed class Solid : ISolid
    {
        public Solid(SolidHandle h) { Handle = h; }
        public SolidHandle Handle { get; }
    }

    private OcctKernel(int versionHex)
    {
        Version = $"OpenCASCADE {(versionHex >> 16) & 0xFF}.{(versionHex >> 8) & 0xFF}.{versionHex & 0xFF}";
    }

    /// <summary>Desviación máxima del teselado respecto a la superficie exacta, en mm.</summary>
    public double LinearDeflection { get; set; } = 0.02;

    /// <summary>Ángulo máximo entre triángulos vecinos al teselar, en radianes (0,2 rad ≈ 32 lados por círculo).</summary>
    public double AngularDeflection { get; set; } = 0.2;

    public string Name => Version;
    public string Version { get; }
    public bool SupportsExactGeometry => true;

    /// <summary>Intenta cargar la biblioteca nativa. Devuelve false (y el motivo) si no está o no arranca.</summary>
    public static bool TryCreate(out OcctKernel? kernel, out string? reason)
    {
        try
        {
            kernel = new OcctKernel(Native.sc_version());
            reason = null;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            kernel = null;
            reason = ex.Message;
            return false;
        }
    }

    private static ISolid Wrap(IntPtr p, string what)
    {
        if (p == IntPtr.Zero) throw new InvalidOperationException($"{what}: {Native.LastError()}");
        return new Solid(new SolidHandle(p));
    }

    private static SolidHandle H(ISolid s) => s is Solid o ? o.Handle : throw new ArgumentException("Sólido de otro motor geométrico.");

    private static double[] Flat(IReadOnlyList<Vec2> pts) => pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();

    public ISolid Box(Vec3 size) => Wrap(Native.sc_box(size.X, size.Y, size.Z), "Caja");
    public ISolid Cylinder(double radiusBottom, double radiusTop, double height, int segments) =>
        Wrap(Native.sc_cylinder(radiusBottom, radiusTop, height), "Cilindro");
    public ISolid Sphere(double radius, int segments) => Wrap(Native.sc_sphere(radius), "Esfera");

    public ISolid Extrude(IReadOnlyList<Vec2> profile, double height)
    {
        var pts = Polygon2D.Normalize(profile);
        return Wrap(Native.sc_extrude(Flat(pts), pts.Count, height), "Extrusión");
    }

    public ISolid Revolve(IReadOnlyList<Vec2> profileXZ, double angleDeg) =>
        Wrap(Native.sc_revolve(Flat(profileXZ), profileXZ.Count, angleDeg), "Revolución");

    public ISolid Transform(ISolid solid, Transform3 t)
    {
        var x = t.ApplyVector(Vec3.UnitX);
        var y = t.ApplyVector(Vec3.UnitY);
        var z = t.ApplyVector(Vec3.UnitZ);
        var o = t.Translation;
        var m = new[] { x.X, y.X, z.X, o.X, x.Y, y.Y, z.Y, o.Y, x.Z, y.Z, z.Z, o.Z };
        return Wrap(Native.sc_transform(H(solid), m), "Transformación");
    }

    public ISolid Union(ISolid a, ISolid b) => Wrap(Native.sc_union(H(a), H(b)), "Unión");
    public ISolid Subtract(ISolid a, ISolid b) => Wrap(Native.sc_cut(H(a), H(b)), "Resta");
    public ISolid Intersect(ISolid a, ISolid b) => Wrap(Native.sc_common(H(a), H(b)), "Intersección");
    public ISolid Fillet(ISolid s, double radius, EdgeSelection edges) => Wrap(Native.sc_fillet(H(s), radius, (int)edges), "Redondeo");
    public ISolid Chamfer(ISolid s, double distance, EdgeSelection edges) => Wrap(Native.sc_chamfer(H(s), distance, (int)edges), "Chaflán");
    public ISolid ShellOpenTop(ISolid s, double thickness) => Wrap(Native.sc_shell_open_top(H(s), thickness), "Vaciado");

    public double Volume(ISolid s) => Native.sc_volume(H(s));

    public Mesh ToMesh(ISolid solid)
    {
        if (Native.sc_mesh(H(solid), LinearDeflection, AngularDeflection, out var pv, out int nv, out var pt, out int nt) == 0)
            throw new InvalidOperationException($"Teselado: {Native.LastError()}");
        try
        {
            var verts = new double[nv * 3];
            var tris = new int[nt * 3];
            if (nv > 0) Marshal.Copy(pv, verts, 0, verts.Length);
            if (nt > 0) Marshal.Copy(pt, tris, 0, tris.Length);
            Vec3 V(int i) => new(verts[3 * i], verts[3 * i + 1], verts[3 * i + 2]);
            var triangles = new List<(Vec3, Vec3, Vec3)>(nt);
            for (int i = 0; i < nt; i++) triangles.Add((V(tris[3 * i]), V(tris[3 * i + 1]), V(tris[3 * i + 2])));
            // Las caras comparten los nodos de sus aristas: al unir vértices coincidentes la malla queda cerrada.
            // Repara las uniones en T que a veces quedan entre caras vecinas (p. ej. donde termina un redondeo).
            return Csg.MeshRepair.FixTJunctions(Mesh.FromTriangles(triangles, weldTolerance: 1e-4));
        }
        finally
        {
            Native.sc_free(pv);
            Native.sc_free(pt);
        }
    }

    public void ExportStep(string path, IReadOnlyList<(string Name, ISolid Solid)> solids)
    {
        var handles = solids.Select(s => H(s.Solid)).ToList();
        var names = solids.Select(s => Marshal.StringToCoTaskMemUTF8(s.Name)).ToArray();
        var p = Marshal.StringToCoTaskMemUTF8(path);
        bool[] added = new bool[handles.Count];
        try
        {
            var raw = new IntPtr[handles.Count];
            for (int i = 0; i < handles.Count; i++) { handles[i].DangerousAddRef(ref added[i]); raw[i] = handles[i].DangerousGetHandle(); }
            if (Native.sc_write_step(raw, names, raw.Length, p) == 0)
                throw new InvalidOperationException($"STEP: {Native.LastError()}");
        }
        finally
        {
            for (int i = 0; i < handles.Count; i++) if (added[i]) handles[i].DangerousRelease();
            foreach (var n in names) Marshal.FreeCoTaskMem(n);
            Marshal.FreeCoTaskMem(p);
        }
    }

    public ISolid ImportStep(string path)
    {
        var p = Marshal.StringToCoTaskMemUTF8(path);
        try { return Wrap(Native.sc_read_step(p), $"STEP {Path.GetFileName(path)}"); }
        finally { Marshal.FreeCoTaskMem(p); }
    }
}
