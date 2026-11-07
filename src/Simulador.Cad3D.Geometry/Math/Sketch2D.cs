namespace Simulador.Cad3D.Geometry;

/// <summary>
/// Operaciones del editor de bocetos 2D: perfil cerrado de puntos en mm. Cada tramo i (del punto i al siguiente)
/// puede ser un arco, dado por su "abombamiento" (bulge, como en DXF): tan(ángulo del arco / 4).
/// 0 = recto, 1 = semicírculo; positivo = el arco gira en sentido antihorario (queda a la derecha del tramo,
/// es decir, hacia fuera en un perfil antihorario).
/// </summary>
public static class Sketch2D
{
    /// <summary>Tolerancia de cuerda por defecto al convertir arcos en tramos rectos (mm).</summary>
    public const double ArcTolerance = 0.01;

    /// <summary>Abombamiento máximo permitido (algo más que un semicírculo).</summary>
    public const double MaxBulge = 2.0;

    public static double Bulge(IReadOnlyList<double>? bulges, int i) =>
        bulges is not null && i < bulges.Count ? bulges[i] : 0;

    /// <summary>Lista de abombamientos del mismo tamaño que el perfil (rellena con 0).</summary>
    public static List<double> FitBulges(IReadOnlyList<double>? bulges, int count) =>
        Enumerable.Range(0, count).Select(i => Bulge(bulges, i)).ToList();

    /// <summary>Radio y centro del arco de a a b con abombamiento b (null si es recto).</summary>
    public static (Vec2 Center, double Radius)? ArcGeometry(Vec2 a, Vec2 b, double bulge)
    {
        double c = Length(a, b);
        if (Math.Abs(bulge) < 1e-9 || c < 1e-9) return null;
        var d = new Vec2((b.X - a.X) / c, (b.Y - a.Y) / c);
        var left = new Vec2(-d.Y, d.X);
        double off = c / 2 * (1 - bulge * bulge) / (2 * bulge);
        var center = new Vec2((a.X + b.X) / 2 + left.X * off, (a.Y + b.Y) / 2 + left.Y * off);
        return (center, c * (1 + bulge * bulge) / (4 * Math.Abs(bulge)));
    }

    /// <summary>Punto medio del arco (o del tramo recto).</summary>
    public static Vec2 ArcMidpoint(Vec2 a, Vec2 b, double bulge)
    {
        double c = Length(a, b);
        var m = new Vec2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        if (c < 1e-9) return m;
        double sag = bulge * c / 2; // flecha con signo, hacia la derecha del tramo
        return new Vec2(m.X + (b.Y - a.Y) / c * sag, m.Y - (b.X - a.X) / c * sag);
    }

    /// <summary>Abombamiento para que el punto medio del arco quede a la altura de p (proyectado sobre la mediatriz).</summary>
    public static double BulgeThrough(Vec2 a, Vec2 b, Vec2 p)
    {
        double c = Length(a, b);
        if (c < 1e-9) return 0;
        // Distancia con signo de p a la recta ab, positiva a la derecha.
        double sag = ((p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X)) / c;
        return Math.Clamp(2 * sag / c, -MaxBulge, MaxBulge);
    }

    /// <summary>Abombamiento del arco menor de radio r que mantiene el lado (signo) indicado. Null si r es menor que media cuerda.</summary>
    public static double? BulgeFromRadius(Vec2 a, Vec2 b, double radius, double sign)
    {
        double c = Length(a, b);
        if (radius <= 0 || c < 1e-9 || radius < c / 2 - 1e-9) return null;
        double sag = radius - Math.Sqrt(Math.Max(0, radius * radius - c * c / 4));
        return Math.Sign(sign == 0 ? 1 : sign) * 2 * sag / c;
    }

    /// <summary>Longitud del tramo i siguiendo el arco.</summary>
    public static double PathLength(IReadOnlyList<Vec2> pts, IReadOnlyList<double>? bulges, int i)
    {
        var a = pts[i]; var b = pts[(i + 1) % pts.Count];
        double bulge = Bulge(bulges, i);
        return ArcGeometry(a, b, bulge) is { } g ? g.Radius * 4 * Math.Abs(Math.Atan(bulge)) : Length(a, b);
    }

    /// <summary>Convierte el perfil con arcos en un polígono (los arcos se dividen hasta la tolerancia de cuerda).</summary>
    public static List<Vec2> Tessellate(IReadOnlyList<Vec2> pts, IReadOnlyList<double>? bulges, double tolerance = ArcTolerance)
    {
        var result = new List<Vec2>();
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            result.Add(a);
            double bulge = Bulge(bulges, i);
            if (ArcGeometry(a, b, bulge) is not { } g) continue;
            double sweep = 4 * Math.Atan(bulge);
            double step = 2 * Math.Acos(Math.Clamp(1 - tolerance / g.Radius, -1, 1));
            int n = (int)Math.Clamp(Math.Ceiling(Math.Abs(sweep) / Math.Max(step, 1e-6)), 2, 256);
            double start = Math.Atan2(a.Y - g.Center.Y, a.X - g.Center.X);
            for (int k = 1; k < n; k++)
            {
                double ang = start + sweep * k / n;
                result.Add(new Vec2(g.Center.X + g.Radius * Math.Cos(ang), g.Center.Y + g.Radius * Math.Sin(ang)));
            }
        }
        return result;
    }

    /// <summary>
    /// Deja el perfil en sentido antihorario y sin puntos repetidos, conservando los arcos
    /// (si se invierte el recorrido, cada arco pasa al tramo opuesto y cambia de signo).
    /// </summary>
    public static (List<Vec2> Points, List<double> Bulges) Normalize(IReadOnlyList<Vec2> pts, IReadOnlyList<double>? bulges)
    {
        var p = new List<Vec2>();
        var bl = new List<double>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (p.Count > 0 && Length(p[^1], pts[i]) <= 1e-9) { bl[^1] = Bulge(bulges, i); continue; }
            p.Add(pts[i]);
            bl.Add(Bulge(bulges, i));
        }
        while (p.Count > 1 && Length(p[0], p[^1]) <= 1e-9) { p.RemoveAt(p.Count - 1); bl.RemoveAt(bl.Count - 1); }
        if (Polygon2D.SignedArea(Tessellate(p, bl)) < 0)
        {
            int n = p.Count;
            // Tramo i (p[i]→p[i+1]) pasa a ser el tramo n-2-i del perfil invertido.
            var rp = Enumerable.Range(0, n).Select(i => p[n - 1 - i]).ToList();
            var rb = Enumerable.Range(0, n).Select(j => -bl[((n - 2 - j) % n + n) % n]).ToList();
            return (rp, rb);
        }
        return (p, bl);
    }
    /// <summary>Redondea al múltiplo más cercano de la rejilla (0 = sin ajuste).</summary>
    public static Vec2 Snap(Vec2 p, double grid) =>
        grid <= 0 ? p : new Vec2(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);

    public static double Length(Vec2 a, Vec2 b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    /// <summary>Longitud del tramo que va del punto i al siguiente (el último cierra con el primero).</summary>
    public static double SegmentLength(IReadOnlyList<Vec2> pts, int i) => Length(pts[i], pts[(i + 1) % pts.Count]);

    /// <summary>
    /// Fija la longitud del tramo i moviendo su punto final en la misma dirección.
    /// Devuelve false si el tramo tiene longitud cero (no hay dirección).
    /// </summary>
    public static bool SetSegmentLength(List<Vec2> pts, int i, double length)
    {
        int j = (i + 1) % pts.Count;
        double l = SegmentLength(pts, i);
        if (l < 1e-9 || length <= 0) return false;
        var d = pts[j] - pts[i];
        pts[j] = new Vec2(pts[i].X + d.X * length / l, pts[i].Y + d.Y * length / l);
        return true;
    }

    /// <summary>Distancia de p al segmento ab y parámetro t (0..1) del punto más cercano.</summary>
    public static (double Distance, double T) DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return (Length(p, new Vec2(a.X + ab.X * t, a.Y + ab.Y * t)), t);
    }

    /// <summary>Índice del tramo más cercano a p, o -1 si ninguno está a menos de maxDistance.</summary>
    public static int NearestSegment(IReadOnlyList<Vec2> pts, Vec2 p, double maxDistance)
    {
        int best = -1;
        double bestD = maxDistance;
        for (int i = 0; i < pts.Count; i++)
        {
            var (d, _) = DistanceToSegment(p, pts[i], pts[(i + 1) % pts.Count]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Índice del punto más cercano a p, o -1 si ninguno está a menos de maxDistance.</summary>
    public static int NearestPoint(IReadOnlyList<Vec2> pts, Vec2 p, double maxDistance)
    {
        int best = -1;
        double bestD = maxDistance;
        for (int i = 0; i < pts.Count; i++)
        {
            double d = Length(pts[i], p);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Añade un punto: si cae cerca de un tramo se inserta en él; si no, se inserta en el tramo más cercano
    /// (así el perfil sigue cerrado). Devuelve el índice del punto nuevo.
    /// </summary>
    public static int AddPoint(List<Vec2> pts, Vec2 p)
    {
        if (pts.Count < 2) { pts.Add(p); return pts.Count - 1; }
        int seg = NearestSegment(pts, p, double.MaxValue);
        pts.Insert(seg + 1, p);
        return seg + 1;
    }

    /// <summary>True si dos tramos no contiguos se cruzan (el perfil no sirve para extruir).</summary>
    public static bool SelfIntersects(IReadOnlyList<Vec2> pts)
    {
        int n = pts.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue; // contiguos
                if (SegmentsCross(pts[i], pts[(i + 1) % n], pts[j], pts[(j + 1) % n])) return true;
            }
        return false;
    }

    private static bool SegmentsCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a);
        double d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
        if (((d1 > 1e-12 && d2 < -1e-12) || (d1 < -1e-12 && d2 > 1e-12)) &&
            ((d3 > 1e-12 && d4 < -1e-12) || (d3 < -1e-12 && d4 > 1e-12))) return true;
        // Colineales que se solapan, o un extremo apoyado en el otro tramo.
        return (Math.Abs(d1) <= 1e-12 && OnSegment(a, b, c)) || (Math.Abs(d2) <= 1e-12 && OnSegment(a, b, d)) ||
               (Math.Abs(d3) <= 1e-12 && OnSegment(c, d, a)) || (Math.Abs(d4) <= 1e-12 && OnSegment(c, d, b));
    }

    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 p) =>
        p.X >= Math.Min(a.X, b.X) - 1e-9 && p.X <= Math.Max(a.X, b.X) + 1e-9 &&
        p.Y >= Math.Min(a.Y, b.Y) - 1e-9 && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;

    /// <summary>Comprueba un perfil con arcos (sobre su versión dividida en tramos rectos).</summary>
    public static string? Validate(IReadOnlyList<Vec2> pts, IReadOnlyList<double>? bulges, bool revolve)
    {
        if (pts.Count < 2) return "Hacen falta al menos 2 puntos con un arco, o 3 sin arcos.";
        if (bulges is null || bulges.All(b => Math.Abs(b) < 1e-9)) return Validate(pts, revolve);
        return Validate(Tessellate(pts, bulges, 0.05), revolve);
    }

    /// <summary>Comprueba que el perfil sirve para extruir o revolucionar. Devuelve null si es válido o el motivo.</summary>
    public static string? Validate(IReadOnlyList<Vec2> pts, bool revolve)
    {
        if (pts.Count < 3) return "Hacen falta al menos 3 puntos.";
        if (Math.Abs(Polygon2D.SignedArea(pts)) < 1e-6) return "El perfil no tiene área (puntos en línea).";
        if (SelfIntersects(pts)) return "Hay tramos que se cruzan.";
        if (revolve && pts.Any(p => p.X < -1e-9)) return "En una revolución el radio (X) no puede ser negativo.";
        return null;
    }

    /// <summary>
    /// Redondea la esquina i con un arco tangente de radio r: el punto i se sustituye por dos puntos y el tramo
    /// entre ellos pasa a ser un arco. Los dos tramos que llegan a la esquina tienen que ser rectos y lo bastante largos.
    /// Devuelve null si se pudo o el motivo.
    /// </summary>
    public static string? FilletCorner(List<Vec2> pts, List<double> bulges, int i, double radius)
    {
        int n = pts.Count;
        if (n < 3) return "Hacen falta al menos 3 puntos.";
        if (radius <= 0) return "El radio tiene que ser mayor que 0.";
        while (bulges.Count < n) bulges.Add(0);
        int prev = (i - 1 + n) % n, next = (i + 1) % n;
        if (Math.Abs(bulges[prev]) > 1e-9 || Math.Abs(bulges[i]) > 1e-9) return "Los dos tramos de la esquina tienen que ser rectos.";
        var v = pts[i];
        double lIn = Length(pts[prev], v), lOut = Length(v, pts[next]);
        if (lIn < 1e-9 || lOut < 1e-9) return "La esquina tiene un tramo de longitud cero.";
        var dIn = new Vec2((v.X - pts[prev].X) / lIn, (v.Y - pts[prev].Y) / lIn);
        var dOut = new Vec2((pts[next].X - v.X) / lOut, (pts[next].Y - v.Y) / lOut);
        double turn = Math.Atan2(dIn.Cross(dOut), dIn.X * dOut.X + dIn.Y * dOut.Y); // giro con signo
        if (Math.Abs(turn) < 1e-6) return "Los dos tramos están en línea: no hay esquina que redondear.";
        if (Math.Abs(turn) > Math.PI - 1e-6) return "La esquina es un pico de 0°: no se puede redondear.";
        double t = radius * Math.Tan(Math.Abs(turn) / 2);
        if (t > lIn + 1e-9 || t > lOut + 1e-9) return $"El radio es demasiado grande para esta esquina (máximo {Math.Min(lIn, lOut) / Math.Tan(Math.Abs(turn) / 2):0.##} mm).";
        var p1 = new Vec2(v.X - dIn.X * t, v.Y - dIn.Y * t);
        var p2 = new Vec2(v.X + dOut.X * t, v.Y + dOut.Y * t);
        // Giro a la izquierda = arco antihorario = abombamiento positivo.
        double bulge = Math.Tan(turn / 4);
        pts[i] = p1;
        pts.Insert(i + 1, p2);
        bulges[i] = bulge;
        bulges.Insert(i + 1, 0);
        // Si el arco llega justo al punto vecino, sobra el punto repetido (y su tramo de longitud cero).
        if (Length(p2, pts[(i + 2) % pts.Count]) < 1e-9) { pts.RemoveAt(i + 1); bulges.RemoveAt(i + 1); }
        int before = (i - 1 + pts.Count) % pts.Count;
        if (pts.Count > 2 && Length(pts[before], p1) < 1e-9) { pts.RemoveAt(before); bulges.RemoveAt(before); }
        return null;
    }

    /// <summary>Rectángulo con esquinas redondeadas hecho con arcos de verdad (4 tramos rectos + 4 arcos).</summary>
    public static (List<Vec2> Points, List<double> Bulges) RoundedRectangle(double w, double h, double r)
    {
        var pts = Rectangle(w, h);
        var bl = new List<double> { 0, 0, 0, 0 };
        r = Math.Min(r, Math.Min(w, h) / 2);
        if (r <= 1e-9) return (pts, bl);
        for (int k = 3; k >= 0; k--) FilletCorner(pts, bl, k * 1, r);
        return (pts, bl);
    }

    /// <summary>Círculo exacto: dos semicírculos.</summary>
    public static (List<Vec2> Points, List<double> Bulges) CircleArcs(Vec2 c, double radius) =>
        (new List<Vec2> { new(c.X + radius, c.Y), new(c.X - radius, c.Y) }, new List<double> { 1, 1 });

    /// <summary>Rectángulo con la esquina inferior izquierda en el origen.</summary>
    public static List<Vec2> Rectangle(double w, double h) => new() { new(0, 0), new(w, 0), new(w, h), new(0, h) };

    /// <summary>Polígono regular centrado en c.</summary>
    public static List<Vec2> RegularPolygon(Vec2 c, double radius, int sides) =>
        Polygon2D.Circle(radius, Math.Max(3, sides)).Select(p => p + c).ToList();
}
