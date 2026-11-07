using Simulador.Cad3D.Geometry.Csg;
using Simulador.Cad3D.Geometry.Model;
using Simulador.Cad3D.Geometry.Templates;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Pcb;
using Simulador.Core.Libraries.Model;
using Simulador.Core.Pcb;

namespace Simulador.Cad3D.Tests;

public class BoardEnclosureTests
{
    private static FootprintDefinition MountingHole() => new()
    {
        Name = "MountingHole_3.2mm_M3",
        Pads = { new FootprintPad("", PadType.NpThroughHole, PadShape.Circle, new Vec2(0, 0), 0, new Vec2(3.2, 3.2), 3.2, null, new[] { "*.Cu" }) },
    };

    private static PcbDocument Board()
    {
        var catalog = new ComponentCatalog();
        var doc = new PcbDocument { Title = "Prueba", BoardWidth = 50, BoardHeight = 30 };
        doc.Footprints.Add(new PcbFootprint { Reference = "H1", FootprintName = "MountingHole_3.2mm_M3", Position = new Vec2(4, 4) });
        doc.Footprints.Add(new PcbFootprint { Reference = "H2", FootprintName = "MountingHole_3.2mm_M3", Position = new Vec2(46, 26) });
        // Bornera junto al borde inferior en el editor (Y hacia abajo): y = 28 → y = 2 en la caja.
        doc.Footprints.Add(new PcbFootprint { Reference = "J1", Value = "Bornera", FootprintName = "Basicos:Bornera_2_P5.08mm", Position = new Vec2(20, 28) });
        doc.Footprints.Add(new PcbFootprint { Reference = "D1", Value = "LED", FootprintName = "Basicos:LED_D5.0mm", Position = new Vec2(30, 15) });
        doc.ResolveFootprints(name => name.Contains("MountingHole") ? MountingHole() : catalog.FindFootprint(name));
        return doc;
    }

    [Fact]
    public void Placa_se_convierte_al_modelo_neutro_con_Y_hacia_arriba()
    {
        var b = PcbBoardExport.ToBoardExport(Board());
        Assert.Equal(4, b.Outline.Count);
        Assert.Equal((50.0, 30.0), (b.Bounds().Max.X, b.Bounds().Max.Y));
        var mounting = b.Holes.Where(h => h.ComponentRef is null).ToList();
        Assert.Equal(2, mounting.Count);
        Assert.Contains(mounting, h => h.Position == new PointMm(4, 26));
        Assert.DoesNotContain(b.Components, c => c.Reference.StartsWith('H'));
        var j1 = b.Components.Single(c => c.Reference == "J1");
        Assert.Equal(new PointMm(20, 2), j1.Position);
        Assert.Equal(10, j1.HeightMm);
        Assert.NotEmpty(b.ConnectorPins);
    }

    [Fact]
    public void Caja_a_medida_de_la_placa_tiene_postes_y_hueco_de_bornera()
    {
        var doc = EnclosureTemplate.FromBoard(PcbBoardExport.ToBoardExport(Board()));
        var box = doc.Parts[0];
        Assert.Equal(2, box.Features.Count(f => f.Name.StartsWith("Poste")));
        Assert.Contains(box.Features, f => f.Name == "Hueco J1");
        var r = new DocumentEvaluator(new MeshKernel()).Evaluate(box);
        Assert.Empty(r.Errors);
        Assert.Equal(0, r.Mesh!.CountOpenEdges());
    }

    [Fact]
    public void Hueco_del_conector_mide_lo_que_su_huella()
    {
        var board = PcbBoardExport.ToBoardExport(Board());
        var j1 = board.Components.Single(c => c.Reference == "J1");
        Assert.NotNull(j1.BoundsMin);
        Assert.NotNull(j1.BoundsMax);
        double width = j1.BoundsMax!.Value.X - j1.BoundsMin!.Value.X;
        Assert.InRange(width, 5, 15);
        Assert.InRange(j1.BoundsMin.Value.Y, -2, 4); // pegado al borde frontal

        var box = EnclosureTemplate.FromBoard(board).Parts[0];
        var hueco = Assert.IsType<BoxFeature>(box.Features.Single(f => f.Name == "Hueco J1"));
        Assert.Equal(width + 1, hueco.SizeX, 6);
        Assert.Equal(j1.BoundsMin.Value.X + 2 + 2 - 0.5, hueco.Position.X, 6); // pared 2 + margen 2 - holgura
        Assert.Equal(-1, hueco.Position.Y, 6); // pared frontal
        Assert.Equal(j1.HeightMm + 1, hueco.SizeZ, 6);
    }

    [Fact]
    public void Sin_huella_se_usa_el_hueco_generico()
    {
        var c = new BoardComponent("J9", "USB", "?", new PointMm(25, 1), 0, BoardSide.Top, 4, null);
        var board = new BoardExport("P", new[] { new PointMm(0, 0), new PointMm(50, 0), new PointMm(50, 30), new PointMm(0, 30) },
            Array.Empty<IReadOnlyList<PointMm>>(), 1.6, Array.Empty<BoardHole>(), new[] { c }, Array.Empty<BoardConnectorPin>());
        var hueco = Assert.IsType<BoxFeature>(EnclosureTemplate.FromBoard(board).Parts[0].Features.Single(f => f.Name == "Hueco J9"));
        Assert.Equal(12, hueco.SizeX, 6);
    }

    [Fact]
    public void Archivo_placa_se_abre_desde_disco()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prueba-{Guid.NewGuid():N}{PcbDocument.FileExtension}");
        try
        {
            File.WriteAllText(path, Board().ToJson());
            var (doc, missing) = Views.BoardEnclosure.FromPlacaFile(path, null);
            Assert.Equal("Caja para Prueba", doc.Name);
            // Las huellas de taladro no están en las librerías de serie, pero se reconocen por el nombre.
            Assert.Empty(missing);
            Assert.Equal(2, doc.Parts[0].Features.Count(f => f.Name.StartsWith("Poste")));
        }
        finally { File.Delete(path); }
    }
}
