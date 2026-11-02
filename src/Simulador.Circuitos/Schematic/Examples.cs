using Simulador.Circuitos.Catalog;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Schematic;

/// <summary>Esquemas de ejemplo para aprender a usar el editor.</summary>
public static class Examples
{
    public static IReadOnlyList<(string Name, Func<ComponentCatalog, SchematicDocument> Build)> All { get; } =
    [
        ("LED con transistor", LedTransistor),
        ("Divisor de tensión", Divider),
    ];

    public static SchematicDocument LedTransistor(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "LED con transistor");
        b.Add("VDC", 0, 0, value: "9");
        b.Add("GND", 0, 12.7);
        b.Add("R", 25.4, -15.24, value: "470");
        b.Add("LED", 25.4, -3.81, rotation: 270);
        b.Add("Q_NPN", 24.13, 7.62);
        b.Add("R", 12.7, 7.62, rotation: 90, value: "10k");
        b.Add("GND", 25.4, 17.78);
        b.Wire((0, -5.08), (0, -22.86), (25.4, -22.86), (25.4, -19.05));
        b.Wire((25.4, -11.43), (25.4, -7.62));
        b.Wire((25.4, 0), (25.4, 2.54));
        b.Wire((19.05, 7.62), (16.51, 7.62));
        b.Wire((8.89, 7.62), (8.89, -22.86));
        b.Wire((25.4, 12.7), (25.4, 17.78));
        b.Wire((0, 5.08), (0, 12.7));
        return b.Doc;
    }

    public static SchematicDocument Divider(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "Divisor de tensión");
        b.Add("VDC", 0, 0, value: "12");
        b.Add("R", 20.32, -7.62, value: "10k");
        b.Add("R", 20.32, 7.62, value: "4.7k");
        b.Add("GND", 0, 15.24);
        b.Add("GND", 20.32, 15.24);
        b.Wire((0, -5.08), (0, -15.24), (20.32, -15.24), (20.32, -11.43));
        b.Wire((20.32, -3.81), (20.32, 3.81));
        b.Wire((20.32, 11.43), (20.32, 15.24));
        b.Wire((0, 5.08), (0, 15.24));
        b.Doc.Labels.Add(new NetLabel { Name = "SALIDA", Position = new Vec2(20.32, 0) });
        return b.Doc;
    }

    private sealed class Builder(ComponentCatalog catalog, string title)
    {
        public SchematicDocument Doc { get; } = new() { Title = title };

        public void Add(string symbol, double x, double y, int rotation = 0, string? value = null)
        {
            var def = catalog.FindSymbol(ComponentCatalog.BuiltInLibrary + ":" + symbol)
                ?? throw new InvalidOperationException("Falta el símbolo " + symbol);
            Doc.Symbols.Add(new PlacedSymbol
            {
                SymbolName = def.FullName,
                Symbol = def,
                Reference = def.IsPower ? Doc.NextReference("#PWR") : Doc.NextReference(def.Reference),
                Value = value ?? def.Value,
                Position = new Vec2(x, y),
                Rotation = rotation,
            });
        }

        public void Wire(params (double X, double Y)[] points)
        {
            for (int i = 1; i < points.Length; i++)
                Doc.Wires.Add(new Wire(new Vec2(points[i - 1].X, points[i - 1].Y), new Vec2(points[i].X, points[i].Y)));
        }
    }
}
