using System.Text;
using System.Text.RegularExpressions;
using Simulador.Circuitos.Catalog;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Schematic;

public sealed record SpiceNetlist(string Text, IReadOnlyList<string> Warnings, ConnectivityResult Connectivity);

/// <summary>Convierte un esquema en una netlist SPICE para ngspice.</summary>
public static partial class SpiceNetlister
{
    public const string TemplateProperty = "Sim.Template";

    /// <param name="analysis">Líneas de análisis (".op", ".tran 1u 10m"…); se añaden antes de ".end".</param>
    public static SpiceNetlist Build(SchematicDocument doc, ComponentCatalog catalog, IEnumerable<string>? analysis = null)
    {
        var warnings = new List<string>();
        var conn = Connectivity.Analyze(doc);
        var sb = new StringBuilder();
        sb.AppendLine("* " + doc.Title);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasGround = conn.Nets.Any(n => n.Name == Connectivity.GroundNet);

        foreach (var s in doc.Symbols.OrderBy(s => s.Reference, StringComparer.OrdinalIgnoreCase))
        {
            if (s.Symbol is null) { warnings.Add($"{s.Reference}: falta el símbolo {s.SymbolName}."); continue; }
            if (s.Symbol.IsPower) continue;
            if (!refs.Add(s.Reference)) warnings.Add($"Referencia repetida: {s.Reference}.");

            var template = s.Symbol.Properties.GetValueOrDefault(TemplateProperty) ?? InferTemplate(s, catalog);
            if (template is null)
            {
                warnings.Add($"{s.Reference} ({s.Symbol.Name}): no se sabe cómo simularlo; se omite.");
                continue;
            }

            string line = Placeholder().Replace(template, m =>
            {
                var key = m.Groups[1].Value;
                if (key.Equals("ref", StringComparison.OrdinalIgnoreCase)) return s.Reference;
                if (key.Equals("value", StringComparison.OrdinalIgnoreCase)) return s.Value;
                if (key.StartsWith('n'))
                {
                    var pinNo = key[1..];
                    var net = conn.NetOf(s, pinNo);
                    bool alone = net is null || net.Pins.Count < 2 && net.Name.StartsWith('N');
                    if (alone) warnings.Add($"{s.Reference}: el pin {pinNo} no está conectado.");
                    return net?.Name ?? $"NC_{s.Reference}_{pinNo}";
                }
                return m.Value;
            });
            sb.AppendLine(line);

            foreach (var token in s.Value.Split((char[])[' ', '(', ')', '='], StringSplitOptions.RemoveEmptyEntries))
                usedNames.Add(token);
        }

        if (!hasGround) warnings.Add("El circuito no tiene masa (GND). ngspice necesita un nodo 0.");

        var models = catalog.SpiceModels.Where(m => usedNames.Contains(m.Name)).ToList();
        var subckts = catalog.SpiceSubcircuits.Where(m => usedNames.Contains(m.Name)).ToList();
        if (models.Count + subckts.Count > 0)
        {
            sb.AppendLine();
            foreach (var m in models) sb.AppendLine(m.SourceText);
            foreach (var x in subckts) sb.AppendLine(x.SourceText);
        }

        if (analysis is not null)
        {
            sb.AppendLine();
            foreach (var a in analysis) sb.AppendLine(a);
        }
        sb.AppendLine(".end");
        return new SpiceNetlist(sb.ToString(), warnings.Distinct().ToList(), conn);
    }

    /// <summary>Plantilla para símbolos importados que no traen <c>Sim.Template</c>.</summary>
    internal static string? InferTemplate(PlacedSymbol s, ComponentCatalog catalog)
    {
        var pins = SymbolTransform.Pins(s).Select(p => p.Pin).DistinctBy(p => p.Number).ToList();
        string ByName(string name) =>
            pins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } p ? "{n" + p.Number + "}" : "";
        string AllPins() => string.Join(' ', pins.OrderBy(p => p.Number, NaturalComparer.Instance).Select(p => "{n" + p.Number + "}"));

        if (catalog.FindSubcircuit(s.Value) is not null)
            return "X{ref} " + AllPins() + " {value}";

        char kind = char.ToUpperInvariant(s.Reference.FirstOrDefault());
        return kind switch
        {
            'R' or 'C' or 'L' or 'V' or 'I' when pins.Count == 2 => "{ref} " + AllPins() + " {value}",
            'D' when ByName("A") != "" && ByName("K") != "" => $"{{ref}} {ByName("A")} {ByName("K")} {{value}}",
            'Q' when ByName("C") != "" && ByName("B") != "" && ByName("E") != "" =>
                $"{{ref}} {ByName("C")} {ByName("B")} {ByName("E")} {{value}}",
            'M' when ByName("D") != "" && ByName("G") != "" && ByName("S") != "" =>
                $"{{ref}} {ByName("D")} {ByName("G")} {ByName("S")} {ByName("S")} {{value}}",
            _ => null,
        };
    }

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
    private static partial Regex Placeholder();

    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();
        public int Compare(string? x, string? y) =>
            int.TryParse(x, out var a) && int.TryParse(y, out var b) ? a.CompareTo(b) : string.Compare(x, y, StringComparison.Ordinal);
    }
}
