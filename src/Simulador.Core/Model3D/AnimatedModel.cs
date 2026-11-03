namespace Simulador.Core.Model3D;

// Modelos 3D animables para la vista del banco de pruebas (contratos/modelo-3d-animable.md).
// Responsable: módulo de diseño 3D. Lo consume: entorno combinado (co-simulación).
// Medidas en mm, Z hacia arriba, mano derecha. Ángulos en radianes.

public readonly record struct Vector3Mm(double X, double Y, double Z);

/// <summary>Tipo de movimiento de una pieza.</summary>
public enum JointKind
{
    /// <summary>Gira alrededor del eje (ejes, rodetes, rotores).</summary>
    Revolute,
    /// <summary>Se desplaza a lo largo del eje (vástagos, émbolos, válvulas).</summary>
    Prismatic,
    /// <summary>Escala a lo largo del eje desde el origen (nivel de un tanque): valor 0..1.</summary>
    Scale,
    /// <summary>Se muestra u oculta (pilotos, indicadores): valor booleano.</summary>
    Visibility,
}

/// <summary>Cómo se interpreta el valor de la clave de Snapshot().</summary>
public enum BindingMode
{
    /// <summary>El valor es una velocidad (rad/s o m/s) y la vista lo integra en el tiempo.</summary>
    Velocity,
    /// <summary>El valor es la posición directa (rad, m, fracción 0..1 o bool).</summary>
    Position,
}

/// <summary>
/// Articulación de una pieza móvil. <see cref="Binding"/> es la clave del ComponentState que publica el módulo
/// (cada módulo expone sus claves como constantes, p. ej. PumpKeys.Speed = "speed").
/// </summary>
public sealed record ModelJoint(
    JointKind Kind,
    Vector3Mm Origin,
    Vector3Mm Axis,
    string Binding,
    BindingMode Mode = BindingMode.Position,
    double? Min = null,
    double? Max = null);

/// <summary>Malla de una pieza: vértices (x, y, z consecutivos, mm) e índices de triángulos.</summary>
public sealed record ModelMesh(IReadOnlyList<float> Positions, IReadOnlyList<int> Indices);

/// <summary>Pieza o grupo rígido. Si tiene <see cref="Parent"/>, hereda el movimiento del padre.</summary>
public sealed record ModelPart(string Name, string? Parent, ModelMesh Mesh, string ColorHex = "#B0B0B0", ModelJoint? Joint = null);

/// <summary>Punto de conexión (borne, brida). La vista forma "Equipo.Puerto" con el Id del equipo.</summary>
public sealed record ModelConnector(string Port, Vector3Mm Position, Vector3Mm Normal, double SizeMm = 0);

/// <summary>Modelo completo de un equipo. <see cref="TypeId"/> casa con el ComponentDescriptor del módulo.</summary>
public sealed record AnimatedModel(string TypeId, string Name, IReadOnlyList<ModelPart> Parts, IReadOnlyList<ModelConnector> Connectors)
{
    /// <summary>Extensión del archivo: ZIP con model.json y las mallas.</summary>
    public const string FileExtension = ".sm3d";
}
