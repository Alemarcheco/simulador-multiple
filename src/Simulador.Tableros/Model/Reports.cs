using System.Globalization;
using System.Text;

namespace Simulador.Tableros.Model;

/// <summary>Una línea de una tabla (lista de materiales, de cableado o de bornes).</summary>
public sealed record ReportTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>CSV con punto y coma (lo abre Excel en español sin preguntar) y BOM UTF-8.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', Headers.Select(Esc)));
        foreach (var r in Rows) sb.AppendLine(string.Join(';', r.Select(Esc)));
        return sb.ToString();
    }

    /// <summary>Texto con tabuladores, para pegar en una hoja de cálculo.</summary>
    public string ToTabbed()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join('\t', Headers));
        foreach (var r in Rows) sb.AppendLine(string.Join('\t', r));
        return sb.ToString();
    }

    private static string Esc(string s) => s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}

/// <summary>Listas de fabricación: materiales, cableado (de-a), bornes y cables de campo.</summary>
public static class Reports
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string N(double v, string f = "0.##") => v.ToString(f, Es);

    /// <summary>Lista de materiales agrupada: aparatos iguales en una línea, con sus designaciones.</summary>
    public static ReportTable BillOfMaterials(PanelDocument doc)
    {
        var rows = new List<(string Group, double Qty, string Unit, string Desc, string Tags, string Ref)>();
        var e = doc.Enclosure;
        rows.Add(("Envolvente", 1, "ud", $"{Enclosure.KindName(e.Kind)} {N(e.Width)}×{N(e.Height)}×{N(e.Depth)} mm, {e.Ip}", "", ""));
        rows.Add(("Envolvente", 1, "ud", $"Placa de montaje galvanizada {N(e.PlateWidth)}×{N(e.PlateHeight)} mm", "", ""));
        if (e.FanFlow > 0)
            rows.Add(("Envolvente", 1, "ud", $"Ventilador con filtro {N(e.FanFlow)} m³/h y rejilla de salida", "", ""));

        foreach (var g in doc.Devices.Where(d => d.Location != DeviceLocation.Field && d.Kind != "regleta")
                     .GroupBy(d => (d.Description, d.Reference))
                     .OrderBy(g => SchematicImport.Order(g.First().Kind)).ThenBy(g => g.First().Tag, TagComparer.Instance))
        {
            var group = g.First().Location == DeviceLocation.Door ? "Puerta" : "Aparamenta";
            rows.Add((group, g.Count(), "ud", g.Key.Description, string.Join(", ", g.Select(d => d.Tag).OrderBy(t => t, TagComparer.Instance)), g.Key.Reference));
            foreach (var acc in g.SelectMany(d => d.Accessories).GroupBy(a => a))
                rows.Add((group, acc.Count(), "ud", acc.Key, string.Join(", ", g.Select(d => d.Tag)), ""));
        }

        // Regletas: bornes por sección, tapas y topes.
        var strips = doc.Devices.Where(d => d.Kind == "regleta").ToList();
        if (strips.Count > 0)
        {
            var bySection = new Dictionary<double, int>();
            foreach (var s in strips)
                foreach (var label in s.Terminals.Select(t => t.Label).Distinct())
                {
                    var sec = doc.Wires.Where(w => (w.From.Tag == s.Tag && w.From.Terminal == label) || (w.To.Tag == s.Tag && w.To.Terminal == label))
                        .Select(w => w.Section).DefaultIfEmpty(1.5).Max();
                    var field = doc.Cables.Where(c => c.Terminals.Contains($"{s.Tag}:{label}")).Select(c => c.Section).DefaultIfEmpty(0).Max();
                    var size = DeviceLibrary.StripTerminalSection(Math.Max(sec, field));
                    bySection[size] = bySection.GetValueOrDefault(size) + 1;
                }
            foreach (var (sec, count) in bySection.OrderBy(k => k.Key))
                rows.Add(("Bornes", count, "ud", $"Borne de paso de carril {N(sec)} mm²", string.Join(", ", strips.Select(s => s.Tag)), ""));
            rows.Add(("Bornes", strips.Count, "ud", "Tapa final de borne", "", ""));
            rows.Add(("Bornes", 2 * strips.Count, "ud", "Tope final de carril", "", ""));
            rows.Add(("Bornes", strips.Sum(s => s.Terminals.Count / 2), "ud", "Marcador de borne", "", ""));
        }

        // Carril y canaletas (barras de 2 m).
        if (doc.Rails.Count > 0)
            rows.Add(("Montaje", Math.Round(doc.Rails.Sum(r => r.Length) / 1000, 2), "m", "Carril DIN simétrico 35×7,5 mm perforado", "", ""));
        foreach (var g in doc.Ducts.GroupBy(k => (k.Width, k.Depth)))
            rows.Add(("Montaje", Math.Round(g.Sum(k => k.Length) / 1000, 2), "m", $"Canaleta ranurada gris {N(g.Key.Width)}×{N(g.Key.Depth)} mm con tapa", "", ""));

        // Conductores (+10 % de recortes).
        foreach (var g in doc.Wires.GroupBy(w => (w.Section, w.Color)).OrderByDescending(g => g.Key.Section))
            rows.Add(("Cableado", Math.Ceiling(g.Sum(w => w.Length) * 1.1 / 1000), "m", $"Conductor flexible H07V-K {N(g.Key.Section)} mm² {g.Key.Color.ToLowerInvariant()}", "", ""));
        foreach (var g in doc.Wires.GroupBy(w => w.Section).OrderBy(g => g.Key))
            rows.Add(("Cableado", 2 * g.Count(), "ud", $"Puntera aislada para {N(g.Key)} mm²", "", ""));
        if (doc.Wires.Count > 0)
            rows.Add(("Cableado", 2 * doc.Wires.Count, "ud", "Marcador de hilo (número de potencial)", "", ""));
        foreach (var c in doc.Cables)
            rows.Add(("Cables de campo", 1, "ud", $"Manguera {c.Designation} mm² hacia {c.Target} (longitud según obra)", c.Tag, ""));
        if (doc.Cables.Count > 0)
            rows.Add(("Cables de campo", doc.Cables.Count, "ud", "Prensaestopas para la entrada de cables", "", ""));

        var table = rows.Select((r, i) => (IReadOnlyList<string>)[(i + 1).ToString(), r.Group, N(r.Qty), r.Unit, r.Desc, r.Tags, r.Ref]).ToList();
        return new ReportTable("Lista de materiales", ["Pos.", "Grupo", "Cant.", "Ud.", "Descripción", "Designaciones", "Referencia"], table);
    }

    /// <summary>Lista de cableado de-a (lo que necesita el montador o una máquina de corte y marcado).</summary>
    public static ReportTable WireList(PanelDocument doc)
    {
        var rows = doc.Wires
            .OrderBy(w => int.TryParse(w.Number, out var n) ? n : -1).ThenBy(w => w.Number)
            .Select((w, i) => (IReadOnlyList<string>)
            [
                (i + 1).ToString(), w.Number, w.From.ToString(), w.To.ToString(), N(w.Section), w.Color, N(w.Length, "0"), w.Kind.ToString(),
            ]).ToList();
        return new ReportTable("Lista de cableado", ["Nº", "Hilo", "Desde", "Hasta", "Sección (mm²)", "Color", "Longitud (mm)", "Tipo"], rows);
    }

    /// <summary>Plano de bornes: cada borne de regleta, qué hilo le llega por dentro y qué cable sale por fuera.</summary>
    public static ReportTable TerminalPlan(PanelDocument doc)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var s in doc.Devices.Where(d => d.Kind == "regleta").OrderBy(d => d.Tag, TagComparer.Instance))
            foreach (var label in s.Terminals.Select(t => t.Label).Distinct())
            {
                var key = new WireEnd(s.Tag, label);
                var inner = doc.Wires.Where(w => w.From == key || w.To == key).ToList();
                var others = string.Join(", ", inner.Select(w => w.From == key ? w.To.ToString() : w.From.ToString()));
                var number = inner.FirstOrDefault()?.Number ?? "";
                var cable = doc.Cables.FirstOrDefault(c => c.Terminals.Contains(key.ToString()));
                rows.Add([s.Tag, label, number, others, cable?.Tag ?? "", cable?.Target ?? ""]);
            }
        return new ReportTable("Plano de bornes", ["Regleta", "Borne", "Hilo", "Interior (hacia)", "Cable", "Exterior (hacia)"], rows);
    }

    public static ReportTable CableList(PanelDocument doc)
    {
        var rows = doc.Cables.Select(c => (IReadOnlyList<string>)[c.Tag, c.Designation + " mm²", string.Join(", ", c.Terminals), c.Target, c.Description]).ToList();
        return new ReportTable("Cables de campo", ["Cable", "Tipo", "Bornes del tablero", "Destino", "Descripción"], rows);
    }

    /// <summary>Resumen de cifras para la cabecera de las listas.</summary>
    public static string Summary(PanelDocument doc)
    {
        var meters = doc.Wires.Sum(w => w.Length) / 1000;
        var th = PanelChecks.Thermal(doc);
        return $"{doc.OnPlate.Count()} aparatos en placa · {doc.OnDoor.Count()} en puerta · {doc.Wires.Count} hilos ({N(meters, "0.0")} m) · " +
               $"{doc.Devices.Where(d => d.Kind == "regleta").Sum(d => d.Terminals.Count / 2)} bornes · {N(th.PowerW, "0")} W · ≈{N(th.InsideC, "0")} °C dentro";
    }
}
