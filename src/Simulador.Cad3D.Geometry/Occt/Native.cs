using System.Runtime.InteropServices;

namespace Simulador.Cad3D.Geometry.Occt;

/// <summary>Llamadas a la biblioteca nativa simcad_occt (native/simcad_occt), que envuelve OpenCASCADE.</summary>
internal static class Native
{
    private const string Lib = "simcad_occt";

    [DllImport(Lib)] public static extern int sc_version();
    [DllImport(Lib)] public static extern IntPtr sc_last_error();
    [DllImport(Lib)] public static extern void sc_release(IntPtr s);
    [DllImport(Lib)] public static extern void sc_free(IntPtr p);

    [DllImport(Lib)] public static extern IntPtr sc_box(double x, double y, double z);
    [DllImport(Lib)] public static extern IntPtr sc_cylinder(double r1, double r2, double h);
    [DllImport(Lib)] public static extern IntPtr sc_sphere(double r);
    [DllImport(Lib)] public static extern IntPtr sc_extrude(double[] xy, int n, double h);
    [DllImport(Lib)] public static extern IntPtr sc_revolve(double[] xz, int n, double angleDeg);

    [DllImport(Lib)] public static extern IntPtr sc_transform(SolidHandle s, double[] m);
    [DllImport(Lib)] public static extern IntPtr sc_union(SolidHandle a, SolidHandle b);
    [DllImport(Lib)] public static extern IntPtr sc_cut(SolidHandle a, SolidHandle b);
    [DllImport(Lib)] public static extern IntPtr sc_common(SolidHandle a, SolidHandle b);
    [DllImport(Lib)] public static extern IntPtr sc_fillet(SolidHandle s, double radius, int filter);
    [DllImport(Lib)] public static extern IntPtr sc_chamfer(SolidHandle s, double distance, int filter);
    [DllImport(Lib)] public static extern IntPtr sc_shell_open_top(SolidHandle s, double thickness);

    [DllImport(Lib)] public static extern double sc_volume(SolidHandle s);
    [DllImport(Lib)] public static extern int sc_bounds(SolidHandle s, double[] out6);
    [DllImport(Lib)] public static extern int sc_mesh(SolidHandle s, double lin, double ang, out IntPtr verts, out int nVerts, out IntPtr tris, out int nTris);

    [DllImport(Lib)] public static extern int sc_write_step(IntPtr[] shapes, IntPtr[] names, int n, IntPtr pathUtf8);
    [DllImport(Lib)] public static extern IntPtr sc_read_step(IntPtr pathUtf8);

    public static string LastError() => Marshal.PtrToStringUTF8(sc_last_error()) is { Length: > 0 } s ? s : "error desconocido";
}

/// <summary>Sólido de OpenCASCADE; se libera solo cuando el recolector lo recoge.</summary>
internal sealed class SolidHandle : SafeHandle
{
    public SolidHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public SolidHandle(IntPtr h) : this() { SetHandle(h); }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle() { Native.sc_release(handle); return true; }
}
