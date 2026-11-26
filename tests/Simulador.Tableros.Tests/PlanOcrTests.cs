using Simulador.Tableros.Import;

namespace Simulador.Tableros.Tests;

/// <summary>Planos con las letras dibujadas (fuentes SHX o escaneados): se leen con OCR.</summary>
public class PlanOcrTests
{
    private static string Drawn => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plano-dibujado.pdf");

    private sealed class FakeOcr : IPlanOcr
    {
        public string Name => "OCR de prueba";
        public bool IsAvailable => true;
        public List<int> Asked { get; } = [];

        public IEnumerable<(int Page, List<PlanWord> Words)> Recognize(string pdfPath, IReadOnlyList<int> pages, CancellationToken ct = default)
        {
            Asked.AddRange(pages);
            foreach (var p in pages)
                yield return (p, [new("CB1(3)", 100, 100, 30, 8), new("/", 134, 100, 4, 8), new("CB120(4)", 142, 100, 36, 8)]);
        }
    }

    [Fact]
    public void SoloPasaPorElOcrLasPaginasSinTexto()
    {
        Assert.True(PlanText.ReadPdf(Drawn).Pages[0].Words.Count < PlanText.OcrThreshold);
        var ocr = new FakeOcr();
        var plan = PlanText.ReadPdf(Drawn, ocr);
        Assert.Equal([1], ocr.Asked);
        Assert.Equal([1], plan.OcrPages);
        Assert.Equal("OCR de prueba", plan.OcrName);
        Assert.Contains(plan.Pages[0].Words, w => w.Text == "CB120(4)");

        // El plano con texto no pasa por el OCR.
        var ocr2 = new FakeOcr();
        PlanText.ReadPdf(Path.Combine(AppContext.BaseDirectory, "Fixtures", "plano-ejemplo.pdf"), ocr2);
        Assert.DoesNotContain(2, ocr2.Asked);
    }

    [Fact]
    public void SinReconocedorElInformeLoDice()
    {
        var r = PlanImporter.Import(Drawn, null);
        Assert.Empty(r.Document.Devices);
        Assert.Contains(r.Notes, n => n.Contains("OCR"));
    }

    /// <summary>OCR de Windows (en la integración continua, que corre en Windows) o Tesseract (si está instalado).</summary>
    [Fact]
    public void ElOcrDelSistemaLeeLasLetrasDibujadas()
    {
        foreach (IPlanOcr ocr in new IPlanOcr[] { new WindowsOcr(), new TesseractOcr() })
        {
            if (!ocr.IsAvailable) continue;
            List<PlanWord> words;
            try
            {
                words = ocr.Recognize(Drawn, [1]).Single().Words;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("idioma"))
            {
                continue; // Windows sin paquete de idioma de OCR (algunas imágenes de servidor)
            }
            var text = string.Join(' ', words.Select(w => w.Text));
            Assert.Contains("CB120", text);
            Assert.Contains("ALIMENTACION", text);
            // Las coordenadas vuelven en puntos PDF: el texto empieza a 300 px de 200 ppp = 108 pt.
            var first = words.First(w => w.Text.StartsWith("ALIMENTACION"));
            Assert.InRange(first.X, 90, 125);

            var r = PlanImporter.Import(Drawn, ocr);
            Assert.NotNull(r.Document.FindTag("CB1"));
            Assert.NotNull(r.Document.FindTag("CB120"));
            Assert.NotEmpty(r.Document.Wires);
        }
    }
}
