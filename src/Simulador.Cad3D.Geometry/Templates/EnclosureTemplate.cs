using Simulador.Cad3D.Geometry.Model;
using Simulador.Core.Pcb;

namespace Simulador.Cad3D.Geometry.Templates;

/// <summary>Poste de fijación dentro de la caja (para atornillar una placa).</summary>
public sealed record Standoff(double X, double Y, double OuterDiameter = 6, double HoleDiameter = 2.5);

/// <summary>Hueco en una pared (conector USB, jack, interruptor…). Posición medida en el interior de la caja.</summary>
public enum Wall { Frontal, Trasera, Izquierda, Derecha }

public sealed record WallCutout(string Name, Wall Wall, double Offset, double Z, double Width, double Height);

/// <summary>Parámetros de una caja con tapa para electrónica. Medidas interiores en mm.</summary>
public sealed record EnclosureParameters
{
    public double InnerX { get; init; } = 80;
    public double InnerY { get; init; } = 50;
    public double InnerZ { get; init; } = 25;
    public double Wall { get; init; } = 2;
    public double Floor { get; init; } = 2;
    public double LidThickness { get; init; } = 2;
    public double CornerRadius { get; init; } = 3;
    /// <summary>Altura del reborde de la tapa que encaja dentro de la caja.</summary>
    public double LipHeight { get; init; } = 3;
    /// <summary>Holgura entre reborde y pared para que encaje en impresión FDM.</summary>
    public double Clearance { get; init; } = 0.25;
    public double StandoffHeight { get; init; } = 5;
    public IReadOnlyList<Standoff> Standoffs { get; init; } = Array.Empty<Standoff>();
    public IReadOnlyList<WallCutout> Cutouts { get; init; } = Array.Empty<WallCutout>();
    /// <summary>Ranuras de ventilación en la tapa (0 = sin ventilación).</summary>
    public int VentSlots { get; init; } = 0;
}

/// <summary>Genera la caja y la tapa como dos piezas editables, colocadas una al lado de otra en la bandeja.</summary>
public static class EnclosureTemplate
{
    public static CadDocument Create(EnclosureParameters p, string name = "Caja electrónica")
    {
        var doc = new CadDocument { Name = name };
        doc.Parts.Add(CreateBox(p));
        doc.Parts.Add(CreateLid(p));
        return doc;
    }

    public static Part CreateBox(EnclosureParameters p)
    {
        double ox = p.InnerX + 2 * p.Wall, oy = p.InnerY + 2 * p.Wall, oz = p.InnerZ + p.Floor;
        var part = new Part { Name = "Caja", Color = "#4A90D9" };
        part.Features.Add(new RoundedBoxFeature { Name = "Cuerpo", SizeX = ox, SizeY = oy, SizeZ = oz, CornerRadius = p.CornerRadius });
        part.Features.Add(new RoundedBoxFeature
        {
            Name = "Vaciado",
            Mode = CombineMode.Hueco,
            SizeX = p.InnerX,
            SizeY = p.InnerY,
            SizeZ = p.InnerZ + 1, // sobresale por arriba para abrir la caja
            CornerRadius = Math.Max(0, p.CornerRadius - p.Wall),
            Position = new Vec3(p.Wall, p.Wall, p.Floor),
        });
        int n = 1;
        foreach (var s in p.Standoffs)
        {
            var at = new Vec3(p.Wall + s.X, p.Wall + s.Y, p.Floor - 0.01);
            part.Features.Add(new CylinderFeature { Name = $"Poste {n}", Diameter = s.OuterDiameter, Height = p.StandoffHeight + 0.01, Position = at, Segments = 32 });
            part.Features.Add(new CylinderFeature { Name = $"Taladro poste {n}", Mode = CombineMode.Hueco, Diameter = s.HoleDiameter, Height = p.StandoffHeight + 0.02, Position = at with { Z = p.Floor }, Segments = 24 });
            n++;
        }
        foreach (var c in p.Cutouts) part.Features.Add(CutoutFeature(p, c));
        return part;
    }

    public static Part CreateLid(EnclosureParameters p)
    {
        double ox = p.InnerX + 2 * p.Wall, oy = p.InnerY + 2 * p.Wall;
        // La tapa se coloca al lado de la caja, boca abajo (como se imprime): placa en z = 0 y reborde hacia arriba.
        var origin = new Vec3(ox + 10, 0, 0);
        var part = new Part { Name = "Tapa", Color = "#E8A33D" };
        part.Features.Add(new RoundedBoxFeature { Name = "Placa", SizeX = ox, SizeY = oy, SizeZ = p.LidThickness, CornerRadius = p.CornerRadius, Position = origin });
        double lipW = p.InnerX - 2 * p.Clearance, lipD = p.InnerY - 2 * p.Clearance;
        double lipWall = Math.Max(1.2, p.Wall * 0.6);
        var lipAt = origin + new Vec3(p.Wall + p.Clearance, p.Wall + p.Clearance, p.LidThickness - 0.01);
        part.Features.Add(new RoundedBoxFeature
        {
            Name = "Reborde",
            SizeX = lipW, SizeY = lipD, SizeZ = p.LipHeight + 0.01,
            CornerRadius = Math.Max(0, p.CornerRadius - p.Wall - p.Clearance),
            Position = lipAt,
        });
        part.Features.Add(new RoundedBoxFeature
        {
            Name = "Vaciado reborde",
            Mode = CombineMode.Hueco,
            SizeX = lipW - 2 * lipWall, SizeY = lipD - 2 * lipWall, SizeZ = p.LipHeight + 1,
            CornerRadius = Math.Max(0, p.CornerRadius - p.Wall - p.Clearance - lipWall),
            Position = lipAt + new Vec3(lipWall, lipWall, 0.01),
        });
        if (p.VentSlots > 0)
        {
            double slotW = 2, gap = 3;
            double total = p.VentSlots * slotW + (p.VentSlots - 1) * gap;
            double startX = origin.X + (ox - total) / 2;
            double len = Math.Max(5, lipD - 2 * lipWall - 6);
            for (int i = 0; i < p.VentSlots; i++)
                part.Features.Add(new BoxFeature
                {
                    Name = $"Ranura {i + 1}",
                    Mode = CombineMode.Hueco,
                    SizeX = slotW, SizeY = len, SizeZ = p.LidThickness + 2,
                    Position = new Vec3(startX + i * (slotW + gap), (oy - len) / 2, -1),
                });
        }
        return part;
    }

    private static Feature CutoutFeature(EnclosureParameters p, WallCutout c)
    {
        double ox = p.InnerX + 2 * p.Wall, oy = p.InnerY + 2 * p.Wall;
        double depth = p.Wall + 2;
        double z = p.Floor + c.Z;
        var (pos, sx, sy) = c.Wall switch
        {
            Wall.Frontal => (new Vec3(p.Wall + c.Offset, -1, z), c.Width, depth),
            Wall.Trasera => (new Vec3(p.Wall + c.Offset, oy - p.Wall - 1, z), c.Width, depth),
            Wall.Izquierda => (new Vec3(-1, p.Wall + c.Offset, z), depth, c.Width),
            _ => (new Vec3(ox - p.Wall - 1, p.Wall + c.Offset, z), depth, c.Width),
        };
        return new BoxFeature { Name = c.Name, Mode = CombineMode.Hueco, SizeX = sx, SizeY = sy, SizeZ = c.Height, Position = pos };
    }

    /// <summary>
    /// Caja a medida de una placa del módulo de circuitos: interior = contorno de la placa + holgura,
    /// postes en los taladros de fijación y huecos en las paredes para los conectores del borde.
    /// </summary>
    public static CadDocument FromBoard(BoardExport board, double margin = 2, double clearanceAbove = 5, double wall = 2)
    {
        var (min, max) = board.Bounds();
        double bw = max.X - min.X, bd = max.Y - min.Y;
        double standoffH = 5;
        double tallest = board.Components.Where(c => c.Side == BoardSide.Top).Select(c => c.HeightMm).DefaultIfEmpty(10).Max();
        if (tallest <= 0) tallest = 10;

        var standoffs = board.Holes
            .Where(h => h.ComponentRef is null && h.DiameterMm >= 2.0)
            .Select(h => new Standoff(h.Position.X - min.X + margin, h.Position.Y - min.Y + margin, OuterDiameter: h.DiameterMm + 3.5, HoleDiameter: Math.Max(1.8, h.DiameterMm - 0.7)))
            .ToList();

        double boardTopZ = standoffH + board.ThicknessMm;
        var cutouts = new List<WallCutout>();
        const double edge = 4; // un conector a menos de 4 mm del borde se considera de borde
        foreach (var c in board.Components.Where(IsConnector))
        {
            double x = c.Position.X - min.X, y = c.Position.Y - min.Y;
            double w = 12, h = Math.Max(6, c.HeightMm + 2);
            double z = boardTopZ - 1;
            if (y <= edge) cutouts.Add(new WallCutout($"Hueco {c.Reference}", Wall.Frontal, x + margin - w / 2, z, w, h));
            else if (y >= bd - edge) cutouts.Add(new WallCutout($"Hueco {c.Reference}", Wall.Trasera, x + margin - w / 2, z, w, h));
            else if (x <= edge) cutouts.Add(new WallCutout($"Hueco {c.Reference}", Wall.Izquierda, y + margin - w / 2, z, w, h));
            else if (x >= bw - edge) cutouts.Add(new WallCutout($"Hueco {c.Reference}", Wall.Derecha, y + margin - w / 2, z, w, h));
        }

        var p = new EnclosureParameters
        {
            InnerX = bw + 2 * margin,
            InnerY = bd + 2 * margin,
            InnerZ = boardTopZ + tallest + clearanceAbove,
            Wall = wall,
            StandoffHeight = standoffH,
            Standoffs = standoffs,
            Cutouts = cutouts,
        };
        return Create(p, $"Caja para {board.Name}");
    }

    private static bool IsConnector(BoardComponent c)
    {
        var r = c.Reference.ToUpperInvariant();
        return r.StartsWith('J') || r.StartsWith('P') && !r.StartsWith("PS") || r.StartsWith("USB") || r.StartsWith("CN");
    }
}
