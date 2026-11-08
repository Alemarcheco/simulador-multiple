using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Simulador.Help;

/// <summary>Un tema de la ayuda: una sección <c>##</c> de la guía de una pestaña (o la introducción de la guía).</summary>
public sealed class HelpTopic
{
    internal HelpTopic(string id, HelpGuide guide, string title, string body, IReadOnlyList<string> keywords, IReadOnlyList<string> contexts)
    {
        Id = id;
        Guide = guide;
        Title = title;
        Body = body;
        Keywords = keywords;
        Contexts = contexts;
    }

    /// <summary>Identificador estable: <c>modulo/titulo-sin-acentos</c>.</summary>
    public string Id { get; }
    public HelpGuide Guide { get; }
    public string Title { get; }
    /// <summary>Texto en Markdown sencillo (párrafos, listas, <c>###</c>, **negrita** y `teclas`).</summary>
    public string Body { get; }
    /// <summary>Palabras extra para el buscador (sinónimos, otras formas de preguntarlo).</summary>
    public IReadOnlyList<string> Keywords { get; }
    /// <summary>Nombres de sub-pestañas de la aplicación en las que F1 abre este tema (p. ej. "PCB").</summary>
    public IReadOnlyList<string> Contexts { get; }
    /// <summary>El tema es una pregunta frecuente (su título empieza por «¿»).</summary>
    public bool IsQuestion => Title.StartsWith('¿');
    /// <summary>El tema es la introducción de la guía (el texto bajo el título <c>#</c>).</summary>
    public bool IsOverview => ReferenceEquals(Guide.Overview, this);

    internal HelpLibrary.TermIndex Index { get; set; } = HelpLibrary.TermIndex.Empty;

    public override string ToString() => Title;
}

/// <summary>La guía de una pestaña (o la guía general): un archivo <c>Temas/*.md</c>.</summary>
public sealed class HelpGuide
{
    internal HelpGuide(string moduleId, string title, int order)
    {
        ModuleId = moduleId;
        Title = title;
        Order = order;
    }

    /// <summary>El <c>IModule.Id</c> de la pestaña, o "general".</summary>
    public string ModuleId { get; }
    public string Title { get; }
    public int Order { get; }
    public HelpTopic Overview { get; internal set; } = null!;
    /// <summary>Todos los temas, empezando por la introducción, en el orden del archivo.</summary>
    public IReadOnlyList<HelpTopic> Topics { get; internal set; } = [];

    public override string ToString() => Title;
}

/// <summary>Un resultado del buscador.</summary>
public sealed record HelpSearchResult(HelpTopic Topic, double Score, string Snippet);

/// <summary>
/// Las guías de ayuda y su buscador. No depende de Avalonia, para poder probarlo sin ventana.
/// </summary>
/// <remarks>
/// Formato de cada archivo <c>Temas/*.md</c>:
/// <code>
/// &lt;!-- modulo: circuitos --&gt;      (el Id de la pestaña; "general" para la guía general)
/// &lt;!-- orden: 10 --&gt;
/// # Circuitos y PCB                   (título de la guía; el texto que sigue es la introducción)
/// ## Dibujar un esquema               (cada ## es un tema; si empieza por ¿ es una pregunta frecuente)
/// &lt;!-- contexto: Esquemático --&gt;   (opcional: F1 en esa sub-pestaña abre este tema)
/// &lt;!-- palabras: cable, unir --&gt;   (opcional: sinónimos para el buscador)
/// </code>
/// </remarks>
public sealed class HelpLibrary
{
    public const string GeneralId = "general";

    private static readonly Lazy<HelpLibrary> BuiltIn = new(() => FromResources(typeof(HelpLibrary).Assembly));

    /// <summary>Las guías incluidas en la aplicación.</summary>
    public static HelpLibrary Default => BuiltIn.Value;

    public IReadOnlyList<HelpGuide> Guides { get; }
    public IEnumerable<HelpTopic> AllTopics => Guides.SelectMany(g => g.Topics);

    public HelpLibrary(IEnumerable<HelpGuide> guides)
    {
        Guides = guides.OrderBy(g => g.Order).ThenBy(g => g.Title, StringComparer.CurrentCulture).ToList();
    }

    public static HelpLibrary FromResources(Assembly assembly)
    {
        var guides = new List<HelpGuide>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            guides.Add(Parse(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(name.Split('.')[^2])));
        }
        return new HelpLibrary(guides);
    }

    public HelpGuide? FindGuide(string? moduleId) =>
        Guides.FirstOrDefault(g => string.Equals(g.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

    public HelpTopic? FindTopic(string id) => AllTopics.FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// El tema que debe abrir F1: el de la sub-pestaña visible más concreta (<paramref name="contexts"/>, de la más
    /// interna a la más externa) si alguno la declara, si no la introducción de la pestaña, y si no la guía general.
    /// </summary>
    public HelpTopic ForContext(string? moduleId, IEnumerable<string>? contexts = null)
    {
        var guide = FindGuide(moduleId) ?? FindGuide(GeneralId) ?? Guides[0];
        foreach (var context in contexts ?? [])
        {
            var key = ContextKey(context);
            if (key.Length == 0) continue;
            var match = guide.Topics.FirstOrDefault(t => t.Contexts.Any(c => ContextKey(c) == key));
            if (match is not null) return match;
        }
        return guide.Overview;
    }

    /// <summary>
    /// Busca temas por palabras o por una pregunta escrita con naturalidad («¿cómo simulo un circuito?»).
    /// No distingue acentos ni mayúsculas, ignora palabras vacías y reconoce variantes de la misma palabra
    /// (simular, simulo, simulación). Los temas de <paramref name="currentModuleId"/> salen un poco antes.
    /// </summary>
    public IReadOnlyList<HelpSearchResult> Search(string? query, string? currentModuleId = null, int max = 30)
    {
        var terms = QueryTerms(query);
        if (terms.Count == 0) return [];

        var results = new List<(HelpSearchResult Result, int Matched)>();
        foreach (var topic in AllTopics)
        {
            int matched = 0;
            double score = 0;
            foreach (var term in terms)
            {
                double s = topic.Index.Score(term);
                if (s <= 0) continue;
                matched++;
                score += s;
            }
            if (matched == 0) continue;
            // Lo que más pesa es cuántas palabras de la pregunta aparecen en el tema.
            double total = matched * 100.0 / terms.Count + score;
            if (string.Equals(topic.Guide.ModuleId, currentModuleId, StringComparison.OrdinalIgnoreCase)) total += 5;
            if (topic.IsQuestion) total += 1;
            results.Add((new HelpSearchResult(topic, total, Snippet(topic, terms)), matched));
        }
        if (results.Count == 0) return [];
        // Fuera la cola de temas que solo comparten una palabra suelta con la pregunta: se quedan los que tienen
        // todas las palabras que tiene el mejor (o todas menos una, en preguntas de tres palabras o más).
        int best = results.Max(r => r.Matched);
        int needed = terms.Count >= 3 ? Math.Max(1, best - 1) : best;
        return results.Where(r => r.Matched >= needed).Select(r => r.Result)
            .OrderByDescending(r => r.Score).ThenBy(r => r.Topic.Title, StringComparer.CurrentCulture).Take(max).ToList();
    }

    // ---------------------------------------------------------------- lectura de los .md

    private static readonly Regex Meta = new(@"^<!--\s*([a-záéíóúñ]+)\s*:\s*(.*?)\s*-->\s*$", RegexOptions.IgnoreCase);

    internal static HelpGuide Parse(string markdown, string fallbackId)
    {
        string moduleId = fallbackId;
        int order = 100;
        string title = fallbackId;
        var sections = new List<(string Title, List<string> Lines, List<string> Keywords, List<string> Contexts)>();
        var current = (Title: "", Lines: new List<string>(), Keywords: new List<string>(), Contexts: new List<string>());

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var meta = Meta.Match(line.Trim());
            if (meta.Success)
            {
                var key = Normalize(meta.Groups[1].Value).Trim();
                var value = meta.Groups[2].Value;
                switch (key)
                {
                    case "modulo": moduleId = value.Trim(); break;
                    case "orden": order = int.TryParse(value, CultureInfo.InvariantCulture, out var o) ? o : order; break;
                    case "palabras": current.Keywords.AddRange(SplitList(value)); break;
                    case "contexto": current.Contexts.AddRange(SplitList(value)); break;
                }
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = line[2..].Trim();
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                sections.Add(current);
                current = (line[3..].Trim(), new List<string>(), new List<string>(), new List<string>());
                continue;
            }
            current.Lines.Add(line);
        }
        sections.Add(current);

        var guide = new HelpGuide(moduleId, title, order);
        var topics = new List<HelpTopic>();
        var usedIds = new HashSet<string>();
        foreach (var s in sections)
        {
            bool overview = s.Title.Length == 0;
            var topicTitle = overview ? title : s.Title;
            var slug = overview ? "" : "/" + Slug(s.Title);
            var id = moduleId + slug;
            for (int n = 2; !usedIds.Add(id); n++) id = $"{moduleId}{slug}-{n}";
            var topic = new HelpTopic(id, guide, topicTitle, string.Join('\n', s.Lines).Trim('\n'), s.Keywords, s.Contexts);
            topic.Index = TermIndex.Build(topic);
            topics.Add(topic);
            if (overview) guide.Overview = topic;
        }
        guide.Topics = topics;
        return guide;
    }

    /// <summary>Solo las palabras, para que «⚡ Esquemático» y «esquematico» coincidan.</summary>
    private static string ContextKey(string text) => string.Join(' ', Words(text));

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Slug(string text) =>
        Regex.Replace(Normalize(text), "[^a-z0-9]+", "-").Trim('-');

    // ---------------------------------------------------------------- buscador

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "al", "algo", "algun", "alguna", "alguno", "como", "con", "cual", "cuales", "cuando", "de", "del",
        "donde", "el", "ella", "en", "es", "esta", "este", "esto", "hacer", "hago", "hay", "la", "las", "le", "lo",
        "los", "me", "mi", "mis", "muy", "no", "o", "otra", "otro", "para", "pero", "por", "porque", "puedo", "puede",
        "que", "quiero", "se", "si", "sin", "sobre", "son", "su", "sus", "te", "tengo", "tiene", "un", "una", "uno",
        "unos", "unas", "y", "ya", "yo", "sirve",
    };

    /// <summary>Minúsculas sin acentos (la ñ se conserva como n).</summary>
    internal static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static IEnumerable<string> Words(string text)
    {
        foreach (Match m in Regex.Matches(Normalize(text), "[a-z0-9]+"))
            yield return m.Value;
    }

    /// <summary>Raíz aproximada: las 5 primeras letras, sin la «s» del plural (simular, simulo, simulación → simul).</summary>
    internal static string Stem(string word)
    {
        if (word.Length > 3 && word.EndsWith('s')) word = word[..^1];
        return word.Length > 5 ? word[..5] : word;
    }

    internal sealed record QueryTerm(string Word, string Stem);

    internal static List<QueryTerm> QueryTerms(string? query)
    {
        var terms = new List<QueryTerm>();
        if (string.IsNullOrWhiteSpace(query)) return terms;
        var all = Words(query).ToList();
        foreach (var w in all)
        {
            // Las palabras vacías se ignoran, salvo que la búsqueda sea solo eso.
            if (all.Count > 1 && (StopWords.Contains(w) || w.Length < 2)) continue;
            if (terms.All(t => t.Word != w)) terms.Add(new QueryTerm(w, Stem(w)));
        }
        return terms;
    }

    internal static bool Matches(QueryTerm term, string docStem) =>
        docStem == term.Stem
        // Palabra a medio escribir («osci» → osciloscopio).
        || (term.Word.Length >= 3 && term.Stem.Length < 5 && docStem.StartsWith(term.Stem, StringComparison.Ordinal));

    private static string Snippet(HelpTopic topic, List<QueryTerm> terms)
    {
        var lines = topic.Body.Split('\n')
            .Select(PlainText)
            .Where(l => l.Length > 0)
            .ToList();
        var hit = lines.FirstOrDefault(l => Words(l).Any(w => terms.Any(t => Matches(t, Stem(w))))) ?? lines.FirstOrDefault() ?? "";
        return hit.Length > 160 ? hit[..157].TrimEnd() + "…" : hit;
    }

    /// <summary>Quita las marcas de Markdown de una línea.</summary>
    internal static string PlainText(string line)
    {
        var t = line.Trim();
        t = Regex.Replace(t, @"^(#{1,6}\s+|[-*]\s+|\d+\.\s+|>\s*)", "");
        t = t.Replace("**", "").Replace("`", "");
        return t.Trim();
    }

    /// <summary>Palabras de un tema, por campo, con su peso.</summary>
    internal sealed class TermIndex
    {
        public static readonly TermIndex Empty = new([], [], [], [], new Dictionary<string, int>());

        private readonly HashSet<string> _title, _keywords, _contexts, _guide;
        private readonly Dictionary<string, int> _body;

        private TermIndex(HashSet<string> title, HashSet<string> keywords, HashSet<string> contexts, HashSet<string> guide, Dictionary<string, int> body)
        {
            _title = title;
            _keywords = keywords;
            _contexts = contexts;
            _guide = guide;
            _body = body;
        }

        public static TermIndex Build(HelpTopic t)
        {
            static HashSet<string> Set(IEnumerable<string> texts) => texts.SelectMany(Words).Select(Stem).ToHashSet();
            var body = new Dictionary<string, int>();
            foreach (var w in Words(t.Body))
            {
                var s = Stem(w);
                body[s] = body.GetValueOrDefault(s) + 1;
            }
            return new TermIndex(Set([t.Title]), Set(t.Keywords), Set(t.Contexts), Set([t.Guide.Title, t.Guide.ModuleId]), body);
        }

        public double Score(QueryTerm term)
        {
            double s = 0;
            if (_title.Any(d => Matches(term, d))) s += 6;
            if (_keywords.Any(d => Matches(term, d))) s += 4;
            if (_contexts.Any(d => Matches(term, d))) s += 3;
            if (_guide.Any(d => Matches(term, d))) s += 2;
            int hits = _body.Where(kv => Matches(term, kv.Key)).Sum(kv => kv.Value);
            if (hits > 0) s += 1 + Math.Min(hits, 4) * 0.5;
            return s;
        }
    }
}
