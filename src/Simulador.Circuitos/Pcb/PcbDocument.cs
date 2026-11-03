using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

// Coordenadas de la placa en milímetros, Y hacia abajo, vista desde arriba (cara de componentes).
// Rotaciones en grados, positivas en sentido antihorario en pantalla (como KiCad).

public static class Layers
{
    public const string Top = "F.Cu";
    public const string Bottom = "B.Cu";
}

/// <summary>Componente colocado en la placa.</summary>
public sealed class PcbFootprint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = "";
    public string Value { get; set; } = "";
    public string FootprintName { get; set; } = "";
    public Vec2 Position { get; set; }
    public double Rotation { get; set; }
    /// <summary>Red de cada pad, por número de pad (viene del esquema).</summary>
    public Dictionary<string, string> PadNets { get; set; } = [];

    [JsonIgnore] public FootprintDefinition? Definition { get; set; }
}

/// <summary>Tramo de pista de cobre.</summary>
public sealed class PcbTrack
{
    public Vec2 A { get; set; }
    public Vec2 B { get; set; }
    public double Width { get; set; } = 0.8;
    public string Layer { get; set; } = Layers.Bottom;
    public string Net { get; set; } = "";
}

/// <summary>Reglas de diseño. Valores por defecto pensados para placas caseras (transferencia térmica).</summary>
public sealed class DesignRules
{
    public double TrackWidth { get; set; } = 0.8;
    public double Clearance { get; set; } = 0.5;
    public double EdgeClearance { get; set; } = 1.0;
}

/// <summary>Documento de placa (archivo .placa).</summary>
public sealed class PcbDocument
{
    public const string FileExtension = ".placa";

    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = "Sin título";
    /// <summary>Contorno rectangular de la placa, con la esquina superior izquierda en (0, 0).</summary>
    public double BoardWidth { get; set; } = 60;
    public double BoardHeight { get; set; } = 40;
    /// <summary>Cara de cobre por defecto: placa de una cara con pistas abajo.</summary>
    public bool SingleSided { get; set; } = true;
    public DesignRules Rules { get; set; } = new();
    public List<PcbFootprint> Footprints { get; set; } = [];
    public List<PcbTrack> Tracks { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static PcbDocument FromJson(string json) =>
        JsonSerializer.Deserialize<PcbDocument>(json, JsonOptions) ?? throw new FormatException("El archivo de placa está vacío.");

    public List<string> ResolveFootprints(Func<string, FootprintDefinition?> find)
    {
        var missing = new List<string>();
        foreach (var f in Footprints)
        {
            f.Definition = find(f.FootprintName);
            if (f.Definition is null) missing.Add($"{f.Reference} ({f.FootprintName})");
        }
        return missing;
    }

    public IEnumerable<string> CopperLayers => SingleSided ? [Layers.Bottom] : [Layers.Top, Layers.Bottom];
}

/// <summary>Un pad ya colocado en la placa.</summary>
public sealed record PlacedPad(PcbFootprint Footprint, FootprintPad Pad, Vec2 Center, double RotationDeg, string Net)
{
    public bool IsThroughHole => Pad.Type is PadType.ThroughHole or PadType.NpThroughHole;

    /// <summary>Capas de cobre donde está el pad.</summary>
    public bool OnLayer(string layer) =>
        IsThroughHole || Pad.Layers.Any(l => l == layer || l == "*.Cu" || (l == "F&B.Cu"));
}

public static class FootprintTransform
{
    public static Vec2 ToBoard(PcbFootprint f, Vec2 local)
    {
        double a = f.Rotation * Math.PI / 180;
        double c = Math.Cos(a), s = Math.Sin(a);
        // Antihorario en pantalla con Y hacia abajo.
        return new Vec2(f.Position.X + local.X * c + local.Y * s, f.Position.Y - local.X * s + local.Y * c);
    }

    public static IEnumerable<PlacedPad> Pads(PcbFootprint f)
    {
        if (f.Definition is null) yield break;
        foreach (var pad in f.Definition.Pads)
            yield return new PlacedPad(f, pad, ToBoard(f, pad.Position), pad.RotationDeg + f.Rotation,
                f.PadNets.GetValueOrDefault(pad.Number, ""));
    }

    public static (Vec2 Min, Vec2 Max) Bounds(PcbFootprint f)
    {
        var pts = new List<Vec2>();
        if (f.Definition is not null)
        {
            foreach (var p in f.Definition.Pads)
            {
                double r = Math.Max(p.Size.X, p.Size.Y) / 2;
                var c = ToBoard(f, p.Position);
                pts.Add(new Vec2(c.X - r, c.Y - r));
                pts.Add(new Vec2(c.X + r, c.Y + r));
            }
            foreach (var g in f.Definition.Graphics.Where(g => g.Layer is "F.SilkS" or "F.CrtYd" or "F.Fab"))
            {
                switch (g)
                {
                    case RectangleGraphic r: pts.Add(ToBoard(f, r.Start)); pts.Add(ToBoard(f, r.End)); pts.Add(ToBoard(f, new Vec2(r.Start.X, r.End.Y))); pts.Add(ToBoard(f, new Vec2(r.End.X, r.Start.Y))); break;
                    case CircleGraphic c:
                        var cc = ToBoard(f, c.Center);
                        pts.Add(new Vec2(cc.X - c.Radius, cc.Y - c.Radius));
                        pts.Add(new Vec2(cc.X + c.Radius, cc.Y + c.Radius));
                        break;
                    case PolylineGraphic p: pts.AddRange(p.Points.Select(v => ToBoard(f, v))); break;
                    case ArcGraphic a: pts.Add(ToBoard(f, a.Start)); pts.Add(ToBoard(f, a.Mid)); pts.Add(ToBoard(f, a.End)); break;
                }
            }
        }
        if (pts.Count == 0) pts.Add(f.Position);
        return (new Vec2(pts.Min(p => p.X), pts.Min(p => p.Y)), new Vec2(pts.Max(p => p.X), pts.Max(p => p.Y)));
    }
}
