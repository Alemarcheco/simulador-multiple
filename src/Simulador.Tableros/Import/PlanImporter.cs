using System.Globalization;
using System.Text.RegularExpressions;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Import;

/// <summary>Resultado de leer un plano: el tablero propuesto y el informe de lo encontrado y lo supuesto.</summary>
public sealed class PlanImportResult
{
    public required PanelDocument Document { get; init; }
    public List<string> Notes { get; } = [];
    public int Pages { get; init; }
    public int PagesWithText { get; init; }
    public int Connections { get; set; }
}

/// <summary>
/// Lee el texto de un plano eléctrico en PDF (exportado por cualquier programa de CAD eléctrico) y monta un tablero:
/// reconoce las designaciones IEC 81346 (-Q1, -KM1, =A1+B2-K3…), la lista de materiales (descripción, referencia,
/// calibre), y las listas de bornes y de cables (conexiones borne a borne, sección y color). Después elige la
/// envolvente, distribuye los aparatos, tiende los hilos por las canaletas y calcula el ventilador.
/// No mira los dibujos: lo que solo está dibujado (sin texto) no se puede leer.
/// </summary>
public static partial class PlanImporter
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Prefijos de designación que se aceptan siempre que el texto aparezca al menos dos veces.</summary>
    private static readonly HashSet<string> StrongPrefixes =
        ["Q", "QA", "QF", "QM", "QS", "F", "FU", "FR", "FC", "K", "KA", "KM", "KT", "KF", "KL", "S", "SB", "SA", "SF", "SJ",
         "SQ", "H", "HL", "HA", "PF", "M", "MA", "X", "XT", "XD", "T", "TA", "TC", "TV", "U", "TB", "G", "GB", "GD", "P", "PG", "B", "BG", "PE",
         // Nomenclatura americana (NEMA/ANSI), habitual en México: CB1, TB-120N, PS1, CR1…
         "CB", "TB", "TS", "PS", "CR", "PB", "PL", "SS", "LT", "TR", "DS", "FU", "SPD", "RY", "LS", "PLC", "VFD", "MCB", "MS"];
    /// <summary>Prefijos ambiguos (A3 es también un formato de hoja…): solo con guion delante o dentro de una lista.</summary>
    private static readonly HashSet<string> WeakPrefixes = ["A", "E", "EA", "R", "Y", "YV", "MB", "W", "WD", "Z", "C"];

    [GeneratedRegex(@"^(?:[=+][A-Za-z0-9._]+)*(-)?([A-Z]{1,3})(\d{1,3})([A-Z]?)(?::([A-Za-z0-9+\-./]{1,8}))?$")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"^(?:[A-Z]{0,2}\d{1,3}[A-Z]?|L[123]|L|N|PE|U|V|W|[UVW][12]|T[123]|A[123]|B[12]|L\+|L-|M|\+|-|\+24V|0V|24V|NO|NC|COM|C)$", RegexOptions.IgnoreCase)]
    private static partial Regex TerminalRx();

    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)\s*(?:mm²|mm2|mm\^2)$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRx();

    [GeneratedRegex(@"^(?:[=+][A-Za-z0-9._]+)*-PE(?::(PE|\d{1,3}))?$")]
    private static partial Regex PeRx();

    [GeneratedRegex(@"^\d+(?:[.,]\d+)?-(\d+(?:[.,]\d+)?)(A)?$")]
    private static partial Regex RangeRx();

    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)$")]
    private static partial Regex NumberRx();

    [GeneratedRegex(@"^([BCDKZ])(\d{1,3})$")]
    private static partial Regex CurveRx();

    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)(A|kW|KW|kVA|VA|mA|V)$")]
    private static partial Regex RatingRx();

    [GeneratedRegex(@"^([1-4])P(?:\+N)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PolesRx();

    /// <summary>Una designación leída de una palabra.</summary>
    private readonly record struct TagToken(string Tag, string Prefix, string? Terminal, bool Dashed);

    private sealed class Found
    {
        public string Tag = "";
        public string Prefix = "";
        public int Count;
        public bool Dashed;
        public bool InList;
        public string? Description;
        public string? Reference;
        public double RatedA;
        public double PowerKw;
        public int Poles;
        public string Context = "";
        public readonly SortedSet<string> Terminals = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Link
    {
        public readonly List<WireEnd> Ends = [];
        public string? Number;
        public double Section;
        public string? Color;
    }

    public static PlanImportResult Import(string path) => Import(PlanText.ReadPdf(path));

    /// <summary>Lee el PDF (con OCR para las páginas de letras dibujadas, si hay reconocedor) y monta el tablero.</summary>
    public static PlanImportResult Import(string path, IPlanOcr? ocr, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var plan = PlanText.ReadPdf(path, ocr, progress, ct);
        progress?.Report("Montando el tablero…");
        return Import(plan);
    }

    public static PlanImportResult Import(PlanText plan)
    {
        var found = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
        var links = new List<Link>();
        var bomPages = new List<int>();
        var listPages = new List<int>();
        var withText = plan.Pages.Count(p => p.Words.Count > 0);
        var labelCount = 0;

        // En las páginas leídas por OCR, «CBI(3)» y «Fl(L)» son CB1(3) y F1(L).
        foreach (var page in plan.Pages.Where(p => plan.OcrPages.Contains(p.Number)))
        {
            for (var i = 0; i < page.Words.Count; i++)
            {
                var fixedText = FixOcrDigits(page.Words[i].Text);
                if (fixedText != page.Words[i].Text) page.Words[i] = page.Words[i] with { Text = fixedText };
            }
        }

        foreach (var page in plan.Pages)
        {
            var text = page.Text;
            var isBom = ContainsAny(text, "lista de materiales", "lista de material", "listado de materiales", "bill of material", "parts list",
                "stückliste", "lista de piezas", "relación de material", "materiales");
            var isList = ContainsAny(text, "lista de cables", "lista de hilos", "lista de conductores", "lista de conexiones", "lista de bornes",
                "plano de bornes", "regleta", "bornero", "terminal", "wire list", "wiring list", "connection list", "klemmen", "cable list",
                "conexiones", "cableado", "bornes");
            if (isBom) bomPages.Add(page.Number);
            string? strip = null;

            foreach (var line in page.Lines())
            {
                var tokens = line.Select(w => w.Text.Trim(',', ';', '(', ')', '[', ']', '|')).Where(t => t.Length > 0).ToList();
                var tags = new List<(int Index, TagToken T)>();
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (ParseTag(tokens[i]) is not { } t) continue;
                    // «-H1 X1»: X1 es el borne del piloto, no una regleta.
                    if (!t.Dashed && tags.Count > 0 && tags[^1].Index == i - 1 && tags[^1].T.Terminal is null && TerminalRx().IsMatch(tokens[i])) continue;
                    tags.Add((i, t));
                }
                foreach (var (_, t) in tags) Count(found, t, isBom || isList);

                // Etiquetas de hilo «CB1(3) / CB120(4)» (SolidWorks Electrical y otros): cada una es una conexión.
                foreach (var l in ReadWireLabels(line))
                {
                    foreach (var e in l.Ends) Count(found, new TagToken(e.Tag, PrefixOf(e.Tag), e.Terminal, true), true);
                    links.Add(l);
                    labelCount++;
                    if (!listPages.Contains(page.Number)) listPages.Add(page.Number);
                }

                // Lista de materiales: una designación al principio del renglón y texto detrás.
                if (isBom && tags.Count >= 1 && tags[0].Index <= 2 && tags[0].T.Terminal is null)
                    ReadBomLine(found[tags[0].T.Tag], tokens.Skip(tags[0].Index + 1).ToList());
                else if (tags.Count == 1 && tags[0].T.Terminal is null)
                    ReadRatings(found[tags[0].T.Tag], tokens, tags[0].Index);

                if (!isList) continue;
                // Encabezado de regleta: «-X1» solo (o con su título) da contexto a los renglones siguientes.
                if (tags.Count == 1 && tags[0].T.Terminal is null && tags[0].T.Prefix is "X" or "XT" or "XD"
                    && (tags[0].Index + 1 >= tokens.Count || !TerminalRx().IsMatch(tokens[tags[0].Index + 1])))
                {
                    strip = tags[0].T.Tag;
                    continue;
                }
                var link = ReadConnection(tokens, tags, strip);
                if (link is null) continue;
                links.Add(link);
                if (!listPages.Contains(page.Number)) listPages.Add(page.Number);
            }

            // En los esquemas el calibre va debajo de la designación («CB1» y debajo «6 AMP»).
            foreach (var w in page.Words)
            {
                if (ParseTag(w.Text) is not { Terminal: null } t || !found.TryGetValue(t.Tag, out var f)) continue;
                var below = page.Words.Where(o => o != w && o.Y > w.Y && o.Y < w.Y + w.Height * 3.2
                                                  && Math.Abs((o.X + o.Width / 2) - (w.X + w.Width / 2)) < 40)
                    .OrderBy(o => o.Y).ThenBy(o => o.X).Select(o => o.Text).ToList();
                if (below.Count > 0) ReadRatings(f, below, -1);
            }
        }

        // El OCR confunde letras parecidas (C1RLY → CIRLY): se juntan con la designación más repetida.
        var merged = MergeConfusables(found);
        if (merged.Count > 0)
            foreach (var l in links)
                for (var i = 0; i < l.Ends.Count; i++)
                    if (merged.TryGetValue(l.Ends[i].Tag, out var to)) l.Ends[i] = l.Ends[i] with { Tag = to };

        // Designaciones aceptadas.
        var accepted = found.Values.Where(f =>
                f.Dashed && (f.Tag.Any(char.IsDigit) || f.Count >= 2 || f.Tag == "PE") || f.InList && (StrongPrefixes.Contains(f.Prefix) || f.Description is not null)
                         || StrongPrefixes.Contains(f.Prefix) && f.Count >= 2)
            .ToDictionary(f => f.Tag, StringComparer.OrdinalIgnoreCase);

        // Conexiones con aparatos aceptados y bornes de cada aparato.
        links = links.Select(l =>
        {
            var c = new Link { Number = l.Number, Section = l.Section, Color = l.Color };
            c.Ends.AddRange(l.Ends.Where(e => accepted.ContainsKey(e.Tag)).Distinct());
            return c;
        }).Where(l => l.Ends.Count >= 2).ToList();
        foreach (var l in links)
            foreach (var e in l.Ends) accepted[e.Tag].Terminals.Add(e.Terminal);

        var doc = new PanelDocument
        {
            Name = $"Tablero · {Path.GetFileNameWithoutExtension(plan.FileName)}",
            Description = $"Montado automáticamente desde el plano «{plan.FileName}».",
        };
        var result = new PlanImportResult { Document = doc, Pages = plan.Pages.Count, PagesWithText = withText, Connections = links.Count };

        if (withText == 0 || plan.Pages.Count > 0 && plan.UnreadPages.Count == plan.Pages.Count)
        {
            result.Notes.Add(plan.OcrError is not null
                ? $"El PDF no tiene texto (las letras están dibujadas o escaneadas) y el reconocimiento de texto falló: {plan.OcrError}"
                : "El PDF no tiene texto (las letras están dibujadas o escaneadas) y este equipo no tiene reconocedor de texto (OCR). En Windows 10/11 se usa el de Windows; si no funciona, exporta el PDF desde el programa de CAD con «texto como texto» (TrueType en vez de SHX).");
            doc.ImportNotes.AddRange(result.Notes);
            return result;
        }

        foreach (var f in accepted.Values.OrderBy(f => f.Tag, TagComparer.Instance))
            doc.Devices.Add(MakeDevice(f));

        // Potenciales: une los extremos que comparten borne (un renglón de la lista = un potencial).
        var nets = BuildNets(links, doc);
        AddFieldCables(doc, nets);
        if (nets.Any(n => n.Kind == WireKind.Tierra) && doc.FindTag("PE") is null)
            doc.Devices.Add(DeviceLibrary.Create("barra_pe", "PE"));

        var fits = AutoLayout.Arrange(doc);
        doc.Wires = DuctRouter.BuildWires(doc, nets);
        PanelBuilder.SuggestFan(doc);

        // Informe.
        var plate = doc.OnPlate.Count(d => d.Kind != "barra_pe");
        result.Notes.Add($"Leídas {plan.Pages.Count} páginas ({withText} con texto). Encontrados {doc.Devices.Count(d => d.Kind != "barra_pe")} aparatos: {plate} en la placa, {doc.OnDoor.Count()} en la puerta y {doc.InField.Count()} en campo.");
        if (bomPages.Count > 0) result.Notes.Add($"Lista de materiales en {PagesText(bomPages)}.");
        else result.Notes.Add("No se encontró lista de materiales: las descripciones y medidas son las típicas de cada tipo de aparato.");
        if (plan.OcrPages.Count > 0)
            result.Notes.Add($"{plan.OcrPages.Count} páginas traían las letras dibujadas y se leyeron con {plan.OcrName}. Revisa las designaciones: el reconocimiento puede confundir letras parecidas (B/8, O/0, S/5).");
        if (plan.UnreadPages.Count > 0)
            result.Notes.Add(plan.OcrError is not null
                ? $"No se pudieron reconocer {plan.UnreadPages.Count} páginas con letras dibujadas: {plan.OcrError}"
                : $"{plan.UnreadPages.Count} páginas traen las letras dibujadas como trazos y este equipo no tiene reconocedor de texto (OCR): no se leyeron.");
        if (labelCount > 0) result.Notes.Add($"{labelCount} conexiones leídas de las etiquetas de los hilos («CB1(3) / CB120(4)»).");
        if (links.Count > 0) result.Notes.Add($"{links.Count} conexiones en total ({PagesText(listPages)}); salen {doc.Wires.Count} hilos.");
        else result.Notes.Add("No se encontraron listas de cables ni de bornes con texto: los aparatos quedan colocados pero sin hilos. Si el programa de CAD puede sacar la lista de conexiones, inclúyela en el PDF.");
        var guessed = doc.Devices.Where(d => d.Kind == "aparato").Select(d => d.Tag).ToList();
        if (guessed.Count > 0) result.Notes.Add($"Sin tipo reconocido (quedan como aparato genérico, revísalos): {string.Join(", ", guessed)}.");
        if (!fits) result.Notes.Add("Ni el armario más grande alcanza para todo: revisa la distribución o usa dos envolventes.");
        result.Notes.Add($"Propuesta: envolvente {N(doc.Enclosure.Width)}×{N(doc.Enclosure.Height)}×{N(doc.Enclosure.Depth)} mm, {doc.Rails.Count} carriles DIN, {doc.Ducts.Count} canaletas" +
                         (doc.Enclosure.FanFlow > 0 ? $", ventilador de {N(doc.Enclosure.FanFlow)} m³/h." : ", sin ventilador."));
        result.Notes.Add("Los dibujos del PDF no se leen, solo su texto: revisa que no falte ningún aparato ni conexión.");
        doc.ImportNotes.AddRange(result.Notes);
        return result;
    }

    // ------------------------------------------------------------------ lectura

    private static TagToken? ParseTag(string token)
    {
        var pe = PeRx().Match(token);
        if (pe.Success) return new TagToken("PE", "PE", pe.Groups[1].Success ? pe.Groups[1].Value : null, true);
        var m = TagRx().Match(token);
        if (!m.Success) return null;
        var prefix = m.Groups[2].Value;
        var dashed = m.Groups[1].Success || token.StartsWith('=') || token.StartsWith('+');
        if (!StrongPrefixes.Contains(prefix) && !(dashed && WeakPrefixes.Contains(prefix))) return null;
        // «PE» y similares no son designaciones; «A4» suelto (formato) tampoco.
        if (!dashed && WeakPrefixes.Contains(prefix)) return null;
        var tag = prefix + m.Groups[3].Value + m.Groups[4].Value;
        var term = m.Groups[5].Success ? m.Groups[5].Value : null;
        return new TagToken(tag, prefix, term, dashed);
    }

    private static void Count(Dictionary<string, Found> found, TagToken t, bool inList)
    {
        if (!found.TryGetValue(t.Tag, out var f)) found[t.Tag] = f = new Found { Tag = t.Tag, Prefix = t.Prefix };
        f.Count++;
        f.Dashed |= t.Dashed;
        f.InList |= inList;
    }

    private static void ReadBomLine(Found f, List<string> rest)
    {
        var words = new List<string>();
        foreach (var w in rest)
        {
            if (ParseTag(w) is not null) break;
            // Cantidad suelta («1», «2 uds»).
            if (words.Count == 0 && NumberRx().IsMatch(w)) continue;
            if (w is "ud" or "uds" or "pza" or "pzas" or "pcs" or "x") continue;
            if (IsReference(w) && f.Reference is null && words.Count > 0) { f.Reference = w; continue; }
            words.Add(w);
        }
        if (words.Count > 0)
        {
            var desc = string.Join(' ', words);
            if (f.Description is null || desc.Length > f.Description.Length) f.Description = desc;
        }
        ReadRatings(f, rest, -1);
    }

    private static void ReadRatings(Found f, List<string> tokens, int tagIndex)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i == tagIndex) continue;
            var t = tokens[i];
            f.Context += " " + t.ToLowerInvariant();
            if (CurveRx().Match(t) is { Success: true } c) f.RatedA = Math.Max(f.RatedA, double.Parse(c.Groups[2].Value, Es));
            else if (t.Length is >= 3 and <= 5 && t[0] == 'A' && t.Skip(1).All(char.IsDigit)) f.RatedA = Math.Max(f.RatedA, double.Parse(t[1..], Es)); // «A63»
            else if (RatingRx().Match(t.Replace(" ", "")) is { Success: true } r)
                Rating(f, r.Groups[1].Value, r.Groups[2].Value);
            else if (i + 1 < tokens.Count && NumberRx().IsMatch(t) && tokens[i + 1] is "A" or "kW" or "KW" or "kVA" or "VA" or "V" or "mA")
                Rating(f, t, tokens[i + 1]);
            else if (i + 1 < tokens.Count && NumberRx().IsMatch(t) && tokens[i + 1].ToUpperInvariant() is "AMP" or "AMPS" or "AMPERES" or "AMPERIOS")
                Rating(f, t, "A");
            else if (RangeRx().Match(t) is { Success: true } g && (g.Groups[2].Success || i + 1 < tokens.Count && tokens[i + 1] == "A"))
                Rating(f, g.Groups[1].Value, "A");
            else if (PolesRx().Match(t) is { Success: true } p) f.Poles = int.Parse(p.Groups[1].Value, Es) + (t.Contains('N') ? 1 : 0);
        }
        if (f.Context.Length > 600) f.Context = f.Context[^600..];
    }

    private static void Rating(Found f, string number, string unit)
    {
        var v = double.Parse(number.Replace('.', ','), Es);
        switch (unit)
        {
            case "A": if (v <= 2500) f.RatedA = Math.Max(f.RatedA, v); break;
            case "kW" or "KW": f.PowerKw = Math.Max(f.PowerKw, v); break;
        }
    }

    /// <summary>Código comercial: letras y números mezclados, de 5 caracteres o más, que no es un valor ni una designación.</summary>
    private static bool IsReference(string w) =>
        w.Length >= 5 && w.Any(char.IsDigit) && w.Any(char.IsLetter) && !RatingRx().IsMatch(w) && !SectionRx().IsMatch(w)
        && !w.Contains("mm", StringComparison.OrdinalIgnoreCase) && !w.Contains(':');

    /// <summary>
    /// Un renglón de lista de cables o de bornes: designaciones con borne («-KM1:2», o «-KM1» y «2» en columnas
    /// seguidas), más número de hilo, sección y color si los hay.
    /// </summary>
    private static Link? ReadConnection(List<string> tokens, List<(int Index, TagToken T)> tags, string? strip)
    {
        var link = new Link();
        var used = new HashSet<int>();
        foreach (var (i, t) in tags)
        {
            used.Add(i);
            if (t.Terminal is not null) link.Ends.Add(new WireEnd(t.Tag, t.Terminal));
            else if (i + 1 < tokens.Count && !tags.Any(x => x.Index == i + 1) && TerminalRx().IsMatch(tokens[i + 1]))
            {
                link.Ends.Add(new WireEnd(t.Tag, tokens[i + 1]));
                used.Add(i + 1);
            }
        }
        // Renglón de un plano de bornes: «3  -Q1:2  -M1:U1» bajo el encabezado «-X1».
        if (strip is not null && tokens.Count > 0 && !used.Contains(0) && TerminalRx().IsMatch(tokens[0]) && link.Ends.Count >= 1
            && link.Ends.All(e => !e.Tag.Equals(strip, StringComparison.OrdinalIgnoreCase)))
        {
            link.Ends.Insert(0, new WireEnd(strip, tokens[0]));
            used.Add(0);
        }
        if (link.Ends.Count < 2) return null;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (used.Contains(i)) continue;
            var t = tokens[i];
            if (SectionRx().Match(t) is { Success: true } s) link.Section = double.Parse(s.Groups[1].Value.Replace('.', ','), Es);
            else if (i + 1 < tokens.Count && NumberRx().IsMatch(t) && tokens[i + 1] is "mm²" or "mm2")
                link.Section = double.Parse(t.Replace('.', ','), Es);
            else if (ColorOf(t, i + 1 < tokens.Count ? tokens[i + 1] : "") is { } c) link.Color ??= c;
            else if (link.Number is null && i < tags[0].Index && (NumberRx().IsMatch(t) || t.Length <= 6 && t.Any(char.IsDigit) && !t.Contains(':')))
                link.Number = t;
        }
        return link;
    }

    private static string? ColorOf(string word, string next)
    {
        var w = word.ToLowerInvariant().Replace('_', '-');
        var n = next.ToLowerInvariant();
        return w switch
        {
            "negro" or "black" or "bk" or "ne" => "Negro",
            "rojo" or "red" or "rd" or "ro" => "Rojo",
            "azul" when n is "oscuro" => "Azul oscuro",
            "dbu" or "azul-oscuro" or "darkblue" => "Azul oscuro",
            "azul" or "blue" or "bu" or "lbu" or "az" or "azul-claro" or "celeste" => "Azul claro",
            "gnye" or "gn-ye" or "gn/ye" or "ye-gn" or "ye/gn" or "verde-amarillo" or "v-a" or "va" or "green-yellow" => "Verde-amarillo",
            "verde" when n is "amarillo" or "-amarillo" => "Verde-amarillo",
            "marrón" or "marron" or "brown" or "bn" => "Marrón",
            "gris" or "grey" or "gray" or "gy" => "Gris",
            "blanco" or "white" or "wh" => "Blanco",
            "naranja" or "orange" or "og" => "Naranja",
            "violeta" or "violet" or "vt" => "Violeta",
            _ => null,
        };
    }

    [GeneratedRegex(@"([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*)\(([A-Za-z0-9+\-]{1,5})\)\s*/\s*([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*)\(([A-Za-z0-9+\-]{1,5})\)")]
    private static partial Regex WireLabelRx();

    [GeneratedRegex(@"(?<=[A-Za-z0-9])\s+(?=\()|(?<=\()\s+|\s+(?=\))")]
    private static partial Regex SpacesInRefRx();

    [GeneratedRegex(@"^(TB|TS|XT|XD|X)-?([A-Z0-9]+(?:-[A-Z0-9]+)*)-(\d{1,3})$")]
    private static partial Regex StripTerminalRx();

    /// <summary>Etiquetas «A(x) / B(y)» de un renglón (puede haber varias separadas por comas).</summary>
    private static IEnumerable<Link> ReadWireLabels(List<PlanWord> line)
    {
        var text = string.Join(' ', line.Select(w => w.Text));
        if (!text.Contains('/') || !text.Contains('(')) yield break;
        // Cada OCR parte distinto: «CB1 (3)», «CB1( 3 )», «CB1(3)/CB120(4)».
        text = SpacesInRefRx().Replace(text, "");
        foreach (Match m in WireLabelRx().Matches(text))
        {
            var a = NormalizeRef(m.Groups[1].Value, m.Groups[2].Value);
            var b = NormalizeRef(m.Groups[3].Value, m.Groups[4].Value);
            if (a == b) continue;
            var l = new Link();
            l.Ends.Add(a);
            l.Ends.Add(b);
            yield return l;
        }
    }

    /// <summary>
    /// «TB-120N-1(1)» es el borne 1 de la regleta TB-120N (el paréntesis es el punto de conexión del borne);
    /// «CB1(3)» es el borne 3 del aparato CB1.
    /// </summary>
    internal static WireEnd NormalizeRef(string tag, string terminal)
    {
        var m = StripTerminalRx().Match(tag);
        if (m.Success) return new WireEnd($"{m.Groups[1].Value}-{m.Groups[2].Value}", m.Groups[3].Value);
        return new WireEnd(tag, terminal);
    }

    /// <summary>
    /// El OCR lee a menudo el 1 como I, l o | y el 0 como O justo después del prefijo («CBI», «Fl(L)», «KI-R4»).
    /// Si tras un prefijo conocido viene una de esas letras y luego fin, «(», «-» o cifras, se cambian por cifras.
    /// </summary>
    internal static string FixOcrDigits(string word)
    {
        var lead = word.StartsWith('-') ? 1 : 0;
        foreach (var p in StrongPrefixes.Where(p => word.Length > lead + p.Length && string.CompareOrdinal(word, lead, p, 0, p.Length) == 0)
                     .OrderByDescending(p => p.Length))
        {
            var i = lead + p.Length;
            var j = i;
            while (j < word.Length && (char.IsDigit(word[j]) || word[j] is 'I' or 'l' or '|' or 'O')) j++;
            if (j == i || !word[i..j].Any(c => c is 'I' or 'l' or '|' or 'O')) continue;
            if (j < word.Length && word[j] is not ('(' or '-' or ':' or ')' or ',')) continue;
            var digits = word[i..j].Replace('I', '1').Replace('l', '1').Replace('|', '1').Replace('O', '0');
            return word[..i] + digits + word[j..];
        }
        return word;
    }

    private static string PrefixOf(string tag) => new(tag.TakeWhile(char.IsLetter).ToArray());

    // ------------------------------------------------------------------ montaje

    /// <summary>Tipo de aparato por la descripción (si la hay) o por el prefijo de la designación.</summary>
    [GeneratedRegex(@"^([A-Z]{2,5})-(\d{2,5}[A-Z]{0,3}|[A-Z]{1,2}\d{1,3}[A-Z]{0,2}|X{1,2}\d?)$")]
    private static partial Regex IsaRx();

    private static readonly HashSet<string> IoSegments = ["DI", "DO", "AI", "AO", "AQ", "AOQ", "RLY", "MPU", "ACT", "AL", "AT", "IO", "CPU", "MOD", "COM"];

    /// <summary>
    /// Tipo por la forma de la designación, para las nomenclaturas que no usan los prefijos IEC: instrumentos ISA
    /// (PIT-3202 transmisor, PSH-81A presostato, FCV-3201 válvula), módulos de E/S de un controlador (C1-DI, DO-C1,
    /// C2R1, C1RLY) y lámparas (LZ1).
    /// </summary>
    internal static string? KindByTag(string tag)
    {
        var isa = IsaRx().Match(tag);
        if (isa.Success && isa.Groups[1].Value is var l && l[0] is 'P' or 'T' or 'F' or 'L' or 'Z' or 'A' or 'W' or 'D' or 'S' or 'V' or 'H' or 'X'
            && !StrongPrefixes.Contains(l) && l is not ("TB" or "TS"))
        {
            if (l.EndsWith("CV") || l.EndsWith('V') && l.Length >= 2 || l.EndsWith("Y")) return "receptor";
            return "captador";
        }
        var segs = tag.Split('-');
        if (segs.Length >= 2 && segs.Any(x => IoSegments.Contains(x)) && segs.Any(x => x.Length >= 2 && x[0] == 'C' && char.IsDigit(x[1]))) return "plc";
        if (System.Text.RegularExpressions.Regex.IsMatch(tag, @"^C\d{1,2}(R\d{1,2}|RLY|-RLY)?$")) return "plc";
        if (System.Text.RegularExpressions.Regex.IsMatch(tag, @"^LZ\d{1,3}$")) return "piloto";
        if (tag.StartsWith("HMI", StringComparison.Ordinal)) return "instrumento";
        return null;
    }

    private static readonly Dictionary<char, char[]> Confusable = new()
    {
        ['I'] = ['1'], ['L'] = ['1'], ['A'] = ['1'], ['O'] = ['0'], ['0'] = ['O'], ['S'] = ['5'], ['5'] = ['S'],
        ['B'] = ['8'], ['8'] = ['B'], ['Z'] = ['2'], ['2'] = ['Z'], ['G'] = ['6'], ['6'] = ['G'], ['1'] = ['I', 'L'],
    };

    /// <summary>Junta las designaciones que difieren en una letra confundible con otra mucho más repetida.</summary>
    private static Dictionary<string, string> MergeConfusables(Dictionary<string, Found> found)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in found.Values.OrderBy(f => f.Count).ToList())
        {
            if (!found.ContainsKey(f.Tag)) continue;
            Found? best = null;
            for (var i = 0; i < f.Tag.Length && best is null; i++)
            {
                if (!Confusable.TryGetValue(f.Tag[i], out var alts)) continue;
                foreach (var a in alts)
                {
                    var v = f.Tag[..i] + a + f.Tag[(i + 1)..];
                    if (found.TryGetValue(v, out var g) && (g.Count > f.Count || g.Count == f.Count && char.IsDigit(a) && !char.IsDigit(f.Tag[i])))
                    {
                        best = g;
                        break;
                    }
                }
            }
            if (best is null) continue;
            best.Count += f.Count;
            best.Dashed |= f.Dashed;
            best.InList |= f.InList;
            best.RatedA = Math.Max(best.RatedA, f.RatedA);
            foreach (var t in f.Terminals) best.Terminals.Add(t);
            found.Remove(f.Tag);
            map[f.Tag] = best.Tag;
        }
        // Cadenas (A → B → C): cada una a la final.
        foreach (var k in map.Keys.ToList())
            while (map.TryGetValue(map[k], out var next)) map[k] = next;
        return map;
    }

    internal static string KindFor(string prefix, string text)
    {
        var t = text.ToLowerInvariant();
        if (prefix == "PE") return "barra_pe";
        if (ContainsAny(t, "seta", "emergencia", "emergency", "e-stop", "paro de emergencia")) return "seta";
        if (ContainsAny(t, "guardamotor", "disyuntor motor", "motor protect", "motor circuit breaker", "protector de motor")) return "guardamotor";
        if (ContainsAny(t, "diferencial", "rcd", "rccb", "rcbo", "residual")) return "diferencial";
        if (ContainsAny(t, "magnetot", "interruptor automático", "interruptor automatico", "circuit breaker", "mcb", "pastilla")) return "magnetotermico";
        if (ContainsAny(t, "relé térmico", "rele termico", "relé de sobrecarga", "rele de sobrecarga", "overload", "thermal relay")) return "rele_termico";
        if (ContainsAny(t, "contactor")) return "contactor";
        if (ContainsAny(t, "breaker")) return "magnetotermico";
        if (ContainsAny(t, "fusible", "fuse", "portafusible")) return "fusible";
        if (ContainsAny(t, "temporiz", "timer", "retardo")) return "temporizador";
        if (ContainsAny(t, "telerruptor", "impulse relay")) return "telerruptor";
        if (ContainsAny(t, "variador", "inverter", "vfd", "drive", "convertidor de frecuencia", "arrancador suave", "soft starter")) return "variador";
        if (ContainsAny(t, "fuente", "power supply", "alimentación 24", "alimentacion 24", "psu")) return "fuente";
        if (ContainsAny(t, "plc", "autómata", "automata", "controlador lógico", "cpu", "módulo de entradas", "modulo de entradas", "i/o")) return "plc";
        if (ContainsAny(t, "arduino", "microcontrolador")) return "mcu";
        if (ContainsAny(t, "transformador", "transformer"))
            return ContainsAny(t, "trifásico", "trifasico", "kva") ? "trafo_potencia" : "trafo_mando";
        if (ContainsAny(t, "selector", "conmutador", "maneta", "selector switch")) return "selector";
        if (ContainsAny(t, "pulsador", "push button", "pushbutton", "botón", "boton")) return "pulsador";
        if (ContainsAny(t, "piloto", "señaliz", "senaliz", "lámpara", "lampara", "indicator", "pilot light", "baliza")) return "piloto";
        if (ContainsAny(t, "amperímetro", "amperimetro", "voltímetro", "voltimetro", "multímetro", "analizador", "medidor", "contador de energía", "meter")) return "instrumento";
        if (ContainsAny(t, "motor", "bomba", "ventilador", "compresor")) return prefix is "M" or "MA" ? "motor" : prefix is "Q" or "QF" or "QA" ? "magnetotermico" : "motor";
        if (ContainsAny(t, "final de carrera", "sensor", "detector", "presostato", "termostato", "flotador", "captador", "limit switch", "proximity")) return "captador";
        if (ContainsAny(t, "electroválvula", "electrovalvula", "válvula", "valvula", "resistencia", "calefactor", "solenoid")) return "receptor";
        if (ContainsAny(t, "borne", "regleta", "terminal block", "bornero", "clema")) return "regleta";
        if (ContainsAny(t, "relé", "rele", "relay")) return "rele";
        if (ContainsAny(t, "interruptor", "seccionador", "switch")) return "magnetotermico";
        return prefix switch
        {
            "PE" => "barra_pe",
            "KM" or "QA" => "contactor",
            "Q" or "QF" or "QS" => "magnetotermico",
            "QM" => "guardamotor",
            "F" or "FU" or "FC" => "fusible",
            "FR" => "rele_termico",
            "K" or "KA" or "KL" => "rele",
            "KT" => "temporizador",
            "KF" => "plc",
            "S" or "SB" or "SF" or "SJ" => "pulsador",
            "SS" => "selector",
            "SA" => "selector",
            "SQ" or "B" or "BG" => "captador",
            "H" or "HL" or "HA" or "PF" => "piloto",
            "M" or "MA" => "motor",
            "X" or "XT" or "XD" => "regleta",
            "T" or "TA" or "TC" or "TV" => "trafo_mando",
            "U" or "VFD" => "variador",
            "CB" or "MCB" or "DS" => "magnetotermico",
            "TB" or "TS" => "regleta",
            "PS" => "fuente",
            "CR" or "RY" => "rele",
            "PB" => "pulsador",
            "PL" or "LT" => "piloto",
            "LS" => "captador",
            "TR" => "trafo_mando",
            "PLC" => "plc",
            "MS" => "guardamotor",
            "G" or "GB" or "GD" => "fuente",
            "P" or "PG" => "instrumento",
            "E" or "EA" or "R" or "Y" or "YV" or "MB" => "receptor",
            _ => "aparato",
        };
    }

    private static PanelDevice MakeDevice(Found f)
    {
        var kind = KindByTag(f.Tag) ?? KindFor(f.Prefix, (f.Description ?? "") + " " + f.Context);
        var d = DeviceLibrary.Create(kind, f.Tag);
        if (f.Description is not null) d.Description = Cap(f.Description);
        if (f.Reference is not null) d.Reference = f.Reference;
        d.RatedA = f.RatedA;
        if (kind == "motor" && f.PowerKw > 0 && d.RatedA <= 0) d.RatedA = Math.Round(f.PowerKw * 2, 1); // ~2 A/kW a 400 V
        if (f.Description is null)
        {
            var extra = new List<string>();
            if (f.RatedA > 0) extra.Add($"{N(f.RatedA)} A");
            if (f.PowerKw > 0) extra.Add($"{N(f.PowerKw)} kW");
            if (extra.Count > 0) d.Description += " " + string.Join(", ", extra);
        }

        // Ancho según polos y calibre.
        if (kind is "magnetotermico" or "fusible" && f.Poles > 0) d.Width = f.Poles * DeviceLibrary.Module;
        if (kind == "diferencial" && f.Poles >= 3) d.Width = 4 * DeviceLibrary.Module;
        if (kind == "magnetotermico" && f.Poles == 0 && ContainsAny(f.Context, "tripolar", "trifásico", "3x", "400 v", "400v")) d.Width = 3 * DeviceLibrary.Module;
        if (kind == "contactor" && f.RatedA > 38) { d.Width = 55; d.Height = 120; d.Depth = 115; d.HeatW = 8; }
        if (kind == "magnetotermico" && f.RatedA > 63) { d.Width = 105; d.Height = 140; d.Depth = 90; d.OnRail = false; d.HeatW = 12; }

        // Bornes leídos de las listas.
        if (f.Terminals.Count > 0)
        {
            var labels = f.Terminals.OrderBy(t => t, TagComparer.Instance).ToList();
            if (kind == "regleta") DeviceLibrary.SetStripTerminals(d, labels);
            else if (d.Location != DeviceLocation.Field)
            {
                d.Terminals.Clear();
                foreach (var l in labels) d.Terminals.Add(new DeviceTerminal { Label = l, Side = SideFor(l) });
                DeviceLibrary.SpreadSlots(d);
            }
            else
            {
                d.Terminals.Clear();
                foreach (var l in labels) d.Terminals.Add(new DeviceTerminal { Label = l });
            }
        }
        return d;
    }

    /// <summary>Entradas arriba (impares, A1, L1…) y salidas abajo (pares, A2, U/V/W…), como en los aparatos reales.</summary>
    internal static TerminalSide SideFor(string label)
    {
        var l = label.ToUpperInvariant();
        if (l is "A2" or "-" or "M" or "0V" or "N" or "PE" or "COM" or "U" or "V" or "W" || l.StartsWith("T") && l.Length == 2
            || l is "U1" or "V1" or "W1" or "U2" or "V2" or "W2") return TerminalSide.Bottom;
        if (l is "A1" or "+" or "L+" or "+24V" or "24V" or "L" or "L1" or "L2" or "L3") return TerminalSide.Top;
        var digits = new string(l.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (digits.Length > 0 && int.TryParse(digits, out var n)) return n % 2 == 1 ? TerminalSide.Top : TerminalSide.Bottom;
        return TerminalSide.Top;
    }

    private static List<PanelNet> BuildNets(List<Link> links, PanelDocument doc)
    {
        // Unión de conjuntos sobre «Tag:Borne».
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Find(string k)
        {
            if (!parent.TryGetValue(k, out var p)) { parent[k] = k; return k; }
            if (p.Equals(k, StringComparison.OrdinalIgnoreCase)) return k;
            return parent[k] = Find(p);
        }
        foreach (var l in links)
            for (var i = 1; i < l.Ends.Count; i++)
            {
                var a = Find(l.Ends[0].ToString());
                var b = Find(l.Ends[i].ToString());
                if (!a.Equals(b, StringComparison.OrdinalIgnoreCase)) parent[b] = a;
            }

        var groups = new Dictionary<string, (PanelNet Net, List<Link> Links)>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in links)
        {
            var root = Find(l.Ends[0].ToString());
            if (!groups.TryGetValue(root, out var g)) groups[root] = g = (new PanelNet(), []);
            g.Links.Add(l);
            foreach (var e in l.Ends)
                if (!g.Net.Ends.Contains(e)) g.Net.Ends.Add(e);
        }

        var nets = new List<PanelNet>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var auto = 0;
        foreach (var (net, ls) in groups.Values)
        {
            var color = ls.Select(l => l.Color).FirstOrDefault(c => c is not null);
            var section = ls.Select(l => l.Section).DefaultIfEmpty(0).Max();
            var number = ls.Select(l => l.Number).FirstOrDefault(n => n is not null);
            Classify(net, doc, color);
            if (section > 0) net.Section = section;
            if (number is null || !used.Add(number))
            {
                do number = (++auto).ToString(Es); while (used.Contains(number));
                used.Add(number);
            }
            net.Number = number;
            nets.Add(net);
        }
        return nets.OrderBy(n => n.Number, TagComparer.Instance).ToList();
    }

    private static void Classify(PanelNet net, PanelDocument doc, string? color)
    {
        var labels = net.Ends.Select(e => e.Terminal.ToUpperInvariant()).ToList();
        var devices = net.Ends.Select(e => doc.FindTag(e.Tag)).Where(d => d is not null).Select(d => d!).ToList();
        bool pe = color == "Verde-amarillo" || labels.Contains("PE");
        bool neutral = color == "Azul claro" || labels.Contains("N");
        bool dc = color == "Azul oscuro" || labels.Any(l => l is "+" or "-" or "L+" or "+24V" or "24V" or "0V");
        bool power = color is "Negro" or "Marrón" or "Gris"
                     || devices.Any(d => d.Kind == "motor")
                     || net.Ends.Any(e => doc.FindTag(e.Tag) is { Kind: "contactor" or "magnetotermico" or "guardamotor" or "rele_termico" or "variador" or "diferencial" or "fusible" }
                                          && e.Terminal.ToUpperInvariant() is "1" or "2" or "3" or "4" or "5" or "6" or "L1" or "L2" or "L3" or "T1" or "T2" or "T3" or "U" or "V" or "W");
        // El color de la lista de cables manda: es lo que el proyectista decidió.
        WireKind? byColor = color switch
        {
            "Verde-amarillo" => WireKind.Tierra,
            "Azul claro" => WireKind.Neutro,
            "Azul oscuro" => WireKind.MandoDc,
            "Rojo" or "Naranja" or "Blanco" or "Violeta" => WireKind.Mando,
            "Negro" or "Marrón" or "Gris" => WireKind.Potencia,
            _ => null,
        };
        net.Kind = byColor ?? (pe ? WireKind.Tierra : neutral ? WireKind.Neutro : dc ? WireKind.MandoDc : power ? WireKind.Potencia : WireKind.Mando);
        var amps = devices.Select(d => d.RatedA).DefaultIfEmpty(0).Max();
        net.Section = net.Kind switch
        {
            WireKind.Potencia or WireKind.Tierra => SchematicImport.SectionFor(amps),
            WireKind.Neutro => power ? SchematicImport.SectionFor(amps) : 1,
            WireKind.MandoDc => 0.75,
            _ => 1,
        };
    }

    /// <summary>
    /// Cables hacia el campo: cada aparato de campo (motor, captador…) lleva un cable con un conductor por potencial
    /// que le llega. Si en el plano no pasa por una regleta, se le asigna un borne en X1.
    /// </summary>
    private static void AddFieldCables(PanelDocument doc, List<PanelNet> nets)
    {
        var x1 = doc.FindTag(SchematicImport.FieldStrip);
        var added = new List<string>();
        var w = 0;
        foreach (var field in doc.InField.OrderBy(d => d.Tag, TagComparer.Instance).ToList())
        {
            var mine = nets.Where(n => n.Ends.Any(e => e.Tag.Equals(field.Tag, StringComparison.OrdinalIgnoreCase))).ToList();
            var cable = new FieldCable
            {
                Tag = $"W{++w}",
                Target = field.Tag,
                Description = $"Cable a {field.Tag} ({field.Description})",
            };
            foreach (var n in mine)
            {
                var strip = n.Ends.FirstOrDefault(e => doc.FindTag(e.Tag) is { Kind: "regleta" });
                if (strip is null)
                {
                    if (x1 is null)
                    {
                        x1 = DeviceLibrary.Create("regleta", SchematicImport.FieldStrip);
                        x1.Terminals.Clear();
                        doc.Devices.Add(x1);
                    }
                    var label = (added.Count + 1).ToString(CultureInfo.InvariantCulture);
                    while (x1.Terminals.Any(t => t.Label == label)) label = (int.Parse(label, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                    added.Add(label);
                    DeviceLibrary.SetStripTerminals(x1, x1.Terminals.Select(t => t.Label).Distinct().Append(label).ToList());
                    strip = new WireEnd(x1.Tag, label);
                    n.Ends.Add(strip);
                }
                cable.Terminals.Add(strip.ToString());
                if (n.Kind == WireKind.Tierra) cable.WithPe = true;
                else cable.Cores++;
                cable.Section = Math.Max(cable.Section, n.Section);
            }
            if (cable.Cores == 0 && !cable.WithPe)
            {
                cable.Cores = field.Kind == "motor" ? 3 : 2;
                cable.WithPe = field.Kind == "motor";
                cable.Section = field.Kind == "motor" ? SchematicImport.SectionFor(field.RatedA) : 1;
            }
            doc.Cables.Add(cable);
        }
    }

    // ------------------------------------------------------------------ utilidades

    private static bool ContainsAny(string text, params string[] words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static string PagesText(List<int> pages) =>
        pages.Count == 1 ? $"la página {pages[0]}" : pages.Count <= 6 ? $"las páginas {string.Join(", ", pages)}" : $"{pages.Count} páginas (de la {pages[0]} a la {pages[^1]})";

    private static string N(double v) => v.ToString("0.##", Es);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Es) + s[1..];
}
