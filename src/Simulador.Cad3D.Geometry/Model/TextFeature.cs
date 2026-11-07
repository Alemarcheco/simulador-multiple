using SkiaSharp;

namespace Simulador.Cad3D.Geometry.Model;

public enum TextAlign
{
    Izquierda = 0,
    Centro = 1,
    Derecha = 2,
}

/// <summary>
/// Texto en relieve (modo Sólido) o grabado (modo Hueco). Las letras se dibujan en el plano XY local, con la línea
/// base en Y = 0, y se extruyen hacia +Z. Se gira y se coloca como cualquier otra operación.
/// </summary>
public sealed class TextFeature : Feature
{
    public override string KindName => "Texto";

    public string Text { get; set; } = "Hola";

    /// <summary>Nombre de la fuente instalada (si no existe se usa la fuente por defecto del sistema).</summary>
    public string FontFamily { get; set; } = "Arial";

    public bool Bold { get; set; } = true;
    public TextAlign Align { get; set; } = TextAlign.Izquierda;

    [Param("Altura de las mayúsculas", Min = 0.5, Max = 1000, Step = 1)] public double Size { get; set; } = 10;
    [Param("Grosor (relieve o profundidad)", Min = 0.05, Max = 1000, Step = 0.2)] public double Depth { get; set; } = 1;

    public override ISolid BuildLocal(IGeometryKernel kernel)
    {
        var contours = TextOutline.Contours(Text, FontFamily, Bold, Size, Align);
        if (contours.Count == 0) throw new InvalidOperationException("el texto está vacío o la fuente no tiene esas letras");
        // Profundidad de anidado: 0 = contorno exterior, 1 = hueco de una letra (la «o»), 2 = isla dentro del hueco…
        var depth = contours.Select(c => contours.Count(o => !ReferenceEquals(o, c) && TextOutline.Contains(o, c))).ToList();
        ISolid? result = null;
        for (int i = 0; i < contours.Count; i++)
        {
            if (depth[i] % 2 != 0) continue;
            // Huecos de esta región: los contornos de un nivel más que quedan dentro de ella.
            var holes = Enumerable.Range(0, contours.Count)
                .Where(j => depth[j] == depth[i] + 1 && TextOutline.Contains(contours[i], contours[j]))
                .Select(j => (IReadOnlyList<Vec2>)contours[j]).ToList();
            var s = kernel.ExtrudeRegion(contours[i], holes, Depth);
            result = result is null ? s : kernel.Union(result, s);
        }
        return result ?? throw new InvalidOperationException("el texto no tiene contornos");
    }
}

/// <summary>Contornos de un texto en mm (polígonos antihorarios), leídos de la fuente con SkiaSharp.</summary>
public static class TextOutline
{
    private const float FontUnits = 200; // tamaño de trabajo; luego se escala a mm

    public static List<List<Vec2>> Contours(string text, string family, bool bold, double capHeightMm, TextAlign align, double toleranceMm = 0.02)
    {
        var result = new List<List<Vec2>>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        using var typeface = SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
        using var paint = new SKPaint { Typeface = typeface, TextSize = FontUnits, IsAntialias = false };
        double cap = paint.FontMetrics.CapHeight > 0 ? paint.FontMetrics.CapHeight : FontUnits * 0.7;
        double scale = capHeightMm / cap;
        using var path = paint.GetTextPath(text.Replace('\n', ' '), 0, 0);
        if (path is null || path.IsEmpty) return result;

        double tol = toleranceMm / scale;
        var raw = Flatten(path, tol);
        // Se alinea con la tinta (sin el margen lateral de la fuente), para que X = 0 sea el borde o el centro real.
        var ink = path.TightBounds;
        double shift = align switch { TextAlign.Centro => -(ink.Left + ink.Right) / 2, TextAlign.Derecha => -ink.Right, _ => -ink.Left };
        foreach (var c in raw)
        {
            // Skia tiene Y hacia abajo: se invierte y se pasa a mm.
            var pts = c.Select(p => new Vec2((p.X + shift) * scale, -p.Y * scale)).ToList();
            pts = Simplify(pts);
            if (pts.Count < 3 || Math.Abs(Polygon2D.SignedArea(pts)) < 1e-4) continue;
            result.Add(Polygon2D.Normalize(pts));
        }
        return result;
    }

    /// <summary>True si el contorno inner queda dentro de outer (se prueba con un punto de inner).</summary>
    public static bool Contains(IReadOnlyList<Vec2> outer, IReadOnlyList<Vec2> inner)
    {
        if (Math.Abs(Polygon2D.SignedArea(outer)) <= Math.Abs(Polygon2D.SignedArea(inner))) return false;
        // Punto medio del primer tramo, que casi nunca cae justo sobre el otro contorno.
        var p = new Vec2((inner[0].X + inner[1].X) / 2, (inner[0].Y + inner[1].Y) / 2);
        bool inside = false;
        for (int i = 0, j = outer.Count - 1; i < outer.Count; j = i++)
        {
            var a = outer[i]; var b = outer[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static List<List<Vec2>> Flatten(SKPath path, double tol)
    {
        var contours = new List<List<Vec2>>();
        List<Vec2>? cur = null;
        using var it = path.CreateRawIterator();
        var pts = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = it.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    if (cur is { Count: > 2 }) contours.Add(cur);
                    cur = new List<Vec2> { V(pts[0]) };
                    break;
                case SKPathVerb.Line:
                    cur?.Add(V(pts[1]));
                    break;
                case SKPathVerb.Quad:
                    Curve(cur, t => Quad(pts[0], pts[1], pts[2], t), pts, tol);
                    break;
                case SKPathVerb.Conic:
                {
                    float w = it.ConicWeight();
                    var (p0, p1, p2) = (pts[0], pts[1], pts[2]);
                    Curve(cur, t => Conic(p0, p1, p2, w, t), pts, tol);
                    break;
                }
                case SKPathVerb.Cubic:
                    Curve(cur, t => Cubic(pts[0], pts[1], pts[2], pts[3], t), pts, tol);
                    break;
                case SKPathVerb.Close:
                    if (cur is { Count: > 2 }) contours.Add(cur);
                    cur = null;
                    break;
            }
        }
        if (cur is { Count: > 2 }) contours.Add(cur);
        return contours;
    }

    private static Vec2 V(SKPoint p) => new(p.X, p.Y);

    private static void Curve(List<Vec2>? cur, Func<double, Vec2> f, SKPoint[] ctrl, double tol)
    {
        if (cur is null) return;
        // Número de tramos según la longitud del polígono de control (error de cuerda ≈ tol).
        double len = 0;
        for (int i = 1; i < ctrl.Length && i < 4; i++) len += Math.Sqrt(Math.Pow(ctrl[i].X - ctrl[i - 1].X, 2) + Math.Pow(ctrl[i].Y - ctrl[i - 1].Y, 2));
        int n = (int)Math.Clamp(Math.Ceiling(Math.Sqrt(len / Math.Max(tol, 1e-6)) / 2), 2, 32);
        for (int k = 1; k <= n; k++) cur.Add(f((double)k / n));
    }

    private static Vec2 Quad(SKPoint a, SKPoint b, SKPoint c, double t)
    {
        double u = 1 - t;
        return new Vec2(u * u * a.X + 2 * u * t * b.X + t * t * c.X, u * u * a.Y + 2 * u * t * b.Y + t * t * c.Y);
    }

    private static Vec2 Conic(SKPoint a, SKPoint b, SKPoint c, float w, double t)
    {
        double u = 1 - t, d = u * u + 2 * w * u * t + t * t;
        return new Vec2((u * u * a.X + 2 * w * u * t * b.X + t * t * c.X) / d, (u * u * a.Y + 2 * w * u * t * b.Y + t * t * c.Y) / d);
    }

    private static Vec2 Cubic(SKPoint a, SKPoint b, SKPoint c, SKPoint d, double t)
    {
        double u = 1 - t;
        return new Vec2(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
                        u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    /// <summary>Quita puntos repetidos y casi alineados (las letras traen muchos).</summary>
    private static List<Vec2> Simplify(List<Vec2> pts)
    {
        var res = new List<Vec2>();
        foreach (var p in pts)
            if (res.Count == 0 || Sketch2D.Length(res[^1], p) > 1e-4) res.Add(p);
        while (res.Count > 1 && Sketch2D.Length(res[0], res[^1]) <= 1e-4) res.RemoveAt(res.Count - 1);
        bool changed = true;
        while (changed && res.Count > 3)
        {
            changed = false;
            for (int i = 0; i < res.Count && res.Count > 3; i++)
            {
                var a = res[(i - 1 + res.Count) % res.Count]; var b = res[i]; var c = res[(i + 1) % res.Count];
                if (Math.Abs((b - a).Cross(c - a)) < 1e-7) { res.RemoveAt(i); changed = true; i--; }
            }
        }
        return res;
    }
}
