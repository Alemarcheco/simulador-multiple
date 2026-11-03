using System.Globalization;
using System.Text;
using Simulador.Electrico.Engine.Model;

namespace Simulador.Electrico.Engine.Catalog;

/// <summary>Un aparato real de fabricante: un tipo de la librería con sus parámetros de placa.</summary>
public sealed record ManufacturerPreset(
    string Manufacturer, string Reference, string TypeId, string Description, IReadOnlyDictionary<string, double> Params)
{
    public string Display => $"{Manufacturer} {Reference}";

    /// <summary>Aplica los datos del aparato a un elemento colocado.</summary>
    public void ApplyTo(PlacedElement e)
    {
        foreach (var (k, v) in Params) e.Params[k] = v;
        e.Texts["Referencia"] = Display;
    }
}

/// <summary>
/// Catálogos de fabricante en CSV (separador ; o ,), una fila por referencia:
/// <code>Fabricante;Referencia;Tipo;Descripción;Parámetros</code>
/// donde Tipo es un tipo de la librería (bobina_contactor, guardamotor, motor_3f…) y Parámetros es
/// una lista <c>clave=valor</c> separada por espacios o |, p. ej. <c>In=9 V=230 ac=1</c>.
/// Las líneas que empiezan por # son comentarios. Así se pueden pasar a la librería tablas de catálogo
/// de Schneider, Siemens, ABB, WEG, Eaton… copiadas de sus hojas de datos o exportadas a CSV desde Excel.
/// </summary>
public sealed class ManufacturerCatalog
{
    private readonly List<ManufacturerPreset> _presets = [];

    public IReadOnlyList<ManufacturerPreset> Presets => _presets;

    public IEnumerable<ManufacturerPreset> ForType(string typeId) => _presets.Where(p => p.TypeId == typeId);

    public void Add(ManufacturerPreset preset)
    {
        _presets.RemoveAll(p => p.Manufacturer == preset.Manufacturer && p.Reference == preset.Reference && p.TypeId == preset.TypeId);
        _presets.Add(preset);
    }

    public sealed record ImportReport(int Imported, IReadOnlyList<string> Errors);

    public ImportReport ImportCsv(string text, ElementCatalog types)
    {
        var errors = new List<string>();
        var count = 0;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var sep = line.Contains(';') ? ';' : ',';
            var cells = SplitCsv(line, sep);
            if (cells[0].Trim().Equals("Fabricante", StringComparison.OrdinalIgnoreCase)) continue; // cabecera
            if (cells.Count < 3) { errors.Add($"Línea {i + 1}: faltan columnas (Fabricante;Referencia;Tipo;Descripción;Parámetros)."); continue; }
            var typeId = cells[2].Trim();
            if (!types.TryGet(typeId, out var type)) { errors.Add($"Línea {i + 1}: tipo desconocido «{typeId}»."); continue; }
            var ps = new Dictionary<string, double>();
            var paramText = cells.Count > 4 ? cells[4] : "";
            var bad = false;
            foreach (var token in paramText.Split(new[] { ' ', '|', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = token.Split('=', 2);
                if (kv.Length != 2 || !double.TryParse(kv[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                { errors.Add($"Línea {i + 1}: parámetro mal escrito «{token}» (usa clave=valor)."); bad = true; break; }
                if (type.Params.All(p => p.Key != kv[0]))
                { errors.Add($"Línea {i + 1}: «{kv[0]}» no es un parámetro de {type.Name} ({string.Join(", ", type.Params.Select(p => p.Key))})."); bad = true; break; }
                ps[kv[0]] = v;
            }
            if (bad) continue;
            Add(new ManufacturerPreset(cells[0].Trim(), cells[1].Trim(), typeId, cells.Count > 3 ? cells[3].Trim() : "", ps));
            count++;
        }
        return new ImportReport(count, errors);
    }

    public string ExportCsv()
    {
        var sb = new StringBuilder("Fabricante;Referencia;Tipo;Descripción;Parámetros\n");
        foreach (var p in _presets)
            sb.Append(p.Manufacturer).Append(';').Append(p.Reference).Append(';').Append(p.TypeId).Append(';')
              .Append(p.Description.Replace(';', ',')).Append(';')
              .AppendLine(string.Join(' ', p.Params.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")));
        return sb.ToString();
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == sep && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}
