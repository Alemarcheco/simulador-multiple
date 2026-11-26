using UglyToad.PdfPig;

namespace Simulador.Tableros.Import;

/// <summary>Palabra de una página con su posición (puntos PDF, origen arriba a la izquierda, Y hacia abajo).</summary>
public sealed record PlanWord(string Text, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double CenterY => Y + Height / 2;
}

/// <summary>Una página del plano: sus palabras y medidas.</summary>
public sealed class PlanPage
{
    public int Number { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public List<PlanWord> Words { get; init; } = [];

    /// <summary>Todo el texto de la página en minúsculas, para buscar títulos («lista de materiales»…).</summary>
    public string Text => _text ??= string.Join(' ', Words.Select(w => w.Text)).ToLowerInvariant();
    private string? _text;

    /// <summary>Palabras agrupadas en renglones (misma altura con tolerancia), de arriba abajo y de izquierda a derecha.</summary>
    public List<List<PlanWord>> Lines()
    {
        var lines = new List<List<PlanWord>>();
        foreach (var w in Words.OrderBy(w => w.CenterY).ThenBy(w => w.X))
        {
            var tol = Math.Max(2, w.Height * 0.45);
            var line = lines.LastOrDefault(l => Math.Abs(l.Average(x => x.CenterY) - w.CenterY) <= tol);
            if (line is null) lines.Add([w]);
            else line.Add(w);
        }
        foreach (var l in lines) l.Sort((a, b) => a.X.CompareTo(b.X));
        return lines;
    }
}

/// <summary>Texto de un plano en PDF, página a página.</summary>
public sealed class PlanText
{
    public List<PlanPage> Pages { get; } = [];
    public string FileName { get; init; } = "";

    /// <summary>
    /// Lee un PDF con texto (exportado por un programa de CAD eléctrico). Los PDF escaneados (solo imagen) no tienen
    /// texto: salen páginas vacías y el informe lo avisa.
    /// </summary>
    public static PlanText ReadPdf(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadPdf(stream, Path.GetFileName(path));
    }

    public static PlanText ReadPdf(Stream stream, string name)
    {
        var plan = new PlanText { FileName = name };
        using var pdf = PdfDocument.Open(stream);
        foreach (var page in pdf.GetPages())
        {
            var h = page.Height;
            var words = new List<PlanWord>();
            foreach (var w in page.GetWords())
            {
                var t = w.Text.Trim();
                if (t.Length == 0) continue;
                var b = w.BoundingBox;
                // Texto girado 90° (rótulos verticales): se toma igual, con su caja.
                words.Add(new PlanWord(t, b.Left, h - b.Top, Math.Max(0.1, b.Width), Math.Max(1, b.Height)));
            }
            plan.Pages.Add(new PlanPage { Number = page.Number, Width = page.Width, Height = h, Words = words });
        }
        return plan;
    }
}
