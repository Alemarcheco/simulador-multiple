using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Simulador.Banco.Mesa;

/// <summary>
/// Un montaje libre de la mesa del banco de pruebas: las piezas que el usuario ha puesto, dónde están y cómo las ha
/// unido. Se guarda como archivo .simx (JSON). No contiene estado de simulación: eso lo crea <see cref="BenchBuilder"/>.
/// </summary>
public sealed class BenchProject
{
    public const string Extension = ".simx";
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "Montaje nuevo";
    public string Description { get; set; } = "";
    public List<BenchPart> Parts { get; set; } = [];
    public List<BenchWire> Wires { get; set; } = [];

    public BenchPart? Part(string id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>Primer identificador libre con ese prefijo: "M1", "M2"…</summary>
    public string NextId(string prefix)
    {
        for (int n = 1; ; n++)
        {
            string id = prefix + n.ToString(CultureInfo.InvariantCulture);
            if (Part(id) is null) return id;
        }
    }

    /// <summary>Quita una pieza y todas sus conexiones.</summary>
    public void Remove(BenchPart part)
    {
        Parts.Remove(part);
        Wires.RemoveAll(w => w.FromPart == part.Id || w.ToPart == part.Id);
    }

    /// <summary>Cambia el identificador de una pieza y actualiza sus conexiones. Devuelve un motivo si no se puede.</summary>
    public string? Rename(BenchPart part, string newId)
    {
        newId = newId.Trim();
        if (newId == part.Id) return null;
        if (!IsValidId(newId)) return "Usa letras, números, guion o guion bajo (sin espacios ni puntos), p. ej. M1 o KM-2.";
        if (Part(newId) is not null) return $"Ya hay una pieza llamada {newId}.";
        foreach (var w in Wires)
        {
            if (w.FromPart == part.Id) w.FromPart = newId;
            if (w.ToPart == part.Id) w.ToPart = newId;
        }
        part.Id = newId;
        return null;
    }

    public static bool IsValidId(string id) => Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,24}$");

    public IEnumerable<BenchWire> WiresAt(string partId, string port) =>
        Wires.Where(w => (w.FromPart == partId && w.FromPort == port) || (w.ToPart == partId && w.ToPort == port));

    public BenchProject Clone() => Deserialize(Serialize(this));

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(BenchProject project) => JsonSerializer.Serialize(project, Json);

    public static BenchProject Deserialize(string json)
    {
        var p = JsonSerializer.Deserialize<BenchProject>(json, Json) ?? throw new InvalidDataException("El archivo está vacío.");
        if (p.Version > CurrentVersion)
            throw new InvalidDataException($"El montaje es de una versión más nueva del simulador (formato {p.Version}).");
        p.Parts ??= [];
        p.Wires ??= [];
        foreach (var part in p.Parts) part.Params ??= [];
        return p;
    }
}

/// <summary>Una pieza colocada en la mesa. Los parámetros se guardan como texto (números con punto decimal).</summary>
public sealed class BenchPart
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Params { get; set; } = [];

    public string? this[string key]
    {
        get => Params.TryGetValue(key, out var v) ? v : null;
        set { if (value is null) Params.Remove(key); else Params[key] = value; }
    }

    public double Number(string key, double fallback) =>
        Params.TryGetValue(key, out var v) && double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    public string Text(string key, string fallback) => Params.TryGetValue(key, out var v) ? v : fallback;

    public void SetNumber(string key, double value) => Params[key] = value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Un cable, eje, tubería o sonda entre dos bornes de piezas.</summary>
public sealed class BenchWire
{
    public string FromPart { get; set; } = "";
    public string FromPort { get; set; } = "";
    public string ToPart { get; set; } = "";
    public string ToPort { get; set; } = "";

    public BenchWire() { }
    public BenchWire(string fromPart, string fromPort, string toPart, string toPort)
    {
        FromPart = fromPart; FromPort = fromPort; ToPart = toPart; ToPort = toPort;
    }

    public bool Touches(string part, string port) => (FromPart == part && FromPort == port) || (ToPart == part && ToPort == port);

    /// <summary>El otro extremo visto desde (part, port).</summary>
    public (string Part, string Port) Other(string part, string port) =>
        FromPart == part && FromPort == port ? (ToPart, ToPort) : (FromPart, FromPort);

    public override string ToString() => $"{FromPart}.{FromPort} — {ToPart}.{ToPort}";
}
