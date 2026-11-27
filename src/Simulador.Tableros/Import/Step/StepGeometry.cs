namespace Simulador.Tableros.Import.Step;

/// <summary>Vector 3D en doble precisión (los STEP pueden venir en metros o pulgadas).</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static V3 operator *(double k, V3 a) => a * k;
    public static V3 operator /(V3 a, double k) => new(a.X / k, a.Y / k, a.Z / k);
    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public V3 Normalized() { var l = Length; return l > 1e-300 ? this / l : new V3(0, 0, 1); }
    public static readonly V3 Zero = new(0, 0, 0);
}

/// <summary>Sistema de coordenadas (origen y ejes ortonormales); también hace de transformación rígida (con escala opcional).</summary>
public readonly record struct Frame(V3 O, V3 X, V3 Y, V3 Z)
{
    public static readonly Frame Identity = new(V3.Zero, new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
    public static Frame From(V3 o, V3? axis, V3? refDir)
    {
        var z = (axis ?? new V3(0, 0, 1)).Normalized();
        var r = refDir ?? (Math.Abs(z.X) < 0.9 ? new V3(1, 0, 0) : new V3(0, 1, 0));
        var x = (r - z * r.Dot(z));
        if (x.Length < 1e-12) x = Math.Abs(z.X) < 0.9 ? new V3(1, 0, 0) - z * z.X : new V3(0, 1, 0) - z * z.Y;
        x = x.Normalized();
        return new Frame(o, x, z.Cross(x), z);
    }
    /// <summary>Lleva un punto de coordenadas locales a las del padre.</summary>
    public V3 Apply(V3 p) => O + X * p.X + Y * p.Y + Z * p.Z;
    public V3 ApplyDir(V3 d) => X * d.X + Y * d.Y + Z * d.Z;
    /// <summary>Lleva un punto del padre a coordenadas locales (ejes ortogonales, posiblemente escalados).</summary>
    public V3 Inverse(V3 p)
    {
        var d = p - O;
        return new V3(d.Dot(X) / X.Dot(X), d.Dot(Y) / Y.Dot(Y), d.Dot(Z) / Z.Dot(Z));
    }
    /// <summary>Composición: primero <paramref name="inner"/>, luego esta.</summary>
    public Frame Then(Frame inner) => new(Apply(inner.O), ApplyDir(inner.X), ApplyDir(inner.Y), ApplyDir(inner.Z));
    public Frame Inverted()
    {
        // Ejes ortogonales (posible escala uniforme): la inversa es la traspuesta dividida por la escala².
        double sx = X.Dot(X), sy = Y.Dot(Y), sz = Z.Dot(Z);
        var ix = new V3(X.X / sx, Y.X / sy, Z.X / sz);
        var iy = new V3(X.Y / sx, Y.Y / sy, Z.Y / sz);
        var iz = new V3(X.Z / sx, Y.Z / sy, Z.Z / sz);
        var o = -(ix * O.X + iy * O.Y + iz * O.Z);
        return new Frame(o, ix, iy, iz);
    }
}

/// <summary>Curva paramétrica 3D.</summary>
public abstract class Curve3
{
    public abstract V3 At(double t);
    public virtual double Period => 0;
    public abstract (double T0, double T1) Range { get; }
    /// <summary>Parámetro del punto de la curva más cercano a <paramref name="p"/>.</summary>
    public virtual double Param(V3 p)
    {
        var (a, b) = Range;
        double best = a, bd = double.MaxValue;
        const int n = 64;
        for (var i = 0; i <= n; i++)
        {
            var t = a + (b - a) * i / n;
            var d = (At(t) - p).Length;
            if (d < bd) { bd = d; best = t; }
        }
        // Afinar con búsqueda dorada alrededor del mejor.
        double lo = Math.Max(a, best - (b - a) / n), hi = Math.Min(b, best + (b - a) / n);
        for (var k = 0; k < 40; k++)
        {
            double m1 = lo + (hi - lo) * 0.382, m2 = lo + (hi - lo) * 0.618;
            if ((At(m1) - p).Length < (At(m2) - p).Length) hi = m2; else lo = m1;
        }
        return (lo + hi) / 2;
    }
}

public sealed class LineCurve(V3 p, V3 dir) : Curve3
{
    public V3 P => p;
    public V3 Dir => dir;
    public override V3 At(double t) => p + dir * t;
    public override (double T0, double T1) Range => (-1e6, 1e6);
    public override double Param(V3 q) => (q - p).Dot(dir) / Math.Max(dir.Dot(dir), 1e-300);
}

public sealed class EllipseCurve(Frame f, double r1, double r2) : Curve3
{
    public Frame F => f;
    public double R1 => r1;
    public double R2 => r2;
    public override V3 At(double t) => f.O + f.X * (r1 * Math.Cos(t)) + f.Y * (r2 * Math.Sin(t));
    public override double Period => 2 * Math.PI;
    public override (double T0, double T1) Range => (0, 2 * Math.PI);
    public override double Param(V3 q)
    {
        var d = q - f.O;
        var t = Math.Atan2(d.Dot(f.Y) / Math.Max(r2, 1e-300), d.Dot(f.X) / Math.Max(r1, 1e-300));
        return t < 0 ? t + 2 * Math.PI : t;
    }
}

public sealed class PolylineCurve(List<V3> pts) : Curve3
{
    public override V3 At(double t)
    {
        if (pts.Count == 0) return V3.Zero;
        var i = (int)Math.Clamp(Math.Floor(t), 0, pts.Count - 2);
        var f = Math.Clamp(t - i, 0, 1);
        return pts.Count == 1 ? pts[0] : pts[i] + (pts[i + 1] - pts[i]) * f;
    }
    public override (double T0, double T1) Range => (0, Math.Max(0, pts.Count - 1));
    public IReadOnlyList<V3> Points => pts;
}

/// <summary>B-spline (racional o no) en 3D, evaluada con De Boor.</summary>
public sealed class BSplineCurve : Curve3
{
    private readonly int _deg;
    private readonly V3[] _p;
    private readonly double[] _w;
    private readonly double[] _k;
    public bool Closed { get; }

    public BSplineCurve(int degree, List<V3> pts, double[]? weights, List<int> mults, List<double> knots, bool closed)
    {
        _deg = degree;
        _p = pts.ToArray();
        _w = weights ?? Enumerable.Repeat(1.0, pts.Count).ToArray();
        _k = Expand(mults, knots);
        Closed = closed;
    }

    public int Spans => Math.Max(1, _k.Length - 2 * _deg - 1);

    internal static double[] Expand(List<int> mults, List<double> knots)
    {
        var k = new List<double>();
        for (var i = 0; i < knots.Count && i < mults.Count; i++) for (var j = 0; j < mults[i]; j++) k.Add(knots[i]);
        return k.ToArray();
    }

    public override (double T0, double T1) Range => (_k[_deg], _k[_k.Length - _deg - 1]);

    public override V3 At(double t)
    {
        var n = _p.Length;
        var span = BSplineSurface.Span(_k, _deg, n, t);
        Span<double> b = stackalloc double[_deg + 1];
        BSplineSurface.Basis(_k, _deg, span, Math.Clamp(t, _k[_deg], _k[_k.Length - _deg - 1]), b);
        double x = 0, y = 0, z = 0, w = 0;
        for (var i = 0; i <= _deg; i++)
        {
            var ii = Math.Clamp(span - _deg + i, 0, n - 1);
            var k = b[i] * _w[ii];
            x += _p[ii].X * k; y += _p[ii].Y * k; z += _p[ii].Z * k; w += k;
        }
        return w == 0 ? new V3(x, y, z) : new V3(x / w, y / w, z / w);
    }

    internal static V3 DeBoor(int deg, double[] k, double t, Func<int, V3> P, Func<int, double> W, int n)
    {
        var lo = k[deg];
        var hi = k[k.Length - deg - 1];
        t = Math.Clamp(t, lo, hi);
        var s = deg;
        while (s < n - 1 && t >= k[s + 1]) s++;
        Span<double> wx = stackalloc double[deg + 1], wy = stackalloc double[deg + 1], wz = stackalloc double[deg + 1], ww = stackalloc double[deg + 1];
        for (var j = 0; j <= deg; j++)
        {
            var idx = Math.Clamp(s - deg + j, 0, n - 1);
            var p = P(idx);
            var w = W(idx);
            wx[j] = p.X * w; wy[j] = p.Y * w; wz[j] = p.Z * w; ww[j] = w;
        }
        for (var r = 1; r <= deg; r++)
            for (var j = deg; j >= r; j--)
            {
                var i = s - deg + j;
                var den = k[Math.Min(i + deg - r + 1, k.Length - 1)] - k[Math.Clamp(i, 0, k.Length - 1)];
                var a = den == 0 ? 0 : (t - k[Math.Clamp(i, 0, k.Length - 1)]) / den;
                wx[j] = (1 - a) * wx[j - 1] + a * wx[j];
                wy[j] = (1 - a) * wy[j - 1] + a * wy[j];
                wz[j] = (1 - a) * wz[j - 1] + a * wz[j];
                ww[j] = (1 - a) * ww[j - 1] + a * ww[j];
            }
        var wd = ww[deg] == 0 ? 1 : ww[deg];
        return new V3(wx[deg] / wd, wy[deg] / wd, wz[deg] / wd);
    }
}

/// <summary>Superficie paramétrica: punto (u, v) y su inversa aproximada.</summary>
public abstract class Surface
{
    public abstract V3 At(double u, double v);
    /// <summary>Periodo en u (0 = no periódica).</summary>
    public virtual double PeriodU => 0;
    public virtual double PeriodV => 0;
    public abstract (double U, double V) Param(V3 p, (double U, double V)? hint);
    /// <summary>Si es plana, la cuadrícula uv no necesita refinarse.</summary>
    public virtual bool IsPlanar => false;
    /// <summary>Valor de v del polo hacia el que se cierra una cara limitada por un solo lazo que da la vuelta en u.</summary>
    public virtual double? PoleV(bool upper) => null;
}

public sealed class PlaneSurface(Frame f) : Surface
{
    public override V3 At(double u, double v) => f.O + f.X * u + f.Y * v;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint) { var d = p - f.O; return (d.Dot(f.X), d.Dot(f.Y)); }
    public override bool IsPlanar => true;
}

public sealed class CylinderSurface(Frame f, double r) : Surface
{
    public override V3 At(double u, double v) => f.O + f.X * (r * Math.Cos(u)) + f.Y * (r * Math.Sin(u)) + f.Z * v;
    public override double PeriodU => 2 * Math.PI;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    { var d = p - f.O; return (Math.Atan2(d.Dot(f.Y), d.Dot(f.X)), d.Dot(f.Z)); }
}

public sealed class ConeSurface(Frame f, double r, double semiAngle) : Surface
{
    private readonly double _tan = Math.Tan(semiAngle);
    public override V3 At(double u, double v)
    {
        var rr = r + v * _tan;
        return f.O + f.X * (rr * Math.Cos(u)) + f.Y * (rr * Math.Sin(u)) + f.Z * v;
    }
    public override double PeriodU => 2 * Math.PI;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    { var d = p - f.O; return (Math.Atan2(d.Dot(f.Y), d.Dot(f.X)), d.Dot(f.Z)); }
    public override double? PoleV(bool upper) => Math.Abs(_tan) < 1e-12 ? null : -r / _tan;
}

public sealed class SphereSurface(Frame f, double r) : Surface
{
    public override V3 At(double u, double v)
    {
        var c = Math.Cos(v);
        return f.O + f.X * (r * c * Math.Cos(u)) + f.Y * (r * c * Math.Sin(u)) + f.Z * (r * Math.Sin(v));
    }
    public override double PeriodU => 2 * Math.PI;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    {
        var d = p - f.O;
        return (Math.Atan2(d.Dot(f.Y), d.Dot(f.X)), Math.Asin(Math.Clamp(d.Dot(f.Z) / Math.Max(r, 1e-300), -1, 1)));
    }
    public override double? PoleV(bool upper) => upper ? Math.PI / 2 : -Math.PI / 2;
}

public sealed class TorusSurface(Frame f, double major, double minor) : Surface
{
    public override V3 At(double u, double v)
    {
        var rr = major + minor * Math.Cos(v);
        return f.O + f.X * (rr * Math.Cos(u)) + f.Y * (rr * Math.Sin(u)) + f.Z * (minor * Math.Sin(v));
    }
    public override double PeriodU => 2 * Math.PI;
    public override double PeriodV => 2 * Math.PI;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    {
        var d = p - f.O;
        double x = d.Dot(f.X), y = d.Dot(f.Y), z = d.Dot(f.Z);
        var u = Math.Atan2(y, x);
        var w = Math.Sqrt(x * x + y * y) - major;
        return (u, Math.Atan2(z, w));
    }
}

/// <summary>Superficie de revolución: la curva <c>c(v)</c> girada un ángulo u alrededor del eje.</summary>
public sealed class RevolutionSurface(Curve3 c, V3 o, V3 axis) : Surface
{
    private readonly V3 _a = axis.Normalized();
    public override V3 At(double u, double v) => Rotate(c.At(v), u);
    private V3 Rotate(V3 p, double ang)
    {
        var d = p - o;
        var along = _a * d.Dot(_a);
        var perp = d - along;
        return o + along + perp * Math.Cos(ang) + _a.Cross(perp) * Math.Sin(ang);
    }
    public override double PeriodU => 2 * Math.PI;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    {
        var (t0, t1) = c.Range;
        var refP = c.At((t0 + t1) / 2);
        var r0 = refP - o; r0 -= _a * r0.Dot(_a);
        if (r0.Length < 1e-12) { refP = c.At(t0 + (t1 - t0) * 0.3); r0 = refP - o; r0 -= _a * r0.Dot(_a); }
        var d = p - o; var rp = d - _a * d.Dot(_a);
        var u = Math.Atan2(_a.Dot(r0.Cross(rp)), r0.Dot(rp));
        var back = Rotate(p, -u);
        return (u, c.Param(back));
    }
}

/// <summary>Superficie de extrusión lineal: la curva <c>c(u)</c> desplazada v veces el vector.</summary>
public sealed class ExtrusionSurface(Curve3 c, V3 dir) : Surface
{
    public override V3 At(double u, double v) => c.At(u) + dir * v;
    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    {
        var dd = Math.Max(dir.Dot(dir), 1e-300);
        double u = c.Param(p), v = (p - c.At(u)).Dot(dir) / dd;
        for (var i = 0; i < 3; i++) { u = c.Param(p - dir * v); v = (p - c.At(u)).Dot(dir) / dd; }
        return (u, v);
    }
}

/// <summary>Superficie B-spline (racional o no).</summary>
public sealed class BSplineSurface : Surface
{
    private readonly int _du, _dv, _nu, _nv;
    private readonly V3[,] _p;
    private readonly double[,] _w;
    private readonly double[] _ku, _kv;
    private readonly bool _uClosed, _vClosed;
    private V3[,]? _grid;
    private const int G = 24;

    public BSplineSurface(int du, int dv, V3[,] pts, double[,]? w, List<int> um, List<int> vm, List<double> uk, List<double> vk, bool uClosed, bool vClosed)
    {
        _du = du; _dv = dv; _p = pts; _nu = pts.GetLength(0); _nv = pts.GetLength(1);
        _w = w ?? Ones(_nu, _nv);
        _ku = BSplineCurve.Expand(um, uk); _kv = BSplineCurve.Expand(vm, vk);
        _uClosed = uClosed; _vClosed = vClosed;
    }

    private static double[,] Ones(int a, int b) { var r = new double[a, b]; for (var i = 0; i < a; i++) for (var j = 0; j < b; j++) r[i, j] = 1; return r; }

    public (double U0, double U1) URange => (_ku[_du], _ku[_ku.Length - _du - 1]);
    public (double V0, double V1) VRange => (_kv[_dv], _kv[_kv.Length - _dv - 1]);
    public override double PeriodU => _uClosed ? URange.U1 - URange.U0 : 0;
    public override double PeriodV => _vClosed ? VRange.V1 - VRange.V0 : 0;
    public override double? PoleV(bool upper) => upper ? VRange.V1 : VRange.V0;

    public override V3 At(double u, double v)
    {
        var su = Span(_ku, _du, _nu, u); var sv = Span(_kv, _dv, _nv, v);
        Span<double> nu = stackalloc double[_du + 1]; Span<double> nv = stackalloc double[_dv + 1];
        Basis(_ku, _du, su, Math.Clamp(u, _ku[_du], _ku[_ku.Length - _du - 1]), nu);
        Basis(_kv, _dv, sv, Math.Clamp(v, _kv[_dv], _kv[_kv.Length - _dv - 1]), nv);
        double x = 0, y = 0, z = 0, w = 0;
        for (var i = 0; i <= _du; i++)
        {
            var ii = Math.Clamp(su - _du + i, 0, _nu - 1);
            for (var j = 0; j <= _dv; j++)
            {
                var jj = Math.Clamp(sv - _dv + j, 0, _nv - 1);
                var k = nu[i] * nv[j] * _w[ii, jj];
                var p = _p[ii, jj];
                x += p.X * k; y += p.Y * k; z += p.Z * k; w += k;
            }
        }
        return w == 0 ? new V3(x, y, z) : new V3(x / w, y / w, z / w);
    }

    /// <summary>Tramo de nudos que contiene t (algoritmo A2.1 de «The NURBS Book»).</summary>
    internal static int Span(double[] k, int deg, int n, double t)
    {
        var lo = deg; var hi = Math.Min(n, k.Length - deg - 1);
        if (hi <= lo) return lo;
        if (t >= k[hi]) return hi - 1;
        if (t <= k[lo]) return lo;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (t < k[mid]) hi = mid; else lo = mid;
        }
        return lo;
    }

    /// <summary>Funciones base no nulas en t (algoritmo A2.2).</summary>
    internal static void Basis(double[] k, int deg, int span, double t, Span<double> n)
    {
        Span<double> left = stackalloc double[deg + 1]; Span<double> right = stackalloc double[deg + 1];
        n[0] = 1;
        for (var j = 1; j <= deg; j++)
        {
            left[j] = t - k[Math.Clamp(span + 1 - j, 0, k.Length - 1)];
            right[j] = k[Math.Clamp(span + j, 0, k.Length - 1)] - t;
            double saved = 0;
            for (var r = 0; r < j; r++)
            {
                var den = right[r + 1] + left[j - r];
                var tmp = den == 0 ? 0 : n[r] / den;
                n[r] = saved + right[r + 1] * tmp;
                saved = left[j - r] * tmp;
            }
            n[j] = saved;
        }
    }

    public override (double U, double V) Param(V3 p, (double U, double V)? hint)
    {
        var (u0, u1) = URange; var (v0, v1) = VRange;
        if (_grid is null)
        {
            _grid = new V3[G + 1, G + 1];
            for (var i = 0; i <= G; i++) for (var j = 0; j <= G; j++) _grid[i, j] = At(u0 + (u1 - u0) * i / G, v0 + (v1 - v0) * j / G);
        }
        double bu = u0, bv = v0, bd = double.MaxValue;
        for (var i = 0; i <= G; i++)
            for (var j = 0; j <= G; j++)
            {
                var d = (_grid[i, j] - p).Length;
                if (d < bd) { bd = d; bu = u0 + (u1 - u0) * i / G; bv = v0 + (v1 - v0) * j / G; }
            }
        if (hint is { } h && (At(h.U, h.V) - p).Length < bd) { bu = h.U; bv = h.V; }
        // Gauss-Newton con derivadas numéricas.
        double hu = (u1 - u0) * 1e-5, hv = (v1 - v0) * 1e-5;
        for (var it = 0; it < 12; it++)
        {
            var s = At(bu, bv);
            var r = p - s;
            if (r.Length < 1e-9) break;
            var su = (At(Math.Min(bu + hu, u1), bv) - At(Math.Max(bu - hu, u0), bv)) / (2 * hu);
            var sv = (At(bu, Math.Min(bv + hv, v1)) - At(bu, Math.Max(bv - hv, v0))) / (2 * hv);
            double a = su.Dot(su), b = su.Dot(sv), c = sv.Dot(sv), e = r.Dot(su), f = r.Dot(sv);
            var det = a * c - b * b;
            if (Math.Abs(det) < 1e-30) break;
            var du = (e * c - f * b) / det;
            var dv = (a * f - b * e) / det;
            bu = Math.Clamp(bu + du, u0, u1);
            bv = Math.Clamp(bv + dv, v0, v1);
            if (Math.Abs(du) < hu * 0.1 && Math.Abs(dv) < hv * 0.1) break;
        }
        return (bu, bv);
    }
}
