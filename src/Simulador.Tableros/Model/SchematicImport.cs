using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;

namespace Simulador.Tableros.Model;

/// <summary>Potencial del esquema (red eléctrica): todos los bornes unidos entre sí por cables.</summary>
public sealed class PanelNet
{
    public string Number { get; set; } = "";
    public WireKind Kind { get; set; }
    public double Section { get; set; } = 1;
    /// <summary>Bornes del tablero (placa, puerta y regletas) que hay que unir con conductores.</summary>
    public List<WireEnd> Ends { get; } = [];
}

/// <summary>Resultado de leer un esquema: aparatos agrupados por designación, potenciales y cables de campo.</summary>
public sealed class SchematicAnalysis
{
    public List<PanelDevice> Devices { get; } = [];
    public List<PanelNet> Nets { get; } = [];
    public List<FieldCable> Cables { get; } = [];
}

/// <summary>
/// Enlace con el módulo de Instalaciones eléctricas: del esquema (.selx) saca los aparatos del tablero (uno por
/// designación), decide qué va en la placa, en la puerta o fuera (campo), crea las regletas de bornes para lo que sale
/// del tablero y la lista de potenciales con su tipo, sección y color de conductor.
/// </summary>
public static class SchematicImport
{
    public const string SupplyStrip = "X0";
    public const string FieldStrip = "X1";

    public static SchematicAnalysis Analyze(Schematic schematic, ElementCatalog? catalog = null)
    {
        catalog ??= ElementCatalog.Default;
        var result = new SchematicAnalysis();
        var types = schematic.Elements.ToDictionary(e => e.Id, e => catalog.TryGet(e.Type, out var t) ? t : null);

        // 1. Designaciones. Los bornes sueltos del esquema («borne» XN, X1…) forman regletas.
        string TagOf(PlacedElement e) => string.IsNullOrWhiteSpace(e.Tag) ? $"?{schematic.Elements.IndexOf(e) + 1}" : e.Tag.Trim();
        // Los bornes con nombre de regleta (X1, X2…) van en esa regleta; los de distribución con otro nombre (XN, X+…)
        // se juntan en una regleta propia y conservan su nombre como número de borne.
        var junctionLabel = new Dictionary<Guid, string>();
        var junctionStrip = new Dictionary<Guid, string>();
        var junctions = schematic.Elements.Where(e => types[e.Id]?.Behavior == Behavior.Junction).ToList();
        var usedStrips = junctions.Select(TagOf).Where(IsStripName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        usedStrips.Add(FieldStrip);
        var distribution = Enumerable.Range(2, 50).Select(i => $"X{i}").First(n => !usedStrips.Contains(n));
        foreach (var g in junctions.GroupBy(TagOf, StringComparer.OrdinalIgnoreCase))
        {
            var n = 0;
            var isStrip = IsStripName(g.Key);
            foreach (var e in g.OrderBy(e => e.Y).ThenBy(e => e.X))
            {
                n++;
                var custom = e.Text("Bornes");
                junctionStrip[e.Id] = isStrip ? g.Key : distribution;
                junctionLabel[e.Id] = !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : isStrip ? n.ToString() : n == 1 ? g.Key : $"{g.Key}.{n}";
            }
        }

        var devices = new Dictionary<string, PanelDevice>(StringComparer.OrdinalIgnoreCase);
        var strips = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in schematic.Elements.Where(e => types[e.Id] is { } t && t.Behavior != Behavior.Junction).GroupBy(TagOf, StringComparer.OrdinalIgnoreCase))
        {
            var d = DeviceLibrary.FromSchematic(g.Key, g.ToList(), catalog);
            devices[g.Key] = d;
        }
        foreach (var (id, label) in junctionLabel)
        {
            var tag = junctionStrip[id];
            if (!strips.TryGetValue(tag, out var list)) strips[tag] = list = [];
            if (!list.Contains(label)) list.Add(label);
        }

        // 2. Potenciales (unión de bornes por los cables).
        var parent = new Dictionary<string, string>();
        string Find(string k)
        {
            if (!parent.TryGetValue(k, out var p)) { parent[k] = k; return k; }
            if (p == k) return k;
            var r = Find(p);
            parent[k] = r;
            return r;
        }
        foreach (var w in schematic.Wires)
        {
            var a = Find(w.A.Key);
            var b = Find(w.B.Key);
            if (a != b) parent[a] = b;
        }

        var nets = new Dictionary<string, List<(PlacedElement E, ElementType T, int Index)>>();
        foreach (var e in schematic.Elements)
        {
            if (types[e.Id] is not { } t) continue;
            for (var i = 0; i < t.Terminals.Count; i++)
            {
                var root = Find(new TerminalRef(e.Id, t.Terminals[i].Id).Key);
                if (!nets.TryGetValue(root, out var list)) nets[root] = list = [];
                list.Add((e, t, i));
            }
        }

        // 3. Cada potencial: tipo, sección, bornes del tablero y regleta para lo que va fuera.
        var fieldTerminals = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var autoNumber = 0;
        foreach (var members in nets.Values
                     .Select(m => m.OrderBy(p => p.E.Y).ThenBy(p => p.E.X).ToList())
                     .OrderBy(m => m.Min(p => p.E.Y)).ThenBy(m => m.Min(p => p.E.X)))
        {
            if (members.Count < 2 && members.All(p => p.T.Behavior != Behavior.Junction)) continue;
            var net = new PanelNet();
            Classify(net, members, devices, TagOf);

            WireEnd? stripEnd = null;
            var fieldEnds = new List<(PanelDevice Dev, string Label)>();
            foreach (var (e, t, i) in members)
            {
                var tag = TagOf(e);
                if (t.Behavior == Behavior.Junction)
                {
                    var end = new WireEnd(junctionStrip[e.Id], junctionLabel[e.Id]);
                    stripEnd ??= end;
                    if (!net.Ends.Contains(end)) net.Ends.Add(end);
                    continue;
                }
                var dev = devices[tag];
                var label = DeviceLibrary.TerminalLabel(e, t, i);
                if (dev.Location == DeviceLocation.Field) fieldEnds.Add((dev, label));
                else
                {
                    var end = new WireEnd(tag, label);
                    if (!net.Ends.Contains(end)) net.Ends.Add(end);
                }
            }

            // Lo que sale del tablero pasa por una regleta: X0 para la acometida, X1 para el resto.
            if (fieldEnds.Count > 0)
            {
                if (stripEnd is null)
                {
                    var supply = fieldEnds.FirstOrDefault(f => f.Dev.Kind == "acometida");
                    var strip = supply.Dev is not null ? SupplyStrip : FieldStrip;
                    if (!strips.TryGetValue(strip, out var list)) strips[strip] = list = [];
                    var label = supply.Dev is not null ? supply.Label : NextFree(list, ref autoNumber);
                    if (list.Contains(label)) label = NextFree(list, ref autoNumber);
                    list.Add(label);
                    stripEnd = new WireEnd(strip, label);
                    net.Ends.Add(stripEnd);
                }
                foreach (var (dev, _) in fieldEnds)
                {
                    if (!fieldTerminals.TryGetValue(dev.Tag, out var l)) fieldTerminals[dev.Tag] = l = [];
                    if (!l.Contains(stripEnd.ToString())) l.Add(stripEnd.ToString());
                }
            }
            if (net.Ends.Count >= 2 || fieldEnds.Count > 0) result.Nets.Add(net);
        }

        // 4. Números de hilo: los de la acometida con su nombre (L1, N…), el resto 1, 2, 3… de arriba abajo.
        var n2 = 0;
        foreach (var net in result.Nets)
        {
            var supply = net.Ends.FirstOrDefault(e => e.Tag.Equals(SupplyStrip, StringComparison.OrdinalIgnoreCase));
            net.Number = supply is not null ? supply.Terminal : net.Kind == WireKind.Tierra ? "PE" : (++n2).ToString();
        }

        // 5. Regletas como aparatos.
        foreach (var (tag, labels) in strips.OrderBy(s => s.Key))
        {
            var d = DeviceLibrary.Create("regleta", tag);
            var ordered = tag.Equals(SupplyStrip, StringComparison.OrdinalIgnoreCase)
                ? labels.OrderBy(l => Array.IndexOf(SupplyOrder, l) is var i && i >= 0 ? i : 99).ThenBy(l => l).ToList()
                : labels.OrderBy(l => int.TryParse(l, out var v) ? v : 9999).ThenBy(l => l).ToList();
            DeviceLibrary.SetStripTerminals(d, ordered);
            d.Description = ordered.Count == 1 ? "Regleta de 1 borne de paso" : $"Regleta de {ordered.Count} bornes de paso";
            devices[tag] = d;
        }

        // 6. Cables de campo: uno por receptor o captador, con sus bornes de regleta.
        foreach (var (tag, terms) in fieldTerminals.OrderBy(f => f.Key))
        {
            var dev = devices[tag];
            var power = result.Nets.Where(n => n.Ends.Any(e => terms.Contains(e.ToString()))).ToList();
            var section = power.Count > 0 ? power.Max(n => n.Section) : 1.5;
            if (dev.Kind != "captador") section = Math.Max(section, 1.5);
            result.Cables.Add(new FieldCable
            {
                Tag = $"W{tag}",
                Target = tag,
                Description = $"{dev.Description} ({tag})",
                Cores = terms.Count(t => !t.EndsWith(":PE")),
                WithPe = dev.Kind is "motor" or "acometida" or "receptor",
                Section = section,
                Terminals = terms,
            });
        }

        result.Devices.AddRange(devices.Values.OrderBy(d => Order(d.Kind)).ThenBy(d => d.Tag, TagComparer.Instance));
        return result;
    }

    private static bool IsStripName(string tag) =>
        tag.Length >= 2 && (tag[0] == 'X' || tag[0] == 'x') && tag[1..].All(char.IsDigit);

    private static readonly string[] SupplyOrder = ["L1", "L2", "L3", "L", "N", "PE", "+", "-"];

    private static string NextFree(List<string> used, ref int n)
    {
        string s;
        do s = (++n).ToString(); while (used.Contains(s));
        return s;
    }

    /// <summary>Orden de los aparatos en la placa: de la entrada de la alimentación hacia los receptores.</summary>
    public static int Order(string kind) => kind switch
    {
        "acometida" => 0,
        "diferencial" => 1,
        "magnetotermico" => 2,
        "fusible" => 3,
        "guardamotor" => 4,
        "trafo_potencia" => 5,
        "trafo_mando" => 6,
        "fuente" => 7,
        "contactor" => 8,
        "rele_termico" => 9,
        "variador" => 10,
        "rele" => 11,
        "temporizador" => 12,
        "telerruptor" => 13,
        "plc" or "mcu" => 14,
        "aparato" => 15,
        "regleta" => 20,
        "barra_pe" => 21,
        _ => 30,
    };

    private static bool IsPowerTerminal(ElementType t, PlacedElement e, int index)
    {
        var id = t.Terminals[index].Id;
        return t.Behavior switch
        {
            Behavior.MainPoles or Behavior.ThermalRelay or Behavior.MotorBreaker or Behavior.Rcd or Behavior.Breaker
                or Behavior.Motor1 or Behavior.Motor3 or Behavior.Motor6 or Behavior.Heater or Behavior.Lamp => true,
            Behavior.Fuse => e.Param(t, "In") > 4,
            Behavior.Source3Ph => id is "L1" or "L2" or "L3",
            Behavior.SourceAc1 => id == "L",
            Behavior.Vfd => id is "L1" or "L2" or "L3" or "U" or "V" or "W",
            Behavior.Transformer3 => true,
            _ => false,
        };
    }

    private static bool IsDcTerminal(ElementType t, PlacedElement e, int index)
    {
        var id = t.Terminals[index].Id;
        return t.Behavior switch
        {
            Behavior.SourceDc or Behavior.Transmitter => true,
            Behavior.Plc => id is "L+" or "M" || id.StartsWith("I0.") || id.StartsWith("AI"),
            Behavior.Vfd => id is "STF" or "STR" or "SD",
            _ => t.IsCoil && t.Params.Any(p => p.Key == "ac") && e.Param(t, "ac") < 0.5,
        };
    }

    private static void Classify(PanelNet net, List<(PlacedElement E, ElementType T, int Index)> members,
        Dictionary<string, PanelDevice> devices, Func<PlacedElement, string> tagOf)
    {
        bool pe = members.Any(m => m.T.Terminals[m.Index].Id == "PE");
        bool neutral = members.Any(m => m.T.IsSource && m.T.Terminals[m.Index].Id == "N")
                       || members.Any(m => m.T.Behavior == Behavior.Rcd && DeviceLibrary.TerminalLabel(m.E, m.T, m.Index) == "N");
        bool power = members.Any(m => IsPowerTerminal(m.T, m.E, m.Index));
        bool dc = members.Any(m => IsDcTerminal(m.T, m.E, m.Index));

        double amps = 0;
        foreach (var (e, t, i) in members)
            if (IsPowerTerminal(t, e, i) && devices.TryGetValue(tagOf(e), out var d)) amps = Math.Max(amps, d.RatedA);

        net.Kind = pe ? WireKind.Tierra : neutral ? WireKind.Neutro : power ? WireKind.Potencia : dc ? WireKind.MandoDc : WireKind.Mando;
        net.Section = net.Kind switch
        {
            WireKind.Potencia or WireKind.Tierra => SectionFor(amps),
            WireKind.Neutro => power ? SectionFor(amps) : 1,
            WireKind.MandoDc => 0.75,
            _ => 1,
        };
    }

    /// <summary>Sección (mm²) de un conductor de cobre en canaleta para una corriente (simplificado, PVC a 40 °C).</summary>
    public static double SectionFor(double amps) => amps switch
    {
        <= 0 => 2.5,
        <= 13 => 1.5,
        <= 18 => 2.5,
        <= 25 => 4,
        <= 32 => 6,
        <= 45 => 10,
        <= 63 => 16,
        <= 85 => 25,
        _ => 35,
    };

    public static string ColorFor(WireKind k) => k switch
    {
        WireKind.Potencia => "Negro",
        WireKind.Neutro => "Azul claro",
        WireKind.MandoDc => "Azul oscuro",
        WireKind.Tierra => "Verde-amarillo",
        _ => "Rojo",
    };
}

/// <summary>Ordena designaciones como KM2 &lt; KM10 (letras y luego número).</summary>
public sealed class TagComparer : IComparer<string>
{
    public static readonly TagComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        var (pa, na) = Split(a);
        var (pb, nb) = Split(b);
        var c = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : na != nb ? na.CompareTo(nb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static (string, int) Split(string s)
    {
        var i = s.Length;
        while (i > 0 && char.IsDigit(s[i - 1])) i--;
        return (s[..i], i < s.Length && int.TryParse(s[i..], out var n) ? n : 0);
    }
}
