using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SharpCompress.Archives;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Import;

/// <summary>
/// Lee librerías de fabricante en paquete .edz: el formato de EPLAN Data Portal (archivo 7z) o los paquetes que
/// genera Simulador multiple, que usan la misma estructura. Dentro busca cualquier XML con piezas descritas con las
/// propiedades de EPLAN (<c>P_ARTICLE_PARTNR</c>, <c>P_ARTICLE_MANUFACTURER</c>, <c>P_ARTICLE_TYPENR</c>,
/// <c>P_ARTICLE_DESCR1</c>, <c>P_ARTICLE_WIDTH/HEIGHT/DEPTH</c>…), ya vengan como atributos, como elementos hijos o
/// como pares nombre/valor, y el modelo 3D que las acompañe (.obj con su .mtl, o .stl).
/// Las macros gráficas de EPLAN (.ema) son binarias y propietarias: no se leen.
/// </summary>
public static class EdzImporter
{
    /// <summary>Tamaño máximo que se descomprime por archivo interno (protege de paquetes dañados).</summary>
    private const long MaxEntry = 64L * 1024 * 1024;

    public sealed record Result(List<CatalogItem> Items, List<string> Notes);

    public static Result Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, Path.GetFileName(path));
    }

    public static Result Read(Stream stream, string fileName)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (var archive = ArchiveFactory.OpenArchive(stream))
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory || e.Key is null || e.Size > MaxEntry) continue;
                using var s = e.OpenEntryStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                files[Normalize(e.Key)] = ms.ToArray();
            }

        var items = new List<CatalogItem>();
        var notes = new List<string>();
        foreach (var (name, data) in files.Where(f => f.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument xml;
            try { xml = XDocument.Parse(Decode(data)); }
            catch (Exception ex) { notes.Add($"{name}: XML no válido ({ex.Message})."); continue; }
            foreach (var part in Parts(xml))
            {
                var item = ToItem(part, files, fileName, notes);
                if (item is not null) items.Add(item);
            }
        }
        if (items.Count == 0)
            notes.Add(files.Keys.Any(k => k.EndsWith(".ema", StringComparison.OrdinalIgnoreCase))
                ? $"{fileName}: solo trae macros de EPLAN (.ema), que no se pueden leer fuera de EPLAN."
                : $"{fileName}: no se encontraron piezas con datos P_ARTICLE_*.");
        return new Result(items, notes);
    }

    private static string Normalize(string key) => key.Replace('\\', '/').TrimStart('/');

    private static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        return Encoding.UTF8.GetString(data).TrimStart('﻿');
    }

    /// <summary>Elementos que describen una pieza: los que tienen P_ARTICLE_PARTNR o P_ARTICLE_TYPENR de algún modo.</summary>
    private static IEnumerable<(XElement Element, Dictionary<string, string> Props)> Parts(XDocument xml)
    {
        foreach (var el in xml.Descendants())
        {
            var props = Properties(el);
            if (props.ContainsKey("P_ARTICLE_PARTNR") || props.ContainsKey("P_ARTICLE_TYPENR")) yield return (el, props);
        }
    }

    private static Dictionary<string, string> Properties(XElement el)
    {
        var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in el.Attributes())
            if (a.Name.LocalName.StartsWith("P_ARTICLE_", StringComparison.OrdinalIgnoreCase)) p[a.Name.LocalName] = a.Value;
        foreach (var c in el.Elements())
        {
            var n = c.Name.LocalName;
            if (n.StartsWith("P_ARTICLE_", StringComparison.OrdinalIgnoreCase) && !c.HasElements) p[n] = c.Value.Trim();
            // <property name="P_ARTICLE_X" value="…"/> o <property id="P_ARTICLE_X">…</property>
            var key = (string?)c.Attribute("name") ?? (string?)c.Attribute("id") ?? (string?)c.Attribute("key");
            if (key is not null && key.StartsWith("P_ARTICLE_", StringComparison.OrdinalIgnoreCase))
                p[key] = ((string?)c.Attribute("value") ?? c.Value).Trim();
        }
        return p;
    }

    private static CatalogItem? ToItem((XElement Element, Dictionary<string, string> Props) part, Dictionary<string, byte[]> files,
        string fileName, List<string> notes)
    {
        var (el, p) = part;
        var sim = el.Element("simulador");
        var brand = Get(p, "P_ARTICLE_MANUFACTURER");
        var partNr = Get(p, "P_ARTICLE_PARTNR");
        var reference = Get(p, "P_ARTICLE_TYPENR");
        if (reference.Length == 0) reference = Get(p, "P_ARTICLE_ORDERNR");
        if (reference.Length == 0) reference = partNr.Contains('.') ? partNr[(partNr.IndexOf('.') + 1)..] : partNr;
        if (brand.Length == 0) brand = partNr.Contains('.') ? partNr[..partNr.IndexOf('.')] : "Sin marca";
        if (reference.Length == 0) return null;
        var description = Get(p, "P_ARTICLE_DESCR1");
        if (description.Length == 0) description = Get(p, "P_ARTICLE_NOTE");
        double w = Num(p, "P_ARTICLE_WIDTH"), h = Num(p, "P_ARTICLE_HEIGHT"), d = Num(p, "P_ARTICLE_DEPTH");
        var hasSize = w > 0 && h > 0 && d > 0;

        var kind = (string?)sim?.Attribute("kind") is { } k && DeviceLibrary.Get(k).Kind == k ? k : GuessKind(description + " " + reference);
        var model = DeviceLibrary.Get(kind);
        var location = Enum.TryParse<DeviceLocation>((string?)sim?.Attribute("location"), true, out var loc) ? loc
            : Get(p, "P_ARTICLE_MOUNTINGSITE").Contains("puerta", StringComparison.OrdinalIgnoreCase)
              || Get(p, "P_ARTICLE_MOUNTINGSITE").Contains("door", StringComparison.OrdinalIgnoreCase) ? DeviceLocation.Door : model.Location;
        var onRail = bool.TryParse((string?)sim?.Attribute("onRail"), out var r) ? r
            : Get(p, "P_ARTICLE_MOUNTINGSPACE").Contains("DIN", StringComparison.OrdinalIgnoreCase) || model.OnRail;
        if (!hasSize)
        {
            notes.Add($"{brand} {reference}: el paquete no trae medidas; se usan las típicas de «{model.Name}».");
            (w, h, d) = (model.Width, model.Height, model.Depth);
        }
        var heat = Num(p, "P_ARTICLE_POWER_DISSIPATION") is var q && q > 0 ? q : model.HeatW;
        // Medidas de la ficha: las de EPLAN Data Portal vienen del fabricante; en los paquetes del simulador lo dice «verified».
        var verified = hasSize && (!bool.TryParse((string?)sim?.Attribute("verified"), out var v) || v);
        var source = (string?)sim?.Attribute("source") is { Length: > 0 } s ? $"{s} (paquete {fileName})" : $"Paquete {fileName}";
        var keys = ((string?)sim?.Attribute("keys") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(partNr).Where(x => x.Length > 0 && !string.Equals(x, reference, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();

        var mesh = FindMesh((string?)sim?.Attribute("model3d"), reference, files, notes)
                   ?? FindStep(sim, reference, files, notes, location == DeviceLocation.Door);
        if (mesh is not null && !hasSize)
            (w, h, d) = StepImporter.Size(mesh, location == DeviceLocation.Door);
        return new CatalogItem(brand, reference, kind, description.Length > 0 ? description : model.Name, w, h, d, heat, location, onRail,
            verified, source, keys) { Mesh = mesh };
    }

    private static DeviceMesh? FindMesh(string? hint, string reference, Dictionary<string, byte[]> files, List<string> notes)
    {
        var candidates = new List<string>();
        if (hint is { Length: > 0 }) candidates.Add(Normalize(hint));
        var models = files.Keys.Where(k => k.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".stl", StringComparison.OrdinalIgnoreCase)).ToList();
        var refKey = new string(reference.Where(char.IsLetterOrDigit).ToArray());
        candidates.AddRange(models.Where(k => new string(Path.GetFileNameWithoutExtension(k).Where(char.IsLetterOrDigit).ToArray())
            .Contains(refKey, StringComparison.OrdinalIgnoreCase)));
        if (models.Count == 1) candidates.Add(models[0]);
        foreach (var c in candidates.Distinct())
        {
            if (!files.TryGetValue(c, out var data)) continue;
            try
            {
                DeviceMesh mesh;
                if (c.EndsWith(".stl", StringComparison.OrdinalIgnoreCase)) mesh = DeviceMesh.FromStl(data);
                else
                {
                    var mtlName = Path.ChangeExtension(c, ".mtl");
                    var mtl = files.TryGetValue(mtlName, out var m) ? Encoding.UTF8.GetString(m) : null;
                    mesh = DeviceMesh.FromObj(Encoding.UTF8.GetString(data), mtl);
                }
                if (mesh.Triangles.Count > 0) return mesh;
            }
            catch (Exception ex) { notes.Add($"{c}: no se pudo leer el modelo 3D ({ex.Message})."); }
        }
        return null;
    }

    /// <summary>Paquetes que solo traen el STEP del fabricante: se lee y se gira (con front/up si el paquete los da).</summary>
    private static DeviceMesh? FindStep(XElement? sim, string reference, Dictionary<string, byte[]> files, List<string> notes, bool door)
    {
        var steps = files.Keys.Where(StepImporter.IsStep).ToList();
        if (steps.Count == 0) return null;
        var hint = (string?)sim?.Attribute("step") is { Length: > 0 } h ? Normalize(h) : null;
        var refKey = new string(reference.Where(char.IsLetterOrDigit).ToArray());
        var pick = hint is not null && files.ContainsKey(hint) ? hint
            : steps.FirstOrDefault(k => new string(Path.GetFileNameWithoutExtension(k).Where(char.IsLetterOrDigit).ToArray())
                .Contains(refKey, StringComparison.OrdinalIgnoreCase)) ?? (steps.Count == 1 ? steps[0] : null);
        if (pick is null) return null;
        try
        {
            var c = StepImporter.Prepare(files[pick], Path.GetFileName(pick));
            if ((string?)sim?.Attribute("front") is { } f && StepImporter.Axes.Contains(f)) c.Front = f;
            else c.Front = StepImporter.AutoOrientation(c.Raw, door).Front;
            if ((string?)sim?.Attribute("up") is { } u && StepImporter.Axes.Contains(u)) c.Up = u;
            var mesh = StepImporter.Orient(c.Raw, c.Front, c.Up, door);
            return mesh.Triangles.Count > 0 ? mesh : null;
        }
        catch (Exception ex) { notes.Add($"{pick}: no se pudo leer el STEP ({ex.Message})."); return null; }
    }

    private static string Get(Dictionary<string, string> p, string key)
    {
        if (!p.TryGetValue(key, out var v)) return "";
        // EPLAN guarda los textos multilingües como «es_ES@texto;en_US@text;».
        if (v.Contains('@'))
        {
            var parts = v.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('@', 2)).Where(x => x.Length == 2).ToList();
            var pick = parts.FirstOrDefault(x => x[0].StartsWith("es", StringComparison.OrdinalIgnoreCase))
                       ?? parts.FirstOrDefault(x => x[0].StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? parts.FirstOrDefault();
            if (pick is not null) return pick[1].Trim();
        }
        return v.Trim();
    }

    private static double Num(Dictionary<string, string> p, string key)
    {
        var s = Get(p, key).Replace(',', '.');
        var digits = new string(s.TakeWhile(ch => char.IsDigit(ch) || ch is '.' or '-').ToArray());
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Tipo genérico a partir de la descripción (cuando el paquete no lo dice).</summary>
    public static string GuessKind(string text)
    {
        var t = text.ToLowerInvariant();
        (string Kind, string[] Words)[] rules =
        [
            ("seta", ["emergencia", "emergency", "e-stop", "mushroom"]),
            ("piloto", ["piloto", "pilot", "indicator light", "señalización"]),
            ("selector", ["selector"]),
            ("pulsador", ["pulsador", "push button", "pushbutton"]),
            ("diferencial", ["diferencial", "rcd", "rccb"]),
            ("magnetotermico", ["magnetotérmico", "interruptor automático", "circuit breaker", "mcb", "breaker"]),
            ("guardamotor", ["guardamotor", "motor protect"]),
            ("contactor", ["contactor"]),
            ("rele_termico", ["térmico", "overload"]),
            ("temporizador", ["temporiz", "timer", "timing"]),
            ("rele", ["relé", "rele", "relay"]),
            ("fuente", ["fuente", "power supply", "alimentación"]),
            ("variador", ["variador", "drive", "inverter"]),
            ("fusible", ["fusible", "fuse"]),
            ("regleta", ["borne", "terminal block"]),
            ("barra_pe", ["tierra", "ground", "busbar"]),
            ("instrumento", ["indicador", "medidor", "meter"]),
            ("controlador", ["hmi", "pantalla", "display", "panel pc", "monitor"]),
            ("plc", ["plc", "controlador", "controller", "e/s", "i/o", "módulo", "module"]),
        ];
        foreach (var (kind, words) in rules)
            if (words.Any(w => t.Contains(w))) return kind;
        return "aparato";
    }
}
