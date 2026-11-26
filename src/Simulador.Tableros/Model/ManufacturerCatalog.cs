namespace Simulador.Tableros.Model;

/// <summary>
/// Referencia comercial de un fabricante con sus medidas. Solo datos publicados gratis por el propio fabricante
/// (fichas de producto y especificaciones de su web). Sin logotipos ni dibujos de la marca: el aparato se ve con el
/// aspecto genérico de su tipo y con la medida real.
/// </summary>
/// <param name="Kind">Tipo genérico de <see cref="DeviceLibrary"/> que le corresponde.</param>
/// <param name="Verified">
/// true = medidas leídas en la ficha del fabricante (<paramref name="Source"/>); false = la ficha no da medidas y se
/// usan las típicas del tipo (hay que comprobarlas en el catálogo).
/// </param>
/// <param name="Keys">Textos que identifican el aparato en un plano (además de la referencia).</param>
public sealed record CatalogItem(
    string Brand, string Reference, string Kind, string Description, double Width, double Height, double Depth, double HeatW,
    DeviceLocation Location, bool OnRail, bool Verified, string Source, string[]? Keys = null)
{
    public string Display => $"{Brand} {Reference} · {Description}";
    /// <summary>Modelo 3D (mm) que trae el paquete .edz; null = se dibuja con el aspecto genérico del tipo.</summary>
    public DeviceMesh? Mesh { get; init; }
    public string FullReference => $"{Brand} {Reference}";
}

/// <summary>Librerías por fabricante: ABB, Allen-Bradley (Rockwell), Schneider Electric y Woodward.</summary>
public static class ManufacturerCatalog
{
    private const string Rockwell = "rockwellautomation.com, ficha de producto";
    private const string Abb = "new.abb.com, ficha de producto";
    private const string Se = "se.com, ficha de producto";

    /// <summary>Marcas con algún modelo (las de serie primero y luego las importadas, por orden alfabético).</summary>
    public static IReadOnlyList<string> Brands =>
        BuiltInBrands.Concat(All.Select(i => i.Brand).Where(b => !BuiltInBrands.Contains(b)).Distinct().OrderBy(b => b, StringComparer.CurrentCultureIgnoreCase))
            .Where(b => All.Any(i => i.Brand == b)).ToList();

    private static readonly string[] BuiltInBrands = ["ABB", "Allen-Bradley", "Schneider Electric", "Woodward"];

    private static readonly List<CatalogItem> Imported = [];
    private static readonly object Gate = new();
    private static IReadOnlyList<CatalogItem>? _all;

    /// <summary>
    /// Todos los modelos: los de serie y los importados de paquetes .edz. Si un paquete trae una referencia que ya
    /// estaba, manda la del paquete (suele traer modelo 3D y medidas de la ficha).
    /// </summary>
    public static IReadOnlyList<CatalogItem> All
    {
        get
        {
            lock (Gate)
                return _all ??= Imported.Concat(BuiltIn.Where(b => !Imported.Any(i => Same(i, b)))).ToList();
        }
    }

    /// <summary>Modelos que vienen con el programa (sin paquetes importados).</summary>
    public static IReadOnlyList<CatalogItem> BuiltIn { get; } =
    [
        // ---------------- Allen-Bradley (Rockwell Automation)
        new("Allen-Bradley", "1606-XLS120E", "fuente", "Fuente XLS 24 V DC 5 A, 120 W", 40, 124, 117, 9.4, DeviceLocation.Plate, true, true, Rockwell),
        new("Allen-Bradley", "1606-XLS240E", "fuente", "Fuente XLS 24 V DC 10 A, 240 W", 60, 124, 117, 18, DeviceLocation.Plate, true, true, Rockwell, ["1606-XLS"]),
        new("Allen-Bradley", "1606-XLS480E", "fuente", "Fuente XLS 24 V DC 20 A, 480 W", 82, 124, 127, 31.4, DeviceLocation.Plate, true, true, Rockwell),
        new("Allen-Bradley", "1492-J4", "regleta", "Borne de paso 4 mm² (6,1 mm)", 6.1, 60, 39.5, 0, DeviceLocation.Plate, true, true, Rockwell),
        new("Allen-Bradley", "100-C09D10", "contactor", "Contactor IEC 9 A, 1 NA, bobina 24 V DC", 45, 81, 80, 2, DeviceLocation.Plate, true, false, Rockwell),
        new("Allen-Bradley", "100-C23D10", "contactor", "Contactor IEC 23 A, 1 NA, bobina 24 V DC", 45, 81, 92, 3, DeviceLocation.Plate, true, false, Rockwell),
        new("Allen-Bradley", "1489-M1C160", "magnetotermico", "Interruptor automático UL489 1P C16", 12.7, 85, 70, 2, DeviceLocation.Plate, true, false, Rockwell),
        new("Allen-Bradley", "1489-M2C100", "magnetotermico", "Interruptor automático UL489 2P C10", 25.4, 85, 70, 3, DeviceLocation.Plate, true, false, Rockwell),
        new("Allen-Bradley", "140M-C2E-B63", "guardamotor", "Guardamotor 4–6,3 A", 45, 90, 97, 6, DeviceLocation.Plate, true, false, Rockwell),
        new("Allen-Bradley", "700-HLT1Z24", "rele", "Relé de interfaz 24 V DC, 1 conmutado", 6.2, 90, 80, 0.4, DeviceLocation.Plate, true, false, Rockwell),

        // ---------------- ABB
        new("ABB", "S201-C16", "magnetotermico", "Interruptor automático S200 1P C16", 17.5, 85, 70, 2, DeviceLocation.Plate, true, false, Abb),
        new("ABB", "S203-C16", "magnetotermico", "Interruptor automático S200 3P C16", 52.5, 85, 70, 6, DeviceLocation.Plate, true, false, Abb),
        new("ABB", "AF09-30-10-13", "contactor", "Contactor AF09, 4 kW/400 V, 1 NA, bobina 100–250 V", 45, 85, 86, 2, DeviceLocation.Plate, true, false, Abb),
        new("ABB", "MS132-10", "guardamotor", "Guardamotor MS132, 6,3–10 A, 100 kA/400 V", 45, 90, 97, 6, DeviceLocation.Plate, true, false, Abb),
        new("ABB", "CP-E 24/10.0", "fuente", "Fuente CP-E 24 V DC 10 A", 60, 90, 100, 20, DeviceLocation.Plate, true, false, Abb),

        // ---------------- Schneider Electric
        new("Schneider Electric", "A9F74116", "magnetotermico", "Interruptor automático Acti9 iC60N 1P C16", 18, 85, 78.5, 2, DeviceLocation.Plate, true, false, Se),
        new("Schneider Electric", "A9F74316", "magnetotermico", "Interruptor automático Acti9 iC60N 3P C16", 54, 85, 78.5, 6, DeviceLocation.Plate, true, false, Se),
        new("Schneider Electric", "LC1D09BD", "contactor", "Contactor TeSys D 9 A, bobina 24 V DC", 45, 77, 93, 2, DeviceLocation.Plate, true, false, Se),
        new("Schneider Electric", "GV2ME10", "guardamotor", "Guardamotor TeSys GV2ME 4–6,3 A", 45, 89, 97, 6, DeviceLocation.Plate, true, false, Se),
        new("Schneider Electric", "RXM2AB2BD", "rele", "Relé enchufable Zelio RXM 2 conmutados 24 V DC con zócalo", 27, 80, 75, 0.9, DeviceLocation.Plate, true, false, Se),

        // ---------------- Woodward (turbinas)
        new("Woodward", "505D", "controlador", "Control digital 505 para turbinas de vapor (montaje en panel)", 356, 279, 102, 25, DeviceLocation.Door, false, true,
            "woodward.com, especificación 03348 (505, 356×279×102 mm)", ["505D", "505DR"]),
        new("Woodward", "505 en caja NEMA 4", "controlador", "Control 505 en caja estanca para mamparo", 508, 508, 193, 25, DeviceLocation.Plate, false, true,
            "woodward.com, especificación 03348 (caja NEMA 4, 508×508×193 mm)"),
        new("Woodward", "ProTech-TPS", "controlador", "Protección total de sobrevelocidad ProTech-TPS (3 módulos A/B/C, montaje en puerta)", 330, 445, 159, 30, DeviceLocation.Door, false, true,
            "woodward.com, especificación 03371 (330×445×159 mm con frontal)", ["PROTECH", "ProTech-TPS"]),
    ];

    public static IEnumerable<CatalogItem> OfBrand(string brand) => All.Where(i => i.Brand == brand);

    /// <summary>Añade (o sustituye, si ya estaba la referencia) modelos importados.</summary>
    public static void Register(IEnumerable<CatalogItem> items)
    {
        lock (Gate)
        {
            foreach (var it in items)
            {
                Imported.RemoveAll(i => Same(i, it));
                Imported.Add(it);
            }
            _all = null;
        }
    }

    /// <summary>Quita todo lo importado (para pruebas).</summary>
    internal static void ClearImported()
    {
        lock (Gate) { Imported.Clear(); _all = null; }
    }

    private static bool Same(CatalogItem a, CatalogItem b) =>
        string.Equals(a.Brand, b.Brand, StringComparison.OrdinalIgnoreCase) && Normalize(a.Reference) == Normalize(b.Reference);

    /// <summary>Busca por referencia (sin distinguir mayúsculas ni espacios), con o sin marca delante.</summary>
    public static CatalogItem? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Normalize(text);
        var all = All;
        return all.FirstOrDefault(i => t == Normalize(i.Reference) || t == Normalize(i.FullReference))
               ?? all.Where(i => Normalize(i.Reference).Length >= 5).FirstOrDefault(i => t.Contains(Normalize(i.Reference)));
    }

    /// <summary>Pone al aparato la referencia, la descripción y las medidas del catálogo (conserva bornes y posición).</summary>
    public static void Apply(PanelDevice d, CatalogItem item)
    {
        var m = DeviceLibrary.Get(item.Kind);
        d.Location = item.Location;
        d.Kind = m.Kind == item.Kind ? item.Kind : d.Kind;
        d.Reference = item.FullReference;
        d.Description = item.Description;
        d.Width = item.Width;
        d.Height = item.Height;
        d.Depth = item.Depth;
        d.HeatW = item.HeatW;
        d.OnRail = item.OnRail;
        if (item.Kind == "regleta" && d.Terminals.Count > 0)
            d.Width = Math.Max(item.Width, d.Terminals.Count / 2 * item.Width + 10);
    }

    private static string Normalize(string s) => new(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
