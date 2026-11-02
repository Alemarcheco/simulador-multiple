using Simulador.Core.Libraries.Model;
using static Simulador.Core.Libraries.KiCad.KiCadSymbolImporter;

namespace Simulador.Core.Libraries.KiCad;

/// <summary>
/// Importa huellas de KiCad (<c>.kicad_mod</c>, una por archivo, dentro de carpetas <c>.pretty</c>).
/// Admite el formato moderno <c>(footprint …)</c> y el antiguo <c>(module …)</c>.
/// </summary>
public sealed class KiCadFootprintImporter : ILibraryImporter
{
    public string Name => "Huellas KiCad";
    public IReadOnlyList<string> Extensions { get; } = [".kicad_mod"];

    public LibraryImportResult Import(string content, string libraryName)
    {
        var result = new LibraryImportResult();
        var root = SExpr.Parse(content);
        if (root.Head is not ("footprint" or "module"))
            throw new FormatException($"Se esperaba 'footprint' y se encontró '{root.Head}'.");

        var fp = new FootprintDefinition
        {
            Name = StripLibPrefix(root.Arg(1) ?? "sin_nombre"),
            Library = libraryName,
            Description = root.Child("descr")?.Arg(1) ?? "",
            Tags = root.Child("tags")?.Arg(1) ?? "",
        };
        fp.IsSmd = root.Child("attr")?.HasFlag("smd") ?? false;

        foreach (var item in root.Items)
        {
            var layer = item.Child("layer")?.Arg(1);
            switch (item.Head)
            {
                case "pad": fp.Pads.Add(ReadPad(item)); break;
                case "fp_line":
                    fp.Graphics.Add(new PolylineGraphic([Pt(item.Child("start")), Pt(item.Child("end"))], 0, Stroke(item), false, layer));
                    break;
                case "fp_rect":
                    fp.Graphics.Add(new RectangleGraphic(Pt(item.Child("start")), Pt(item.Child("end")), 0, Stroke(item), FpFilled(item), layer));
                    break;
                case "fp_circle":
                {
                    var c = Pt(item.Child("center"));
                    var e = Pt(item.Child("end"));
                    double r = Math.Sqrt((e.X - c.X) * (e.X - c.X) + (e.Y - c.Y) * (e.Y - c.Y));
                    fp.Graphics.Add(new CircleGraphic(c, r, 0, Stroke(item), FpFilled(item), layer));
                    break;
                }
                case "fp_arc": fp.Graphics.Add(ReadArc(item, layer)); break;
                case "fp_poly":
                    fp.Graphics.Add(new PolylineGraphic(Points(item), 0, Stroke(item), FpFilled(item), layer));
                    break;
                case "fp_text":
                {
                    var at = item.Child("at");
                    fp.Graphics.Add(new TextGraphic(item.Arg(2) ?? "", Pt(at), at?.Num(3) ?? 0, FontSize(item), 0, layer));
                    break;
                }
                case "model":
                    if (item.Arg(1) is { Length: > 0 } path) fp.Models3D.Add(path);
                    break;
            }
        }

        if (!fp.IsSmd && fp.Pads.Count > 0 && fp.Pads.All(p => p.Type == PadType.Smd))
            fp.IsSmd = true;

        result.Footprints.Add(fp);
        return result;
    }

    private static string StripLibPrefix(string name)
    {
        int i = name.IndexOf(':');
        return i >= 0 ? name[(i + 1)..] : name;
    }

    private static bool FpFilled(SExpr item) =>
        Filled(item) || item.Child("fill")?.Arg(1) is "solid" or "yes";

    private static FootprintPad ReadPad(SExpr pad)
    {
        var at = pad.Child("at");
        var size = pad.Child("size");
        var drill = pad.Child("drill");
        double drillDia = 0;
        Vec2? drillSize = null;
        if (drill is not null)
        {
            if (drill.Arg(1) == "oval")
            {
                drillSize = new Vec2(drill.Num(2), drill.Num(3, drill.Num(2)));
                drillDia = Math.Min(drillSize.Value.X, drillSize.Value.Y);
            }
            else
            {
                drillDia = drill.Num(1);
            }
        }

        var layers = pad.Child("layers")?.Items.Skip(1).Select(i => i.Atom ?? "").ToList() ?? [];

        return new FootprintPad(
            Number: pad.Arg(1) ?? "",
            Type: pad.Arg(2) switch
            {
                "thru_hole" => PadType.ThroughHole,
                "np_thru_hole" => PadType.NpThroughHole,
                "connect" => PadType.Connect,
                _ => PadType.Smd,
            },
            Shape: pad.Arg(3) switch
            {
                "circle" => PadShape.Circle,
                "rect" => PadShape.Rect,
                "oval" => PadShape.Oval,
                "roundrect" => PadShape.RoundRect,
                "trapezoid" => PadShape.Trapezoid,
                _ => PadShape.Custom,
            },
            Position: Pt(at),
            RotationDeg: at?.Num(3) ?? 0,
            Size: size is null ? default : new Vec2(size.Num(1), size.Num(2)),
            Drill: drillDia,
            DrillSize: drillSize,
            Layers: layers,
            RoundRectRatio: pad.Child("roundrect_rratio")?.Num(1) ?? 0);
    }

    private static ArcGraphic ReadArc(SExpr item, string? layer)
    {
        if (item.Child("mid") is { } mid)
            return new ArcGraphic(Pt(item.Child("start")), Pt(mid), Pt(item.Child("end")), 0, Stroke(item), false, layer);

        // Formato antiguo (KiCad 5): (start = centro) (end = punto inicial) (angle = barrido en grados).
        var center = Pt(item.Child("start"));
        var p0 = Pt(item.Child("end"));
        double sweep = (item.Child("angle")?.Num(1) ?? 0) * Math.PI / 180.0;
        Vec2 Rotate(double a)
        {
            double dx = p0.X - center.X, dy = p0.Y - center.Y;
            return new Vec2(center.X + dx * Math.Cos(a) - dy * Math.Sin(a), center.Y + dx * Math.Sin(a) + dy * Math.Cos(a));
        }
        return new ArcGraphic(p0, Rotate(sweep / 2), Rotate(sweep), 0, Stroke(item), false, layer);
    }
}
