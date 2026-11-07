using System.Text.Json.Serialization;

namespace Simulador.Cad3D.Geometry.Model;

/// <summary>
/// Operación que modifica lo que ya hay en la pieza (redondeo, chaflán, vaciado) en vez de añadir una forma.
/// Su posición, rotación y modo no se usan.
/// </summary>
public abstract class ModifierFeature : Feature
{
    public override ISolid BuildLocal(IGeometryKernel kernel) =>
        throw new NotSupportedException("Una operación de modificación no crea una forma propia.");

    public abstract ISolid Apply(IGeometryKernel kernel, ISolid current);

    /// <summary>True si solo funciona con OpenCASCADE (redondeos, chaflanes, vaciados).</summary>
    public virtual bool NeedsExactKernel => true;

    protected static IExactKernel Exact(IGeometryKernel k) =>
        k as IExactKernel ?? throw new NotSupportedException("necesita el motor exacto (OpenCASCADE)");
}

/// <summary>Redondea aristas de la pieza construida hasta este punto.</summary>
public sealed class FilletFeature : ModifierFeature
{
    public override string KindName => "Redondeo";
    [Param("Radio", Min = 0.05, Step = 0.25)] public double Radius { get; set; } = 1;
    public EdgeSelection Edges { get; set; } = EdgeSelection.Verticales;
    public override ISolid Apply(IGeometryKernel k, ISolid current) => Exact(k).Fillet(current, Radius, Edges);
}

/// <summary>Achaflana aristas de la pieza construida hasta este punto.</summary>
public sealed class ChamferFeature : ModifierFeature
{
    public override string KindName => "Chaflán";
    [Param("Distancia", Min = 0.05, Step = 0.25)] public double Distance { get; set; } = 1;
    public EdgeSelection Edges { get; set; } = EdgeSelection.Superiores;
    public override ISolid Apply(IGeometryKernel k, ISolid current) => Exact(k).Chamfer(current, Distance, Edges);
}

/// <summary>Vacía la pieza dejando paredes del grosor indicado y la parte de arriba abierta.</summary>
public sealed class ShellFeature : ModifierFeature
{
    public override string KindName => "Vaciado";
    [Param("Grosor de pared", Min = 0.2, Step = 0.2)] public double Thickness { get; set; } = 2;
    public override ISolid Apply(IGeometryKernel k, ISolid current) => Exact(k).ShellOpenTop(current, Thickness);
}

/// <summary>Revolución de un perfil (X = radio, Y = altura) alrededor del eje Z local.</summary>
public sealed class RevolveFeature : Feature
{
    public override string KindName => "Revolución";
    public List<Vec2> Profile { get; set; } = new() { new(0, 0), new(15, 0), new(12, 5), new(12, 25), new(0, 25) };
    /// <summary>Arco de cada tramo del perfil (ver <see cref="Sketch2D"/>); vacío = todos rectos.</summary>
    public List<double> Bulges { get; set; } = new();
    [Param("Ángulo", Min = 1, Max = 360, Step = 15)] public double Angle { get; set; } = 360;
    public override ISolid BuildLocal(IGeometryKernel k) =>
        (k as IExactKernel ?? throw new NotSupportedException("necesita el motor exacto (OpenCASCADE)"))
            .Revolve(Sketch2D.Tessellate(Profile, Bulges, 0.02), Angle);
}

/// <summary>Importa un modelo STEP (p. ej. los modelos 3D de componentes de KiCad o piezas de otros programas).</summary>
public sealed class StepImportFeature : Feature
{
    public override string KindName => "Modelo STEP";
    public string FilePath { get; set; } = "";
    public override ISolid BuildLocal(IGeometryKernel k)
    {
        var exact = k as IExactKernel ?? throw new NotSupportedException("necesita el motor exacto (OpenCASCADE)");
        if (!File.Exists(FilePath)) throw new FileNotFoundException($"no se encuentra {FilePath}");
        return exact.ImportStep(FilePath);
    }
}
