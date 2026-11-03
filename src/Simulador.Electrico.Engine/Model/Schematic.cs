using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulador.Electrico.Engine.Model;

/// <summary>
/// Esquema eléctrico (mando y potencia en la misma hoja). Se guarda como JSON con extensión <c>.selx</c>.
/// Las coordenadas están en unidades de rejilla; los símbolos se dibujan a partir del tipo de elemento.
/// </summary>
public sealed class Schematic
{
    public string Name { get; set; } = "Esquema sin título";
    public string Description { get; set; } = "";
    public List<PlacedElement> Elements { get; set; } = [];
    public List<Wire> Wires { get; set; } = [];

    public PlacedElement? Find(Guid id) => Elements.FirstOrDefault(e => e.Id == id);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Schematic FromJson(string json) =>
        JsonSerializer.Deserialize<Schematic>(json, Json) ?? throw new InvalidDataException("Archivo de esquema vacío.");

    public Schematic Clone() => FromJson(ToJson());
}

/// <summary>Un símbolo colocado en el esquema.</summary>
public sealed class PlacedElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Tipo del catálogo, p. ej. "bobina_contactor".</summary>
    public string Type { get; set; } = "";
    /// <summary>
    /// Designación IEC 81346 (KM1, KA2, S1, F1…). Todos los elementos con la misma designación forman un mismo
    /// aparato: la bobina KM1 mueve todos los contactos marcados KM1.
    /// </summary>
    public string Tag { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Giro en pasos de 90° (0..3), en sentido horario.</summary>
    public int Rotation { get; set; }
    /// <summary>Parámetros numéricos (tensión, potencia, retardo…). Los que falten toman el valor por defecto.</summary>
    public Dictionary<string, double> Params { get; set; } = [];
    /// <summary>Textos libres: numeración de bornes, rótulo, referencia de fabricante.</summary>
    public Dictionary<string, string> Texts { get; set; } = [];

    public double Param(Catalog.ElementType type, string key) =>
        Params.TryGetValue(key, out var v) ? v : type.Default(key);

    public string Text(string key, string fallback = "") =>
        Texts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}

/// <summary>Conductor entre dos bornes. El trazado se calcula al dibujar.</summary>
public sealed class Wire
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TerminalRef A { get; set; } = new();
    public TerminalRef B { get; set; } = new();
}

public sealed class TerminalRef
{
    public Guid Element { get; set; }
    public string Terminal { get; set; } = "";

    public TerminalRef() { }
    public TerminalRef(Guid element, string terminal) { Element = element; Terminal = terminal; }

    public string Key => $"{Element:N}:{Terminal}";
}
