namespace Simulador.Core.Libraries.Model;

public enum PadType { Smd, ThroughHole, NpThroughHole, Connect }

public enum PadShape { Circle, Rect, Oval, RoundRect, Trapezoid, Custom }

/// <summary>Pad de una huella. Posición relativa al origen de la huella, en mm, Y hacia abajo (convención PCB de KiCad).</summary>
public sealed record FootprintPad(
    string Number,
    PadType Type,
    PadShape Shape,
    Vec2 Position,
    double RotationDeg,
    Vec2 Size,
    // Diámetro de taladro (0 si es SMD). Para taladros ovalados, el ancho; ver DrillSize.
    double Drill,
    Vec2? DrillSize,
    IReadOnlyList<string> Layers,
    double RoundRectRatio = 0);

/// <summary>Huella (footprint) para el PCB.</summary>
public sealed class FootprintDefinition
{
    public required string Name { get; init; }
    public string Library { get; init; } = "";
    public string FullName => string.IsNullOrEmpty(Library) ? Name : $"{Library}:{Name}";
    public string Description { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool IsSmd { get; set; }
    public List<FootprintPad> Pads { get; init; } = [];
    // <summary>Serigrafía, contorno de cortesía (courtyard), fabricación… con su capa en <see cref="Graphic.Layer"/>.</summary>
    public List<Graphic> Graphics { get; init; } = [];
    // <summary>Rutas a modelos 3D (STEP/WRL), con variables tipo ${KICAD8_3DMODEL_DIR} sin expandir.</summary>
    public List<string> Models3D { get; init; } = [];
}
