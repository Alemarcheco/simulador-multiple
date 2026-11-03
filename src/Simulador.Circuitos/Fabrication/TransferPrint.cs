using Simulador.Circuitos.Pcb;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Fabrication;

public enum TransferLayer { BottomCopper, TopCopper, TopSilk }

public sealed class TransferOptions
{
    public TransferLayer Layer { get; set; } = TransferLayer.BottomCopper;
    /// <summary>
    /// Espejo de la imagen impresa. Por defecto se elige solo: el cobre inferior NO se espeja (al apoyarlo
    /// boca abajo sobre la cara de abajo queda bien) y el superior y la serigrafía SÍ.
    /// </summary>
    public bool? Mirror { get; set; }
    public int Copies { get; set; } = 1;
    /// <summary>Deja un punto sin tóner en el centro de cada pad para que la broca no se desvíe.</summary>
    public bool DrillMarks { get; set; } = true;
    public double DrillMarkDiameter { get; set; } = 0.6;

    public bool EffectiveMirror => Mirror ?? Layer != TransferLayer.BottomCopper;
}

/// <summary>Hoja A4 a escala 1:1 para el método de transferencia térmica (plancha o laminadora).</summary>
public static class TransferPrint
{
    public const double Margin = 12;
    public const double Gap = 8;
    public const double RulerLength = 50;

    public static byte[] Pdf(PcbDocument doc, TransferOptions opt)
    {
        var pdf = new PdfWriter();
        double w = doc.BoardWidth, h = doc.BoardHeight;
        double usableW = pdf.PageWidthMm - 2 * Margin;
        int perRow = Math.Max(1, (int)((usableW + Gap) / (w + Gap)));
        double top = Margin + 22;

        Header(pdf, doc, opt);
        int placed = 0;
        double y = top;
        for (int i = 0; i < Math.Max(1, opt.Copies); i++)
        {
            int col = placed % perRow;
            if (col == 0 && placed > 0) y += h + Gap + 4;
            if (y + h + 4 > pdf.PageHeightMm - Margin - 20)
            {
                pdf.NewPage();
                Header(pdf, doc, opt);
                y = top;
                col = 0;
                placed = 0;
            }
            DrawBoard(pdf, doc, opt, Margin + col * (w + Gap), y);
            placed++;
        }
        Ruler(pdf, Margin, pdf.PageHeightMm - Margin - 10);
        return pdf.ToBytes();
    }

    private static void Header(PdfWriter pdf, PcbDocument doc, TransferOptions opt)
    {
        string layer = opt.Layer switch
        {
            TransferLayer.BottomCopper => "Cobre inferior (B.Cu)",
            TransferLayer.TopCopper => "Cobre superior (F.Cu)",
            _ => "Serigrafía superior",
        };
        pdf.Gray(0);
        pdf.Text(Margin, Margin + 4, 4.2, $"{doc.Title}  -  {layer}  -  {(opt.EffectiveMirror ? "ESPEJADO" : "sin espejo")}");
        pdf.Text(Margin, Margin + 10, 3, $"Placa {doc.BoardWidth:0.##} x {doc.BoardHeight:0.##} mm. Imprimir al 100 % (\"tamaño real\", sin ajustar a página) en impresora LÁSER, sobre papel satinado o de transferencia.");
        pdf.Text(Margin, Margin + 15, 3, "Compruebe con una regla que la escala de abajo mide exactamente 50 mm antes de planchar.");
    }

    public static void DrawBoard(PdfWriter pdf, PcbDocument doc, TransferOptions opt, double ox, double oy)
    {
        double w = doc.BoardWidth;
        (double, double) M(Vec2 v) => (ox + (opt.EffectiveMirror ? w - v.X : v.X), oy + v.Y);

        var layer = opt.Layer switch
        {
            TransferLayer.BottomCopper => ArtworkLayer.BottomCopper,
            TransferLayer.TopCopper => ArtworkLayer.TopCopper,
            _ => ArtworkLayer.TopSilk,
        };
        pdf.Gray(0);
        foreach (var s in Artwork.Layer(doc, layer))
        {
            switch (s)
            {
                case Stroke st:
                    var (ax, ay) = M(st.A);
                    var (bx, by) = M(st.B);
                    pdf.Line(ax, ay, bx, by, st.Width);
                    break;
                case Disc d:
                    var (cx, cy) = M(d.Center);
                    pdf.FillCircle(cx, cy, d.Diameter / 2);
                    break;
                case Polygon p:
                    pdf.FillPolygon(p.Points.Select(M).ToList());
                    break;
            }
        }
        if (opt.DrillMarks && layer != ArtworkLayer.TopSilk)
        {
            pdf.Gray(1);
            foreach (var d in Artwork.Drills(doc))
            {
                var (cx, cy) = M(d.Center);
                pdf.FillCircle(cx, cy, Math.Min(opt.DrillMarkDiameter, d.Diameter) / 2);
            }
            pdf.Gray(0);
        }
        // Contorno fino y marcas de esquina para recortar.
        pdf.Rect(ox, oy, w, doc.BoardHeight, 0.1);
        foreach (var (cx, cy) in new[] { (ox, oy), (ox + w, oy), (ox, oy + doc.BoardHeight), (ox + w, oy + doc.BoardHeight) })
        {
            double sx = cx == ox ? -1 : 1, sy = cy == oy ? -1 : 1;
            pdf.Line(cx + sx * 1, cy, cx + sx * 4, cy, 0.15);
            pdf.Line(cx, cy + sy * 1, cx, cy + sy * 4, 0.15);
        }
    }

    private static void Ruler(PdfWriter pdf, double x, double y)
    {
        pdf.Gray(0);
        pdf.Line(x, y, x + RulerLength, y, 0.25);
        for (int i = 0; i <= RulerLength; i++)
        {
            double len = i % 10 == 0 ? 4 : i % 5 == 0 ? 2.5 : 1.5;
            pdf.Line(x + i, y, x + i, y - len, 0.15);
        }
        pdf.Text(x, y + 4, 3, "0");
        pdf.Text(x + RulerLength - 3, y + 4, 3, "50 mm");
        pdf.Rect(x + RulerLength + 10, y - 10, 20, 10, 0.25);
        pdf.Text(x + RulerLength + 33, y - 3, 3, "Cuadro de 20 x 10 mm");
    }
}
