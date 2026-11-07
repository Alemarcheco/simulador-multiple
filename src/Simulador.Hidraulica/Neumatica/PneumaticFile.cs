using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulador.Hidraulica.Neumatica;

/// <summary>Circuito neumático en disco: archivo <c>.neux</c> (JSON legible).</summary>
public static class PneumaticFile
{
    public const string Extension = ".neux";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Envelope(int Version, PneumaticCircuit Circuit);

    public static string Serialize(PneumaticCircuit circuit) => JsonSerializer.Serialize(new Envelope(FormatVersion, circuit), Options);

    public static PneumaticCircuit Deserialize(string json)
    {
        var env = JsonSerializer.Deserialize<Envelope>(json, Options) ?? throw new InvalidDataException("El archivo está vacío.");
        if (env.Version > FormatVersion) throw new InvalidDataException($"El archivo es de una versión más nueva ({env.Version}).");
        return env.Circuit;
    }

    public static void Save(PneumaticCircuit c, string path) => File.WriteAllText(path, Serialize(c), Encoding.UTF8);
    public static PneumaticCircuit Load(string path) => Deserialize(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Lista de materiales del circuito en Markdown.</summary>
    public static string BillOfMaterials(PneumaticCircuit c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {c.Name}").AppendLine();
        if (!string.IsNullOrWhiteSpace(c.Description)) sb.AppendLine(c.Description).AppendLine();
        sb.AppendLine("| Ref. | Componente | Datos |").AppendLine("|---|---|---|");
        foreach (var comp in c.Components.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            string data = comp switch
            {
                Cylinder cy => $"Ø{cy.Bore * 1000:0}/{cy.RodDiameter * 1000:0} mm, carrera {cy.Stroke * 1000:0} mm",
                DirectionalValve v => $"C = {v.Conductance * 1e8:0.##} L/(s·bar)" + (v.Coil14 != "" ? $", bobinas {v.Coil14} {v.Coil12}".TrimEnd() : ""),
                AirSupply s => $"{s.Pressure / 1e5:0.#} bar",
                Compressor k => $"{k.FreeAirDelivery * 60_000:0} L/min, {k.CutInPressure / 1e5:0.#}–{k.CutOutPressure / 1e5:0.#} bar",
                AirReceiver r => $"{r.Volume * 1000:0} L",
                FlowControl f => $"C máx. {f.MaxConductance * 1e8:0.##} L/(s·bar)",
                _ => "",
            };
            sb.AppendLine($"| {comp.Id} | {comp.TypeName} | {data} |");
        }
        foreach (var g in c.Tubes.GroupBy(t => Math.Round(t.InnerDiameter * 1000, 1)))
            sb.AppendLine($"| — | Tubo Ø{g.Key:0.#} mm interior | {g.Sum(t => t.Length):0.##} m |");
        return sb.ToString();
    }
}
