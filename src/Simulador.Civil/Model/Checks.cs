using Simulador.Core.Building;

namespace Simulador.Civil.Model;

public enum CheckSeverity { Info, Warning, Error }

/// <summary>Aviso de una comprobación. <paramref name="ElementId"/> permite seleccionar el elemento en el plano.</summary>
public sealed record CheckResult(CheckSeverity Severity, string ElementId, string Message);

/// <summary>Comprobaciones geométricas básicas (choques y medidas). Solo avisan, nunca bloquean.</summary>
public static class BuildingChecks
{
    public static IReadOnlyList<CheckResult> Run(BuildingDocument doc)
    {
        var r = new List<CheckResult>();
        var walls = doc.Walls.ToDictionary(w => w.Id);

        // Huecos que no caben en su muro o se pisan entre sí.
        foreach (var g in doc.Openings.GroupBy(o => o.HostWallId))
        {
            if (!walls.TryGetValue(g.Key, out var w)) continue;
            var list = g.OrderBy(o => o.Offset).ToList();
            foreach (var o in list)
            {
                if (o.Offset - o.Width / 2 < -1e-6 || o.Offset + o.Width / 2 > w.Length + 1e-6)
                    r.Add(new(CheckSeverity.Error, o.Id, $"{Name(o)} {o.Id} se sale del muro {w.Id}."));
                if (o.SillHeight + o.Height > w.Height + 1e-6)
                    r.Add(new(CheckSeverity.Error, o.Id, $"{Name(o)} {o.Id} es más alta que el muro {w.Id}."));
                if (o.Kind == OpeningKind.Door && o.Width < 0.70)
                    r.Add(new(CheckSeverity.Warning, o.Id, $"Puerta {o.Id} de {o.Width:0.00} m: estrecha (mínimo habitual 0,70 m; 0,80 m accesible)."));
            }
            for (int i = 0; i + 1 < list.Count; i++)
                if (list[i].Offset + list[i].Width / 2 > list[i + 1].Offset - list[i + 1].Width / 2 + 1e-6)
                    r.Add(new(CheckSeverity.Error, list[i + 1].Id, $"{list[i].Id} y {list[i + 1].Id} se solapan en el muro {w.Id}."));
        }

        // Elementos de instalación colocados dentro de un hueco o en el barrido de una puerta.
        foreach (var f in doc.Fixtures)
        {
            if (f.HostId is null || !walls.TryGetValue(f.HostId, out var w)) continue;
            var level = doc.GetLevel(w.LevelId);
            double along = Geo.ProjectT(w.Start, w.End, f.Position.Xy) * w.Length;
            double h = f.Position.Z - level.Elevation;
            foreach (var o in doc.Openings.Where(o => o.HostWallId == w.Id))
            {
                double lo = o.Offset - o.Width / 2, hi = o.Offset + o.Width / 2;
                if (along > lo - 0.05 && along < hi + 0.05 && h > o.SillHeight - 0.05 && h < o.SillHeight + o.Height + 0.05)
                    r.Add(new(CheckSeverity.Error, f.Id, $"{f.ComponentId} queda dentro del hueco {o.Id}."));
                else if (o.Kind == OpeningKind.Door && h < o.Height && DoorSwingCovers(w, o, f.Position.Xy))
                    r.Add(new(CheckSeverity.Warning, f.Id, $"{f.ComponentId} queda tapado por la hoja de la puerta {o.Id} al abrir."));
            }
            if (h < 0 || h > w.Height)
                r.Add(new(CheckSeverity.Error, f.Id, $"{f.ComponentId} está fuera de la altura del muro {w.Id}."));
        }

        // Escaleras: que lleguen al nivel superior y que sean cómodas.
        foreach (var s in doc.Stairs)
        {
            var lvl = doc.GetLevel(s.LevelId);
            var above = doc.Levels.Where(l => l.Elevation > lvl.Elevation + 1e-6).MinBy(l => l.Elevation);
            if (above is not null && Math.Abs(above.Elevation - lvl.Elevation - s.Rise) > 0.02)
                r.Add(new(CheckSeverity.Warning, s.Id, $"Escalera {s.Id} salva {s.Rise:0.00} m pero entre {lvl.Name} y {above.Name} hay {above.Elevation - lvl.Elevation:0.00} m."));
            foreach (var w in s.Design.Warnings)
                r.Add(new(CheckSeverity.Warning, s.Id, $"Escalera {s.Id}: {w}"));
        }

        // Recintos muy pequeños para su uso.
        foreach (var sp in doc.Spaces)
        {
            double min = sp.Use switch { "dormitorio" => 6.0, "baño" => 2.5, "cocina" => 5.0, "habitacion_hotel" => 9.0, _ => 0 };
            if (sp.Area < min)
                r.Add(new(CheckSeverity.Warning, sp.Id, $"{sp.Name}: {sp.Area:0.0} m² es poco para {sp.Use.Replace('_', ' ')} (orientativo ≥ {min:0.#} m²)."));
        }

        return r;
    }

    private static string Name(Opening o) => o.Kind == OpeningKind.Door ? "La puerta" : "La ventana";

    /// <summary>¿El punto cae dentro del cuarto de círculo que barre la hoja de la puerta?</summary>
    internal static bool DoorSwingCovers(Wall w, Opening o, Point2 p)
    {
        var dir = w.Direction;
        var hinge = w.Start + dir * (o.HingeAtStart ? o.Offset - o.Width / 2 : o.Offset + o.Width / 2);
        var normal = dir.Perp() * (o.SwingLeft ? 1 : -1);
        var v = p - hinge;
        if (v.Length > o.Width + 0.05) return false;
        double side = Point2.Dot(v, normal);
        double alongDoor = Point2.Dot(v, o.HingeAtStart ? dir : dir * -1);
        // Delante del muro en el lado de apertura (o pegado a él) y hacia el lado de la hoja.
        return side > -w.Thickness && alongDoor > -0.05;
    }
}
