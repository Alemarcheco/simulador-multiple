using Simulador.Tableros.Import;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Tests;

public class PlanImportTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plano-ejemplo.pdf");

    [Fact]
    public void LeeElTextoDelPdfConPosiciones()
    {
        var plan = PlanText.ReadPdf(Fixture);
        Assert.Equal(5, plan.Pages.Count);
        var bom = plan.Pages[2];
        Assert.Contains("lista de materiales", bom.Text);
        var line = bom.Lines().First(l => l.Any(w => w.Text == "-KM1"));
        Assert.Contains(line, w => w.Text == "Contactor");
        Assert.True(line.Select(w => w.X).SequenceEqual(line.Select(w => w.X).OrderBy(x => x)));
    }

    [Fact]
    public void ReconoceLosAparatosConSuTipoYDatosDeLaListaDeMateriales()
    {
        var doc = PlanImporter.Import(Fixture).Document;
        string Kind(string tag) => doc.FindTag(tag)!.Kind;
        Assert.Equal("magnetotermico", Kind("Q1"));
        Assert.Equal("guardamotor", Kind("QM1"));
        Assert.Equal("contactor", Kind("KM1"));
        Assert.Equal("seta", Kind("S0"));
        Assert.Equal("pulsador", Kind("S1"));
        Assert.Equal("piloto", Kind("H1"));
        Assert.Equal("motor", Kind("M1"));
        Assert.Equal("captador", Kind("B1"));
        Assert.Equal("regleta", Kind("X1"));

        var q1 = doc.FindTag("Q1")!;
        Assert.Equal(16, q1.RatedA);
        Assert.Equal(3 * DeviceLibrary.Module, q1.Width);
        Assert.Equal("MT-3P-C16", q1.Reference);
        Assert.Equal(14, doc.FindTag("QM1")!.RatedA);
        Assert.Equal(DeviceLocation.Door, doc.FindTag("S0")!.Location);
        Assert.Equal(DeviceLocation.Field, doc.FindTag("M1")!.Location);
        // «A4» (formato de hoja) y «2.3» (referencia de columna) no son aparatos.
        Assert.Null(doc.FindTag("A4"));
    }

    [Fact]
    public void SacaLasConexionesDeLasListasDeBornesYDeCables()
    {
        var doc = PlanImporter.Import(Fixture).Document;
        bool Wired(string a, string b) => doc.Wires.Any(w => w.From.ToString() == a && w.To.ToString() == b || w.From.ToString() == b && w.To.ToString() == a);
        Assert.True(Wired("KM1:2", "X1:1"));
        Assert.True(Wired("KM1:1", "QM1:2"));
        Assert.True(Wired("H1:X1", "KM1:14"));

        var x1 = doc.FindTag("X1")!;
        Assert.Equal(10, x1.Terminals.Count / 2);
        var km = doc.FindTag("KM1")!;
        Assert.Equal(TerminalSide.Top, km.Terminal("A1")!.Side);
        Assert.Equal(TerminalSide.Bottom, km.Terminal("A2")!.Side);

        var power = doc.Wires.First(w => w.Number == "11");
        Assert.Equal(WireKind.Potencia, power.Kind);
        Assert.Equal(2.5, power.Section);
        Assert.Equal(WireKind.Neutro, doc.Wires.First(w => w.From.Tag == "H2" && w.From.Terminal == "X2" || w.To.Tag == "H2" && w.To.Terminal == "X2").Kind);
        Assert.Equal(WireKind.Mando, doc.Wires.First(w => w.Number == "23").Kind);
        Assert.Equal(WireKind.Tierra, doc.Wires.Single(w => w.From.Tag == "PE" || w.To.Tag == "PE").Kind);

        var motor = Assert.Single(doc.Cables, c => c.Target == "M1");
        Assert.Equal(3, motor.Cores);
        Assert.True(motor.WithPe);
    }

    [Fact]
    public void ProponeUnTableroCompletoSinErrores()
    {
        var r = PlanImporter.Import(Fixture);
        var doc = r.Document;
        Assert.Contains(AutoLayout.StandardSizes, s => s.W == doc.Enclosure.Width && s.H == doc.Enclosure.Height);
        Assert.NotEmpty(doc.Rails);
        Assert.NotEmpty(doc.Ducts);
        var routes = DuctRouter.Route(doc);
        Assert.All(routes, rt => Assert.True(rt.Wire.Length > 0));
        var checks = PanelChecks.Run(doc, routes);
        Assert.DoesNotContain(checks, c => c.Level == CheckLevel.Error);
        Assert.Contains(checks, c => c.Message.StartsWith("Plano PDF:"));

        // Se guarda y se vuelve a abrir igual, con su informe.
        var back = PanelDocument.FromJson(doc.ToJson())!;
        Assert.Equal(doc.Devices.Count, back.Devices.Count);
        Assert.Equal(doc.Wires.Count, back.Wires.Count);
        Assert.Equal(r.Notes, back.ImportNotes);
        // Al rehacer el cableado (sin esquema) los hilos leídos se conservan.
        DuctRouter.Rebuild(back);
        Assert.Equal(doc.Wires.Count, back.Wires.Count);
    }

    [Fact]
    public void UnPdfEscaneadoSinTextoLoDiceEnElInforme()
    {
        var plan = new PlanText { FileName = "escaneado.pdf" };
        plan.Pages.Add(new PlanPage { Number = 1, Width = 842, Height = 595 });
        var r = PlanImporter.Import(plan);
        Assert.Empty(r.Document.Devices);
        Assert.Contains(r.Notes, n => n.Contains("OCR"));
    }

    [Fact]
    public void EsquemaSinListasColocaLosAparatosSinHilos()
    {
        var plan = new PlanText { FileName = "solo-esquema.pdf" };
        var words = new List<PlanWord>();
        var x = 50.0;
        foreach (var t in new[] { "-Q1", "C10", "-KM1", "-KM2", "-S1", "-H1", "-M1", "A3", "Q7" })
        {
            words.Add(new PlanWord(t, x, 100, 20, 8));
            x += 60;
        }
        plan.Pages.Add(new PlanPage { Number = 1, Width = 1190, Height = 842, Words = words });
        var r = PlanImporter.Import(plan);
        var tags = r.Document.Devices.Select(d => d.Tag).ToHashSet();
        Assert.Superset(new HashSet<string> { "Q1", "KM1", "KM2", "S1", "H1", "M1" }, tags);
        Assert.DoesNotContain("A3", tags);
        Assert.DoesNotContain("Q7", tags); // sin guion y una sola vez: no se toma
        Assert.Empty(r.Document.Wires);
        Assert.Contains(r.Notes, n => n.Contains("sin hilos"));
    }

    [Fact]
    public void LeeLasEtiquetasDeHiloDeSolidWorksElectrical()
    {
        // Como sale del OCR de una hoja de SolidWorks Electrical: nomenclatura americana y «origen / destino» en cada hilo.
        var plan = new PlanText { FileName = "lazos.pdf" };
        var words = new List<PlanWord>();
        void Line(double y, params string[] texts)
        {
            var x = 40.0;
            foreach (var t in texts)
            {
                words.Add(new PlanWord(t, x, y, t.Length * 4, 6));
                x += t.Length * 4 + 4;
            }
        }
        Line(50, "CB120");
        Line(58, "A63", "Braker", "Principal");
        Line(100, "CB1(3)", "/", "CB120(4)");
        Line(120, "TB-120L-1(1)", "/", "CB1(4)");
        Line(140, "F1(L)", "/", "TB-120L-1(1),", "TB-120L-1(1)", "/", "CB1(4)");
        Line(160, "CB1");
        Line(168, "6", "AMP");
        Line(200, "CB2", "(3)", "/", "CB120(", "4)"); // partido por el OCR
        Line(220, "CBL(4)", "/", "CB2(4)"); // ruido del OCR: «CBL» sin número, una sola vez
        plan.Pages.Add(new PlanPage { Number = 13, Width = 1190, Height = 842, Words = words });

        var r = PlanImporter.Import(plan);
        var doc = r.Document;
        Assert.Equal("magnetotermico", doc.FindTag("CB1")!.Kind);
        Assert.Equal(6, doc.FindTag("CB1")!.RatedA);
        Assert.Equal(63, doc.FindTag("CB120")!.RatedA);
        var tb = doc.FindTag("TB-120L")!;
        Assert.Equal("regleta", tb.Kind);
        Assert.NotNull(tb.Terminal("1"));
        Assert.Null(doc.FindTag("CBL"));
        // CB1:3, CB120:4 y CB2:3 son el mismo potencial; F1:L, TB-120L:1 y CB1:4 otro.
        bool Same(string a, string b) => doc.Wires.Where(w => w.From.ToString() == a || w.To.ToString() == a).Select(w => w.Number)
            .Intersect(doc.Wires.Where(w => w.From.ToString() == b || w.To.ToString() == b).Select(w => w.Number)).Any();
        Assert.True(Same("CB1:3", "CB2:3"));
        Assert.True(Same("F1:L", "CB1:4"));
        Assert.False(Same("CB1:3", "CB1:4"));
        Assert.Contains(r.Notes, n => n.Contains("etiquetas de los hilos"));
    }

    [Theory]
    [InlineData("TB-120N-1", "1", "TB-120N", "1")]
    [InlineData("TB-120L-5", "5", "TB-120L", "5")]
    [InlineData("CB1", "3", "CB1", "3")]
    [InlineData("F1", "N", "F1", "N")]
    public void UnBorneDeRegletaSeNombraPorSuRegleta(string tag, string term, string strip, string label) =>
        Assert.Equal(new WireEnd(strip, label), PlanImporter.NormalizeRef(tag, term));

    [Theory]
    [InlineData("1", TerminalSide.Top)]
    [InlineData("14", TerminalSide.Bottom)]
    [InlineData("A1", TerminalSide.Top)]
    [InlineData("A2", TerminalSide.Bottom)]
    [InlineData("T2", TerminalSide.Bottom)]
    [InlineData("L3", TerminalSide.Top)]
    public void BornesDeEntradaArribaYDeSalidaAbajo(string label, TerminalSide side) => Assert.Equal(side, PlanImporter.SideFor(label));
}
