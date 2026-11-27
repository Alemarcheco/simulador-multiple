using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Tests;

public class EnclosureCatalogTests
{
    [Fact]
    public void HayVariosFabricantesConVariosTamanosYMedidasCoherentes()
    {
        foreach (var brand in new[] { "Rittal", "Eaton", "nVent Hoffman", "Schneider Electric", "ABB" })
            Assert.True(EnclosureCatalog.OfBrand(brand).Count() >= 10, brand);
        foreach (var m in EnclosureCatalog.All)
        {
            Assert.True(m.PlateWidth < m.Width && m.PlateHeight < m.Height, m.FullReference);
            Assert.True(m.Depth >= 150 && m.Depth <= 800, m.FullReference);
            Assert.False(string.IsNullOrWhiteSpace(m.Source));
        }
        Assert.Equal(EnclosureCatalog.All.Count, EnclosureCatalog.All.Select(m => m.FullReference).Distinct().Count());
    }

    [Fact]
    public void LasMedidasLeidasEnLasFichasEstanBien()
    {
        // Rittal VX 8606.000: 600 × 2000 × 600 mm, placa 499 × 1896 mm (ficha de Rittal).
        var vx = EnclosureCatalog.Find("Rittal", "VX 8606.000")!;
        Assert.Equal((600.0, 2000.0, 600.0, 499.0, 1896.0), (vx.Width, vx.Height, vx.Depth, vx.PlateWidth, vx.PlateHeight));
        Assert.True(vx.FloorStanding);
        // Eaton CS-64/200: 600 × 400 × 200 mm (alto × ancho × fondo), placa 570 × 350 mm.
        var cs = EnclosureCatalog.Find("Eaton", "CS-64/200")!;
        Assert.Equal((600.0, 400.0, 570.0, 350.0), (cs.Height, cs.Width, cs.PlateHeight, cs.PlateWidth));
        // Schneider: placa NSYMM64 de 550 × 350 para la caja de 600 × 400.
        var crn = EnclosureCatalog.Find("Schneider Electric", "NSYCRN64200")!;
        Assert.Equal((550.0, 350.0), (crn.PlateHeight, crn.PlateWidth));
        // ABB no publica la medida útil de la placa: queda marcada como supuesta.
        Assert.All(EnclosureCatalog.OfBrand("ABB"), m => Assert.False(m.PlateOfficial));
        Assert.Contains("supuesta", EnclosureCatalog.OfBrand("ABB").First().Provenance);
    }

    [Fact]
    public void AlElegirUnModeloLaPlacaYLaPuertaSeAjustan()
    {
        var doc = new PanelDocument();
        var e = doc.Enclosure;
        EnclosureCatalog.Find("Rittal", "VX 8206.000")!.ApplyTo(e);
        Assert.Equal(1099, e.PlateWidth);
        Assert.Equal(1896, e.PlateHeight);
        Assert.Equal((1200 - 1099) / 2.0, e.PlateLeft);
        Assert.Equal(2, e.Doors);
        Assert.True(e.Plinth);
        Assert.Equal(EnclosureKind.ArmarioDePie, e.Kind);

        // Se guarda y se vuelve a abrir igual.
        var back = PanelDocument.FromJson(doc.ToJson()).Enclosure;
        Assert.Equal(("Rittal", "VX 8206.000", 1099.0, 2), (back.Brand, back.Reference, back.PlateWidth, back.Doors));

        // Cambiar la medida a mano lo deja a medida.
        e.Width = 1000;
        e.ClearCatalog();
        Assert.Null(e.Brand);
        Assert.Equal(1000 - 2 * e.PlateMargin, e.PlateWidth);
    }

    [Fact]
    public void LaVista3DDibujaDosHojasYZocalo()
    {
        var doc = PanelBuilder.Examples[0].Create();
        EnclosureCatalog.Find("Rittal", "VX 8206.000")!.ApplyTo(doc.Enclosure);
        AutoLayout.Arrange(doc, chooseEnclosure: false);
        var scene = PanelScene.Build(doc, null, new PanelSceneOptions());
        Assert.True(scene.Min.Z < -0.09f);
        Assert.Contains(scene.Triangles, t => t.A.Z < -0.05f);
        // Los mandos van en la hoja izquierda, no sobre la junta del centro.
        Assert.All(doc.OnDoor, d => Assert.True(d.X + d.Width < doc.Enclosure.Width / 2 - 15, d.Tag));
        Assert.DoesNotContain(PanelChecks.Run(doc), c => c.Message.Contains("junta"));
    }

    [Fact]
    public void ElAutoajusteEligeElModeloMasPequenoDeLaMarca()
    {
        var doc = PanelBuilder.Examples[0].Create();
        doc.Enclosure.Brand = "Eaton";
        doc.Enclosure.Reference = "CS-1412/400-2D";
        Assert.True(AutoLayout.Arrange(doc));
        var chosen = doc.Enclosure.CatalogModel;
        Assert.NotNull(chosen);
        Assert.Equal("Eaton", chosen!.Brand);
        // No hay uno más pequeño de la marca en el que también quepa (se prueban de menor a mayor).
        Assert.True(chosen.Width * chosen.Height <= 1400 * 1200);
        Assert.DoesNotContain(PanelChecks.Run(doc), c => c.Level == CheckLevel.Error && c.Message.Contains("fuera de la placa"));
    }
}
