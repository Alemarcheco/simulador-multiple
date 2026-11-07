using System.Text;
using Simulador.Civil.Export;
using Simulador.Civil.Import;
using Simulador.Civil.Model;

namespace Simulador.Civil.Tests;

public class PlanExportTests
{
    private static BuildingDocument Casa() => BuildingTemplates.CasaConInstalaciones();
    private static string Level0(BuildingDocument d) => d.Levels.OrderBy(l => l.Elevation).First().Id;

    [Fact]
    public void Drawing_has_walls_openings_rooms_and_dimensions()
    {
        var doc = Casa();
        var d = PlanDrawing.Build(doc, Level0(doc));
        Assert.Contains(d.Items, i => i is PlanPolygon { Layer: PlanLayers.Walls, Filled: true });
        Assert.Contains(d.Items, i => i.Layer == PlanLayers.Openings);
        Assert.Contains(d.Items, i => i is PlanText { Layer: PlanLayers.Rooms } t && t.Text.Contains("m²"));
        Assert.Contains(d.Items, i => i is PlanText { Layer: PlanLayers.Dimensions });
        Assert.Contains(d.Items, i => i.Layer == PlanLayers.Electric);
    }

    [Fact]
    public void Overall_dimensions_match_the_outer_wall_box()
    {
        var doc = Casa();
        var d = PlanDrawing.Build(doc, Level0(doc));
        var walls = d.Items.OfType<PlanPolygon>().Where(p => p.Layer == PlanLayers.Walls).SelectMany(p => p.Points).ToList();
        double width = walls.Max(p => p.X) - walls.Min(p => p.X);
        double depth = walls.Max(p => p.Y) - walls.Min(p => p.Y);
        var texts = d.Items.OfType<PlanText>().Where(t => t.Layer == PlanLayers.Dimensions).Select(t => t.Text).ToList();
        Assert.Contains(width.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), texts);
        Assert.Contains(depth.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), texts);
        // La cota total de abajo no se repite con la del muro de fachada que ocupa todo el lado (queda la de arriba).
        Assert.True(texts.Count(t => t == width.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) <= 2);
    }

    [Fact]
    public void Options_can_leave_out_dimensions_and_installations()
    {
        var doc = Casa();
        var d = PlanDrawing.Build(doc, Level0(doc), new PlanOptions(Dimensions: false, Installations: false));
        Assert.DoesNotContain(d.Items, i => i.Layer is PlanLayers.Dimensions or PlanLayers.Electric or PlanLayers.Plumbing);
    }

    [Fact]
    public void Dxf_round_trips_through_the_reader_in_meters()
    {
        var doc = Casa();
        var d = PlanDrawing.Build(doc, Level0(doc));
        var text = Encoding.Latin1.GetString(PlanExport.ToDxfBytes([d]));
        Assert.Contains("AC1009", text);
        Assert.Contains("ANSI_1252", text);
        var back = DxfReader.Parse(text);
        Assert.Equal(1.0, back.MetersPerUnit);
        Assert.Contains(PlanLayers.Walls, back.Layers);
        Assert.Contains(PlanLayers.Dimensions, back.Layers);
        var (min, max) = back.Extents;
        var (dmin, dmax) = d.Extents;
        Assert.InRange(max.X - min.X, (dmax.X - dmin.X) * 0.8, (dmax.X - dmin.X) * 1.2);
        // Los muros exportados se pueden volver a calcar y salen como muros.
        Assert.NotEmpty(WallDetector.Detect(back.Segments));
    }

    [Fact]
    public void Several_levels_go_side_by_side_in_dxf_and_one_page_each_in_pdf()
    {
        var doc = BuildingTemplates.Hotel();
        var drawings = doc.Levels.Select(l => PlanDrawing.Build(doc, l.Id)).Where(d => d.Items.Count > 0).ToList();
        Assert.True(drawings.Count >= 2);
        var one = DxfReader.Parse(PlanExport.ToDxf([drawings[0]]));
        var all = DxfReader.Parse(PlanExport.ToDxf(drawings));
        Assert.True(all.Extents.Max.X - all.Extents.Min.X > (one.Extents.Max.X - one.Extents.Min.X) * 1.8);

        var pdf = Encoding.Latin1.GetString(PlanExport.ToPdf(drawings, Paper.A3));
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        Assert.Contains($"/Count {drawings.Count}", pdf);
        Assert.Contains("(Escala 1:", pdf);
    }

    [Fact]
    public void Pdf_xref_offsets_point_at_objects()
    {
        var doc = Casa();
        var bytes = PlanExport.ToPdf([PlanDrawing.Build(doc, Level0(doc))], Paper.A4);
        var pdf = Encoding.Latin1.GetString(bytes);
        int xref = int.Parse(pdf[(pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", pdf[xref..]);
        var lines = pdf[xref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1]);
        for (int i = 1; i < count; i++)
        {
            int off = int.Parse(lines[2 + i][..10]);
            Assert.StartsWith($"{i} 0 obj", pdf[off..]);
        }
    }

    [Fact]
    public void Auto_scale_picks_the_largest_scale_that_fits()
    {
        var doc = Casa();
        var d = PlanDrawing.Build(doc, Level0(doc));
        int a4 = PlanExport.AutoScale(d, Paper.A4);
        int a1 = PlanExport.AutoScale(d, Paper.A1);
        Assert.Contains(a4, PlanExport.StandardScales);
        Assert.True(a1 < a4); // en papel más grande cabe a una escala mayor (1:N con N menor)
    }
}
