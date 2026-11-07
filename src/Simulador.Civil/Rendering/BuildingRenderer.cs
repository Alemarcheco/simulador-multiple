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

    /// <summary>
    /// Cámara de la vista de edificación. Por defecto en perspectiva suave (lo lejano se ve algo más pequeño, como lo
    /// ve el ojo; en paralela el fondo parece más ancho que el frente). Gira por completo en vertical, también por
    /// debajo del suelo para ver la cara inferior.
    /// </summary>
    public static OrbitCamera CreateCamera(bool perspective = true) => new()
    {
        Orthographic = !perspective, FieldOfView = 35, Yaw = -55, Pitch = 30, MinPitch = -89, MaxPitch = 89,
    };

    /// <summary>
    /// Encuadra la caja. En paralela usa el encuadre común; en perspectiva aleja la cámara hasta que la esfera que
    /// envuelve la caja quepa entera en vertical, así nada se sale al girar en cualquier dirección.
    /// </summary>
    public static void Fit(OrbitCamera camera, Vector3 min, Vector3 max)
    {
        camera.Fit(min, max);
        if (camera.Orthographic || min.X > max.X) return;
        double radius = Math.Max((max - min).Length(), 0.5f) / 2;
        camera.Distance = radius / Math.Sin(camera.FieldOfView * Math.PI / 360) * 1.06;
    }

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
