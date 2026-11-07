using System.Globalization;
using Simulador.Core.Building;

namespace Simulador.Civil.Import;

/// <summary>Segmento de un plano importado, ya en metros, con la capa de origen.</summary>
public readonly record struct DrawingSegment(Point2 A, Point2 B, string Layer);

/// <summary>Dibujo 2D leído de un DXF: segmentos en metros y la unidad que se usó para convertirlos.</summary>
public sealed class DxfDrawing
{
    public List<DrawingSegment> Segments { get; } = [];
    /// <summary>Metros por unidad del archivo (0,001 si estaba en milímetros).</summary>
    public double MetersPerUnit { get; set; } = 1;
    /// <summary>Cómo se decidió la unidad: leída de la cabecera o deducida del tamaño.</summary>
    public string UnitSource { get; set; } = "";
    public IReadOnlyList<string> Layers => Segments.Select(s => s.Layer).Distinct().Order().ToList();
    /// <summary>Entidades que no se pudieron dibujar (textos, sombreados…), por tipo.</summary>
    public Dictionary<string, int> Skipped { get; } = [];

    public (Point2 Min, Point2 Max) Extents
    {
        get
        {
            if (Segments.Count == 0) return (new(0, 0), new(0, 0));
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var s in Segments)
            foreach (var p in new[] { s.A, s.B })
            {
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
            }
            return (new(x0, y0), new(x1, y1));
        }
    }
}

/// <summary>
/// Lector de DXF en texto (AutoCAD, LibreCAD, QCAD, FreeCAD, BricsCAD…). Toma líneas, polilíneas (con arcos por
/// "bulge"), círculos, arcos, elipses aproximadas y bloques insertados (puertas, muebles), y lo pasa todo a metros.
/// Ignora textos, cotas y sombreados: el plano sirve como calco para dibujar encima.
/// </summary>
public static class DxfReader
{
    private const int MaxBlockDepth = 8;
    private const int ArcSegmentsPerCircle = 48;

    public static DxfDrawing Read(string path) => Parse(File.ReadAllText(path));

    public static DxfDrawing Parse(string text)
    {
        var pairs = ReadPairs(text);
        var blocks = new Dictionary<string, List<Entity>>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<Entity>();
        int insUnits = 0;

        int i = 0;
        while (i < pairs.Count)
        {
            if (pairs[i].Code == 0 && pairs[i].Value == "SECTION" && i + 1 < pairs.Count && pairs[i + 1].Code == 2)
            {
                string name = pairs[i + 1].Value;
                i += 2;
                int end = FindEndSection(pairs, i);
                var section = pairs.GetRange(i, end - i);
                switch (name)
                {
                    case "HEADER": insUnits = ReadInsUnits(section); break;
                    case "BLOCKS": ReadBlocks(section, blocks); break;
                    case "ENTITIES": entities.AddRange(ReadEntities(section, 0, section.Count)); break;
                }
                i = end + 1;
            }
            else i++;
        }

        var raw = new DxfDrawing();
        foreach (var e in entities) Emit(e, Transform.Identity, blocks, raw, 0, null);

        double mpu = UnitFactor(insUnits);
        if (mpu > 0) raw.UnitSource = $"cabecera ($INSUNITS = {insUnits})";
        else
        {
            // Sin unidad declarada: un plano de edificio mide entre unos metros y unos cientos de metros.
            var (min, max) = raw.Extents;
            double size = Math.Max(max.X - min.X, max.Y - min.Y);
            mpu = size > 2000 ? 0.001 : size > 300 ? 0.01 : 1;
            raw.UnitSource = mpu switch { 0.001 => "deducida: milímetros", 0.01 => "deducida: centímetros", _ => "deducida: metros" };
        }
        if (Math.Abs(mpu - 1) < 1e-12) { raw.MetersPerUnit = 1; return raw; }

        var result = new DxfDrawing { MetersPerUnit = mpu, UnitSource = raw.UnitSource };
        foreach (var s in raw.Segments) result.Segments.Add(new(s.A * mpu, s.B * mpu, s.Layer));
        foreach (var kv in raw.Skipped) result.Skipped[kv.Key] = kv.Value;
        return result;
    }

    /// <summary>Metros por unidad según $INSUNITS; 0 si no está declarada o no se conoce.</summary>
    public static double UnitFactor(int insUnits) => insUnits switch
    {
        1 => 0.0254, 2 => 0.3048, 4 => 0.001, 5 => 0.01, 6 => 1, 7 => 1000, 14 => 0.1,
        _ => 0,
    };

    // ------------------------------------------------------------------ lectura de pares código/valor

    private readonly record struct Pair(int Code, string Value);

    private static List<Pair> ReadPairs(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var pairs = new List<Pair>(lines.Length / 2);
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                throw new InvalidDataException($"DXF no válido cerca de la línea {i + 1}: se esperaba un código de grupo. ¿Es un DXF binario? Guárdalo como DXF de texto (ASCII).");
            pairs.Add(new Pair(code, lines[i + 1].Trim()));
        }
        return pairs;
    }

    private static int FindEndSection(List<Pair> pairs, int from)
    {
        for (int i = from; i < pairs.Count; i++)
            if (pairs[i].Code == 0 && pairs[i].Value == "ENDSEC") return i;
        return pairs.Count;
    }

    private static int ReadInsUnits(List<Pair> header)
    {
        for (int i = 0; i + 1 < header.Count; i++)
            if (header[i].Code == 9 && header[i].Value == "$INSUNITS" && header[i + 1].Code == 70)
                return int.TryParse(header[i + 1].Value, out int u) ? u : 0;
        return 0;
    }

    private static void ReadBlocks(List<Pair> section, Dictionary<string, List<Entity>> blocks)
    {
        int i = 0;
        while (i < section.Count)
        {
            if (section[i].Code == 0 && section[i].Value == "BLOCK")
            {
                // Cabecera del bloque hasta la primera entidad.
                int j = i + 1;
                string name = "";
                double bx = 0, by = 0;
                while (j < section.Count && section[j].Code != 0)
                {
                    if (section[j].Code == 2) name = section[j].Value;
                    else if (section[j].Code == 10) bx = D(section[j].Value);
                    else if (section[j].Code == 20) by = D(section[j].Value);
                    j++;
                }
                int end = j;
                while (end < section.Count && !(section[end].Code == 0 && section[end].Value == "ENDBLK")) end++;
                var ents = ReadEntities(section, j, end);
                // Las entidades del bloque se guardan respecto a su punto base.
                if (bx != 0 || by != 0)
                    foreach (var e in ents) e.BaseOffset = new Point2(-bx, -by);
                if (name.Length > 0) blocks[name] = ents;
                i = end + 1;
            }
            else i++;
        }
    }

    private sealed class Entity
    {
        public string Type = "";
        public string Layer = "0";
        public List<Pair> Codes = [];
        public List<Entity> Vertices = []; // POLYLINE
        public Point2 BaseOffset;
    }

    private static List<Entity> ReadEntities(List<Pair> section, int from, int to)
    {
        var list = new List<Entity>();
        Entity? polyline = null;
        int i = from;
        while (i < to)
        {
            if (section[i].Code != 0) { i++; continue; }
            var e = new Entity { Type = section[i].Value };
            i++;
            while (i < to && section[i].Code != 0)
            {
                if (section[i].Code == 8) e.Layer = section[i].Value;
                e.Codes.Add(section[i]);
                i++;
            }
            if (e.Type == "VERTEX" && polyline is not null) polyline.Vertices.Add(e);
            else if (e.Type == "SEQEND") polyline = null;
            else
            {
                if (e.Type == "POLYLINE") polyline = e;
                list.Add(e);
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ geometría

    private readonly record struct Transform(double A, double B, double C, double D, double Tx, double Ty)
    {
        public static Transform Identity => new(1, 0, 0, 1, 0, 0);
        public Point2 Apply(Point2 p) => new(A * p.X + C * p.Y + Tx, B * p.X + D * p.Y + Ty);
        /// <summary>this ∘ inner: primero inner, luego this.</summary>
        public Transform Then(Transform outer) => new(
            outer.A * A + outer.C * B, outer.B * A + outer.D * B,
            outer.A * C + outer.C * D, outer.B * C + outer.D * D,
            outer.A * Tx + outer.C * Ty + outer.Tx, outer.B * Tx + outer.D * Ty + outer.Ty);
    }

    private static void Emit(Entity e, Transform t, Dictionary<string, List<Entity>> blocks, DxfDrawing d, int depth, string? parentLayer)
    {
        // Las entidades de un bloque en la capa "0" toman la capa del bloque insertado (regla de AutoCAD).
        string layer = e.Layer == "0" && parentLayer is not null ? parentLayer : e.Layer;
        if (e.BaseOffset != default)
            t = new Transform(1, 0, 0, 1, e.BaseOffset.X, e.BaseOffset.Y).Then(t);
        void Seg(Point2 a, Point2 b) { if (a.DistanceTo(b) > 1e-9) d.Segments.Add(new(t.Apply(a), t.Apply(b), layer)); }

        switch (e.Type)
        {
            case "LINE":
                Seg(new(G(e, 10), G(e, 20)), new(G(e, 11), G(e, 21)));
                break;

            case "LWPOLYLINE":
            {
                var pts = new List<(Point2 P, double Bulge)>();
                foreach (var c in e.Codes)
                {
                    if (c.Code == 10) pts.Add((new Point2(D(c.Value), 0), 0));
                    else if (c.Code == 20 && pts.Count > 0) pts[^1] = (new Point2(pts[^1].P.X, D(c.Value)), pts[^1].Bulge);
                    else if (c.Code == 42 && pts.Count > 0) pts[^1] = (pts[^1].P, D(c.Value));
                }
                bool closed = ((int)G(e, 70) & 1) != 0;
                Polyline(pts, closed, Seg);
                break;
            }

            case "POLYLINE":
            {
                var pts = e.Vertices.Select(v => (new Point2(G(v, 10), G(v, 20)), G(v, 42))).ToList();
                bool closed = ((int)G(e, 70) & 1) != 0;
                Polyline(pts, closed, Seg);
                break;
            }

            case "CIRCLE":
                Arc(new(G(e, 10), G(e, 20)), G(e, 40), 0, 2 * Math.PI, Seg);
                break;

            case "ARC":
            {
                double a0 = G(e, 50) * Math.PI / 180, a1 = G(e, 51) * Math.PI / 180;
                if (a1 <= a0) a1 += 2 * Math.PI;
                Arc(new(G(e, 10), G(e, 20)), G(e, 40), a0, a1, Seg);
                break;
            }

            case "ELLIPSE":
            {
                var c = new Point2(G(e, 10), G(e, 20));
                var major = new Point2(G(e, 11), G(e, 21));
                double ratio = G(e, 40, 1), p0 = G(e, 41, 0), p1 = G(e, 42, 2 * Math.PI);
                if (p1 <= p0) p1 += 2 * Math.PI;
                var minor = major.Perp() * ratio;
                int n = Math.Max(4, (int)Math.Ceiling(ArcSegmentsPerCircle * (p1 - p0) / (2 * Math.PI)));
                Point2 At(double k) => c + major * Math.Cos(k) + minor * Math.Sin(k);
                for (int s = 0; s < n; s++) Seg(At(p0 + (p1 - p0) * s / n), At(p0 + (p1 - p0) * (s + 1) / n));
                break;
            }

            case "INSERT":
            {
                string name = e.Codes.FirstOrDefault(c => c.Code == 2).Value ?? "";
                if (depth >= MaxBlockDepth || !blocks.TryGetValue(name, out var ents)) { Skip(d, "INSERT sin bloque"); break; }
                double sx = G(e, 41, 1), sy = G(e, 42, 1), rot = G(e, 50) * Math.PI / 180;
                double cos = Math.Cos(rot), sin = Math.Sin(rot);
                var local = new Transform(cos * sx, sin * sx, -sin * sy, cos * sy, G(e, 10), G(e, 20));
                var combined = local.Then(t);
                foreach (var be in ents) Emit(be, combined, blocks, d, depth + 1, layer);
                break;
            }

            default:
                Skip(d, e.Type);
                break;
        }
    }

    private static void Skip(DxfDrawing d, string type) => d.Skipped[type] = d.Skipped.GetValueOrDefault(type) + 1;

    private static void Polyline(List<(Point2 P, double Bulge)> pts, bool closed, Action<Point2, Point2> seg)
    {
        int count = closed ? pts.Count : pts.Count - 1;
        for (int k = 0; k < count; k++)
        {
            var (a, bulge) = pts[k];
            var b = pts[(k + 1) % pts.Count].P;
            if (Math.Abs(bulge) < 1e-9) { seg(a, b); continue; }
            // Arco por "bulge": tan(ángulo/4). El centro está a la izquierda de a→b si bulge > 0.
            double theta = 4 * Math.Atan(bulge);
            double chord = a.DistanceTo(b);
            double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
            var mid = (a + b) * 0.5;
            var dir = (b - a) * (1 / chord);
            double h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4));
            var center = mid + dir.Perp() * (h * Math.Sign(bulge) * (Math.Abs(theta) > Math.PI ? -1 : 1));
            double a0 = Math.Atan2(a.Y - center.Y, a.X - center.X);
            Arc(center, r, a0, a0 + theta, seg);
        }
    }

    private static void Arc(Point2 c, double r, double a0, double a1, Action<Point2, Point2> seg)
    {
        if (r <= 0) return;
        int n = Math.Max(2, (int)Math.Ceiling(ArcSegmentsPerCircle * Math.Abs(a1 - a0) / (2 * Math.PI)));
        Point2 At(double k) => new(c.X + r * Math.Cos(k), c.Y + r * Math.Sin(k));
        for (int s = 0; s < n; s++) seg(At(a0 + (a1 - a0) * s / n), At(a0 + (a1 - a0) * (s + 1) / n));
    }

    private static double G(Entity e, int code, double fallback = 0)
    {
        foreach (var c in e.Codes) if (c.Code == code) return D(c.Value);
        return fallback;
    }

    private static double D(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
}
