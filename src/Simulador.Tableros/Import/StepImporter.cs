using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Security;
using System.Text;
using Simulador.Tableros.Import.Step;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Import;

/// <summary>
/// Importa los modelos 3D tal como los dan las webs de los fabricantes (STEP .stp/.step, sueltos o dentro de un .zip):
/// los lee, los gira para que el frente mire hacia fuera del cuadro y los guarda como paquete .edz en la librería.
/// </summary>
public static class StepImporter
{
    /// <summary>Más triángulos que esto hacen lenta la vista 3D: se simplifica al importar.</summary>
    public const int MaxTriangles = 150_000;

    public static readonly string[] Extensions = [".stp", ".step"];

    public static bool IsStep(string path) => Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ejes del archivo: hacia dónde mira el frente del aparato y hacia dónde queda su parte de arriba.</summary>
    public static readonly string[] Axes = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];

    /// <summary>Un modelo STEP leído, con los ajustes que se proponen (el usuario los puede cambiar antes de guardar).</summary>
    public sealed class Candidate
    {
        public required string FileName { get; init; }
        public required byte[] Data { get; init; }
        public required DeviceMesh Raw { get; init; }
        public required string ProductName { get; init; }
        public List<string> Notes { get; } = [];
        public string Brand { get; set; } = "";
        public string Reference { get; set; } = "";
        public string Kind { get; set; } = "aparato";
        public string Front { get; set; } = "+Y";
        public string Up { get; set; } = "+Z";
    }

    /// <summary>Lee un .stp/.step, o todos los que haya dentro de un .zip.</summary>
    public static List<Candidate> Prepare(string path)
    {
        var list = new List<Candidate>();
        if (IsStep(path)) list.Add(Prepare(File.ReadAllBytes(path), Path.GetFileName(path)));
        else
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var e in zip.Entries.Where(e => IsStep(e.FullName) && e.Length < 256L * 1024 * 1024))
            {
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                list.Add(Prepare(ms.ToArray(), Path.GetFileName(e.FullName)));
            }
        }
        return list;
    }

    public static Candidate Prepare(byte[] data, string fileName)
    {
        var r = StepReader.Read(Encoding.Latin1.GetString(data), fileName);
        var c = new Candidate { FileName = fileName, Data = data, Raw = r.Mesh.Simplified(MaxTriangles), ProductName = r.ProductName };
        c.Notes.AddRange(r.Notes.Select(n => n.StartsWith(fileName, StringComparison.Ordinal) ? n : $"{fileName}: {n}"));
        if (r.Mesh.Triangles.Count > MaxTriangles) c.Notes.Add($"{fileName}: modelo simplificado a {c.Raw.Triangles.Count} triángulos.");
        var stem = Path.GetFileNameWithoutExtension(fileName);
        c.Reference = LooksGeneric(r.ProductName) ? CleanStem(stem) : CleanStem(r.ProductName.Trim());
        c.Brand = GuessBrand(c.Reference + " " + stem + " " + r.ProductName);
        c.Kind = EdzImporter.GuessKind(c.Reference + " " + stem + " " + r.ProductName);
        if (c.Kind == "aparato") c.Kind = GuessKindByBrand(c.Reference + " " + stem);
        (c.Front, c.Up) = AutoOrientation(c.Raw, DeviceLibrary.Get(c.Kind).Location == DeviceLocation.Door);
        return c;
    }

    private static bool LooksGeneric(string name) =>
        name.Trim().Length < 3 || name.All(ch => char.IsDigit(ch) || ch is '_' or '-' or ' ')
        || name.StartsWith("PRT", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ASM", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("BASE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PART", StringComparison.OrdinalIgnoreCase);

    private static string CleanStem(string stem)
    {
        foreach (var junk in new[] { "_3D-simplified", "-3D-simplified", "_3D-CAD-Simpl", "_3D", "_sr_prt", "stp_" })
            stem = stem.Replace(junk, "", StringComparison.OrdinalIgnoreCase);
        return stem.Trim('_', '-', ' ');
    }

    /// <summary>Marca a partir de cómo numera cada fabricante sus referencias.</summary>
    public static string GuessBrand(string text)
    {
        var t = text.ToUpperInvariant();
        (string Brand, string[] Prefixes)[] rules =
        [
            ("Allen-Bradley", ["1756", "1769", "1734", "1444", "1492", "1606", "800H", "800T", "800F", "700-", "100-C", "140M", "2080", "2711"]),
            ("ABB", ["2CDS", "2CSF", "1SBL", "1SAM", "1SVR", "S201", "S202", "S203", "S204"]),
            ("Schneider Electric", ["GV2", "GV3", "LC1", "LRD", "A9F", "A9N", "RXM", "RXZ", "XB4", "XB5", "ATV", "TM2", "TM3", "LV4", "METSE"]),
            ("Siemens", ["3RT", "3RV", "5SY", "5SL", "6ES7", "3SU", "6EP"]),
            ("Phoenix Contact", ["UT ", "PT ", "QUINT", "TRIO"]),
            ("Woodward", ["8200-", "8237-", "9907-"]),
        ];
        var words = t.Split(new[] { ' ', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var (brand, prefixes) in rules)
            if (prefixes.Any(p => t.StartsWith(p, StringComparison.Ordinal) || words.Any(w => w.StartsWith(p.Trim(), StringComparison.Ordinal))))
                return brand;
        return "Sin marca";
    }

    private static string GuessKindByBrand(string text)
    {
        var t = text.ToUpperInvariant();
        if (t.Contains("800H") || t.Contains("800T") || t.Contains("XB4") || t.Contains("XB5") || t.Contains("3SU")) return "pulsador";
        if (t.Contains("1756") || t.Contains("1769") || t.Contains("1734") || t.Contains("1444") || t.Contains("6ES7")) return "plc";
        if (t.Contains("2CDS") || t.Contains("A9F") || t.Contains("5SY") || t.Contains("S20")) return "magnetotermico";
        if (t.Contains("GV2") || t.Contains("GV3") || t.Contains("3RV")) return "guardamotor";
        if (t.Contains("LC1") || t.Contains("3RT")) return "contactor";
        if (t.Contains("RXM") || t.Contains("RXZ")) return "rele";
        return "aparato";
    }

    private static Vector3 Dir(string axis)
    {
        var v = axis[1] switch { 'X' => Vector3.UnitX, 'Y' => Vector3.UnitY, _ => Vector3.UnitZ };
        return axis[0] == '-' ? -v : v;
    }

    private static string Name(int axis, bool negative) => (negative ? "-" : "+") + "XYZ"[axis];

    private static float Comp(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>
    /// Frente y arriba propuestos. En placa, la espalda es la cara plana más grande en un extremo de la caja (la que
    /// apoya en el carril o la placa). En puerta, el eje más largo es el de fondo y el frente es el lado del bisel
    /// (la sección más ancha). Arriba: el eje Z o Y del archivo, como suelen dibujar los programas de CAD.
    /// </summary>
    public static (string Front, string Up) AutoOrientation(DeviceMesh m, bool door)
    {
        if (m.Triangles.Count == 0) return ("+Y", "+Z");
        var size = m.Size;
        // Área plana en cada extremo de la caja. Los laterales de un aparato modular son planos por los dos lados;
        // la espalda es plana y el frente no: el eje de fondo es el más asimétrico.
        var area = new double[6];
        foreach (var (a, b, c, _) in m.Triangles)
        {
            var n = Vector3.Cross(b - a, c - a);
            var len = n.Length();
            if (len <= 0) continue;
            n /= len;
            var cen = (a + b + c) / 3;
            for (var k = 0; k < 3; k++)
            {
                if (MathF.Abs(Comp(n, k)) < 0.95f) continue;
                var tol = Math.Max(0.5f, Comp(size, k) * 0.01f);
                if (Comp(cen, k) - Comp(m.Min, k) < tol) area[2 * k] += len / 2;
                else if (Comp(m.Max, k) - Comp(cen, k) < tol) area[2 * k + 1] += len / 2;
            }
        }
        double Asym(int k) => Math.Abs(area[2 * k] - area[2 * k + 1]);
        int depthAxis;
        if (door)
        {
            // Pulsadores y pilotos: el eje más asimétrico; si dos se parecen, el más largo (el cuerpo va detrás).
            var order = Enumerable.Range(0, 3).OrderByDescending(Asym).ToArray();
            depthAxis = Asym(order[0]) < 1.5 * Asym(order[1]) && Comp(size, order[1]) > Comp(size, order[0]) ? order[1] : order[0];
        }
        else
        {
            // Ancho: el eje con las dos caras planas más grandes (los laterales). De los otros dos, el fondo es el más
            // asimétrico (espalda plana, frente con relieve); si se parecen, se toma Y del archivo como arriba.
            var width = Enumerable.Range(0, 3).OrderByDescending(k => Math.Min(area[2 * k], area[2 * k + 1])).First();
            var rest = Enumerable.Range(0, 3).Where(k => k != width).OrderByDescending(Asym).ToArray();
            depthAxis = Asym(rest[0]) >= 1.5 * Asym(rest[1]) && Asym(rest[0]) > 0.005 * area.Sum() ? rest[0] : rest[0] == 1 ? rest[1] : rest[1] == 1 ? rest[0] : rest[0];
        }
        bool frontNegative;
        // Las piezas de color (manetas, botones, etiquetas) suelen estar en el frente de los aparatos de placa.
        int colLo = 0, colHi = 0;
        {
            float lo = Comp(m.Min, depthAxis), ext = Math.Max(1e-6f, Comp(size, depthAxis));
            var main = m.Triangles.GroupBy(t => t.Color).OrderByDescending(g => g.Count()).First().Key;
            foreach (var t in m.Triangles)
            {
                if (t.Color == main) continue;
                var f = (Comp((t.A + t.B + t.C) / 3, depthAxis) - lo) / ext;
                if (f < 0.25f) colLo++;
                else if (f > 0.75f) colHi++;
            }
        }
        if (door)
        {
            // En puerta, el frente es el lado del bisel (la sección más ancha).
            var at = WidestSection(m, depthAxis);
            frontNegative = at < (Comp(m.Min, depthAxis) + Comp(m.Max, depthAxis)) / 2;
        }
        else if (Math.Max(colLo, colHi) > 100 && Math.Max(colLo, colHi) > 3 * Math.Min(colLo, colHi)) frontNegative = colLo > colHi;
        else frontNegative = area[2 * depthAxis + 1] > area[2 * depthAxis]; // la espalda es el lado más plano
        // Arriba: en placa, el eje que no es ni ancho ni fondo; en puerta, Y o Z del archivo.
        var others = Enumerable.Range(0, 3).Where(k => k != depthAxis).ToArray();
        int up;
        if (door) up = others.Contains(1) ? 1 : 2;
        else
        {
            var width = Enumerable.Range(0, 3).Where(k => k != depthAxis).OrderByDescending(k => Math.Min(area[2 * k], area[2 * k + 1])).First();
            up = others.First(k => k != width);
        }
        return (Name(depthAxis, frontNegative), Name(up, false));
    }

    /// <summary>Posición (en el eje dado) de la sección más ancha de la malla.</summary>
    private static float WidestSection(DeviceMesh m, int axis)
    {
        float lo = Comp(m.Min, axis), hi = Comp(m.Max, axis);
        const int bins = 120;
        var mins = Enumerable.Repeat(new Vector3(float.MaxValue), bins).ToArray();
        var maxs = Enumerable.Repeat(new Vector3(float.MinValue), bins).ToArray();
        foreach (var (a, b, c, _) in m.Triangles)
            foreach (var p in new[] { a, b, c })
            {
                var i = Math.Clamp((int)((Comp(p, axis) - lo) / Math.Max(1e-6f, hi - lo) * bins), 0, bins - 1);
                mins[i] = Vector3.Min(mins[i], p);
                maxs[i] = Vector3.Max(maxs[i], p);
            }
        var bestI = 0;
        var bestA = 0f;
        for (var i = 0; i < bins; i++)
        {
            if (mins[i].X > maxs[i].X) continue;
            var d = maxs[i] - mins[i];
            var a = axis == 0 ? d.Y * d.Z : axis == 1 ? d.X * d.Z : d.X * d.Y;
            if (a > bestA * 1.02f) { bestA = a; bestI = i; }
        }
        return lo + (bestI + 0.5f) * (hi - lo) / bins;
    }

    /// <summary>
    /// Gira la malla a los ejes del simulador (X ancho, Y hacia fuera, Z arriba) y la apoya en el origen. En puerta,
    /// Y = 0 queda en la cara exterior de la puerta (detrás del bisel).
    /// </summary>
    public static DeviceMesh Orient(DeviceMesh raw, string front, string up, bool door)
    {
        var y = Dir(front);
        var z = Dir(up);
        if (MathF.Abs(Vector3.Dot(y, z)) > 0.5f) z = MathF.Abs(y.Z) < 0.5f ? Vector3.UnitZ : Vector3.UnitY;
        // La escena dibuja el fondo del aparato hacia -Y del mundo (X, -Y, Z): para que el modelo no salga como en un
        // espejo, el giro del archivo a los ejes del aparato tiene que invertir la orientación (X = Z × Y).
        var x = Vector3.Cross(z, y);
        var outMesh = new DeviceMesh();
        Vector3 R(Vector3 p) => new(Vector3.Dot(p, x), Vector3.Dot(p, y), Vector3.Dot(p, z));
        foreach (var (a, b, c, col) in raw.Triangles) outMesh.Add(R(a), R(b), R(c), col);
        outMesh.Seal();
        var shift = outMesh.Min;
        if (door) shift.Y = WidestSection(outMesh, 1);
        var moved = new DeviceMesh();
        foreach (var (a, b, c, col) in outMesh.Triangles) moved.Add(a - shift, b - shift, c - shift, col);
        return moved.Seal();
    }

    /// <summary>Medidas del aparato (mm) a partir de la malla ya girada.</summary>
    public static (double W, double H, double D) Size(DeviceMesh oriented, bool door) =>
        (Math.Round(oriented.Size.X, 1), Math.Round(oriented.Size.Z, 1),
         Math.Round(door ? Math.Max(1, -oriented.Min.Y) : oriented.Size.Y, 1));

    /// <summary>Convierte el modelo en ficha de catálogo, con los ajustes del candidato.</summary>
    public static CatalogItem ToItem(Candidate c)
    {
        var model = DeviceLibrary.Get(c.Kind);
        var door = model.Location == DeviceLocation.Door;
        var mesh = Orient(c.Raw, c.Front, c.Up, door);
        var (w, h, d) = Size(mesh, door);
        var brand = c.Brand.Trim().Length > 0 ? c.Brand.Trim() : "Sin marca";
        var reference = c.Reference.Trim().Length > 0 ? c.Reference.Trim() : CleanStem(Path.GetFileNameWithoutExtension(c.FileName));
        return new CatalogItem(brand, reference, c.Kind, model.Name, w, h, d, model.HeatW, model.Location, model.OnRail, true,
            $"Modelo 3D del fabricante ({c.FileName})") { Mesh = mesh };
    }

    /// <summary>Guarda la ficha como paquete .edz (XML + .obj/.mtl + el STEP original) para cargarla rápido después.</summary>
    public static string WriteEdz(CatalogItem item, Candidate c, string folder)
    {
        Directory.CreateDirectory(folder);
        var safe = new string($"{item.Brand}_{item.Reference}".Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_').ToArray());
        var path = Path.Combine(folder, safe + ".edz");
        var inv = CultureInfo.InvariantCulture;
        string A(string s) => SecurityElement.Escape(s) ?? "";
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        xml.AppendLine("<!-- Importado en Simulador multiple desde el modelo STEP del fabricante. -->");
        xml.AppendLine("<partsmanagement>");
        xml.Append($"  <part P_ARTICLE_PARTNR=\"{A(item.Brand)}.{A(item.Reference)}\" P_ARTICLE_MANUFACTURER=\"{A(item.Brand)}\" P_ARTICLE_TYPENR=\"{A(item.Reference)}\"");
        xml.Append($" P_ARTICLE_DESCR1=\"{A(item.Description)}\" P_ARTICLE_WIDTH=\"{item.Width.ToString(inv)}\" P_ARTICLE_HEIGHT=\"{item.Height.ToString(inv)}\" P_ARTICLE_DEPTH=\"{item.Depth.ToString(inv)}\">\n");
        xml.AppendLine($"    <simulador kind=\"{item.Kind}\" location=\"{item.Location}\" onRail=\"{(item.OnRail ? "true" : "false")}\" verified=\"true\" source=\"{A(item.Source)}\" model3d=\"3D/modelo.obj\" step=\"3D/{A(c.FileName)}\" front=\"{c.Front}\" up=\"{c.Up}\" />");
        xml.AppendLine("  </part>");
        xml.AppendLine("</partsmanagement>");

        var (obj, mtl) = ToObj(item.Mesh!);
        if (File.Exists(path)) File.Delete(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] data)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(data);
            }
            Put("items/pieza.xml", Encoding.UTF8.GetBytes(xml.ToString()));
            Put("3D/modelo.obj", Encoding.UTF8.GetBytes(obj));
            Put("3D/modelo.mtl", Encoding.UTF8.GetBytes(mtl));
            Put("3D/" + c.FileName, c.Data);
        }
        return path;
    }

    public static (string Obj, string Mtl) ToObj(DeviceMesh mesh)
    {
        var inv = CultureInfo.InvariantCulture;
        var obj = new StringBuilder("mtllib modelo.mtl\n");
        var mtl = new StringBuilder();
        var index = new Dictionary<Vector3, int>();
        int V(Vector3 p)
        {
            if (index.TryGetValue(p, out var i)) return i;
            index[p] = i = index.Count + 1;
            obj.Append("v ").Append(p.X.ToString("0.###", inv)).Append(' ').Append(p.Y.ToString("0.###", inv)).Append(' ')
                .Append(p.Z.ToString("0.###", inv)).Append('\n');
            return i;
        }
        foreach (var group in mesh.Triangles.GroupBy(t => t.Color))
        {
            var name = $"c_{group.Key & 0xFFFFFF:X6}";
            mtl.Append("newmtl ").Append(name).Append('\n').Append("Kd ")
                .Append(((group.Key >> 16 & 0xFF) / 255.0).ToString("0.###", inv)).Append(' ')
                .Append(((group.Key >> 8 & 0xFF) / 255.0).ToString("0.###", inv)).Append(' ')
                .Append(((group.Key & 0xFF) / 255.0).ToString("0.###", inv)).Append('\n');
            var faces = new StringBuilder();
            foreach (var (a, b, c, _) in group) faces.Append("f ").Append(V(a)).Append(' ').Append(V(b)).Append(' ').Append(V(c)).Append('\n');
            obj.Append("usemtl ").Append(name).Append('\n').Append(faces);
        }
        return (obj.ToString(), mtl.ToString());
    }
}
