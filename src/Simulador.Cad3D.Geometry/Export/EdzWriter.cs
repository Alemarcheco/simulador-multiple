using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Simulador.Cad3D.Geometry.Scan;

namespace Simulador.Cad3D.Geometry.Export;

/// <summary>Cara de la pieza (tal como está en el módulo 3D) que se apoya en la placa de montaje o en la puerta.</summary>
public enum MountFace
{
    /// <summary>La de abajo (Z mínima): la pieza se escaneó tumbada sobre la mesa. En el tablero se ve como en la vista «Arriba».</summary>
    Abajo,
    /// <summary>La de atrás (Y máxima): en el tablero se ve como en la vista «Frente».</summary>
    Atras,
}

/// <summary>
/// Guarda una pieza como paquete de librería de fabricante (.edz, ZIP) que lee el módulo Tableros eléctricos: un XML
/// con las propiedades P_ARTICLE_* de EPLAN (marca, referencia, descripción, medidas…) y el modelo 3D en OBJ + MTL
/// con sus colores, en el sistema de Tableros: X ancho, Y hacia fuera desde la placa y Z alto, en mm desde 0.
/// </summary>
public static class EdzWriter
{
    public sealed record Options(
        string Brand, string Reference, string Description, string Kind,
        bool OnDoor, bool OnRail, MountFace Face, double HeatW = 0, int MaxTriangles = 8000, uint DefaultColor = 0xFFB9C0C7,
        double DoorProtrusion = -1);

    public sealed record Result(double Width, double Height, double Depth, int Triangles);

    /// <summary>Carpeta de librerías del usuario (la misma que usa Tableros eléctricos).</summary>
    public static string UserLibraryFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimuladorMultiple", "Librerias");

    /// <summary>Nombre de archivo seguro a partir de la marca y la referencia.</summary>
    public static string FileName(string brand, string reference)
    {
        var s = new string($"{brand}_{reference}".Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' ? c : '_');
        var r = sb.ToString().Trim('_');
        return (r.Length == 0 ? "pieza" : r) + ".edz";
    }

    public static Result Write(string path, ScanMesh mesh, Options o)
    {
        var m = ScanRepair.Simplify(mesh, o.MaxTriangles);
        if (m.TriangleCount == 0) throw new InvalidOperationException("la pieza no tiene triángulos");
        var (min, max) = m.Bounds();
        Vec3 Map(Vec3 v) => o.Face == MountFace.Abajo
            ? new Vec3(v.X - min.X, v.Z - min.Z, v.Y - min.Y)
            : new Vec3(v.X - min.X, max.Y - v.Y, v.Z - min.Z);
        var pts = m.Vertices.Select(Map).ToList();
        double w = 0, d = 0, h = 0;
        foreach (var p in pts) { w = Math.Max(w, p.X); d = Math.Max(d, p.Y); h = Math.Max(h, p.Z); }
        if (o.OnDoor)
        {
            // En la puerta, Y = 0 es la cara exterior: solo el frente sobresale; el resto queda dentro del armario.
            double front = o.DoorProtrusion >= 0 ? Math.Min(o.DoorProtrusion, d) : d * 0.25;
            var shift = d - front;
            for (int i = 0; i < pts.Count; i++) pts[i] = pts[i] with { Y = pts[i].Y - shift };
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var (obj, mtl) = Obj(name, pts, m, o.DefaultColor);
        var xml = Xml(name, o, w, h, d);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Add(string entry, string text)
            {
                using var s = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
                var b = new UTF8Encoding(false).GetBytes(text);
                s.Write(b, 0, b.Length);
            }
            Add($"items/{name}.xml", xml);
            Add($"3D/{name}.obj", obj);
            Add($"3D/{name}.mtl", mtl);
        }
        File.Move(tmp, path, overwrite: true);
        return new Result(w, h, d, m.TriangleCount);
    }

    private static (string Obj, string Mtl) Obj(string name, List<Vec3> pts, ScanMesh m, uint fallback)
    {
        var inv = CultureInfo.InvariantCulture;
        var obj = new StringBuilder();
        obj.Append("# Pieza escaneada para Simulador multiple. Unidades: mm. X ancho, Y fondo (hacia fuera), Z alto.\n");
        obj.Append("mtllib ").Append(name).Append(".mtl\n");
        foreach (var p in pts) obj.Append(string.Create(inv, $"v {p.X:0.###} {p.Y:0.###} {p.Z:0.###}\n"));
        // Color de cada triángulo (media de sus vértices), en 16 tonos por canal para no tener miles de materiales.
        var groups = new SortedDictionary<uint, List<int>>();
        for (int t = 0; t < m.TriangleCount; t++)
        {
            uint c = fallback;
            if (m.HasColors)
            {
                uint a = m.Colors![m.Indices[3 * t]], b = m.Colors[m.Indices[3 * t + 1]], cc = m.Colors[m.Indices[3 * t + 2]];
                uint Ch(int s) => (uint)Math.Round((((a >> s) & 0xFF) + ((b >> s) & 0xFF) + ((cc >> s) & 0xFF)) / 3.0 / 17.0) * 17;
                c = 0xFF000000 | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
            }
            if (!groups.TryGetValue(c, out var l)) groups[c] = l = new List<int>();
            l.Add(t);
        }
        var mtl = new StringBuilder();
        foreach (var (c, tris) in groups)
        {
            var mat = $"c_{c & 0xFFFFFF:X6}";
            mtl.Append(string.Create(inv, $"newmtl {mat}\nKd {((c >> 16) & 0xFF) / 255.0:0.###} {((c >> 8) & 0xFF) / 255.0:0.###} {(c & 0xFF) / 255.0:0.###}\n\n"));
            obj.Append("usemtl ").Append(mat).Append('\n');
            foreach (var t in tris)
                obj.Append("f ").Append(m.Indices[3 * t] + 1).Append(' ').Append(m.Indices[3 * t + 1] + 1).Append(' ').Append(m.Indices[3 * t + 2] + 1).Append('\n');
        }
        return (obj.ToString(), mtl.ToString());
    }

    private static string Xml(string name, Options o, double w, double h, double d)
    {
        string X(string s) => SecurityElement.Escape(s) ?? "";
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var brand = string.IsNullOrWhiteSpace(o.Brand) ? "Mis piezas" : o.Brand.Trim();
        var reference = o.Reference.Trim();
        var source = "Pieza escaneada y arreglada en el módulo Diseño 3D";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<!-- Datos de pieza con las propiedades P_ARTICLE_* de EPLAN. Generado por Simulador multiple (Diseño 3D). -->\n");
        sb.Append("<partsmanagement>\n");
        sb.Append($"  <part P_ARTICLE_PARTNR=\"{X(brand + "." + reference)}\" P_ARTICLE_MANUFACTURER=\"{X(brand)}\" P_ARTICLE_TYPENR=\"{X(reference)}\" ");
        sb.Append($"P_ARTICLE_ORDERNR=\"{X(reference)}\" P_ARTICLE_DESCR1=\"{X(o.Description)}\" ");
        sb.Append($"P_ARTICLE_WIDTH=\"{N(w)}\" P_ARTICLE_HEIGHT=\"{N(h)}\" P_ARTICLE_DEPTH=\"{N(d)}\" P_ARTICLE_POWER_DISSIPATION=\"{N(o.HeatW)}\" ");
        sb.Append($"P_ARTICLE_MOUNTINGSITE=\"{(o.OnDoor ? "Puerta" : "Placa de montaje")}\" P_ARTICLE_MOUNTINGSPACE=\"{(o.OnRail ? "Carril DIN 35 mm" : "Atornillado")}\" ");
        sb.Append($"P_ARTICLE_NOTE=\"{X(source)}\">\n");
        sb.Append($"    <simulador kind=\"{X(o.Kind)}\" location=\"{(o.OnDoor ? "Door" : "Plate")}\" onRail=\"{(o.OnRail ? "true" : "false")}\" ");
        sb.Append($"verified=\"true\" source=\"{X(source)}\" model3d=\"3D/{X(name)}.obj\" scanned=\"true\" />\n");
        sb.Append("  </part>\n</partsmanagement>\n");
        return sb.ToString();
    }
}
