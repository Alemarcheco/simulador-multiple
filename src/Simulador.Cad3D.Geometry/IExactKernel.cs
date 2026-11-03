namespace Simulador.Cad3D.Geometry;

/// <summary>Qué aristas se redondean o achaflanan.</summary>
public enum EdgeSelection
{
    Todas = 0,
    Verticales = 1,
    Superiores = 2,
    Inferiores = 3,
}

/// <summary>
/// Operaciones que solo puede hacer un motor de geometría exacta (B-rep), como OpenCASCADE:
/// redondeos, chaflanes, vaciados, revoluciones y STEP.
/// </summary>
public interface IExactKernel : IGeometryKernel
{
    /// <summary>Versión del motor, p. ej. "OpenCASCADE 7.8.1".</summary>
    string Version { get; }

    ISolid Fillet(ISolid solid, double radius, EdgeSelection edges);
    ISolid Chamfer(ISolid solid, double distance, EdgeSelection edges);

    /// <summary>Vacía el sólido quitando la cara superior: paredes de <paramref name="thickness"/> hacia dentro.</summary>
    ISolid ShellOpenTop(ISolid solid, double thickness);

    /// <summary>Revolución de un perfil del plano XZ (X = radio) alrededor del eje Z.</summary>
    ISolid Revolve(IReadOnlyList<Vec2> profileXZ, double angleDeg);

    /// <summary>Volumen exacto en mm³.</summary>
    double Volume(ISolid solid);

    void ExportStep(string path, IReadOnlyList<(string Name, ISolid Solid)> solids);
    ISolid ImportStep(string path);
}

/// <summary>Elige el mejor motor disponible: OpenCASCADE si está su biblioteca nativa, si no el de mallas.</summary>
public static class KernelFactory
{
    public static IGeometryKernel CreateBest(Action<string>? log = null)
    {
        if (Occt.OcctKernel.TryCreate(out var occt, out var why)) return occt!;
        log?.Invoke($"OpenCASCADE no disponible ({why}); se usa el motor de mallas.");
        return new Csg.MeshKernel();
    }
}
