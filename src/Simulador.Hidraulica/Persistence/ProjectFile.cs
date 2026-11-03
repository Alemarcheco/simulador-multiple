using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.Persistence;

/// <summary>Proyecto hidráulico en disco: archivo <c>.hidx</c> (JSON legible).</summary>
public static class ProjectFile
{
    public const string Extension = ".hidx";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Envelope(int Version, HydraulicNetwork Network);

    public static string Serialize(HydraulicNetwork network) =>
        JsonSerializer.Serialize(new Envelope(FormatVersion, network), Options);

    public static HydraulicNetwork Deserialize(string json)
    {
        var env = JsonSerializer.Deserialize<Envelope>(json, Options)
            ?? throw new InvalidDataException("El archivo está vacío.");
        if (env.Version > FormatVersion)
            throw new InvalidDataException($"El archivo es de una versión más nueva ({env.Version}).");
        return env.Network;
    }

    public static void Save(HydraulicNetwork network, string path) => File.WriteAllText(path, Serialize(network), Encoding.UTF8);
    public static HydraulicNetwork Load(string path) => Deserialize(File.ReadAllText(path, Encoding.UTF8));
}

/// <summary>Lista de materiales e informe de resultados del diseño.</summary>
public static class Report
{
    public sealed record BomLine(string Item, string Description, double Quantity, string Unit);

    public static IReadOnlyList<BomLine> BillOfMaterials(HydraulicNetwork net)
    {
        var lines = new List<BomLine>();
        foreach (var g in net.Links.OfType<Pipe>().GroupBy(p => (p.Material, Dn: Math.Round(p.Diameter * 1000))).OrderBy(g => g.Key.Dn))
            lines.Add(new("Tubería", $"{g.Key.Material} Ø{g.Key.Dn:0} mm", Math.Round(g.Sum(p => p.Length), 2), "m"));
        foreach (var g in net.Links.OfType<Valve>().GroupBy(v => (v.TypeName, Dn: Math.Round(v.Diameter * 1000))))
            lines.Add(new(g.Key.TypeName, $"DN {g.Key.Dn:0} mm", g.Count(), "ud"));
        foreach (var p in net.Links.OfType<Pump>())
            lines.Add(new("Bomba centrífuga", $"{p.Id}: {p.RatedFlow * 3600:0.##} m³/h a {p.RatedHead:0.#} m, {p.RatedSpeed * 60 / (2 * Math.PI):0} rpm", 1, "ud"));
        foreach (var t in net.Nodes.OfType<Tank>())
            lines.Add(new("Tanque", $"{t.Id}: Ø{t.Diameter:0.##} m × {t.MaxLevel:0.##} m ({t.Capacity * 1000:0} L)", 1, "ud"));
        foreach (var g in net.Nodes.OfType<Junction>().GroupBy(j => Degree(net, j.Id)).Where(g => g.Key >= 2).OrderBy(g => g.Key))
            lines.Add(new(g.Key switch { 2 => "Codo / manguito", 3 => "Te", 4 => "Cruz", _ => "Colector" },
                $"{g.Key} bocas", g.Count(), "ud"));
        foreach (var g in net.Sensors.GroupBy(s => s.TypeName))
            lines.Add(new(g.Key, "", g.Count(), "ud"));
        return lines;
    }

    private static int Degree(HydraulicNetwork net, string id) => net.Links.Count(l => l.From == id || l.To == id);

    /// <summary>Informe en Markdown con la lista de materiales y el estado actual de la simulación.</summary>
    public static string ToMarkdown(HydraulicNetwork net, HydraulicSimulation? sim)
    {
        var c = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine($"# {net.Name}").AppendLine();
        sb.AppendLine($"Fluido: {net.Fluid.Name} (ρ = {net.Fluid.Density.ToString("0.#", c)} kg/m³)").AppendLine();
        sb.AppendLine("## Lista de materiales").AppendLine();
        sb.AppendLine("| Elemento | Descripción | Cantidad | Unidad |").AppendLine("|---|---|---:|---|");
        foreach (var l in BillOfMaterials(net))
            sb.AppendLine($"| {l.Item} | {l.Description} | {l.Quantity.ToString("0.##", c)} | {l.Unit} |");

        if (sim is not null)
        {
            sb.AppendLine().AppendLine($"## Resultados en t = {sim.Time.TotalSeconds.ToString("0.#", c)} s").AppendLine();
            sb.AppendLine("| Nudo | Tipo | Altura (m) | Presión (bar) | Nivel (m) |").AppendLine("|---|---|---:|---:|---:|");
            for (int i = 0; i < net.Nodes.Count; i++)
            {
                var n = net.Nodes[i];
                string level = n is Tank t ? t.Level.ToString("0.000", c) : "";
                sb.AppendLine($"| {n.Id} | {n.TypeName} | {sim.Heads[i].ToString("0.00", c)} | {(sim.PressureAt(i) / 1e5).ToString("0.000", c)} | {level} |");
            }
            sb.AppendLine().AppendLine("| Enlace | Tipo | Caudal (L/s) | Velocidad (m/s) | Notas |").AppendLine("|---|---|---:|---:|---|");
            foreach (var l in net.Links)
            {
                string notes = l switch
                {
                    Pump p => $"H = {sim.PumpHead(p.Id).ToString("0.0", c)} m, P eje = {(sim.PumpShaftPower(p.Id) / 1000).ToString("0.00", c)} kW",
                    Valve v => $"apertura {(v.Opening * 100).ToString("0", c)} %",
                    _ => "",
                };
                sb.AppendLine($"| {l.Id} | {l.TypeName} | {(sim.FlowIn(l.Id) * 1000).ToString("0.000", c)} | {sim.VelocityIn(l.Id).ToString("0.00", c)} | {notes} |");
            }
            if (sim.Warnings.Count > 0)
            {
                sb.AppendLine().AppendLine("## Avisos").AppendLine();
                foreach (var w in sim.Warnings) sb.AppendLine($"- {w}");
            }
        }
        return sb.ToString();
    }
}
