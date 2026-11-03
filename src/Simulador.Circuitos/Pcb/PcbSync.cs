using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

/// <summary>Pasa los componentes y conexiones del esquema a la placa.</summary>
public static class PcbSync
{
    public sealed record SyncReport(int Added, int Removed, int Updated, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Añade a la placa los componentes nuevos del esquema, quita los que ya no están y actualiza valores, huellas y redes.
    /// Los componentes que ya existían conservan su posición.
    /// </summary>
    public static SyncReport Update(PcbDocument pcb, SchematicDocument schematic, ComponentCatalog catalog)
    {
        var warnings = new List<string>();
        var conn = Connectivity.Analyze(schematic);
        var wanted = schematic.Symbols.Where(s => s.Symbol is { IsPower: false }).ToList();
        int added = 0, removed = 0, updated = 0;

        var byRef = pcb.Footprints.ToDictionary(f => f.Reference, StringComparer.OrdinalIgnoreCase);
        var keep = new HashSet<PcbFootprint>();

        // Posición inicial de los nuevos: en fila dentro de la placa, después de los existentes.
        double cursorX = 5, cursorY = 5, rowHeight = 0;

        foreach (var s in wanted.OrderBy(s => s.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var fpName = s.Footprint.Length > 0 ? s.Footprint : s.Symbol!.Footprint;
            var def = catalog.FindFootprint(fpName);
            if (def is null)
            {
                warnings.Add(string.IsNullOrWhiteSpace(fpName)
                    ? $"{s.Reference}: no tiene huella asignada (indícala en sus propiedades del esquema)."
                    : $"{s.Reference}: no se encontró la huella «{fpName}» (impórtala en Librerías).");
                continue;
            }

            var nets = new Dictionary<string, string>();
            foreach (var pad in def.Pads.Where(p => p.Number.Length > 0))
            {
                var net = conn.NetOf(s, pad.Number);
                if (net is not null) nets[pad.Number] = net.Name;
            }
            var pinNumbers = SymbolTransform.Pins(s).Select(p => p.Pin.Number).ToHashSet();
            foreach (var missing in pinNumbers.Where(n => def.Pads.All(p => p.Number != n)))
                warnings.Add($"{s.Reference}: el pin {missing} del símbolo no existe en la huella {def.Name}.");

            if (byRef.TryGetValue(s.Reference, out var existing))
            {
                bool changed = existing.Value != s.Value || existing.FootprintName != def.FullName ||
                               !existing.PadNets.OrderBy(k => k.Key).SequenceEqual(nets.OrderBy(k => k.Key));
                existing.Value = s.Value;
                existing.FootprintName = def.FullName;
                existing.Definition = def;
                existing.PadNets = nets;
                keep.Add(existing);
                if (changed) updated++;
                continue;
            }

            var fp = new PcbFootprint
            {
                Reference = s.Reference,
                Value = s.Value,
                FootprintName = def.FullName,
                Definition = def,
                PadNets = nets,
            };
            // Colocar en fila, saltando de línea si no cabe.
            fp.Position = new Vec2(0, 0);
            var (min, max) = FootprintTransform.Bounds(fp);
            double w = max.X - min.X, h = max.Y - min.Y;
            if (cursorX + w > pcb.BoardWidth - 3 && cursorX > 5)
            {
                cursorX = 5;
                cursorY += rowHeight + 4;
                rowHeight = 0;
            }
            fp.Position = Snap(new Vec2(cursorX - min.X, cursorY - min.Y));
            cursorX += w + 4;
            rowHeight = Math.Max(rowHeight, h);
            pcb.Footprints.Add(fp);
            keep.Add(fp);
            added++;
        }

        removed = pcb.Footprints.RemoveAll(f => !keep.Contains(f));
        if (cursorY > pcb.BoardHeight - 5)
            warnings.Add("Los componentes no caben en la placa: amplía la placa o reorganízalos.");
        if (string.IsNullOrWhiteSpace(pcb.Title) || pcb.Title == "Sin título") pcb.Title = schematic.Title;
        return new SyncReport(added, removed, updated, warnings);
    }

    private static Vec2 Snap(Vec2 v, double grid = 1.27) =>
        new(Math.Round(v.X / grid) * grid, Math.Round(v.Y / grid) * grid);
}
