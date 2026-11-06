using Simulador.Core.Rendering;

namespace Simulador.Hidraulica.View3D;

/// <summary>Cámara orbital en perspectiva de la vista hidráulica (la común de <see cref="Simulador.Core.Rendering"/>).</summary>
public sealed class OrbitCamera : Simulador.Core.Rendering.OrbitCamera
{
}

/// <summary>
/// Rasterizador de la vista hidráulica: el común con búfer de profundidad, sin supermuestreo porque se redibuja en
/// cada fotograma durante la simulación. Los cascos semitransparentes de los tanques se mezclan al final.
/// </summary>
public sealed class Rasterizer : ZBufferRasterizer
{
    public void Render(Scene scene, OrbitCamera camera, uint skyTop = 0xFFE9F1F8, uint skyBottom = 0xFFC9D8E6) =>
        Render(scene.Triangles, camera, skyTop, skyBottom);
}
