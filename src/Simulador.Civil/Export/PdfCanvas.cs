using System.Globalization;
using System.Text;

namespace Simulador.Civil.Export;

/// <summary>
/// Escritor PDF mínimo y sin dependencias para planos: medidas en milímetros con el origen arriba a la izquierda
/// (Y hacia abajo), color RGB, líneas continuas o discontinuas, polígonos y texto Helvetica alineado y girado.
/// Los vectores conservan la escala al imprimir a "tamaño real".
/// </summary>
public sealed class PdfCanvas
{
    public const double PtPerMm = 72.0 / 25.4;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly List<StringBuilder> _pages = [];
    private StringBuilder _c = null!;

    public PdfCanvas(double pageWidthMm, double pageHeightMm)
    {
        PageWidthMm = pageWidthMm;
        PageHeightMm = pageHeightMm;
        NewPage();
    }

    public double PageWidthMm { get; }
    public double PageHeightMm { get; }
    public int PageCount => _pages.Count;

    public void NewPage()
    {
        _c = new StringBuilder("1 J 1 j\n");
        _pages.Add(_c);
    }

    private string P(double x, double y) => $"{F(x * PtPerMm)} {F((PageHeightMm - y) * PtPerMm)}";
    private static string F(double v) => v.ToString("0.###", Inv);

    public void Stroke(double r, double g, double b) => _c.Append($"{F(r)} {F(g)} {F(b)} RG\n");
    public void Fill(double r, double g, double b) => _c.Append($"{F(r)} {F(g)} {F(b)} rg\n");

    public void Line(double x1, double y1, double x2, double y2, double widthMm, bool dashed = false)
    {
        _c.Append(dashed ? "[1.5 1] 0 d " : "[] 0 d ");
        _c.Append(F(widthMm * PtPerMm)).Append(" w ").Append(P(x1, y1)).Append(" m ").Append(P(x2, y2)).Append(" l S\n");
    }

    public void Rect(double x, double y, double w, double h, double widthMm) =>
        Polygon([(x, y), (x + w, y), (x + w, y + h), (x, y + h)], widthMm);

    public void FillRect(double x, double y, double w, double h) =>
        FillPolygon([(x, y), (x + w, y), (x + w, y + h), (x, y + h)]);

    public void Polygon(IReadOnlyList<(double X, double Y)> pts, double widthMm)
    {
        if (pts.Count < 2) return;
        _c.Append("[] 0 d ").Append(F(widthMm * PtPerMm)).Append(" w ");
        Path(pts);
        _c.Append("s\n");
    }

    public void FillPolygon(IReadOnlyList<(double X, double Y)> pts)
    {
        if (pts.Count < 3) return;
        Path(pts);
        _c.Append("f\n");
    }

    private void Path(IReadOnlyList<(double X, double Y)> pts)
    {
        _c.Append(P(pts[0].X, pts[0].Y)).Append(" m ");
        for (int i = 1; i < pts.Count; i++) _c.Append(P(pts[i].X, pts[i].Y)).Append(" l ");
    }

    /// <summary>
    /// Texto en Helvetica. (<paramref name="x"/>, <paramref name="y"/>) es el punto de la línea base que indica la
    /// alineación; <paramref name="rotationDeg"/> gira antihorario (como en el plano).
    /// </summary>
    public void Text(double x, double y, double sizeMm, string text, TextAlign align = TextAlign.Left, double rotationDeg = 0)
    {
        double w = TextWidth(text, sizeMm);
        double shift = align switch { TextAlign.Center => -w / 2, TextAlign.Right => -w, _ => 0 };
        double a = rotationDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        // Origen desplazado a lo largo de la dirección del texto (en papel la Y va hacia abajo).
        double ox = x + shift * c, oy = y - shift * s;
        _c.Append("BT /F1 ").Append(F(sizeMm * PtPerMm)).Append(" Tf ")
          .Append($"{F(c)} {F(s)} {F(-s)} {F(c)} ").Append(P(ox, oy)).Append(" Tm ")
          .Append(Escape(text)).Append(" Tj ET\n");
    }

    /// <summary>Ancho aproximado del texto en Helvetica (mm), para centrar y alinear a la derecha.</summary>
    public static double TextWidth(string text, double sizeMm)
    {
        double units = 0;
        foreach (char ch in text)
            units += ch switch
            {
                ' ' or '.' or ',' or ':' or ';' or '·' or '!' or '|' or 'i' or 'j' or 'l' or 'I' or '\'' => 250,
                'f' or 't' or 'r' or '(' or ')' or '-' or '²' or '/' => 333,
                'm' or 'M' or 'W' or 'w' => 833,
                >= '0' and <= '9' => 556,
                >= 'A' and <= 'Z' => 680,
                _ => 540,
            };
        return units / 1000 * sizeMm;
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder("(");
        foreach (var ch in s)
        {
            char c = ch == '·' ? '·' : ch;
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 128) sb.Append(c);
            else if (c < 256) sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
            else sb.Append('?');
        }
        return sb.Append(')').ToString();
    }

    public byte[] ToBytes()
    {
        var latin1 = Encoding.Latin1;
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void Write(string s) { var b = latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Obj(string body) { offsets.Add(ms.Position); Write($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

        Write("%PDF-1.4\n%âãÏÓ\n");
        int n = _pages.Count;
        var kids = string.Join(' ', Enumerable.Range(0, n).Select(i => $"{4 + 2 * i} 0 R"));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {n} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        string box = $"[0 0 {F(PageWidthMm * PtPerMm)} {F(PageHeightMm * PtPerMm)}]";
        for (int i = 0; i < n; i++)
        {
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox {box} /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + 2 * i} 0 R >>");
            var content = _pages[i].ToString();
            Obj($"<< /Length {latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
        }
        long xref = ms.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
