using System.Text.Json.Serialization;

namespace Simulador.Cad3D.Geometry.Model;

/// <summary>Cómo se combina una operación con lo que ya hay en la pieza.</summary>
public enum CombineMode
{
    /// <summary>Añade material (unión).</summary>
    Solido,
    /// <summary>Quita material (resta): agujeros, huecos, ranuras.</summary>
    Hueco,
    /// <summary>Deja solo la parte común (intersección).</summary>
    Interseccion,
}

/// <summary>Marca una propiedad numérica editable desde el panel de propiedades.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ParamAttribute : Attribute
{
    public ParamAttribute(string label) { Label = label; }
    public string Label { get; }
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    public double Step { get; init; } = 1;
}

/// <summary>Una operación del historial de una pieza. Medidas en mm, ángulos en grados.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(BoxFeature), "caja")]
[JsonDerivedType(typeof(RoundedBoxFeature), "caja_redondeada")]
[JsonDerivedType(typeof(CylinderFeature), "cilindro")]
[JsonDerivedType(typeof(SphereFeature), "esfera")]
[JsonDerivedType(typeof(ExtrudeFeature), "extrusion")]
[JsonDerivedType(typeof(RevolveFeature), "revolucion")]
[JsonDerivedType(typeof(StepImportFeature), "step")]
[JsonDerivedType(typeof(FilletFeature), "redondeo")]
[JsonDerivedType(typeof(ChamferFeature), "chaflan")]
[JsonDerivedType(typeof(ShellFeature), "vaciado")]
public abstract class Feature
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public CombineMode Mode { get; set; } = CombineMode.Solido;
    public bool Enabled { get; set; } = true;

    /// <summary>Posición del origen local de la operación.</summary>
    public Vec3 Position { get; set; }

    /// <summary>Rotación en grados alrededor de X, luego Y, luego Z.</summary>
    public Vec3 Rotation { get; set; }

    [JsonIgnore] public abstract string KindName { get; }

    /// <summary>Crea el sólido en coordenadas locales.</summary>
    public abstract ISolid BuildLocal(IGeometryKernel kernel);

    public ISolid Build(IGeometryKernel kernel) =>
        kernel.Transform(BuildLocal(kernel), Transform3.FromPose(Position, Rotation));

    public Feature Clone()
    {
        var copy = (Feature)MemberwiseClone();
        copy.Id = Guid.NewGuid().ToString("N")[..8];
        return copy;
    }
}

/// <summary>Caja; el origen local es la esquina inferior izquierda delantera.</summary>
public sealed class BoxFeature : Feature
{
    public override string KindName => "Caja";
    [Param("Ancho X", Min = 0.01)] public double SizeX { get; set; } = 20;
    [Param("Fondo Y", Min = 0.01)] public double SizeY { get; set; } = 20;
    [Param("Alto Z", Min = 0.01)] public double SizeZ { get; set; } = 20;
    public override ISolid BuildLocal(IGeometryKernel k) => k.Box(new Vec3(SizeX, SizeY, SizeZ));
}

/// <summary>Caja con aristas verticales redondeadas (típica de encapsulados).</summary>
public sealed class RoundedBoxFeature : Feature
{
    public override string KindName => "Caja redondeada";
    [Param("Ancho X", Min = 0.01)] public double SizeX { get; set; } = 40;
    [Param("Fondo Y", Min = 0.01)] public double SizeY { get; set; } = 30;
    [Param("Alto Z", Min = 0.01)] public double SizeZ { get; set; } = 20;
    [Param("Radio de esquina", Min = 0, Step = 0.5)] public double CornerRadius { get; set; } = 3;
    [Param("Segmentos por esquina", Min = 1, Max = 64)] public int CornerSegments { get; set; } = 8;
    public override ISolid BuildLocal(IGeometryKernel k) =>
        k.Extrude(Polygon2D.RoundedRectangle(SizeX, SizeY, CornerRadius, CornerSegments), SizeZ);
}

/// <summary>Cilindro o cono; el origen local es el centro de la base.</summary>
public sealed class CylinderFeature : Feature
{
    public override string KindName => "Cilindro";
    [Param("Diámetro", Min = 0.01, Step = 0.5)] public double Diameter { get; set; } = 10;
    [Param("Diámetro superior (0 = igual)", Min = 0, Step = 0.5)] public double TopDiameter { get; set; }
    [Param("Alto", Min = 0.01)] public double Height { get; set; } = 20;
    [Param("Segmentos", Min = 3, Max = 256)] public int Segments { get; set; } = 48;
    public override ISolid BuildLocal(IGeometryKernel k) =>
        k.Cylinder(Diameter / 2, (TopDiameter > 0 ? TopDiameter : Diameter) / 2, Height, Segments);
}

/// <summary>Esfera; el origen local es su centro.</summary>
public sealed class SphereFeature : Feature
{
    public override string KindName => "Esfera";
    [Param("Diámetro", Min = 0.01, Step = 0.5)] public double Diameter { get; set; } = 20;
    [Param("Segmentos", Min = 6, Max = 128)] public int Segments { get; set; } = 32;
    public override ISolid BuildLocal(IGeometryKernel k) => k.Sphere(Diameter / 2, Segments);
}

/// <summary>Extrusión de un perfil 2D libre (polígono en el plano XY local).</summary>
public sealed class ExtrudeFeature : Feature
{
    public override string KindName => "Extrusión";
    public List<Vec2> Profile { get; set; } = new() { new(0, 0), new(20, 0), new(10, 17.32) };
    [Param("Alto", Min = 0.01)] public double Height { get; set; } = 10;
    public override ISolid BuildLocal(IGeometryKernel k) => k.Extrude(Profile, Height);

    public new ExtrudeFeature Clone()
    {
        var c = (ExtrudeFeature)base.Clone();
        c.Profile = new List<Vec2>(Profile);
        return c;
    }
}
