namespace Simulador.Core.Libraries.Model;

/// <summary>Punto en milímetros. En símbolos Y crece hacia arriba (convención KiCad para librerías).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public override string ToString() => FormattableString.Invariant($"({X}, {Y})");
}
