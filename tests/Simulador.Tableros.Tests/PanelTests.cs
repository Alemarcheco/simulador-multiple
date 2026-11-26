using Simulador.Electrico.Engine.Model;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Tests;

public class PanelTests
{
    [Fact]
    public void ElArranqueDirectoJuntaBobinaYContactosEnUnSoloContactor()
    {
        var a = SchematicImport.Analyze(Examples.DirectOnLine());
        var km = Assert.Single(a.Devices, d => d.Tag == "KM1");
        Assert.Equal("contactor", km.Kind);
        foreach (var t in new[] { "1", "2", "3", "4", "5", "6", "13", "14", "A1", "A2" }) Assert.NotNull(km.Terminal(t));
        Assert.Contains("bobina 230 V AC", km.Description);

        Assert.Equal(DeviceLocation.Door, a.Devices.Single(d => d.Tag == "S0").Location);
        Assert.Equal("seta", a.Devices.Single(d => d.Tag == "S0").Kind);
        Assert.Equal(DeviceLocation.Door, a.Devices.Single(d => d.Tag == "H1").Location);
        Assert.Equal(DeviceLocation.Field, a.Devices.Single(d => d.Tag == "M1").Location);

        // El motor sale por la regleta X1 con tres bornes y un cable 4G (tres fases + PE).
        var x1 = a.Devices.Single(d => d.Tag == SchematicImport.FieldStrip);
        Assert.Equal(3, x1.Terminals.Count / 2);
        var cable = Assert.Single(a.Cables, c => c.Target == "M1");
        Assert.Equal(3, cable.Cores);
        Assert.StartsWith("4G", cable.Designation);
        // La alimentación entra por X0 con sus nombres de fase.
        var x0 = a.Devices.Single(d => d.Tag == SchematicImport.SupplyStrip);
        Assert.Contains(x0.Terminals, t => t.Label == "L1");
    }

    [Fact]
    public void LosPotencialesTienenTipoSeccionYColor()
    {
        var a = SchematicImport.Analyze(Examples.DirectOnLine());
        var motor = a.Nets.Single(n => n.Ends.Contains(new WireEnd("KM1", "2")));
        Assert.Equal(WireKind.Potencia, motor.Kind);
        Assert.True(motor.Section >= 1.5);
        var coil = a.Nets.Single(n => n.Ends.Contains(new WireEnd("KM1", "A1")));
        Assert.Equal(WireKind.Mando, coil.Kind);
        Assert.Equal(1, coil.Section);
        Assert.Equal("Rojo", SchematicImport.ColorFor(coil.Kind));
        Assert.Contains(a.Nets, n => n.Number == "L1");
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void LosEjemplosSalenSinErrores(string name)
    {
        var doc = PanelBuilder.Examples.Single(e => e.Name == name).Create();
        var routes = DuctRouter.Route(doc);
        var errors = PanelChecks.Run(doc, routes).Where(c => c.Level == CheckLevel.Error).Select(c => c.Message).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        // Los mandos de la puerta no caen delante de un aparato hondo (la puerta cerraría mal).
        Assert.DoesNotContain(doc.OnDoor, d => AutoLayout.ClashesBehind(doc, d));
        Assert.NotEmpty(doc.Wires);
        Assert.All(doc.Wires, w => Assert.True(w.Length > 2 * DuctRouter.EndAllowance, $"{w.From}→{w.To}"));
        // Todo lo de la placa está dentro de ella y ningún aparato pisa a otro.
        Assert.All(doc.OnPlate, d => Assert.True(d.X >= 0 && d.X + d.Width <= doc.Enclosure.PlateWidth, d.Tag));
    }

    public static TheoryData<string> ExampleNames()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in PanelBuilder.Examples) data.Add(name);
        return data;
    }

    [Fact]
    public void ElRecorridoVaPorLasCanaletas()
    {
        var doc = PanelBuilder.Examples[0].Create();
        var routes = DuctRouter.Route(doc);
        var r = routes.First(x => x.Wire.From.Tag != x.Wire.To.Tag && !x.FromDoor && !x.ToDoor);
        Assert.NotEmpty(r.Ducts);
        // Tramos horizontales o verticales.
        for (var i = 0; i + 1 < r.Points.Count; i++)
            Assert.True(Math.Abs(r.Points[i].X - r.Points[i + 1].X) < 0.01 || Math.Abs(r.Points[i].Y - r.Points[i + 1].Y) < 0.01);
        var fill = DuctRouter.Fill(doc, routes);
        Assert.Contains(fill.Values, f => f > 0);
    }

    [Fact]
    public void ActualizarDesdeElEsquemaConservaLaPosicionYAnadeLoNuevo()
    {
        var schematic = Examples.DirectOnLine();
        var doc = PanelBuilder.FromSchematic(schematic);
        var q1 = doc.FindTag("Q1")!;
        q1.Reference = "REF-123";
        var (qx, qy) = (q1.X, q1.Y);

        var changed = schematic.Clone();
        changed.Elements.Add(new PlacedElement { Type = "bobina_rele", Tag = "KA9", X = 40, Y = 30 });
        var msg = PanelBuilder.UpdateFromSchematic(doc, changed);

        Assert.Contains("KA9", msg);
        Assert.NotNull(doc.FindTag("KA9"));
        Assert.Equal((qx, qy), (doc.FindTag("Q1")!.X, doc.FindTag("Q1")!.Y));
        Assert.Equal("REF-123", doc.FindTag("Q1")!.Reference);
    }

    [Fact]
    public void SeGuardaYSeAbreIgual()
    {
        var doc = PanelBuilder.Examples[1].Create();
        var again = PanelDocument.FromJson(doc.ToJson());
        Assert.Equal(doc.Devices.Count, again.Devices.Count);
        Assert.Equal(doc.Wires.Count, again.Wires.Count);
        Assert.Equal(doc.ToJson(), again.ToJson());
    }

    [Fact]
    public void ElCalculoTermicoPideVentiladorConMuchoCalor()
    {
        var doc = new PanelDocument();
        var vfd = DeviceLibrary.Create("variador", "U1");
        vfd.HeatW = 300;
        doc.Devices.Add(vfd);
        var th = PanelChecks.Thermal(doc);
        Assert.True(th.InsideC > doc.Enclosure.MaxInsideC);
        Assert.True(th.FanNeededM3h > 0);
        PanelBuilder.SuggestFan(doc);
        Assert.True(PanelChecks.Thermal(doc).InsideC <= doc.Enclosure.MaxInsideC + 0.01);
    }

    [Fact]
    public void LaListaDeMaterialesLlevaMontajeYCableado()
    {
        var doc = PanelBuilder.Examples[0].Create();
        var bom = Reports.BillOfMaterials(doc);
        var text = string.Join("\n", bom.Rows.Select(r => r[4]));
        Assert.Contains("Carril DIN", text);
        Assert.Contains("Canaleta", text);
        Assert.Contains("H07V-K", text);
        Assert.Contains("Contactor", text);
        Assert.Contains("Borne de paso", text);
        Assert.Contains(";", bom.ToCsv());
        Assert.NotEmpty(Reports.WireList(doc).Rows);
        Assert.NotEmpty(Reports.TerminalPlan(doc).Rows);
    }

    [Fact]
    public void LaEscena3DTieneAparatosHilosYEtiquetas()
    {
        var doc = PanelBuilder.Examples[0].Create();
        var routes = DuctRouter.Route(doc);
        var scene = PanelScene.Build(doc, routes, new PanelSceneOptions());
        Assert.True(scene.Triangles.Count > 500);
        Assert.NotEmpty(scene.Lines);
        Assert.Contains(scene.Labels, l => l.Text == "KM1");
        Assert.True(scene.Max.Z > 0.3f);
    }

    [Fact]
    public void LasDesignacionesSeOrdenanPorNumero()
    {
        var tags = new[] { "KM10", "KM2", "F1", "KM1" }.OrderBy(t => t, TagComparer.Instance).ToArray();
        Assert.Equal(["F1", "KM1", "KM2", "KM10"], tags);
    }
}
