using System.IO.Compression;
using System.Text;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Fabrication;
using Simulador.Circuitos.Pcb;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Tests;

public class FabricationTests
{
    private readonly ComponentCatalog _catalog = new();

    private PcbDocument Board()
    {
        var pcb = new PcbDocument { Title = "LED prueba", BoardWidth = 80, BoardHeight = 50 };
        PcbSync.Update(pcb, Examples.LedTransistor(_catalog), _catalog);
        pcb.Tracks.Add(new PcbTrack { A = new Vec2(10, 10), B = new Vec2(20, 10), Width = 0.8, Layer = Layers.Bottom });
        return pcb;
    }

    [Fact]
    public void Gerber_has_header_apertures_and_flips_y()
    {
        var pcb = Board();
        var g = GerberWriter.Layer(pcb, ArtworkLayer.BottomCopper, "Copper,L2,Bot");

        Assert.Contains("%FSLAX46Y46*%", g);
        Assert.Contains("%MOMM*%", g);
        Assert.Contains("C,0.800*%", g);
        // Y de Gerber hacia arriba: 50 - 10 = 40 mm.
        Assert.Contains("X10000000Y40000000D02*", g);
        Assert.Contains("X20000000Y40000000D01*", g);
        Assert.EndsWith("M02*\n", g);
        // Una apertura definida antes de cada uso.
        foreach (var use in g.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^D\d+\*$")))
            Assert.Contains($"%ADD{use[1..^1]}C,", g);
    }

    [Fact]
    public void Excellon_lists_every_hole()
    {
        var pcb = Board();
        var drl = GerberWriter.Excellon(pcb);
        int holes = pcb.Footprints.SelectMany(FootprintTransform.Pads).Count(p => p.Pad.Drill > 0);

        Assert.StartsWith("M48", drl);
        Assert.Contains("METRIC", drl);
        Assert.Equal(holes, drl.Split('\n').Count(l => l.StartsWith('X')));
        Assert.EndsWith("M30\n", drl);
    }

    [Fact]
    public void Zip_contains_single_sided_set()
    {
        var pcb = Board();
        using var zip = new ZipArchive(new MemoryStream(GerberWriter.Zip(pcb)));
        var names = zip.Entries.Select(e => e.Name).ToList();

        Assert.Contains("LED_prueba-B_Cu.gbl", names);
        Assert.Contains("LED_prueba-Edge_Cuts.gm1", names);
        Assert.Contains("LED_prueba.drl", names);
        Assert.DoesNotContain("LED_prueba-F_Cu.gtl", names);
    }

    [Fact]
    public void Transfer_pdf_is_valid_and_true_scale()
    {
        var pcb = Board();
        var bytes = TransferPrint.Pdf(pcb, new TransferOptions { Copies = 3 });
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        // A4 en puntos.
        Assert.Contains("/MediaBox [0 0 595.276 841.89]", text);
        // La pista de 0.8 mm mide 0.8 * 72 / 25.4 = 2.268 pt.
        Assert.Contains("2.268 w", text);
        // xref apunta a los objetos reales.
        int start = int.Parse(text[(text.LastIndexOf("startxref\n") + 10)..].Split('\n')[0]);
        Assert.Equal("xref", text.Substring(start, 4));
        foreach (var line in text[start..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")))
            Assert.Matches(@"^\d+ 0 obj", text[int.Parse(line[..10])..]);
    }

    [Fact]
    public void Bottom_copper_is_not_mirrored_by_default_but_top_is()
    {
        Assert.False(new TransferOptions { Layer = TransferLayer.BottomCopper }.EffectiveMirror);
        Assert.True(new TransferOptions { Layer = TransferLayer.TopCopper }.EffectiveMirror);
        Assert.True(new TransferOptions { Layer = TransferLayer.BottomCopper, Mirror = true }.EffectiveMirror);
    }

    [Fact]
    public void Mirror_flips_x_in_the_print()
    {
        var pcb = new PcbDocument { BoardWidth = 40, BoardHeight = 20 };
        pcb.Tracks.Add(new PcbTrack { A = new Vec2(5, 5), B = new Vec2(10, 5), Width = 1, Layer = Layers.Bottom });
        static string Pt(double x, double y) => $"{(x * PdfWriter.PtPerMm).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} {((297 - y) * PdfWriter.PtPerMm).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";

        var plain = Encoding.Latin1.GetString(TransferPrint.Pdf(pcb, new TransferOptions()));
        var mirrored = Encoding.Latin1.GetString(TransferPrint.Pdf(pcb, new TransferOptions { Mirror = true }));
        double ox = TransferPrint.Margin, oy = TransferPrint.Margin + 22;

        Assert.Contains($"{Pt(ox + 5, oy + 5)} m {Pt(ox + 10, oy + 5)} l S", plain);
        Assert.Contains($"{Pt(ox + 35, oy + 5)} m {Pt(ox + 30, oy + 5)} l S", mirrored);
    }

    [Fact]
    public void Rotated_rect_pad_becomes_rotated_polygon()
    {
        var pcb = Board();
        var f = pcb.Footprints.First(f => f.Definition!.Pads.Any(p => p.Shape == PadShape.Rect));
        f.Rotation = 90;
        var pad = FootprintTransform.Pads(f).First(p => p.Pad.Shape == PadShape.Rect);
        var poly = Artwork.PadShapes(pad, 0).OfType<Polygon>().Single();
        double w = poly.Points.Max(p => p.X) - poly.Points.Min(p => p.X);
        double h = poly.Points.Max(p => p.Y) - poly.Points.Min(p => p.Y);

        Assert.Equal(pad.Pad.Size.Y, w, 6);
        Assert.Equal(pad.Pad.Size.X, h, 6);
    }
}
