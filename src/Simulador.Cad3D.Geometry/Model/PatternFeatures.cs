namespace Simulador.Cad3D.Geometry.Model;

public enum PatternKind
{
    Fila = 0,
    Rejilla = 1,
    Circular = 2,
}

public enum Axis
{
    X = 0,
    Y = 1,
    Z = 2,
}

/// <summary>
/// Repite las últimas operaciones de la pieza (p. ej. un agujero y su avellanado) en fila, en rejilla o en círculo.
/// Cada copia se suma o resta igual que la operación original. El original cuenta como la primera copia.
/// </summary>
public sealed class PatternFeature : Feature
{
    public override string KindName => "Repetición";

    public PatternKind Kind { get; set; } = PatternKind.Fila;

    /// <summary>Dirección de la fila y de las columnas de la rejilla.</summary>
    public Axis Direction { get; set; } = Axis.X;

    [Param("Operaciones a repetir (las de justo antes)", Min = 1, Max = 20)] public int Operations { get; set; } = 1;
    [Param("Copias (fila, columnas o en círculo)", Min = 2, Max = 500)] public int Count { get; set; } = 4;
    [Param("Separación (mm)", Min = -1000, Max = 1000, Step = 1)] public double Spacing { get; set; } = 10;
    [Param("Filas de la rejilla", Min = 1, Max = 500)] public int Rows { get; set; } = 2;
    [Param("Separación entre filas (mm)", Min = -1000, Max = 1000, Step = 1)] public double RowSpacing { get; set; } = 10;
    [Param("Ángulo total del círculo (°)", Min = 1, Max = 360, Step = 15)] public double Angle { get; set; } = 360;
    [Param("Centro del círculo X", Min = -10000, Max = 10000, Step = 1)] public double CenterX { get; set; }
    [Param("Centro del círculo Y", Min = -10000, Max = 10000, Step = 1)] public double CenterY { get; set; }

    public override ISolid BuildLocal(IGeometryKernel kernel) =>
        throw new NotSupportedException("Una repetición no crea una forma propia: copia las operaciones anteriores.");

    /// <summary>Transformaciones de las copias (sin la identidad del original), en coordenadas de la pieza.</summary>
    public IEnumerable<Transform3> CopyTransforms()
    {
        static Vec3 Dir(Axis a) => a switch { Axis.X => Vec3.UnitX, Axis.Y => Vec3.UnitY, _ => Vec3.UnitZ };
        int count = Math.Max(1, Count);
        switch (Kind)
        {
            case PatternKind.Fila:
                for (int i = 1; i < count; i++) yield return Transform3.Translate(Dir(Direction) * (Spacing * i));
                break;
            case PatternKind.Rejilla:
            {
                // Las filas van en el eje siguiente al de las columnas (X → Y, Y → Z, Z → X).
                var col = Dir(Direction);
                var row = Dir((Axis)(((int)Direction + 1) % 3));
                for (int r = 0; r < Math.Max(1, Rows); r++)
                    for (int c = 0; c < count; c++)
                        if (r != 0 || c != 0) yield return Transform3.Translate(col * (Spacing * c) + row * (RowSpacing * r));
                break;
            }
            case PatternKind.Circular:
            {
                // Círculo completo: copias repartidas sin repetir la primera; arco: de un extremo al otro.
                double step = Angle >= 359.999 ? 360.0 / count : Angle / Math.Max(1, count - 1);
                var c = new Vec3(CenterX, CenterY, 0);
                for (int i = 1; i < count; i++)
                    yield return Transform3.Translate(c) * Transform3.RotateZ(step * i) * Transform3.Translate(-c);
                break;
            }
        }
    }
}

/// <summary>Añade a la pieza su imagen en un espejo (pieza simétrica). El espejo es perpendicular al eje elegido.</summary>
public sealed class MirrorFeature : ModifierFeature
{
    public override string KindName => "Simetría";
    public override bool NeedsExactKernel => false;

    /// <summary>Eje que el espejo invierte: X = espejo en el plano YZ.</summary>
    public Axis Normal { get; set; } = Axis.X;

    [Param("Posición del espejo en ese eje (mm)", Min = -10000, Max = 10000, Step = 1)] public double Offset { get; set; }

    public Transform3 MirrorTransform()
    {
        var s = Normal switch { Axis.X => new Vec3(-1, 1, 1), Axis.Y => new Vec3(1, -1, 1), _ => new Vec3(1, 1, -1) };
        var t = Normal switch { Axis.X => new Vec3(2 * Offset, 0, 0), Axis.Y => new Vec3(0, 2 * Offset, 0), _ => new Vec3(0, 0, 2 * Offset) };
        return Transform3.Translate(t) * Transform3.Scale(s);
    }

    public override ISolid Apply(IGeometryKernel kernel, ISolid current) =>
        kernel.Union(current, kernel.Transform(current, MirrorTransform()));
}
