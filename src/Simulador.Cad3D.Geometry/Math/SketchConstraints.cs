using System.Text.Json.Serialization;

namespace Simulador.Cad3D.Geometry;

/// <summary>Tipo de regla geométrica de un boceto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConstraintKind
{
    /// <summary>El tramo A queda horizontal.</summary>
    Horizontal,
    /// <summary>El tramo A queda vertical.</summary>
    Vertical,
    /// <summary>Los tramos A y B quedan paralelos.</summary>
    Paralelo,
    /// <summary>Los tramos A y B forman 90°.</summary>
    Perpendicular,
    /// <summary>Los tramos A y B miden lo mismo.</summary>
    IgualLongitud,
    /// <summary>El tramo A mide <see cref="SketchConstraint.Value"/> mm (cota fija).</summary>
    Longitud,
    /// <summary>El punto A no se mueve.</summary>
    Fijo,
}

/// <summary>
/// Regla de un boceto. A y B son índices de tramo (tramo i: del punto i al siguiente),
/// salvo en <see cref="ConstraintKind.Fijo"/>, donde A es el índice del punto.
/// En los arcos la regla se aplica a la cuerda (la recta entre sus extremos).
/// </summary>
public sealed record SketchConstraint(ConstraintKind Kind, int A, int B = -1, double Value = 0)
{
    public bool TwoSegments => Kind is ConstraintKind.Paralelo or ConstraintKind.Perpendicular or ConstraintKind.IgualLongitud;

    public string Describe() => Kind switch
    {
        ConstraintKind.Horizontal => $"Tramo {A + 1} horizontal",
        ConstraintKind.Vertical => $"Tramo {A + 1} vertical",
        ConstraintKind.Paralelo => $"Tramos {A + 1} y {B + 1} paralelos",
        ConstraintKind.Perpendicular => $"Tramos {A + 1} y {B + 1} perpendiculares",
        ConstraintKind.IgualLongitud => $"Tramos {A + 1} y {B + 1} iguales",
        ConstraintKind.Longitud => $"Tramo {A + 1} mide {Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} mm",
        _ => $"Punto {A + 1} fijo",
    };
}

/// <summary>
/// Resolución de reglas con Gauss-Newton de paso mínimo (Levenberg-Marquardt): en cada paso se mueven
/// los puntos lo menos posible para acercarse a cumplir todas las reglas a la vez, sin tocar los puntos fijos
/// ni el que el usuario está arrastrando.
/// </summary>
public static class SketchSolver
{
    public const double Tolerance = 1e-6;

    /// <summary>
    /// Ajusta <paramref name="pts"/> para cumplir las reglas. <paramref name="held"/>: puntos que no deben moverse
    /// además de los fijos (p. ej. el que se arrastra). Devuelve el mayor error que queda (0 = todo se cumple).
    /// </summary>
    public static double Solve(List<Vec2> pts, IReadOnlyList<SketchConstraint> constraints, IEnumerable<int>? held = null, int maxIterations = 100)
    {
        int n = pts.Count;
        if (n < 2 || constraints.Count == 0) return 0;
        var valid = constraints.Where(c => IsValid(c, n) && c.Kind != ConstraintKind.Fijo).ToList();
        if (valid.Count == 0) return 0;
        var locked = new bool[n];
        foreach (var c in constraints) if (c.Kind == ConstraintKind.Fijo && c.A >= 0 && c.A < n) locked[c.A] = true;
        var heldList = held?.Where(h => h >= 0 && h < n).ToList() ?? new List<int>();
        foreach (var h in heldList) locked[h] = true;

        var start = pts.ToList();
        Run(pts, valid, locked, maxIterations);
        double err = Residual(pts, valid);
        // Si el punto arrastrado impide cumplirlas, se deja libre: mejor moverlo un poco que romper una regla.
        if (err > 1e-4 && heldList.Count > 0)
        {
            foreach (var h in heldList) locked[h] = constraints.Any(c => c.Kind == ConstraintKind.Fijo && c.A == h);
            Run(pts, valid, locked, maxIterations);
            err = Residual(pts, valid);
        }
        // Reglas que solo se cumplen aplastando un tramo (p. ej. horizontal y vertical a la vez): no valen.
        bool collapsed = valid.Any(c => Len(pts[(c.A + 1) % n] - pts[c.A]) < 1e-4
            || (c.TwoSegments && Len(pts[(c.B + 1) % n] - pts[c.B]) < 1e-4));
        if (collapsed)
        {
            for (int i = 0; i < n; i++) pts[i] = start[i];
            return Math.Max(1, Residual(pts, valid));
        }
        return err;
    }

    private static void Run(List<Vec2> pts, List<SketchConstraint> cons, bool[] locked, int maxIterations)
    {
        int n = pts.Count, m = cons.Count;
        var vars = Enumerable.Range(0, n).Where(i => !locked[i]).ToList();
        int nv = vars.Count * 2;
        if (nv == 0) return;
        var x = new double[2 * n];
        for (int i = 0; i < n; i++) { x[2 * i] = pts[i].X; x[2 * i + 1] = pts[i].Y; }

        double lambda = 1e-6;
        var r = Residuals(x, n, cons);
        double norm = Norm2(r);
        for (int it = 0; it < maxIterations && Math.Sqrt(norm) > Tolerance * 0.1; it++)
        {
            // Jacobiano numérico (los bocetos tienen pocos puntos).
            var J = new double[m, nv];
            for (int v = 0; v < nv; v++)
            {
                int xi = 2 * vars[v / 2] + v % 2;
                double h = 1e-7 * Math.Max(1, Math.Abs(x[xi]));
                double old = x[xi];
                x[xi] = old + h; var rp = Residuals(x, n, cons);
                x[xi] = old - h; var rm = Residuals(x, n, cons);
                x[xi] = old;
                for (int k = 0; k < m; k++) J[k, v] = (rp[k] - rm[k]) / (2 * h);
            }
            bool improved = false;
            for (int tries = 0; tries < 12 && !improved; tries++)
            {
                // Paso de norma mínima: Δ = Jᵀ (J Jᵀ + λI)⁻¹ (−r).
                var A = new double[m, m];
                for (int a = 0; a < m; a++)
                    for (int b = a; b < m; b++)
                    {
                        double sum = 0;
                        for (int v = 0; v < nv; v++) sum += J[a, v] * J[b, v];
                        A[a, b] = A[b, a] = sum;
                    }
                for (int a = 0; a < m; a++) A[a, a] += lambda * (1 + A[a, a]);
                var y = SolveLinear(A, r.Select(v => -v).ToArray());
                if (y is null) { lambda *= 10; continue; }
                var trial = (double[])x.Clone();
                for (int v = 0; v < nv; v++)
                {
                    double d = 0;
                    for (int k = 0; k < m; k++) d += J[k, v] * y[k];
                    trial[2 * vars[v / 2] + v % 2] += d;
                }
                var rt = Residuals(trial, n, cons);
                double nt = Norm2(rt);
                if (nt < norm)
                {
                    x = trial; r = rt; norm = nt; improved = true;
                    lambda = Math.Max(1e-12, lambda / 10);
                }
                else lambda *= 10;
            }
            if (!improved) break;
        }
        for (int i = 0; i < n; i++) pts[i] = new Vec2(x[2 * i], x[2 * i + 1]);
    }

    private static double[] Residuals(double[] x, int n, List<SketchConstraint> cons)
    {
        var r = new double[cons.Count];
        for (int k = 0; k < cons.Count; k++)
        {
            var c = cons[k];
            var (ax, ay) = D(x, n, c.A);
            double la = Math.Sqrt(ax * ax + ay * ay);
            switch (c.Kind)
            {
                case ConstraintKind.Horizontal: r[k] = ay; break;
                case ConstraintKind.Vertical: r[k] = ax; break;
                case ConstraintKind.Longitud: r[k] = la - c.Value; break;
                default:
                {
                    var (bx, by) = D(x, n, c.B);
                    double lb = Math.Sqrt(bx * bx + by * by);
                    double s = Math.Sqrt(Math.Max(la * lb, 1e-12));
                    r[k] = c.Kind switch
                    {
                        ConstraintKind.Paralelo => (ax * by - ay * bx) / s,
                        ConstraintKind.Perpendicular => (ax * bx + ay * by) / s,
                        _ => la - lb,
                    };
                    break;
                }
            }
        }
        return r;
    }

    private static (double, double) D(double[] x, int n, int seg)
    {
        int j = (seg + 1) % n;
        return (x[2 * j] - x[2 * seg], x[2 * j + 1] - x[2 * seg + 1]);
    }

    private static double Norm2(double[] r) => r.Sum(v => v * v);

    private static double[]? SolveLinear(double[,] A, double[] b)
    {
        int m = b.Length;
        var M = (double[,])A.Clone();
        var y = (double[])b.Clone();
        for (int col = 0; col < m; col++)
        {
            int piv = col;
            for (int r2 = col + 1; r2 < m; r2++) if (Math.Abs(M[r2, col]) > Math.Abs(M[piv, col])) piv = r2;
            if (Math.Abs(M[piv, col]) < 1e-14) return null;
            if (piv != col)
            {
                for (int c2 = 0; c2 < m; c2++) (M[col, c2], M[piv, c2]) = (M[piv, c2], M[col, c2]);
                (y[col], y[piv]) = (y[piv], y[col]);
            }
            for (int r2 = col + 1; r2 < m; r2++)
            {
                double f = M[r2, col] / M[col, col];
                if (f == 0) continue;
                for (int c2 = col; c2 < m; c2++) M[r2, c2] -= f * M[col, c2];
                y[r2] -= f * y[col];
            }
        }
        for (int r2 = m - 1; r2 >= 0; r2--)
        {
            double sum = y[r2];
            for (int c2 = r2 + 1; c2 < m; c2++) sum -= M[r2, c2] * y[c2];
            y[r2] = sum / M[r2, r2];
        }
        return y;
    }

    public static bool IsValid(SketchConstraint c, int n)
    {
        if (c.A < 0 || c.A >= n) return false;
        if (c.TwoSegments) return c.B >= 0 && c.B < n && c.B != c.A;
        if (c.Kind == ConstraintKind.Longitud) return c.Value > 0;
        return true;
    }

    /// <summary>Error de una regla: mm para posiciones y longitudes, mm de desvío por tramo para ángulos.</summary>
    public static double Error(IReadOnlyList<Vec2> pts, SketchConstraint c)
    {
        int n = pts.Count;
        var (a0, a1) = Seg(pts, c.A);
        var da = a1 - a0;
        double la = Len(da);
        switch (c.Kind)
        {
            case ConstraintKind.Horizontal: return Math.Abs(da.Y);
            case ConstraintKind.Vertical: return Math.Abs(da.X);
            case ConstraintKind.Longitud: return Math.Abs(la - c.Value);
            case ConstraintKind.Fijo: return 0;
        }
        var (b0, b1) = Seg(pts, c.B);
        var db = b1 - b0;
        double lb = Len(db);
        if (la < 1e-12 || lb < 1e-12) return c.Kind == ConstraintKind.IgualLongitud ? Math.Abs(la - lb) : 0;
        return c.Kind switch
        {
            ConstraintKind.Paralelo => Math.Abs(da.Cross(db)) / Math.Max(la, lb),
            ConstraintKind.Perpendicular => Math.Abs(da.X * db.X + da.Y * db.Y) / Math.Max(la, lb),
            _ => Math.Abs(la - lb),
        };
    }

    public static double Residual(IReadOnlyList<Vec2> pts, IEnumerable<SketchConstraint> constraints) =>
        constraints.Where(c => IsValid(c, pts.Count)).Select(c => Error(pts, c)).DefaultIfEmpty(0).Max();

    private static (Vec2, Vec2) Seg(IReadOnlyList<Vec2> pts, int i) => (pts[i], pts[(i + 1) % pts.Count]);
    private static double Len(Vec2 d) => Math.Sqrt(d.X * d.X + d.Y * d.Y);

    // ---- Mantener los índices al cambiar el boceto ----

    /// <summary>Se insertó un punto en la posición <paramref name="index"/> (el tramo index-1 quedó partido en dos).</summary>
    public static List<SketchConstraint> AfterInsert(IEnumerable<SketchConstraint> constraints, int index)
    {
        int split = index - 1;
        var result = new List<SketchConstraint>();
        foreach (var c in constraints)
        {
            if (c.Kind == ConstraintKind.Fijo) { result.Add(c with { A = c.A >= index ? c.A + 1 : c.A }); continue; }
            // El tramo partido pierde sus reglas; los siguientes avanzan un número.
            if (c.A == split || (c.TwoSegments && c.B == split)) continue;
            int Shift(int s) => s >= index ? s + 1 : s;
            result.Add(c with { A = Shift(c.A), B = c.TwoSegments ? Shift(c.B) : c.B });
        }
        return result;
    }

    /// <summary>Se borró el punto <paramref name="index"/> (los tramos index-1 e index se juntaron).</summary>
    public static List<SketchConstraint> AfterRemove(IEnumerable<SketchConstraint> constraints, int index, int oldCount)
    {
        int prev = (index - 1 + oldCount) % oldCount;
        var result = new List<SketchConstraint>();
        foreach (var c in constraints)
        {
            if (c.Kind == ConstraintKind.Fijo)
            {
                if (c.A == index) continue;
                result.Add(c with { A = c.A > index ? c.A - 1 : c.A });
                continue;
            }
            if (c.A == index || c.A == prev || (c.TwoSegments && (c.B == index || c.B == prev))) continue;
            int Shift(int s) => s > index ? s - 1 : s;
            result.Add(c with { A = Shift(c.A), B = c.TwoSegments ? Shift(c.B) : c.B });
        }
        return result;
    }

    /// <summary>
    /// Se redondeó la esquina del punto <paramref name="corner"/>: el punto pasa a ser dos (inicio y fin del arco).
    /// Las reglas de dirección de los tramos vecinos siguen valiendo; las de longitud y el punto fijo de la esquina no.
    /// </summary>
    public static List<SketchConstraint> AfterFillet(IEnumerable<SketchConstraint> constraints, int corner, int oldCount)
    {
        int prev = (corner - 1 + oldCount) % oldCount;
        var result = new List<SketchConstraint>();
        foreach (var c in constraints)
        {
            if (c.Kind == ConstraintKind.Fijo)
            {
                if (c.A == corner) continue;
                result.Add(c with { A = c.A > corner ? c.A + 1 : c.A });
                continue;
            }
            bool touches = c.A == corner || c.A == prev || (c.TwoSegments && (c.B == corner || c.B == prev));
            if (touches && c.Kind is ConstraintKind.Longitud or ConstraintKind.IgualLongitud) continue;
            int Shift(int s) => s >= corner ? s + 1 : s;
            result.Add(c with { A = Shift(c.A), B = c.TwoSegments ? Shift(c.B) : c.B });
        }
        return result;
    }

    /// <summary>El perfil se invirtió (de horario a antihorario): el punto i pasa a n-1-i y el tramo i a n-2-i.</summary>
    public static List<SketchConstraint> AfterReverse(IEnumerable<SketchConstraint> constraints, int n)
    {
        int S(int s) => ((n - 2 - s) % n + n) % n;
        return constraints.Select(c => c.Kind == ConstraintKind.Fijo
            ? c with { A = n - 1 - c.A }
            : c with { A = S(c.A), B = c.TwoSegments ? S(c.B) : c.B }).ToList();
    }
}
