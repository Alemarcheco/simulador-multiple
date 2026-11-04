using Simulador.Core.Libraries.Model;
using Simulador.Core.Pcb;

namespace Simulador.Circuitos.Pcb;

/// <summary>
/// Convierte una placa del editor en el modelo neutro <see cref="BoardExport"/> que usan otros módulos
/// (el módulo 3D genera con él cajas a medida). Pasa de Y hacia abajo (editor) a Y hacia arriba (BoardExport).
/// </summary>
public static class PcbBoardExport
{
    public const double DefaultThicknessMm = 1.6;

    /// <param name="doc">Placa con las huellas ya resueltas (<see cref="PcbDocument.ResolveFootprints"/>).</param>
    public static BoardExport ToBoardExport(PcbDocument doc, double thicknessMm = DefaultThicknessMm)
    {
        double h = doc.BoardHeight;
        PointMm P(Vec2 v) => new(v.X, h - v.Y);

        var outline = new List<PointMm> { new(0, 0), new(doc.BoardWidth, 0), new(doc.BoardWidth, h), new(0, h) };
        var holes = new List<BoardHole>();
        var components = new List<BoardComponent>();
        var pins = new List<BoardConnectorPin>();

        foreach (var f in doc.Footprints)
        {
            bool mounting = IsMountingHole(f);
            if (mounting && f.Definition is null && MountingHoleDiameter(f.FootprintName) is { } d)
            {
                // Huella de taladro de fijación sin librería: el diámetro va en el nombre (MountingHole_3.2mm_M3).
                holes.Add(new BoardHole(P(f.Position), d, false));
                continue;
            }
            foreach (var pad in FootprintTransform.Pads(f))
            {
                if (pad.Pad.Drill <= 0) continue;
                holes.Add(new BoardHole(P(pad.Center), pad.Pad.Drill, pad.Pad.Type == PadType.ThroughHole, mounting ? null : f.Reference));
                if (IsConnector(f.Reference))
                    pins.Add(new BoardConnectorPin(f.Reference, pad.Pad.Number, pad.Net, P(pad.Center)));
            }
            if (mounting) continue;
            components.Add(new BoardComponent(
                f.Reference, f.Value, f.FootprintName, P(f.Position), f.Rotation, BoardSide.Top,
                EstimateHeightMm(f), f.Definition?.Models3D.FirstOrDefault()));
        }

        return new BoardExport(doc.Title, outline, Array.Empty<IReadOnlyList<PointMm>>(), thicknessMm, holes, components, pins);
    }

    private static bool IsMountingHole(PcbFootprint f) =>
        f.FootprintName.Contains("MountingHole", StringComparison.OrdinalIgnoreCase) ||
        f.Reference.StartsWith('H') && f.Definition?.Pads.All(p => p.Drill > 0) == true && f.Definition.Pads.Count <= 9 && f.FootprintName.Contains("Hole", StringComparison.OrdinalIgnoreCase);

    /// <summary>Diámetro escrito en el nombre de una huella de taladro, p. ej. "MountingHole_3.2mm_M3" → 3,2.</summary>
    public static double? MountingHoleDiameter(string footprintName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(footprintName, @"(\d+(\.\d+)?)mm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static bool IsConnector(string reference)
    {
        var r = reference.ToUpperInvariant();
        return r.StartsWith('J') || r.StartsWith("CN") || r.StartsWith("USB") || (r.StartsWith('P') && !r.StartsWith("PS"));
    }

    /// <summary>Altura aproximada sobre la placa según el tipo de huella (las librerías no la guardan).</summary>
    public static double EstimateHeightMm(PcbFootprint f)
    {
        string n = f.FootprintName.ToUpperInvariant();
        if (n.Contains("PINHEADER") || n.Contains("PIN_HEADER")) return 8.5;
        if (n.Contains("PINSOCKET")) return 8.5;
        if (n.Contains("TERMINALBLOCK") || n.Contains("BORNERA")) return 10;
        if (n.Contains("USB")) return 3.5;
        if (n.Contains("BARREL") || n.Contains("JACK")) return 11;
        if (n.Contains("RELAY") || n.Contains("RELE")) return 15.5;
        if (n.Contains("TO-220")) return 16;
        if (n.Contains("CP_RADIAL") || n.Contains("C_RADIAL"))
        {
            // Los condensadores electrolíticos suelen tener un alto parecido a 2 veces el diámetro.
            var m = System.Text.RegularExpressions.Regex.Match(n, @"D(\d+(\.\d+)?)MM");
            return m.Success ? Math.Min(25, 2 * double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)) : 12;
        }
        if (n.Contains("LED")) return 8.6;
        if (n.Contains("DIP")) return 4.5;
        if (n.Contains("TO-92")) return 5;
        if (f.Definition?.IsSmd == true) return 1.5;
        return 5;
    }
}
