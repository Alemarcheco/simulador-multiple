using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>Tipos de elemento de instalación que se pueden colocar desde la pestaña Edificación.</summary>
public sealed record FixtureType(string Kind, string Name, string ModuleId, string ComponentPrefix, double DefaultHeight, string Icon);

public static class FixtureCatalog
{
    public static readonly FixtureType Tomacorriente = new("tomacorriente", "Tomacorriente", "electrico", "X", 0.30, "🔌");
    public static readonly FixtureType Interruptor = new("interruptor", "Interruptor", "electrico", "S", 1.10, "⏻");
    public static readonly FixtureType Luminaria = new("luminaria", "Luminaria (techo)", "electrico", "E", -0.05, "💡");
    public static readonly FixtureType Tablero = new("tablero", "Tablero eléctrico", "electrico", "Q", 1.50, "▦");
    public static readonly FixtureType PuntoAgua = new("punto_agua", "Punto de agua", "hidraulica", "PA", 0.60, "🚰");
    public static readonly FixtureType Desague = new("desague", "Desagüe", "hidraulica", "D", 0.0, "⊚");
    public static readonly FixtureType Tanque = new("tanque", "Tanque", "hidraulica", "T", 0.0, "🛢");

    public static IReadOnlyList<FixtureType> All { get; } =
        [Tomacorriente, Interruptor, Luminaria, Tablero, PuntoAgua, Desague, Tanque];

    public static FixtureType? ByKind(string kind) => All.FirstOrDefault(t => t.Kind == kind);

    /// <summary>Si el elemento se monta en muro (true) o en el piso/techo del recinto (false).</summary>
    public static bool IsWallMounted(FixtureType t) => t.Kind is not ("luminaria" or "tanque" or "desague");
}

public static class RouteBuilder
{
    /// <summary>Holgura bajo el techo por donde corren canalizaciones y tuberías (m).</summary>
    public const double CeilingGap = 0.10;

    /// <summary>
    /// Recorrido típico de canalización empotrada: sube por el muro desde el elemento de origen hasta el techo, corre por
    /// el techo en dos tramos ortogonales y baja por el muro hasta el destino.
    /// </summary>
    public static IReadOnlyList<RouteSegment> ViaCeiling(BuildingDocument doc, PlacedFixture from, PlacedFixture to)
    {
        var lf = doc.GetLevel(from.LevelId);
        var lt = doc.GetLevel(to.LevelId);
        double zf = lf.Elevation + lf.Height - CeilingGap;
        double zt = lt.Elevation + lt.Height - CeilingGap;
        var a = from.Position;
        var b = to.Position;
        var segs = new List<RouteSegment>();
        void Add(Point3 p, Point3 q, RouteMedium m, string? host = null)
        {
            if (p.DistanceTo(q) > 1e-6) segs.Add(new RouteSegment(p, q, m, host));
        }

        var a1 = new Point3(a.X, a.Y, zf);
        Add(a, a1, from.HostId is null ? RouteMedium.Air : RouteMedium.Wall, from.HostId);
        if (from.LevelId == to.LevelId)
        {
            var corner = new Point3(b.X, a.Y, zf);
            var b1 = new Point3(b.X, b.Y, zt);
            Add(a1, corner, RouteMedium.Ceiling);
            Add(corner, b1, RouteMedium.Ceiling);
            Add(b1, b, to.HostId is null ? RouteMedium.Air : RouteMedium.Wall, to.HostId);
        }
        else
        {
            // Distinto nivel: tramo horizontal en el techo del origen, montante vertical y tramo en el techo del destino.
            var corner = new Point3(b.X, a.Y, zf);
            var riserTop = new Point3(b.X, b.Y, zf);
            var b1 = new Point3(b.X, b.Y, zt);
            Add(a1, corner, RouteMedium.Ceiling);
            Add(corner, riserTop, RouteMedium.Ceiling);
            Add(riserTop, b1, RouteMedium.Slab);
            Add(b1, b, to.HostId is null ? RouteMedium.Air : RouteMedium.Wall, to.HostId);
        }
        return segs;
    }
}
