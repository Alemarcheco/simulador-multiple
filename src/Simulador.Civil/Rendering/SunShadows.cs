using System.Numerics;
using Simulador.Core.Rendering;

namespace Simulador.Civil.Rendering;

/// <summary>
/// Sombras del sol con un mapa de profundidad: se dibuja el edificio visto desde el sol (proyección paralela) y un
/// punto está a la sombra si, mirando desde el sol, hay algo delante de él. Se suaviza el borde promediando 3×3.
/// El mapa solo cambia con el sol o con el edificio: al girar la vista se reutiliza.
/// </summary>
public sealed class SunShadows
{
    /// <summary>Luz que queda en la sombra (luz del cielo).</summary>
    public const float ShadowLight = 0.62f;

    private readonly ZBufferRasterizer _map = new() { Supersampling = 1 };
    private readonly OrbitCamera _sun = new() { Orthographic = true, MinPitch = -90, MaxPitch = 90 };
    private float _texel = 0.05f;

    public int Resolution { get; set; } = 1536;

    /// <summary>Dirección hacia el sol con la que se hizo el mapa.</summary>
    public Vector3 SunDirection { get; private set; }

    /// <summary>Dibuja el mapa de profundidad desde el sol para la malla (incluido el suelo, que también hace de receptor).</summary>
    public void Build(BuildingMesh mesh, Vector3 towardSun)
    {
        SunDirection = Vector3.Normalize(towardSun);
        var (min, max) = BuildingRenderer.Bounds(mesh);
        _sun.Yaw = Math.Atan2(SunDirection.Y, SunDirection.X) * 180 / Math.PI;
        _sun.Pitch = Math.Asin(Math.Clamp(SunDirection.Z, -1, 1)) * 180 / Math.PI;
        _sun.Fit(min, max);
        _map.Resize(Resolution, Resolution);
        _texel = (float)(1 / _sun.PixelsPerMeter(Resolution, Resolution));
        _map.Begin(_sun, 0, 0);
        Span<Vector3> pts = stackalloc Vector3[64];
        foreach (var f in mesh.Faces)
        {
            Span<Vector3> span = f.Pts.Length <= pts.Length ? pts[..f.Pts.Length] : new Vector3[f.Pts.Length];
            for (int i = 0; i < f.Pts.Length; i++) span[i] = BuildingRenderer.ToVector(f.Pts[i]);
            _map.DrawPolygon(span, 0xFF000000);
        }
        _map.End();
    }

    /// <summary>
    /// Luz en un punto de una cara con normal <paramref name="normal"/>: 1 al sol, <see cref="ShadowLight"/> a la
    /// sombra, intermedio en el borde. Una cara de espaldas al sol está a la sombra sin mirar el mapa.
    /// </summary>
    public float LightAt(Vector3 p, Vector3 normal)
    {
        float facing = Vector3.Dot(normal, SunDirection);
        if (facing <= 0.02f) return ShadowLight;
        // Se separa el punto de su cara (más cuanto más rasante llega el sol) para que no se haga sombra a sí misma.
        float sin = Math.Clamp(facing, 0.05f, 1f);
        float cos = MathF.Sqrt(1 - sin * sin);
        var q = p + normal * (_texel * (1 + 2 * cos / sin)) + SunDirection * 0.02f;
        if (_map.Project(q) is not { } s) return 1;
        int lit = 0;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (s.Depth <= _map.DepthAt(s.X + dx, s.Y + dy) + _texel) lit++;
        return ShadowLight + (1 - ShadowLight) * lit / 9f;
    }

    /// <summary>Luz en un punto del suelo o de una cara horizontal (normal hacia arriba).</summary>
    public float LightAt(Vector3 p) => LightAt(p, Vector3.UnitZ);

    /// <summary>¿El punto está a pleno sol? (para pruebas y cálculos).</summary>
    public bool IsLit(Vector3 p, Vector3 normal) => LightAt(p, normal) > 0.99f;
}
