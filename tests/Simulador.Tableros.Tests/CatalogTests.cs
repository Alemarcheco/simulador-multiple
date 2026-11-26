using Simulador.Tableros.Import;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Tests;

public class CatalogTests
{
    [Fact]
    public void LaLibreriaTieneLasCuatroMarcasYCadaModeloSuTipo()
    {
        foreach (var brand in new[] { "ABB", "Allen-Bradley", "Schneider Electric", "Woodward" })
            Assert.NotEmpty(ManufacturerCatalog.OfBrand(brand));
        foreach (var i in ManufacturerCatalog.All)
        {
            Assert.Equal(i.Kind, DeviceLibrary.Get(i.Kind).Kind);
            Assert.True(i.Width > 0 && i.Height > 0 && i.Depth > 0, i.Reference);
            Assert.False(string.IsNullOrWhiteSpace(i.Source));
        }
        Assert.Equal(ManufacturerCatalog.All.Count, ManufacturerCatalog.All.Select(i => i.FullReference).Distinct().Count());
    }

    [Fact]
    public void BuscaPorReferenciaYPoneLasMedidasDeLaFicha()
    {
        var item = ManufacturerCatalog.Find("Allen-Bradley 1606-XLS240E");
        Assert.NotNull(item);
        Assert.Same(item, ManufacturerCatalog.Find("1606 xls240e"));
        var d = DeviceLibrary.Create("fuente", "G1");
        ManufacturerCatalog.Apply(d, item!);
        Assert.Equal((60.0, 124.0, 117.0), (d.Width, d.Height, d.Depth));
        Assert.Equal("Allen-Bradley 1606-XLS240E", d.Reference);
        Assert.True(item!.Verified);
        Assert.Null(ManufacturerCatalog.Find("XYZ-123"));
    }

    private static PlanPage Page(int n, params (string Text, double X, double Y)[] words) =>
        new() { Number = n, Width = 1190, Height = 842, Words = words.Select(w => new PlanWord(w.Text, w.X, w.Y, 30, 8)).ToList() };

    [Fact]
    public void AsociaLosAparatosPorElTextoQueTienenAlLado()
    {
        var doc = new PanelDocument();
        doc.Devices.Add(DeviceLibrary.Create("fusible", "F2"));
        foreach (var t in new[] { "TPS-A", "TPS-B", "TPS-C", "ALARMA" }) doc.Devices.Add(DeviceLibrary.Create("aparato", t));
        var plan = new PlanText();
        plan.Pages.Add(Page(1, ("F2", 100, 100), ("1606-XLS", 100, 130), ("POWER", 100, 140)));
        plan.Pages.Add(Page(2, ("TPS-A", 100, 100), ("PROTECH", 140, 100), ("TPS-B", 400, 100), ("PROTECH", 440, 100),
            ("TPS-C", 700, 100), ("PROTECH", 740, 100), ("ALARMA", 100, 400), ("505D", 140, 400)));
        plan.Pages.Add(Page(3, ("TPS-A", 100, 100), ("PROTECH", 140, 100), ("TPS-B", 400, 100), ("PROTECH", 440, 100),
            ("TPS-C", 700, 100), ("PROTECH", 740, 100), ("ALARMA", 100, 400), ("505D", 140, 400), ("505D", 900, 700)));

        var m = CatalogMatcher.Apply(plan, doc);

        // La fuente dibujada como «F2» con «1606-XLS» al lado era una fuente, no un fusible.
        var f2 = doc.FindTag("F2")!;
        Assert.Equal("fuente", f2.Kind);
        Assert.Equal(60, f2.Width);
        // Los tres módulos del ProTech-TPS van en la puerta y se reparten su frente de 330 mm.
        foreach (var t in new[] { "TPS-A", "TPS-B", "TPS-C" })
        {
            var d = doc.FindTag(t)!;
            Assert.Equal(DeviceLocation.Door, d.Location);
            Assert.Equal(110, d.Width);
            Assert.Contains("ProTech", d.Description);
        }
        // La alarma que se dibuja junto al 505D no es el 505D: el controlador se añade aparte con su medida.
        Assert.Equal("aparato", doc.FindTag("ALARMA")!.Kind);
        var c505 = doc.FindTag("505D");
        Assert.NotNull(c505);
        Assert.Equal((356.0, 279.0), (c505!.Width, c505.Height));
        Assert.Contains(m, x => x.Tag == "505D");
    }

    [Fact]
    public void EnLaPuertaLosEquiposGrandesNoSeMontanUnosEncimaDeOtros()
    {
        var doc = PanelBuilder.Examples[0].Create();
        foreach (var r in new[] { "505D", "ProTech-TPS" })
        {
            var d = DeviceLibrary.Create("controlador", r == "505D" ? "A50" : "A51");
            ManufacturerCatalog.Apply(d, ManufacturerCatalog.Find(r)!);
            doc.Devices.Add(d);
        }
        AutoLayout.Arrange(doc);
        var door = doc.OnDoor.ToList();
        foreach (var a in door)
        foreach (var b in door)
            if (a != b) Assert.False(a.Overlaps(b), $"{a.Tag} y {b.Tag}");
        Assert.All(door, d => Assert.True(d.X >= 0 && d.X + d.Width <= doc.Enclosure.Width && d.Y + d.Height <= doc.Enclosure.Height, d.Tag));
    }
}
