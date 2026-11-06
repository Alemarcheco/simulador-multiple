using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Simulador.Electrico.Engine.Catalog;

/// <summary>
/// Importa símbolos de QElectroTech (archivos .elmt, XML). Es la librería libre de símbolos eléctricos más grande
/// (miles de aparatos de Schneider, Siemens, ABB, Legrand, Phoenix…), y sus elementos se descargan sueltos o en la
/// colección oficial. Del archivo se toman el dibujo, los bornes y el nombre; el comportamiento eléctrico se deduce del
/// tipo de enlace de QElectroTech (bobina maestra, contacto esclavo NA/NC/temporizado) o del nombre del aparato.
/// Lo que no se reconoce queda como símbolo de solo dibujo, con sus bornes para cablearlo.
/// </summary>
public static partial class QetImporter
{
    /// <summary>Píxeles de QElectroTech por unidad de rejilla del editor (QElectroTech usa rejilla de 10 px).</summary>
    public const double PixelsPerGrid = 10;

    public sealed record Result(ElementType Type, string BehaviorReason);

    public static Result Parse(string xml, string fileName, ElementCatalog builtIn, string? language = "es")
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException ex) { throw new FormatException($"{fileName}: no es un XML válido ({ex.Message})."); }
        var def = doc.Root;
        if (def is null || def.Name.LocalName != "definition")
            throw new FormatException($"{fileName}: no es un elemento de QElectroTech (falta <definition>).");
        var desc = def.Element("description") ?? throw new FormatException($"{fileName}: el elemento no tiene <description>.");

        var name = PickName(def, language) ?? Path.GetFileNameWithoutExtension(fileName);
        var primitives = new List<GraphicPrimitive>();
        var rawTerminals = new List<(double X, double Y, string Name, string Orientation)>();
        foreach (var x in desc.Elements())
        {
            var style = (string?)x.Attribute("style") ?? "";
            var dashed = style.Contains("line-style:dashed") || style.Contains("line-style:dotted") || style.Contains("line-style:dashdotted");
            var filled = Regex.IsMatch(style, @"filling:(black|gray|grey|red|blue|green|brown|yellow|lightgray|darkgray)");
            var weight = style.Contains("line-weight:hight") || style.Contains("line-weight:high") ? 1.6
                : style.Contains("line-weight:thin") ? 0.6 : style.Contains("line-weight:none") ? 0.3 : 1;
            switch (x.Name.LocalName)
            {
                case "line":
                    primitives.Add(new GraphicPrimitive(PrimitiveKind.Line, [G(x, "x1", "y1"), G(x, "x2", "y2")], Thickness: weight, Dashed: dashed));
                    break;
                case "rect":
                    primitives.Add(new GraphicPrimitive(PrimitiveKind.Rect, [], N(x, "x") / PixelsPerGrid, N(x, "y") / PixelsPerGrid,
                        N(x, "width") / PixelsPerGrid, N(x, "height") / PixelsPerGrid, Filled: filled, Thickness: weight, Dashed: dashed));
                    break;
                case "ellipse":
                    primitives.Add(new GraphicPrimitive(PrimitiveKind.Ellipse, [], N(x, "x") / PixelsPerGrid, N(x, "y") / PixelsPerGrid,
                        N(x, "width") / PixelsPerGrid, N(x, "height") / PixelsPerGrid, Filled: filled, Thickness: weight, Dashed: dashed));
                    break;
                case "circle": // formato antiguo
                {
                    var d = N(x, "diameter") / PixelsPerGrid;
                    primitives.Add(new GraphicPrimitive(PrimitiveKind.Ellipse, [], N(x, "x") / PixelsPerGrid, N(x, "y") / PixelsPerGrid, d, d,
                        Filled: filled, Thickness: weight, Dashed: dashed));
                    break;
                }
                case "arc":
                    primitives.Add(new GraphicPrimitive(PrimitiveKind.Arc, [], N(x, "x") / PixelsPerGrid, N(x, "y") / PixelsPerGrid,
                        N(x, "width") / PixelsPerGrid, N(x, "height") / PixelsPerGrid, N(x, "start"), N(x, "angle"), Thickness: weight, Dashed: dashed));
                    break;
                case "polygon":
                {
                    var pts = new List<(double, double)>();
                    for (var i = 1; x.Attribute($"x{i}") is not null && x.Attribute($"y{i}") is not null; i++) pts.Add(G(x, $"x{i}", $"y{i}"));
                    var closed = !string.Equals((string?)x.Attribute("closed"), "false", StringComparison.OrdinalIgnoreCase);
                    if (pts.Count >= 2)
                        primitives.Add(new GraphicPrimitive(PrimitiveKind.Polygon, pts, Filled: filled && closed, Closed: closed, Thickness: weight, Dashed: dashed));
                    break;
                }
                case "text":
                {
                    var text = (string?)x.Attribute("text") ?? x.Value;
                    if (string.IsNullOrWhiteSpace(text)) break;
                    var size = x.Attribute("size") is not null ? N(x, "size") : 9;
                    primitives.Add(new GraphicPrimitive(PrimitiveKind.Text, [], N(x, "x") / PixelsPerGrid, N(x, "y") / PixelsPerGrid,
                        Text: text.Trim(), Size: size * 1.2));
                    break;
                }
                case "terminal":
                    rawTerminals.Add((N(x, "x"), N(x, "y"), ((string?)x.Attribute("name") ?? "").Trim(), (string?)x.Attribute("orientation") ?? "n"));
                    break;
            }
        }
        if (primitives.Count == 0 && rawTerminals.Count == 0)
            throw new FormatException($"{fileName}: el elemento está vacío.");

        // Los bornes han de caer en la rejilla para poder cablearlos: se redondean a la unidad más cercana.
        var terms = rawTerminals
            .Select(t => (X: (int)Math.Round(t.X / PixelsPerGrid), Y: (int)Math.Round(t.Y / PixelsPerGrid), t.Name, t.Orientation))
            .ToList();
        // Origen en el primer borne (arriba a la izquierda), como los símbolos propios: así encajan en la rejilla.
        var ox = terms.Count > 0 ? terms.OrderBy(t => t.Y).ThenBy(t => t.X).First().X : 0;
        var oy = terms.Count > 0 ? terms.Min(t => t.Y) : 0;
        terms = terms.Select(t => (t.X - ox, t.Y - oy, t.Name, t.Orientation)).ToList();
        primitives = primitives.Select(p => Shift(p, -ox, -oy)).ToList();

        var (behavior, reason) = GuessBehavior(def, name, terms.Count);
        var template = builtIn.Types.FirstOrDefault(t => t.Behavior == behavior && t.Graphic is null);
        var ordered = terms.OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
        var ids = TerminalIds(behavior, ordered.Count, template);
        var terminals = ordered.Select((t, i) => new TerminalDef(ids[i], t.X, t.Y, t.Name.Length > 0 ? t.Name : ids[i])).ToList();

        var (x0, y0, x1, y1) = Extent(primitives, terminals);
        var graphic = new SymbolGraphic(primitives, x0, y0, x1, y1);
        var id = "qet:" + Slug(Path.GetFileNameWithoutExtension(fileName));
        var description = $"Símbolo importado de QElectroTech ({Path.GetFileName(fileName)}). {reason}";
        var type = new ElementType(id, name, ElementCatalog.CatImported, behavior,
            template?.TagPrefix ?? GuessPrefix(def), terminals, template?.Params ?? [], description,
            template?.DefaultTerminalText ?? "", graphic);
        return new Result(type, reason);
    }

    private static (double, double) G(XElement x, string ax, string ay) => (N(x, ax) / PixelsPerGrid, N(x, ay) / PixelsPerGrid);

    private static double N(XElement x, string attr) =>
        double.TryParse((string?)x.Attribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static GraphicPrimitive Shift(GraphicPrimitive p, double dx, double dy) => p with
    {
        Points = p.Points.Select(q => (q.X + dx, q.Y + dy)).ToList(),
        X = p.X + dx,
        Y = p.Y + dy,
    };

    private static (double, double, double, double) Extent(List<GraphicPrimitive> prims, List<TerminalDef> terms)
    {
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var p in prims)
        {
            if (p.Kind is PrimitiveKind.Line or PrimitiveKind.Polygon) { xs.AddRange(p.Points.Select(q => q.X)); ys.AddRange(p.Points.Select(q => q.Y)); }
            else if (p.Kind == PrimitiveKind.Text) { xs.Add(p.X); ys.Add(p.Y); }
            else { xs.Add(p.X); xs.Add(p.X + p.W); ys.Add(p.Y); ys.Add(p.Y + p.H); }
        }
        xs.AddRange(terms.Select(t => (double)t.X)); ys.AddRange(terms.Select(t => (double)t.Y));
        return xs.Count == 0 ? (-1, -1, 1, 1) : (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static string? PickName(XElement def, string? language)
    {
        var names = def.Element("names")?.Elements("name").ToList() ?? [];
        if (names.Count == 0) return null;
        string? By(string lang) => names.FirstOrDefault(n => (string?)n.Attribute("lang") == lang)?.Value.Trim();
        return (language is null ? null : By(language)) ?? By("es") ?? By("en") ?? By("fr") ?? names[0].Value.Trim();
    }

    private static string AllNames(XElement def) =>
        string.Join(" ", def.Element("names")?.Elements("name").Select(n => n.Value) ?? []).ToLowerInvariant();

    private static string Kind(XElement def, string key) =>
        def.Element("kindInformations")?.Elements("kindInformation").FirstOrDefault(k => (string?)k.Attribute("name") == key)?.Value.Trim().ToLowerInvariant() ?? "";

    /// <summary>Comportamiento eléctrico: primero lo que dice QElectroTech (bobina, contacto NA/NC), luego el nombre.</summary>
    public static (Behavior Behavior, string Reason) GuessBehavior(XElement def, string name, int terminals)
    {
        var link = ((string?)def.Attribute("link_type") ?? "simple").ToLowerInvariant();
        var names = (AllNames(def) + " " + name.ToLowerInvariant());
        bool Has(params string[] words) => words.Any(names.Contains);
        const string NoSim = "No se reconoce su función: se dibuja y se puede cablear, pero no actúa en la simulación.";

        if (link == "master" && terminals == 2)
        {
            var type = Kind(def, "type");
            if (type == "coil" || Has("coil", "bobine", "bobina"))
            {
                if (Has("temporis", "timer", "temporiz", "retard")) return (Behavior.CoilTimerOn, "Bobina de temporizador (maestro QElectroTech).");
                return (Has("relais", "relay", "relé", "rele ") ? Behavior.CoilRelay : Behavior.CoilContactor, "Bobina (maestro QElectroTech): mueve los contactos con su misma designación.");
            }
        }
        if (link == "slave" && terminals == 2)
        {
            var state = Kind(def, "state");
            var timed = Kind(def, "type") is "delayon" or "delayoff" or "delayonoff";
            var nc = state == "nc";
            return timed
                ? (nc ? Behavior.TimedContactNC : Behavior.TimedContactNO, $"Contacto temporizado {(nc ? "NC" : "NA")} (esclavo QElectroTech).")
                : (nc ? Behavior.ContactNC : Behavior.ContactNO, $"Contacto {(nc ? "NC" : "NA")} (esclavo QElectroTech): sigue al aparato con su designación.");
        }
        if (terminals == 2)
        {
            if (Has("lampe", "lamp", "voyant", "lámpara", "lampara", "piloto", "pilot", "signal lumin", "luminaire"))
                return (Behavior.Lamp, "Lámpara (por el nombre).");
            if (Has("bouton", "push", "pulsador", "poussoir", "button"))
            {
                var nc = Has(" nc", "nf", "ouverture", "repos", "break", "normally closed", "cerrado", "arrêt", "arret", "stop", "paro");
                return (nc ? Behavior.PushNC : Behavior.PushNO, $"Pulsador {(nc ? "NC" : "NA")} (por el nombre).");
            }
            if (Has("arrêt d'urgence", "arret d'urgence", "emergency", "emergencia", "coup de poing")) return (Behavior.EmergencyStop, "Seta de emergencia (por el nombre).");
            if (Has("fusible", "fuse")) return (Behavior.Fuse, "Fusible (por el nombre).");
            if (Has("bobine", "coil", "bobina")) return (Behavior.CoilContactor, "Bobina (por el nombre).");
            if (Has("fin de course", "limit switch", "final de carrera")) return (Behavior.LimitNO, "Final de carrera (por el nombre).");
            if (Has("interrupteur", "switch", "interruptor")) return (Behavior.Switch, "Interruptor (por el nombre).");
            if (Has("résistance", "resistance", "resistor", "resistencia", "chauffage", "heater")) return (Behavior.Heater, "Resistencia (por el nombre).");
            if (Has("sonnerie", "bell", "buzzer", "timbre", "klaxon")) return (Behavior.Bell, "Timbre (por el nombre).");
            if (Has("contact")) return (Has("nc", "nf", "ouverture", "repos", "break") ? Behavior.ContactNC : Behavior.ContactNO, "Contacto (por el nombre).");
        }
        if (terminals == 3 && Has("moteur", "motor") && !Has("monophas", "single")) return (Behavior.Motor3, "Motor trifásico (por el nombre).");
        if (terminals == 2 && Has("moteur", "motor")) return (Behavior.Motor1, "Motor monofásico (por el nombre).");
        if (terminals == 1 && Has("borne", "terminal", "bornier")) return (Behavior.Junction, "Borne (por el nombre).");
        return (Behavior.Graphic, NoSim);
    }

    private static string GuessPrefix(XElement def)
    {
        var names = AllNames(def);
        if (names.Contains("borne") || names.Contains("terminal")) return "X";
        if (names.Contains("transfo")) return "T";
        if (names.Contains("capteur") || names.Contains("sensor") || names.Contains("détecteur")) return "B";
        return "A";
    }

    /// <summary>Ids de borne que espera el simulador para ese comportamiento (en orden: arriba-izquierda primero).</summary>
    private static string[] TerminalIds(Behavior b, int count, ElementType? template)
    {
        if (template is not null && template.Terminals.Count == count && b != Behavior.Graphic)
        {
            // Mismo orden geométrico que el símbolo propio.
            return template.Terminals.OrderBy(t => t.Y).ThenBy(t => t.X).Select(t => t.Id).ToArray();
        }
        return Enumerable.Range(1, count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    private static string Slug(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return NonSlug().Replace(sb.ToString(), "-").Trim('-');
    }
}
