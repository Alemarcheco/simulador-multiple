using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Schematic;

// Coordenadas del esquema en milímetros, con Y hacia abajo (como la pantalla).
// Los símbolos de librería usan Y hacia arriba; SymbolTransform hace la conversión.

/// <summary>Un componente colocado en el esquema.</summary>
public sealed class PlacedSymbol
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>"Libreria:Nombre" del símbolo.</summary>
    public string SymbolName { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Value { get; set; } = "";
    public Vec2 Position { get; set; }
    /// <summary>0, 90, 180 o 270 grados, antihorario.</summary>
    public int Rotation { get; set; }
    public bool Mirror { get; set; }
    /// <summary>Huella para el PCB ("Libreria:Nombre"). Vacía = la del símbolo.</summary>
    public string Footprint { get; set; } = "";

    /// <summary>Definición resuelta desde el catálogo (no se guarda en el archivo).</summary>
    [JsonIgnore] public SymbolDefinition? Symbol { get; set; }

    public PlacedSymbol Clone() => (PlacedSymbol)MemberwiseClone();
}

/// <summary>Tramo de cable recto.</summary>
public sealed class Wire
{
    public Vec2 A { get; set; }
    public Vec2 B { get; set; }

    public Wire() { }
    public Wire(Vec2 a, Vec2 b) { A = a; B = b; }
}

/// <summary>Etiqueta de red: todos los puntos con la misma etiqueta quedan unidos.</summary>
public sealed class NetLabel
{
    public string Name { get; set; } = "";
    public Vec2 Position { get; set; }
}

/// <summary>Documento de esquema (archivo .esquema).</summary>
public sealed class SchematicDocument
{
    public const string FileExtension = ".esquema";

    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = "Sin título";
    public List<PlacedSymbol> Symbols { get; set; } = [];
    public List<Wire> Wires { get; set; } = [];
    public List<NetLabel> Labels { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SchematicDocument FromJson(string json) =>
        JsonSerializer.Deserialize<SchematicDocument>(json, JsonOptions)
        ?? throw new FormatException("El archivo de esquema está vacío.");

    /// <summary>Asigna a cada símbolo su definición del catálogo. Devuelve los que no se encontraron.</summary>
    public List<string> ResolveSymbols(Func<string, SymbolDefinition?> find)
    {
        var missing = new List<string>();
        foreach (var s in Symbols)
        {
            s.Symbol = find(s.SymbolName);
            if (s.Symbol is null) missing.Add($"{s.Reference} ({s.SymbolName})");
        }
        return missing;
    }

    /// <summary>Siguiente referencia libre para un prefijo: R1, R2…</summary>
    public string NextReference(string prefix)
    {
        prefix = prefix.TrimEnd('?');
        int max = 0;
        foreach (var s in Symbols)
            if (s.Reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(s.Reference.AsSpan(prefix.Length), out var n))
                max = Math.Max(max, n);
        return prefix + (max + 1);
    }
}

/// <summary>Conversión de coordenadas del símbolo (Y arriba) al esquema (Y abajo).</summary>
public static class SymbolTransform
{
    public static Vec2 ToWorld(PlacedSymbol s, Vec2 local)
    {
        double x = s.Mirror ? -local.X : local.X;
        double y = local.Y;
        (x, y) = (((s.Rotation % 360) + 360) % 360) switch
        {
            90 => (-y, x),
            180 => (-x, -y),
            270 => (y, -x),
            _ => (x, y),
        };
        return new Vec2(s.Position.X + x, s.Position.Y - y);
    }

    /// <summary>Posición de cada pin en el esquema (solo pines de la unidad 1 o comunes).</summary>
    public static IEnumerable<(SymbolPin Pin, Vec2 Position)> Pins(PlacedSymbol s)
    {
        if (s.Symbol is null) yield break;
        foreach (var pin in s.Symbol.Pins)
            if (pin.Unit is 0 or 1)
                yield return (pin, ToWorld(s, pin.Position));
    }

    /// <summary>Rectángulo que ocupa el símbolo (para seleccionar con el ratón).</summary>
    public static (Vec2 Min, Vec2 Max) Bounds(PlacedSymbol s)
    {
        var pts = new List<Vec2>();
        if (s.Symbol is not null)
        {
            foreach (var pin in s.Symbol.Pins.Where(p => p.Unit is 0 or 1))
            {
                pts.Add(ToWorld(s, pin.Position));
                pts.Add(ToWorld(s, pin.BodyEnd));
            }
            foreach (var g in s.Symbol.Graphics.Where(g => g.Unit is 0 or 1))
            {
                switch (g)
                {
                    case RectangleGraphic r: pts.Add(ToWorld(s, r.Start)); pts.Add(ToWorld(s, r.End)); break;
                    case CircleGraphic c:
                        pts.Add(ToWorld(s, new Vec2(c.Center.X - c.Radius, c.Center.Y - c.Radius)));
                        pts.Add(ToWorld(s, new Vec2(c.Center.X + c.Radius, c.Center.Y + c.Radius)));
                        break;
                    case PolylineGraphic p: pts.AddRange(p.Points.Select(v => ToWorld(s, v))); break;
                    case ArcGraphic a: pts.Add(ToWorld(s, a.Start)); pts.Add(ToWorld(s, a.Mid)); pts.Add(ToWorld(s, a.End)); break;
                }
            }
        }
        if (pts.Count == 0) pts.Add(s.Position);
        const double pad = 0.5;
        return (new Vec2(pts.Min(p => p.X) - pad, pts.Min(p => p.Y) - pad),
                new Vec2(pts.Max(p => p.X) + pad, pts.Max(p => p.Y) + pad));
    }
}
