using System.Globalization;
using System.IO.Compression;
using System.Text;
using Simulador.Circuitos.Pcb;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Fabrication;

/// <summary>
/// Escribe Gerber RS-274X (formato X2 básico) y taladros Excellon, que aceptan todos los fabricantes de PCB.
/// Unidades: mm. En Gerber la Y crece hacia arriba, así que se invierte respecto a la placa.
/// </summary>
public static class GerberWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly (ArtworkLayer Layer, string Suffix, string Function)[] Files =
    [
        (ArtworkLayer.TopCopper, "F_Cu.gtl", "Copper,L1,Top"),
        (ArtworkLayer.BottomCopper, "B_Cu.gbl", "Copper,L2,Bot"),
        (ArtworkLayer.TopMask, "F_Mask.gts", "Soldermask,Top"),
        (ArtworkLayer.BottomMask, "B_Mask.gbs", "Soldermask,Bot"),
        (ArtworkLayer.TopSilk, "F_Silkscreen.gto", "Legend,Top"),
        (ArtworkLayer.Edge, "Edge_Cuts.gm1", "Profile,NP"),
    ];

    /// <summary>Todos los archivos de fabricación: nombre → contenido.</summary>
    public static Dictionary<string, string> AllFiles(PcbDocument doc)
    {
        var name = SafeName(doc.Title);
        var files = new Dictionary<string, string>();
        foreach (var (layer, suffix, function) in Files)
        {
            // En placas de una cara el cobre de arriba se omite; los pads pasantes van igualmente en la de abajo.
            if (layer == ArtworkLayer.TopCopper && doc.SingleSided) continue;
            files[$"{name}-{suffix}"] = Layer(doc, layer, function);
        }
        files[$"{name}.drl"] = Excellon(doc);
        return files;
    }

    public static byte[] Zip(PcbDocument doc)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (file, content) in AllFiles(doc))
            {
                var entry = zip.CreateEntry(file, CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }
        return ms.ToArray();
    }

    public static string Layer(PcbDocument doc, ArtworkLayer layer, string function)
    {
        var shapes = Artwork.Layer(doc, layer);
        var sb = new StringBuilder();
        sb.Append("G04 Simulador multiple - ").Append(doc.Title.Replace('*', ' ')).Append("*\n");
        sb.Append("%TF.GenerationSoftware,Simulador multiple,Circuitos,1.0*%\n");
        sb.Append("%TF.FileFunction,").Append(function).Append("*%\n");
        sb.Append("%TF.FilePolarity,Positive*%\n");
        sb.Append("%FSLAX46Y46*%\n%MOMM*%\n%LPD*%\nG01*\n");

        // Aperturas circulares, una por diámetro.
        var apertures = new Dictionary<double, int>();
        int next = 10;
        foreach (var d in shapes.Select(s => s switch { Disc c => c.Diameter, Stroke st => st.Width, _ => -1 }).Where(d => d > 0).Distinct().Order())
        {
            apertures[d] = next;
            sb.Append("%ADD").Append(next).Append("C,").Append(d.ToString("0.000###", Inv)).Append("*%\n");
            next++;
        }

        string Xy(Vec2 v) => $"X{Coord(v.X)}Y{Coord(doc.BoardHeight - v.Y)}";
        int current = -1;
        void Use(double d)
        {
            int ap = apertures[d];
            if (ap != current) { sb.Append('D').Append(ap).Append("*\n"); current = ap; }
        }

        foreach (var s in shapes.OfType<Stroke>())
        {
            Use(s.Width);
            sb.Append(Xy(s.A)).Append("D02*\n").Append(Xy(s.B)).Append("D01*\n");
        }
        foreach (var d in shapes.OfType<Disc>())
        {
            Use(d.Diameter);
            sb.Append(Xy(d.Center)).Append("D03*\n");
        }
        foreach (var p in shapes.OfType<Polygon>())
        {
            sb.Append("G36*\n").Append(Xy(p.Points[0])).Append("D02*\n");
            foreach (var pt in p.Points.Skip(1)) sb.Append(Xy(pt)).Append("D01*\n");
            sb.Append(Xy(p.Points[0])).Append("D01*\nG37*\n");
        }
        sb.Append("M02*\n");
        return sb.ToString();
    }

    public static string Excellon(PcbDocument doc)
    {
        var drills = Artwork.Drills(doc);
        var tools = drills.Select(d => Math.Round(d.Diameter, 3)).Distinct().Order().ToList();
        var sb = new StringBuilder();
        sb.Append("M48\n; Taladros - Simulador multiple - ").Append(doc.Title).Append('\n');
        sb.Append("; FORMAT={-:-/ absolute / metric / decimal}\nFMAT,2\nMETRIC,TZ\n");
        for (int i = 0; i < tools.Count; i++) sb.Append($"T{i + 1}C{tools[i].ToString("0.000", Inv)}\n");
        sb.Append("%\nG90\nG05\n");
        string Xy(Vec2 v) => $"X{(v.X).ToString("0.000", Inv)}Y{(doc.BoardHeight - v.Y).ToString("0.000", Inv)}";
        for (int i = 0; i < tools.Count; i++)
        {
            sb.Append($"T{i + 1}\n");
            foreach (var d in drills.Where(d => Math.Round(d.Diameter, 3) == tools[i]))
                sb.Append(d.SlotEnd is { } end ? $"{Xy(d.Center)}G85{Xy(end)}\n" : Xy(d.Center) + "\n");
        }
        sb.Append("T0\nM30\n");
        return sb.ToString();
    }

    /// <summary>Coordenada Gerber 4.6 en mm: entero en millonésimas.</summary>
    private static string Coord(double mm) => ((long)Math.Round(mm * 1_000_000)).ToString(Inv);

    public static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(title.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_');
        return s.Length == 0 ? "placa" : s;
    }
}
