namespace Simulador.Core.Libraries.Model;

/// <summary>Tipo eléctrico de un pin (sirve para la comprobación de reglas eléctricas).</summary>
public enum PinElectricalType
{
    Passive, Input, Output, Bidirectional, TriState, Unspecified, PowerIn, PowerOut,
    OpenCollector, OpenEmitter, NoConnect, Free,
}

/// <summary>Pin de un símbolo. <see cref="Position"/> es la punta donde se conecta el cable.</summary>
public sealed record SymbolPin(
    string Number,
    string Name,
    PinElectricalType ElectricalType,
    Vec2 Position,
    double Length,
    double OrientationDeg,
    int Unit,
    bool Hidden)
{
    /// <summary>Extremo del pin pegado al cuerpo del símbolo.</summary>
    public Vec2 BodyEnd
    {
        get
        {
            double a = OrientationDeg * Math.PI / 180.0;
            return new Vec2(Position.X + Length * Math.Cos(a), Position.Y + Length * Math.Sin(a));
        }
    }
}

/// <summary>Elemento gráfico de un símbolo o huella. Unit 0 = común a todas las unidades.</summary>
public abstract record Graphic(int Unit, double StrokeWidth, bool Filled, string? Layer = null);

public sealed record PolylineGraphic(IReadOnlyList<Vec2> Points, int Unit, double StrokeWidth, bool Filled, string? Layer = null)
    : Graphic(Unit, StrokeWidth, Filled, Layer);

public sealed record RectangleGraphic(Vec2 Start, Vec2 End, int Unit, double StrokeWidth, bool Filled, string? Layer = null)
    : Graphic(Unit, StrokeWidth, Filled, Layer);

public sealed record CircleGraphic(Vec2 Center, double Radius, int Unit, double StrokeWidth, bool Filled, string? Layer = null)
    : Graphic(Unit, StrokeWidth, Filled, Layer);

/// <summary>Arco definido por tres puntos (inicio, intermedio, fin).</summary>
public sealed record ArcGraphic(Vec2 Start, Vec2 Mid, Vec2 End, int Unit, double StrokeWidth, bool Filled, string? Layer = null)
    : Graphic(Unit, StrokeWidth, Filled, Layer);

public sealed record TextGraphic(string Text, Vec2 Position, double RotationDeg, double Size, int Unit, string? Layer = null)
    : Graphic(Unit, 0, false, Layer);

/// <summary>Símbolo esquemático importado.</summary>
public sealed class SymbolDefinition
{
    public required string Name { get; init; }
    /// <summary>Nombre de la librería de origen (normalmente el nombre del archivo sin extensión).</summary>
    public string Library { get; init; } = "";
    public string FullName => string.IsNullOrEmpty(Library) ? Name : $"{Library}:{Name}";

    /// <summary>Propiedades: Reference, Value, Footprint, Datasheet, Description, ki_keywords, Sim.* …</summary>
    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SymbolPin> Pins { get; init; } = [];
    public List<Graphic> Graphics { get; init; } = [];
    public int UnitCount { get; set; } = 1;
    public bool IsPower { get; set; }
    /// <summary>Símbolo del que hereda (KiCad "extends"); ya resuelto por el importador.</summary>
    public string? Extends { get; init; }

    public string Reference => Properties.GetValueOrDefault("Reference", "U");
    public string Value => Properties.GetValueOrDefault("Value", Name);
    public string Footprint => Properties.GetValueOrDefault("Footprint", "");
    public string Description =>
        Properties.TryGetValue("Description", out var d) ? d : Properties.GetValueOrDefault("ki_description", "");
}
