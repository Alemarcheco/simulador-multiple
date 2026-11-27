using System.Text.Json.Serialization;
using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Columna (pilar) de sección rectangular que va del piso al techo de su nivel. En naves industriales sirve también
/// de soporte para contactos, apagadores, cajas y tubería.
/// </summary>
public sealed record Column(string Id, string LevelId, Point2 Center, double Width = 0.30, double Depth = 0.30,
    double Rotation = 0, string Material = "concreto")
{
    /// <summary>Contorno de la sección en planta (antihorario).</summary>
    [JsonIgnore]
    public Point2[] Outline
    {
        get
        {
            var u = new Point2(Math.Cos(Rotation), Math.Sin(Rotation));
            var v = u.Perp();
            double a = Width / 2, b = Depth / 2;
            return [Center - u * a - v * b, Center + u * a - v * b, Center + u * a + v * b, Center - u * a + v * b];
        }
    }

    /// <summary>Punto de la cara de la columna más cercana a <paramref name="p"/> y su normal hacia fuera.</summary>
    public (Point2 Point, Point2 Normal) NearestFace(Point2 p)
    {
        var u = new Point2(Math.Cos(Rotation), Math.Sin(Rotation));
        var v = u.Perp();
        var d = p - Center;
        double du = Point2.Dot(d, u), dv = Point2.Dot(d, v);
        double a = Width / 2, b = Depth / 2;
        // Cara cuyo lado mira hacia el punto (comparando en proporción a la sección).
        if (Math.Abs(du) / Math.Max(a, 1e-6) >= Math.Abs(dv) / Math.Max(b, 1e-6))
        {
            var n = u * Math.Sign(du == 0 ? 1 : du);
            return (Center + n * a + v * Math.Clamp(dv, -b, b), n);
        }
        else
        {
            var n = v * Math.Sign(dv == 0 ? 1 : dv);
            return (Center + n * b + u * Math.Clamp(du, -a, a), n);
        }
    }
}

/// <summary>
/// Viga recta bajo el techo de su nivel: su cara superior está en el techo y cuelga <paramref name="Depth"/> (peralte).
/// En naves industriales de ella cuelgan las campanas y corre la tubería.
/// </summary>
public sealed record Beam(string Id, string LevelId, Point2 Start, Point2 End, double Width = 0.25, double Depth = 0.45,
    string Material = "concreto")
{
    [JsonIgnore]
    public double Length => Start.DistanceTo(End);

    [JsonIgnore]
    public Point2 Direction => (End - Start).Normalized();

    [JsonIgnore]
    public Point2[] Outline
    {
        get
        {
            var n = Direction.Perp() * (Width / 2);
            return [Start - n, End - n, End + n, Start + n];
        }
    }
}

public static class StructureMaterials
{
    public static readonly string[] Column = ["concreto", "acero", "madera"];
    public static readonly string[] Beam = ["concreto", "acero", "madera"];
}
