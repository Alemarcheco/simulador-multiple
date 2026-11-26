using Simulador.Tableros.Import;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Tests;

public class DoubleFrontTests
{
    private static PanelDocument DoubleFront(int example = 6)
    {
        var doc = PanelBuilder.Examples[example].Create();
        doc.Enclosure.DoubleFront = true;
        Assert.True(AutoLayout.Arrange(doc));
        DuctRouter.Rebuild(doc);
        return doc;
    }

    [Fact]
    public void ReparteLosAparatosEntreLasDosCarasConSusCarrilesYCanaletas()
    {
        var doc = DoubleFront();
        var e = doc.Enclosure;
        Assert.Contains(AutoLayout.DoubleFrontSizes, s => s.W == e.Width && s.H == e.Height && s.D == e.Depth);
        Assert.NotEmpty(doc.PlateOf(false));
        Assert.NotEmpty(doc.PlateOf(true));
        Assert.NotEmpty(doc.RailsOf(true));
        Assert.NotEmpty(doc.DuctsOf(true));
        // Cada aparato de carril está sobre un carril de su misma cara, y nada choca con la otra cara.
        foreach (var d in doc.OnPlate.Where(d => d.OnRail))
            Assert.Contains(doc.RailsOf(d.Rear), r => d.X >= r.X - 1 && d.X + d.Width <= r.X + r.Length + 1 && Math.Abs(r.CenterY - (d.Y + d.Height / 2)) < 1);
        var checks = PanelChecks.Run(doc, DuctRouter.Rebuild(doc));
        Assert.DoesNotContain(checks, c => c.Level == CheckLevel.Error);
        // Las puertas: los mandos van en la delantera.
        Assert.All(doc.OnDoor, d => Assert.False(d.Rear));
    }

    [Fact]
    public void LosHilosQueCambianDeCaraPasanPorElPasoEntrePlacas()
    {
        var doc = DoubleFront();
        var routes = DuctRouter.Rebuild(doc);
        var cross = routes.Where(r => doc.FindTag(r.Wire.From.Tag)!.Rear != doc.FindTag(r.Wire.To.Tag)!.Rear
                                      && doc.FindTag(r.Wire.From.Tag)!.Location == DeviceLocation.Plate
                                      && doc.FindTag(r.Wire.To.Tag)!.Location == DeviceLocation.Plate).ToList();
        Assert.NotEmpty(cross);
        foreach (var r in cross)
        {
            Assert.True(r.Points.Count >= 2);
            Assert.True(r.RearPoints.Count >= 2);
            Assert.Contains(DuctRouter.CrossPoint, r.Points);
            Assert.Contains(DuctRouter.CrossPoint, r.RearPoints);
            Assert.True(r.Wire.Length > DuctRouter.CrossLength(doc));
        }
    }

    [Fact]
    public void SeGuardaYSeVuelveAAbrirConLasDosCaras()
    {
        var doc = DoubleFront();
        var back = PanelDocument.FromJson(doc.ToJson());
        Assert.True(back.Enclosure.DoubleFront);
        Assert.Equal(doc.PlateOf(true).Select(d => d.Tag), back.PlateOf(true).Select(d => d.Tag));
        Assert.Equal(doc.RailsOf(true).Count(), back.RailsOf(true).Count());
        Assert.Equal(doc.DuctsOf(true).Count(), back.DuctsOf(true).Count());
    }

    [Fact]
    public void AlQuitarElDobleFrenteTodoVuelveALaCaraDelantera()
    {
        var doc = DoubleFront();
        doc.Enclosure.DoubleFront = false;
        AutoLayout.Arrange(doc);
        Assert.DoesNotContain(doc.Devices, d => d.Rear);
        Assert.DoesNotContain(doc.Rails, r => r.Rear);
        Assert.DoesNotContain(doc.Ducts, k => k.Rear);
        Assert.Contains(AutoLayout.StandardSizes, s => s.W == doc.Enclosure.Width && s.H == doc.Enclosure.Height);
    }

    [Fact]
    public void La3DDibujaLasDosPlacasYLasDosPuertas()
    {
        var single = PanelBuilder.Examples[6].Create();
        AutoLayout.Arrange(single);
        var a = PanelScene.Build(single, DuctRouter.Rebuild(single), new PanelSceneOptions(110, true, true, true));
        var doc = DoubleFront();
        var b = PanelScene.Build(doc, DuctRouter.Rebuild(doc), new PanelSceneOptions(110, true, true, true));
        Assert.True(b.Triangles.Count > a.Triangles.Count);
        // Hay piezas detrás de la mitad del fondo (la placa trasera y su puerta).
        var half = (float)(doc.Enclosure.Depth / 2000);
        Assert.Contains(b.Triangles, t => t.A.Y > half + 0.01f);
    }

    [Fact]
    public void ElImportadorProponeDobleFrenteSiSeLePide()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plano-ejemplo.pdf");
        var r = PlanImporter.Import(PlanText.ReadPdf(path), doubleFront: true);
        Assert.True(r.Document.Enclosure.DoubleFront);
        Assert.Contains(r.Notes, n => n.Contains("doble frente"));
        var single = PlanImporter.Import(PlanText.ReadPdf(path));
        Assert.False(single.Document.Enclosure.DoubleFront); // el ejemplo es pequeño: cabe a una cara
    }
}
