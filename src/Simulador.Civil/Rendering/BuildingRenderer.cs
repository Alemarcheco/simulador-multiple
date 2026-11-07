using System.Numerics;
using Simulador.Core.Building;
using Simulador.Core.Rendering;

namespace Simulador.Civil.Rendering;

/// <summary>
/// Dibuja la malla del edificio con el rasterizador común (<see cref="ZBufferRasterizer"/>): caras con búfer de
/// profundidad, aristas que tapan las superficies de delante y rutas de instalaciones siempre visibles.
/// </summary>
public static class BuildingRenderer
{
    public const uint Background = 0xFFE0EAF2;

    /// <summary>Cámara ortográfica de la vista de edificación (las verticales no se inclinan).</summary>
    public static OrbitCamera CreateCamera() => new()
    {
        Orthographic = true, Yaw = -55, Pitch = 34, MinPitch = 3, MaxPitch = 86,
    };

    /// <summary>Rasterizador preparado para edificios: más luz ambiente para que los muros a la sombra se lean.</summary>
    public static ZBufferRasterizer CreateRasterizer(int supersampling = 1) => new() { Ambient = 0.62f, Supersampling = supersampling };

    public static Vector3 ToVector(Point3 p) => new((float)p.X, (float)p.Y, (float)p.Z);

    public static (Vector3 Min, Vector3 Max) Bounds(BuildingMesh mesh)
    {
        var (min, max) = mesh.Bounds;
        return (ToVector(min), ToVector(max));
    }

    /// <summary>Fotograma completo: fondo, caras (con aristas si <paramref name="edges"/>) y rutas.</summary>
    public static void Render(ZBufferRasterizer r, BuildingMesh mesh, OrbitCamera camera, bool edges = true)
    {
        r.Begin(camera, Background, Background);
        Span<Vector3> pts = stackalloc Vector3[64];
        foreach (var f in mesh.Faces)
        {
            Span<Vector3> span = f.Pts.Length <= pts.Length ? pts[..f.Pts.Length] : new Vector3[f.Pts.Length];
            for (int i = 0; i < f.Pts.Length; i++) span[i] = ToVector(f.Pts[i]);
            r.DrawPolygon(span, f.Argb, cullBackFaces: true, edgeColor: edges ? ZBufferRasterizer.Shade(f.Argb, 0.5f) : null);
        }
        foreach (var l in mesh.Lines) r.DrawLine(ToVector(l.A), ToVector(l.B), l.Argb, depthTest: false, width: 2);
        r.End();
    }
}
