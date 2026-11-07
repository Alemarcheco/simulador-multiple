using System.Globalization;
using System.Text;
using Simulador.Core.Building;

namespace Simulador.Civil.Export;

/// <summary>Tamaño de papel en apaisado (mm).</summary>
public sealed record Paper(string Name, double Width, double Height)
{
    public static readonly Paper A4 = new("A4", 297, 210);
    public static readonly Paper A3 = new("A3", 420, 297);
    public static readonly Paper A2 = new("A2", 594, 420);
    public static readonly Paper A1 = new("A1", 841, 594);
    public static readonly Paper[] All = [A4, A3, A2, A1];
}

/// <summary>Exporta planos a DXF (R12, abre en cualquier CAD) y a PDF vectorial a escala.</summary>
public static class PlanExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Escalas normalizadas que se prueban al elegir la escala automática (1:N).</summary>
    public static readonly int[] StandardScales = [20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 500, 750, 1000];

    // ------------------------------------------------------------------ DXF

    /// <summary>Color AutoCAD (ACI) de cada capa.</summary>
    private static int LayerColor(string layer) => layer switch
    {
        PlanLayers.Walls => 7, PlanLayers.Openings => 5, PlanLayers.Stairs => 30, PlanLayers.Rooms => 3,
        PlanLayers.Dimensions => 1, PlanLayers.Electric => 1, PlanLayers.Plumbing => 4, PlanLayers.Frame => 8, _ => 7,
    };

    /// <summary>
    /// DXF en metros ($INSUNITS = 6). Varios niveles se colocan uno al lado del otro, cada uno con su título,
    /// separados por <paramref name="gap"/> metros. Los muros van como polilíneas cerradas con su relleno (SOLID).
    /// </summary>
    public static string ToDxf(IReadOnlyList<PlanDrawing> drawings, double gap = 5)
    {
        var sb = new StringBuilder();
        void P(int code, string value) => sb.Append(code).Append('\n').Append(value).Append('\n');
        string F(double v) => v.ToString("0.######", Inv);

        P(0, "SECTION"); P(2, "HEADER");
        P(9, "$ACADVER"); P(1, "AC1009");
        P(9, "$DWGCODEPAGE"); P(3, "ANSI_1252");
        P(9, "$INSUNITS"); P(70, "6");
        P(9, "$MEASUREMENT"); P(70, "1");
        P(0, "ENDSEC");

        P(0, "SECTION"); P(2, "TABLES");
        P(0, "TABLE"); P(2, "LTYPE"); P(70, "2");
        P(0, "LTYPE"); P(2, "CONTINUOUS"); P(70, "0"); P(3, "Continua"); P(72, "65"); P(73, "0"); P(40, "0");
        P(0, "LTYPE"); P(2, "DASHED"); P(70, "0"); P(3, "Discontinua"); P(72, "65"); P(73, "2"); P(40, "0.15"); P(49, "0.1"); P(49, "-0.05");
        P(0, "ENDTAB");
        P(0, "TABLE"); P(2, "LAYER"); P(70, PlanLayers.All.Length.ToString(Inv));
        foreach (var l in PlanLayers.All) { P(0, "LAYER"); P(2, l); P(70, "0"); P(62, LayerColor(l).ToString(Inv)); P(6, "CONTINUOUS"); }
        P(0, "ENDTAB");
        P(0, "TABLE"); P(2, "STYLE"); P(70, "1");
        P(0, "STYLE"); P(2, "STANDARD"); P(70, "0"); P(40, "0"); P(41, "1"); P(50, "0"); P(71, "0"); P(42, "0.25"); P(3, "txt"); P(4, "");
        P(0, "ENDTAB");
        P(0, "ENDSEC");

        P(0, "SECTION"); P(2, "ENTITIES");
        double x = 0;
        foreach (var d in drawings)
        {
            var (min, max) = d.Extents;
            var shift = new Point2(x - min.X, -min.Y);
            Point2 S(Point2 p) => p + shift;
            foreach (var item in d.Items)
            {
                switch (item)
                {
                    case PlanPolygon pg:
                        if (pg.Filled && pg.Points.Count is 3 or 4)
                        {
                            // SOLID: los vértices 3 y 4 van cruzados respecto al orden del contorno.
                            var q = pg.Points.Select(S).ToList();
                            if (q.Count == 3) q.Add(q[2]);
                            P(0, "SOLID"); P(8, pg.Layer);
                            P(10, F(q[0].X)); P(20, F(q[0].Y)); P(30, "0");
                            P(11, F(q[1].X)); P(21, F(q[1].Y)); P(31, "0");
                            P(12, F(q[3].X)); P(22, F(q[3].Y)); P(32, "0");
                            P(13, F(q[2].X)); P(23, F(q[2].Y)); P(33, "0");
                        }
                        P(0, "POLYLINE"); P(8, pg.Layer); P(66, "1"); P(10, "0"); P(20, "0"); P(30, "0"); P(70, "1");
                        foreach (var v in pg.Points.Select(S)) { P(0, "VERTEX"); P(8, pg.Layer); P(10, F(v.X)); P(20, F(v.Y)); P(30, "0"); }
                        P(0, "SEQEND"); P(8, pg.Layer);
                        break;
                    case PlanLine ln:
                        P(0, "LINE"); P(8, ln.Layer);
                        if (ln.Dashed) P(6, "DASHED");
                        P(10, F(S(ln.A).X)); P(20, F(S(ln.A).Y)); P(30, "0");
                        P(11, F(S(ln.B).X)); P(21, F(S(ln.B).Y)); P(31, "0");
                        break;
                    case PlanText t:
                        var at = S(t.At);
                        P(0, "TEXT"); P(8, t.Layer);
                        P(10, F(at.X)); P(20, F(at.Y)); P(30, "0");
                        P(40, F(t.Height)); P(1, DxfText(t.Text));
                        if (Math.Abs(t.Rotation) > 1e-9) P(50, F(t.Rotation));
                        int h = t.Align switch { TextAlign.Center => 1, TextAlign.Right => 2, _ => 0 };
                        if (h != 0) { P(72, h.ToString(Inv)); P(11, F(at.X)); P(21, F(at.Y)); P(31, "0"); }
                        break;
                }
            }
            // Título bajo cada nivel (el dibujo empieza en Y = 0).
            P(0, "TEXT"); P(8, PlanLayers.Frame); P(10, F(x)); P(20, F(-1.2)); P(30, "0"); P(40, "0.4"); P(1, DxfText($"{d.Title} - {d.Subtitle}"));
            x += max.X - min.X + gap;
        }
        P(0, "ENDSEC");
        P(0, "EOF");
        return sb.ToString();
    }

    /// <summary>Los DXF R12 son ANSI: se cambian los caracteres que no caben por equivalentes.</summary>
    private static string DxfText(string s) => s.Replace("²", "%%178").Replace("±", "%%p").Replace("°", "%%d");

    // ------------------------------------------------------------------ PDF

    /// <summary>Escala normalizada más grande con la que el dibujo cabe en el área útil del papel.</summary>
    public static int AutoScale(PlanDrawing d, Paper paper)
    {
        var (min, max) = d.Extents;
        var (w, h) = DrawingArea(paper);
        double dx = Math.Max(max.X - min.X, 0.1) * 1000, dy = Math.Max(max.Y - min.Y, 0.1) * 1000; // mm reales
        foreach (int s in StandardScales)
            if (dx / s <= w && dy / s <= h) return s;
        return (int)Math.Ceiling(Math.Max(dx / w, dy / h));
    }

    private const double Margin = 10, TitleHeight = 18;

    private static (double W, double H) DrawingArea(Paper p) => (p.Width - 2 * Margin - 10, p.Height - 2 * Margin - TitleHeight - 10);

    /// <summary>
    /// PDF vectorial: una hoja por nivel, con marco, cajetín (proyecto, nivel, escala, fecha) y barra de escala.
    /// <paramref name="scale"/> = N de 1:N; null = automática para cada hoja.
    /// </summary>
    /// <summary>Bytes del DXF en la página de códigos de la cabecera (ANSI 1252 ≈ Latin-1: tildes y ñ).</summary>
    public static byte[] ToDxfBytes(IReadOnlyList<PlanDrawing> drawings, double gap = 5) => Encoding.Latin1.GetBytes(ToDxf(drawings, gap));

    public static byte[] ToPdf(IReadOnlyList<PlanDrawing> drawings, Paper paper, int? scale = null, DateTime? date = null)
    {
        var pdf = new PdfCanvas(paper.Width, paper.Height);
        bool first = true;
        foreach (var d in drawings)
        {
            if (!first) pdf.NewPage();
            first = false;
            int s = scale ?? AutoScale(d, paper);
            DrawSheet(pdf, d, paper, s, date ?? DateTime.Now);
        }
        return pdf.ToBytes();
    }

    private static void DrawSheet(PdfCanvas pdf, PlanDrawing d, Paper paper, int scale, DateTime date)
    {
        // Marco y cajetín.
        pdf.Stroke(0, 0, 0);
        pdf.Rect(Margin, Margin, paper.Width - 2 * Margin, paper.Height - 2 * Margin, 0.5);
        double ty = paper.Height - Margin - TitleHeight;
        pdf.Line(Margin, ty, paper.Width - Margin, ty, 0.35);
        pdf.Fill(0, 0, 0);
        pdf.Text(Margin + 4, ty + 7.5, 5, d.Title);
        pdf.Text(Margin + 4, ty + 14, 3.5, d.Subtitle);
        string right = $"Escala 1:{scale}   ·   {paper.Name}   ·   {date:dd/MM/yyyy}";
        pdf.Text(paper.Width - Margin - 4, ty + 7.5, 3.5, right, TextAlign.Right);
        pdf.Text(paper.Width - Margin - 4, ty + 14, 2.8, "Simulador múltiple · Edificación · medidas en metros", TextAlign.Right);

        // Barra de escala de 5 m (o lo que quepa bien).
        double unit = scale <= 50 ? 1 : scale <= 200 ? 5 : scale <= 500 ? 10 : 50;
        double barMm = unit * 1000 / scale;
        double bx = paper.Width / 2 - barMm, by = ty + 11;
        for (int i = 0; i < 2; i++)
        {
            if (i == 0) pdf.FillRect(bx, by, barMm, 1.5); else pdf.Rect(bx + barMm, by, barMm, 1.5, 0.2);
        }
        pdf.Text(bx, by - 1, 2.5, "0", TextAlign.Center);
        pdf.Text(bx + barMm, by - 1, 2.5, $"{unit:0}", TextAlign.Center);
        pdf.Text(bx + 2 * barMm, by - 1, 2.5, $"{2 * unit:0} m", TextAlign.Center);

        // Dibujo centrado en el área útil. Papel: X a la derecha, Y hacia abajo.
        var (min, max) = d.Extents;
        double areaX = Margin + 5, areaY = Margin + 5;
        var (aw, ah) = DrawingArea(paper);
        double k = 1000.0 / scale; // mm de papel por metro
        double ox = areaX + (aw - (max.X - min.X) * k) / 2 - min.X * k;
        double oy = areaY + (ah - (max.Y - min.Y) * k) / 2 + max.Y * k;
        (double X, double Y) M(Point2 p) => (ox + p.X * k, oy - p.Y * k);

        // Orden: rellenos primero, luego líneas, luego textos.
        foreach (var pg in d.Items.OfType<PlanPolygon>().Where(p => p.Filled))
        {
            pdf.Fill(0.25, 0.25, 0.27);
            pdf.FillPolygon(pg.Points.Select(M).ToList());
        }
        foreach (var item in d.Items)
        {
            var (r, g, b, w) = Style(item.Layer);
            switch (item)
            {
                case PlanPolygon { Filled: false } pg:
                    pdf.Stroke(r, g, b);
                    pdf.Polygon(pg.Points.Select(M).ToList(), w);
                    break;
                case PlanLine ln:
                    pdf.Stroke(r, g, b);
                    var a = M(ln.A);
                    var c = M(ln.B);
                    pdf.Line(a.X, a.Y, c.X, c.Y, w, ln.Dashed);
                    break;
            }
        }
        foreach (var t in d.Items.OfType<PlanText>())
        {
            var (r, g, b, _) = Style(t.Layer);
            pdf.Fill(r, g, b);
            var at = M(t.At);
            // Altura en papel: la de planta a escala, pero nunca menor de 1,8 mm ni mayor de 5 mm para que se lea.
            double size = Math.Clamp(t.Height * k, 1.8, 5);
            pdf.Text(at.X, at.Y, size, t.Text, t.Align, t.Rotation);
        }
    }

    private static (double R, double G, double B, double Width) Style(string layer) => layer switch
    {
        PlanLayers.Walls => (0.1, 0.1, 0.1, 0.35),
        PlanLayers.Openings => (0.15, 0.35, 0.6, 0.18),
        PlanLayers.Stairs => (0.45, 0.3, 0.15, 0.18),
        PlanLayers.Rooms => (0.1, 0.1, 0.1, 0.18),
        PlanLayers.Dimensions => (0.6, 0.1, 0.1, 0.13),
        PlanLayers.Electric => (0.85, 0.3, 0.1, 0.25),
        PlanLayers.Plumbing => (0.1, 0.45, 0.85, 0.25),
        _ => (0, 0, 0, 0.18),
    };
}
