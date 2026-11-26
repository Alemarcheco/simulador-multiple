using Simulador.Tableros.Model;

namespace Simulador.Tableros.Import;

/// <summary>
/// Asocia los aparatos de un plano con las librerías de fabricante: por la referencia de la lista de materiales o,
/// si el plano no la trae, por los textos que hay junto a la designación en los dibujos («1606-XLS» al lado de F1,
/// «PROTECH TPS» junto a TPS…). También añade los controladores que el plano nombra sin designación (Woodward 505D).
/// </summary>
public static class CatalogMatcher
{
    /// <summary>Distancia máxima (puntos PDF) entre el texto del fabricante y la designación.</summary>
    public const double Near = 110;

    public sealed record Match(string Tag, CatalogItem Item, string How);

    public static List<Match> Apply(PlanText plan, PanelDocument doc)
    {
        var matches = new List<Match>();

        // 1. Referencia exacta (lista de materiales o escrita por el usuario).
        foreach (var d in doc.Devices)
            if (ManufacturerCatalog.Find(d.Reference) is { } item && Compatible(d, item))
            {
                ManufacturerCatalog.Apply(d, item);
                matches.Add(new Match(d.Tag, item, "referencia"));
            }

        // 2. Texto del fabricante junto a la designación: se cuentan votos en todas las páginas.
        var keys = ManufacturerCatalog.All
            .SelectMany(i => (i.Keys ?? []).Append(i.Reference).Select(k => (Key: Norm(k), Item: i)))
            .Where(k => k.Key.Length >= 3).ToList();
        var tags = doc.Devices.Where(d => (d.Location != DeviceLocation.Field || BaseKind(d.Tag) is "fusible" or "fuente") && d.Reference.Length == 0)
            .ToDictionary(d => Norm(d.Tag), d => d);
        var votes = new Dictionary<(PanelDevice, CatalogItem), int>();
        var mentioned = new Dictionary<string, (CatalogItem Item, int Count)>();
        foreach (var page in plan.Pages)
        {
            var words = page.Words;
            foreach (var w in words)
            {
                var n = Norm(w.Text);
                var hit = keys.FirstOrDefault(k => n == k.Key);
                if (hit.Item is null) continue;
                var m = mentioned.GetValueOrDefault(n);
                mentioned[n] = (hit.Item, m.Count + 1);
                PanelDevice? best = null;
                var bestDist = Near;
                foreach (var o in words)
                {
                    if (o == w || !tags.TryGetValue(Norm(o.Text), out var d)) continue;
                    var dist = Math.Sqrt(Math.Pow(o.X + o.Width / 2 - (w.X + w.Width / 2), 2) + Math.Pow(o.CenterY - w.CenterY, 2));
                    if (dist < bestDist) { bestDist = dist; best = d; }
                }
                if (best is not null && Compatible(best, hit.Item)) votes[(best, hit.Item)] = votes.GetValueOrDefault((best, hit.Item)) + 1;
            }
        }
        var chosen = new List<(PanelDevice Device, CatalogItem Item)>();
        foreach (var g in votes.GroupBy(v => v.Key.Item1))
        {
            // El fabricante más votado de los que encajan con la designación.
            var top = g.OrderByDescending(v => v.Value).FirstOrDefault(v => Accepts(v.Key.Item1, v.Key.Item2, v.Value));
            if (top.Key.Item1 is null || top.Key.Item1.Reference.Length > 0) continue;
            chosen.Add(top.Key);
        }
        foreach (var (d, item) in chosen)
        {
            ManufacturerCatalog.Apply(d, item);
            matches.Add(new Match(d.Tag, item, "texto junto a la designación"));
        }
        // Módulos de un mismo equipo de puerta (TPS-A, TPS-B, TPS-C): se reparten su frente, uno al lado del otro.
        foreach (var group in chosen.Where(c => c.Item.Location == DeviceLocation.Door && c.Device.Tag.Contains('-'))
                     .GroupBy(c => (Base: BaseTag(c.Device.Tag), c.Item)).Where(g => g.Count() > 1))
        {
            var item = group.Key.Item;
            var n = group.Count();
            foreach (var (mod, _) in group)
            {
                mod.Width = Math.Round(item.Width / n, 1);
                mod.HeatW = Math.Round(item.HeatW / n, 1);
                mod.Description = $"Módulo {mod.Tag[(group.Key.Base.Length + 1)..]} de {item.Brand} {item.Reference}";
            }
            // La designación común (TPS), si está, son los bornes comunes del equipo: se queda en la placa.
            if (doc.FindTag(group.Key.Base) is { Kind: "aparato" } common)
            {
                common.Description = $"Bornes comunes de {item.Brand} {item.Reference}";
                common.Reference = item.FullReference;
            }
        }

        // 3. Controladores nombrados en el plano sin designación propia (p. ej. «CONTROLADOR PRIMARIO 505D»).
        foreach (var (key, (item, count)) in mentioned)
        {
            if (item.Kind != "controlador" || count < 3) continue;
            if (matches.Any(m => m.Item == item && !m.How.StartsWith("nombrado"))) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "\\d")) continue; // solo modelos (505D, 505DR), no la marca
            if (doc.FindTag(key) is not null) continue;
            var d = DeviceLibrary.Create("controlador", key);
            d.Terminals.Clear();
            ManufacturerCatalog.Apply(d, item);
            doc.Devices.Add(d);
            matches.Add(new Match(d.Tag, item, $"nombrado {count} veces en el plano"));
        }
        return matches;
    }

    /// <summary>Solo se cambia el tipo de un aparato genérico, o se corrige uno parecido (F1 «fusible» que es una fuente).</summary>
    private static bool Compatible(PanelDevice d, CatalogItem item) =>
        d.Kind == item.Kind || d.Kind == "aparato" || (d.Kind == "fusible" && item.Kind == "fuente") ||
        (BaseKind(d.Tag) == "fusible" && item.Kind == "fuente");

    /// <summary>Tipo que daría solo el prefijo de la designación (F1 → fusible), sin el texto de alrededor.</summary>
    private static string BaseKind(string tag) =>
        PlanImporter.KindFor(new string(tag.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant(), "");

    private static bool Accepts(PanelDevice d, CatalogItem item, int votes)
    {
        // Un texto suelto cerca no basta para convertir un aparato genérico en otro equipo: hacen falta dos.
        var close = d.Kind == item.Kind || BaseKind(d.Tag) == item.Kind || (item.Kind == "fuente" && (d.Kind == "fusible" || BaseKind(d.Tag) == "fusible"));
        if (!close && votes < 2) return false;
        // Un equipo grande (controlador) solo para una designación que lo nombre (TPS → ProTech-TPS), no para la
        // alarma o la entrada que se dibuja a su lado.
        return item.Kind != "controlador" || Names(item, d.Tag);
    }

    private static string BaseTag(string tag) => tag.Contains('-') ? tag[..tag.LastIndexOf('-')] : tag;

    private static bool Names(CatalogItem item, string tag)
    {
        var t = Norm(BaseTag(tag));
        return t.Length >= 3 && (item.Keys ?? []).Append(item.Reference).Any(k => Norm(k).Contains(t));
    }

    private static string Norm(string s) => new(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
