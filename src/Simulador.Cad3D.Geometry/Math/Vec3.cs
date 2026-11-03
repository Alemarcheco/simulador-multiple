namespace Simulador.Cad3D.Geometry;

/// <summary>Vector o punto 3D en milímetros.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Normalized() { var l = Length; return l > 1e-15 ? this / l : Zero; }
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
    public static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###}, {Z:0.###})");
}

/// <summary>Punto 2D en milímetros (perfiles de extrusión).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public double Cross(Vec2 b) => X * b.Y - Y * b.X;
}

/// <summary>Transformación afín 4x3 (rotación/escala + traslación), fila mayor.</summary>
public readonly struct Transform3
{
    // Matriz 3x3 en M[0..8] y traslación T.
    private readonly double _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;
    public Vec3 Translation { get; }

    public Transform3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22, Vec3 t)
    {
        _m00 = m00; _m01 = m01; _m02 = m02; _m10 = m10; _m11 = m11; _m12 = m12; _m20 = m20; _m21 = m21; _m22 = m22;
        Translation = t;
    }

    public static Transform3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1, Vec3.Zero);
    public static Transform3 Translate(Vec3 t) => new(1, 0, 0, 0, 1, 0, 0, 0, 1, t);
    public static Transform3 Scale(Vec3 s) => new(s.X, 0, 0, 0, s.Y, 0, 0, 0, s.Z, Vec3.Zero);

    public static Transform3 RotateX(double deg) { var (s, c) = Math.SinCos(deg * Math.PI / 180); return new(1, 0, 0, 0, c, -s, 0, s, c, Vec3.Zero); }
    public static Transform3 RotateY(double deg) { var (s, c) = Math.SinCos(deg * Math.PI / 180); return new(c, 0, s, 0, 1, 0, -s, 0, c, Vec3.Zero); }
    public static Transform3 RotateZ(double deg) { var (s, c) = Math.SinCos(deg * Math.PI / 180); return new(c, -s, 0, s, c, 0, 0, 0, 1, Vec3.Zero); }

    /// <summary>Rotación en grados aplicada en orden X, luego Y, luego Z; después traslación.</summary>
    public static Transform3 FromPose(Vec3 position, Vec3 rotationDeg) =>
        Translate(position) * RotateZ(rotationDeg.Z) * RotateY(rotationDeg.Y) * RotateX(rotationDeg.X);

    /// <summary>Composición: (a * b)(p) = a(b(p)).</summary>
    public static Transform3 operator *(Transform3 a, Transform3 b) => new(
        a._m00 * b._m00 + a._m01 * b._m10 + a._m02 * b._m20, a._m00 * b._m01 + a._m01 * b._m11 + a._m02 * b._m21, a._m00 * b._m02 + a._m01 * b._m12 + a._m02 * b._m22,
        a._m10 * b._m00 + a._m11 * b._m10 + a._m12 * b._m20, a._m10 * b._m01 + a._m11 * b._m11 + a._m12 * b._m21, a._m10 * b._m02 + a._m11 * b._m12 + a._m12 * b._m22,
        a._m20 * b._m00 + a._m21 * b._m10 + a._m22 * b._m20, a._m20 * b._m01 + a._m21 * b._m11 + a._m22 * b._m21, a._m20 * b._m02 + a._m21 * b._m12 + a._m22 * b._m22,
        a.ApplyVector(b.Translation) + a.Translation);

    public Vec3 ApplyVector(Vec3 v) => new(
        _m00 * v.X + _m01 * v.Y + _m02 * v.Z,
        _m10 * v.X + _m11 * v.Y + _m12 * v.Z,
        _m20 * v.X + _m21 * v.Y + _m22 * v.Z);

    public Vec3 Apply(Vec3 p) => ApplyVector(p) + Translation;

    /// <summary>Determinante de la parte lineal; negativo si la transformación invierte (espejo).</summary>
    public double Determinant =>
        _m00 * (_m11 * _m22 - _m12 * _m21) - _m01 * (_m10 * _m22 - _m12 * _m20) + _m02 * (_m10 * _m21 - _m11 * _m20);
}
