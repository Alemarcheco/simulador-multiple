namespace Simulador.Electrico.Engine.Model;

/// <summary>Ayuda para montar esquemas desde código (ejemplos y pruebas).</summary>
public sealed class SchematicBuilder(string name, string description = "")
{
    public Schematic Schematic { get; } = new() { Name = name, Description = description };

    public PlacedElement Add(string type, string tag, int x, int y, params (string key, double value)[] parameters)
    {
        var e = new PlacedElement { Type = type, Tag = tag, X = x, Y = y };
        foreach (var (k, v) in parameters) e.Params[k] = v;
        Schematic.Elements.Add(e);
        return e;
    }

    public PlacedElement Text(PlacedElement e, string key, string value)
    {
        e.Texts[key] = value;
        return e;
    }

    public SchematicBuilder Wire(PlacedElement a, string ta, PlacedElement b, string tb)
    {
        Schematic.Wires.Add(new Wire { A = new TerminalRef(a.Id, ta), B = new TerminalRef(b.Id, tb) });
        return this;
    }

    /// <summary>Cablea en serie una cadena de elementos de dos bornes (2 de uno con 1 del siguiente).</summary>
    public SchematicBuilder Chain(params PlacedElement[] items)
    {
        for (var i = 0; i + 1 < items.Length; i++) Wire(items[i], Out(items[i]), items[i + 1], In(items[i + 1]));
        return this;
    }

    public static string In(PlacedElement e) => e.Type.StartsWith("bobina_") || e.Type == "electrovalvula" ? "A1" : "1";
    public static string Out(PlacedElement e) => e.Type.StartsWith("bobina_") || e.Type == "electrovalvula" ? "A2" : "2";
}
