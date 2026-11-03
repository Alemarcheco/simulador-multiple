// Contrato: /mnt/project-files/contratos/edificio.md (responsable: hilo "Crear módulo de diseño civil de edificaciones").
// Acordado con "Integrar simulaciones en un entorno combinado". Proponer cambios a ese hilo antes de modificar estos tipos.
//
// El edificio no simula (no es ISimComponent): es el soporte geométrico donde los demás módulos colocan sus
// elementos (tomas, luminarias, tuberías, tanques, sensores) y de donde salen longitudes y cotas reales.
// Unidades SI: metros y radianes. Coordenadas: X/Y en planta, Z hacia arriba (Z absoluta, incluye la cota del nivel).

using System.Text.Json.Serialization;

namespace Simulador.Core.Building;

public readonly record struct Point2(double X, double Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator *(Point2 a, double k) => new(a.X * k, a.Y * k);
    [JsonIgnore]
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double DistanceTo(Point2 o) => (this - o).Length;
    public Point2 Normalized() => Length < 1e-12 ? new(0, 0) : this * (1 / Length);
    /// <summary>Perpendicular a la izquierda (giro de +90°).</summary>
    public Point2 Perp() => new(-Y, X);
    public static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
}

public readonly record struct Point3(double X, double Y, double Z)
{
    public Point3(Point2 p, double z) : this(p.X, p.Y, z) { }
    [JsonIgnore]
    public Point2 Xy => new(X, Y);
    public double DistanceTo(Point3 o) =>
        Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y) + (Z - o.Z) * (Z - o.Z));
}

/// <summary>Planta del edificio. <paramref name="Elevation"/> es la cota del piso terminado.</summary>
public sealed record Level(string Id, string Name, double Elevation, double Height);

/// <summary>Muro recto de eje <paramref name="Start"/>→<paramref name="End"/>. Un muro quebrado son varios muros.</summary>
public sealed record Wall(string Id, string LevelId, Point2 Start, Point2 End, double Thickness, double Height, string Material = "bloque")
{
    [JsonIgnore]
    public double Length => Start.DistanceTo(End);
    [JsonIgnore]
    public Point2 Direction => (End - Start).Normalized();
}

public enum OpeningKind { Door, Window }

/// <summary>
/// Hueco alojado en un muro. <paramref name="Offset"/> es la distancia desde <c>Wall.Start</c> hasta el centro del hueco.
/// <paramref name="SwingLeft"/>: la hoja abre hacia la cara izquierda del eje (cara A).
/// </summary>
public sealed record Opening(
    string Id, string HostWallId, OpeningKind Kind, double Offset, double Width, double Height, double SillHeight,
    bool SwingLeft = true, bool HingeAtStart = true);

public enum SlabKind { Floor, Ceiling, Roof }

public sealed record Slab(string Id, string LevelId, IReadOnlyList<Point2> Outline, double Thickness, SlabKind Kind = SlabKind.Floor);

/// <summary>Recinto cerrado detectado a partir de los muros (medido a ejes de muro).</summary>
public sealed record Space(string Id, string LevelId, string Name, string Use, IReadOnlyList<Point2> Outline, double Area, double Perimeter);

/// <summary>Cara de un muro: A = izquierda del eje (de Start a End), B = derecha.</summary>
public enum WallFace { A, B }

/// <summary>
/// Elemento de otro módulo colocado en el edificio. Referencia al componente del banco de pruebas por cadenas
/// (<paramref name="ComponentId"/> = "X1", <paramref name="PortId"/> = "X1.L"), nunca por objeto, para que el proyecto
/// .simx se serialice y el plano y el banco apunten al mismo ISimComponent.
/// </summary>
public sealed record PlacedFixture(
    string Id, string ModuleId, string Kind, string ComponentId, string? PortId,
    string LevelId, string? HostId, Point3 Position, double Rotation = 0, double Width = 0.1, double Depth = 0.05);

public enum RouteMedium { Wall, Slab, Ceiling, Air, Ground }

public sealed record RouteSegment(Point3 Start, Point3 End, RouteMedium Medium, string? HostId = null)
{
    [JsonIgnore]
    public double Length => Start.DistanceTo(End);
}

/// <summary>Recorrido de canalización, cable o tubería entre dos elementos colocados.</summary>
public sealed record Route(string Id, string ModuleId, string Kind, string FromFixtureId, string ToFixtureId, IReadOnlyList<RouteSegment> Segments)
{
    /// <summary>Longitud total (m).</summary>
    [JsonIgnore]
    public double Length => Segments.Sum(s => s.Length);

    /// <summary>Diferencia de cota entre el final y el inicio (m), positiva si sube.</summary>
    [JsonIgnore]
    public double DeltaZ => Segments.Count == 0 ? 0 : Segments[^1].End.Z - Segments[0].Start.Z;

    /// <summary>
    /// Parámetros que se pasan a <c>ISimComponentFactory.Create</c> al crear el cable o la tubería
    /// (caída de tensión, pérdidas de carga, altura geométrica).
    /// </summary>
    public IDictionary<string, object> ToComponentParameters() => new Dictionary<string, object>
    {
        ["length"] = Length,
        ["deltaZ"] = DeltaZ,
        ["segments"] = Segments,
    };
}

/// <summary>Modelo de edificio que el módulo Edificación publica para el resto de la aplicación.</summary>
public interface IBuildingModel
{
    IReadOnlyList<Level> Levels { get; }
    IReadOnlyList<Wall> Walls { get; }
    IReadOnlyList<Opening> Openings { get; }
    IReadOnlyList<Slab> Slabs { get; }
    IReadOnlyList<Space> Spaces { get; }
    IReadOnlyList<PlacedFixture> Fixtures { get; }
    IReadOnlyList<Route> Routes { get; }

    /// <summary>Se dispara tras cualquier cambio del modelo.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Punto 3D absoluto sobre la cara de un muro: <paramref name="distance"/> desde <c>Start</c> a lo largo del eje y
    /// <paramref name="height"/> sobre el piso terminado del nivel del muro.
    /// </summary>
    Point3 GetWallPoint(string wallId, WallFace face, double distance, double height);
}
