using System.Globalization;
using System.Text;

namespace Simulador.Circuitos.Fabrication;

/// <summary>
/// Escritor PDF mínimo con dibujo vectorial en milímetros (origen arriba a la izquierda, Y hacia abajo).
/// Sin dependencias externas; los vectores conservan la escala exacta al imprimir "tamaño real".
/// </summary>
public sealed class PdfWriter
{
    public const double PtPerMm = 72.0 / 25.4;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly List<StringBuilder> _pages = [];
    private StringBuilder _c = null!;

    public PdfWriter(double pageWidthMm = 210, double pageHeightMm = 297)
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
        _c = new StringBuilder();
        _c.Append("1 J 1 j\n");
        _pages.Add(_c);
    }

    private string P(double xMm, double yMm) => $"{F(xMm * PtPerMm)} {F((PageHeightMm - yMm) * PtPerMm)}";
    private static string F(double v) => v.ToString("0.###", Inv);

    public void Gray(double level) => _c.Append(F(level)).Append(" g ").Append(F(level)).Append(" G\n");

    public void Line(double x1, double y1, double x2, double y2, double widthMm)
        => _c.Append(F(widthMm * PtPerMm)).Append(" w ").Append(P(x1, y1)).Append(" m ").Append(P(x2, y2)).Append(" l S\n");

    public void Rect(double x, double y, double w, double h, double widthMm)
        => _c.Append(F(widthMm * PtPerMm)).Append(" w ").Append(P(x, y + h)).Append(' ').Append(F(w * PtPerMm)).Append(' ').Append(F(h * PtPerMm)).Append(" re S\n");

    public void FillRect(double x, double y, double w, double h)
        => _c.Append(P(x, y + h)).Append(' ').Append(F(w * PtPerMm)).Append(' ').Append(F(h * PtPerMm)).Append(" re f\n");

    public void FillCircle(double cx, double cy, double r)
    {
        const double k = 0.5523;
        double c = r * k;
        _c.Append(P(cx + r, cy)).Append(" m ")
          .Append(P(cx + r, cy + c)).Append(' ').Append(P(cx + c, cy + r)).Append(' ').Append(P(cx, cy + r)).Append(" c ")
          .Append(P(cx - c, cy + r)).Append(' ').Append(P(cx - r, cy + c)).Append(' ').Append(P(cx - r, cy)).Append(" c ")
          .Append(P(cx - r, cy - c)).Append(' ').Append(P(cx - c, cy - r)).Append(' ').Append(P(cx, cy - r)).Append(" c ")
          .Append(P(cx + c, cy - r)).Append(' ').Append(P(cx + r, cy - c)).Append(' ').Append(P(cx + r, cy)).Append(" c f\n");
    }

    public void FillPolygon(IReadOnlyList<(double X, double Y)> pts)
    {
        if (pts.Count < 3) return;
        _c.Append(P(pts[0].X, pts[0].Y)).Append(" m ");
        foreach (var p in pts.Skip(1)) _c.Append(P(p.X, p.Y)).Append(" l ");
        _c.Append("h f\n");
    }

    /// <summary>Texto en Helvetica; <paramref name="y"/> es la línea base.</summary>
    public void Text(double x, double y, double sizeMm, string text)
        => _c.Append("BT /F1 ").Append(F(sizeMm * PtPerMm)).Append(" Tf ").Append(P(x, y)).Append(" Td ").Append(Escape(text)).Append(" Tj ET\n");

    private static string Escape(string s)
    {
        var sb = new StringBuilder("(");
        foreach (var ch in s)
        {
            if (ch is '(' or ')' or '\\') sb.Append('\\').Append(ch);
            else if (ch < 128) sb.Append(ch);
            else if (ch < 256) sb.Append('\\').Append(Convert.ToString(ch, 8).PadLeft(3, '0'));
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
        // 1 catálogo, 2 páginas, 3 fuente, luego (página, contenido) por cada hoja.
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
