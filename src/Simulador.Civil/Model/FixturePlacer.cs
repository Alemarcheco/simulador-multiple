using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Coloca un elemento de instalación según su montaje: en la cara de un muro o de una columna, en el techo, bajo una
/// viga, en el piso o libre. Lo usan el plano (clic del usuario), el asistente y las plantillas.
/// </summary>
public static class FixturePlacer
{
    /// <summary>Distancia máxima (m) para pegarse a un muro, columna o viga.</summary>
    public const double SnapDistance = 0.6;

    /// <summary>Calcula dónde quedaría el elemento. Devuelve null y un motivo si no se puede colocar ahí.</summary>
    public static (PlacedFixture? Fixture, string? Error) Locate(BuildingDocument doc, FixtureType t, string levelId, Point2 p,
        double? height = null, double grid = 0.05)
    {
        var lvl = doc.GetLevel(levelId);
        string id = "_", comp = "_";
        switch (t.Mount)
        {
            case MountKind.Wall:
            {
                double h = height ?? t.DefaultHeight;
                var col = NearestColumn(doc, levelId, p);
                var wall = doc.NearestWall(levelId, p, SnapDistance);
                double dCol = col is null ? double.MaxValue : DistanceToColumn(col, p);
                double dWall = wall is { } w0 ? Geo.DistanceToSegment(w0.Wall.Start, w0.Wall.End, p) - w0.Wall.Thickness / 2 : double.MaxValue;
                if (col is not null && dCol <= SnapDistance && dCol <= dWall)
                {
                    var (pt, n) = col.NearestFace(p);
                    return (new PlacedFixture(id, t.ModuleId, t.Kind, comp, null, levelId, col.Id, new Point3(pt, lvl.Elevation + h),
                        Math.Atan2(n.Y, n.X), Math.Max(0.1, t.Size)), null);
                }
                if (wall is not { } nw) return (null, "Los elementos de pared se colocan haciendo clic junto a un muro o una columna.");
                double along = Math.Clamp(Math.Round(nw.Along / grid) * grid, 0, nw.Wall.Length);
                var pos = doc.GetWallPoint(nw.Wall.Id, nw.Face, along, h);
                var normal = nw.Wall.Direction.Perp() * (nw.Face == WallFace.A ? 1 : -1);
                return (new PlacedFixture(id, t.ModuleId, t.Kind, comp, null, levelId, nw.Wall.Id, pos, Math.Atan2(normal.Y, normal.X), Math.Max(0.1, t.Size)), null);
            }
            case MountKind.Hung:
            case MountKind.Ceiling:
            {
                var q = new Point2(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);
                double ceiling = lvl.Elevation + lvl.Height;
                var beam = NearestBeam(doc, levelId, p);
                if (beam is not null && Geo.DistanceToSegment(beam.Start, beam.End, p) <= Math.Max(SnapDistance, beam.Width / 2))
                {
                    // Bajo la viga, centrado en su eje.
                    double tt = Geo.ProjectT(beam.Start, beam.End, p);
                    var on = beam.Start + (beam.End - beam.Start) * tt;
                    double drop = t.Mount == MountKind.Hung ? height ?? t.DefaultHeight : -(height ?? t.DefaultHeight);
                    return (new PlacedFixture(id, t.ModuleId, t.Kind, comp, null, levelId, beam.Id,
                        new Point3(on, ceiling - beam.Depth - Math.Max(0.01, drop)), Math.Atan2(beam.Direction.Y, beam.Direction.X), Math.Max(0.1, t.Size), Math.Max(0.1, t.Size)), null);
                }
                double z = t.Mount == MountKind.Hung ? ceiling - (height ?? t.DefaultHeight) : ceiling + (height ?? t.DefaultHeight);
                return (new PlacedFixture(id, t.ModuleId, t.Kind, comp, null, levelId, null, new Point3(q, z), 0, Math.Max(0.1, t.Size), Math.Max(0.1, t.Size)), null);
            }
            case MountKind.Floor:
            {
                var q = new Point2(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);
                double size = Math.Max(0.12, t.Size);
                return (new PlacedFixture(id, t.ModuleId, t.Kind, comp, null, levelId, null, new Point3(q, lvl.Elevation + (height ?? 0)), 0, size, size), null);
            }
            default:
            {
                var q = new Point2(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);
                double size = t.Kind == "tanque" ? 1.2 : 0.3;
                return (new PlacedFixture(id, t.ModuleId, t.Kind, comp, null, levelId, null, new Point3(q, lvl.Elevation + (height ?? 0)), 0, size, size), null);
            }
        }
    }

    /// <summary>Coloca el elemento con id y designación nuevos (dentro de una edición).</summary>
    public static PlacedFixture? Place(BuildingDocument doc, BuildingDocument.Editor e, FixtureType t, string levelId, Point2 p, double? height = null)
    {
        var (f, _) = Locate(doc, t, levelId, p, height);
        if (f is null) return null;
        return e.AddFixture(f with { Id = e.NewId("F"), ComponentId = e.NewComponentId(t.ComponentPrefix) });
    }

    public static Column? NearestColumn(BuildingDocument doc, string levelId, Point2 p) =>
        doc.Columns.Where(c => c.LevelId == levelId).MinBy(c => DistanceToColumn(c, p));

    public static Beam? NearestBeam(BuildingDocument doc, string levelId, Point2 p) =>
        doc.Beams.Where(b => b.LevelId == levelId).MinBy(b => Geo.DistanceToSegment(b.Start, b.End, p));

    /// <summary>Distancia del punto a la sección de la columna (0 si está dentro).</summary>
    public static double DistanceToColumn(Column c, Point2 p)
    {
        var o = c.Outline;
        if (Geo.Contains(o, p)) return 0;
        double d = double.MaxValue;
        for (int i = 0; i < 4; i++) d = Math.Min(d, Geo.DistanceToSegment(o[i], o[(i + 1) % 4], p));
        return d;
    }
}
