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
        ("Filtro RC paso bajo", RcFilter),
        ("Carga y descarga de un condensador", CapacitorCharge),
        ("Rectificador de media onda con filtro", HalfWaveRectifier),
        ("Multivibrador: dos LEDs que parpadean", AstableMultivibrator),
        ("Amplificador con transistor (emisor común)", CommonEmitterAmplifier),
    ];

    public static SchematicDocument CapacitorCharge(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "Carga y descarga de un condensador");
        // Fuente de pulsos: 5 V durante 10 ms y 0 V otros 10 ms.
        b.Add("VSIN", 0, 0, value: "PULSE(0 5 0 1u 1u 10m 20m)");
        b.Add("R", 12.7, -10.16, rotation: 90, value: "1k");
        b.Add("C", 25.4, -3.81, value: "2.2u");
        b.Add("GND", 0, 12.7);
        b.Add("GND", 25.4, 12.7);
        b.Wire((0, -5.08), (0, -10.16), (8.89, -10.16));
        b.Wire((16.51, -10.16), (25.4, -10.16), (25.4, -7.62));
        b.Wire((25.4, 0), (25.4, 12.7));
        b.Wire((0, 5.08), (0, 12.7));
        b.Doc.Labels.Add(new NetLabel { Name = "PULSOS", Position = new Vec2(0, -10.16) });
        b.Doc.Labels.Add(new NetLabel { Name = "CONDENSADOR", Position = new Vec2(25.4, -10.16) });
        b.Doc.TranStop = "40m";
        b.Doc.TranStep = "20u";
        return b.Doc;
    }

    public static SchematicDocument HalfWaveRectifier(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "Rectificador de media onda con filtro");
        b.Add("VSIN", 0, 0, value: "SIN(0 12 50)");
        b.Add("D", 12.7, -10.16, value: "1N4007");
        b.Add("C", 25.4, -3.81, value: "470u");
        b.Add("R", 38.1, -3.81, value: "1k");
        b.Add("GND", 0, 12.7);
        b.Add("GND", 25.4, 12.7);
        b.Add("GND", 38.1, 12.7);
        b.Wire((0, -5.08), (0, -10.16), (8.89, -10.16));
        b.Wire((16.51, -10.16), (38.1, -10.16), (38.1, -7.62));
        b.Wire((25.4, -10.16), (25.4, -7.62));
        b.Wire((25.4, 0), (25.4, 12.7));
        b.Wire((38.1, 0), (38.1, 12.7));
        b.Wire((0, 5.08), (0, 12.7));
        b.Doc.Labels.Add(new NetLabel { Name = "ALTERNA", Position = new Vec2(0, -10.16) });
        b.Doc.Labels.Add(new NetLabel { Name = "SALIDA", Position = new Vec2(25.4, -10.16) });
        b.Doc.TranStop = "100m";
        b.Doc.TranStep = "50u";
        return b.Doc;
    }

    public static SchematicDocument AstableMultivibrator(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "Multivibrador astable con LEDs");
        b.Add("VDC", -10.16, 0, value: "9");
        b.Add("GND", -10.16, 12.7);
        b.Add("R", 15.24, -19.05, value: "470");
        b.Add("LED", 15.24, -10.16, rotation: 270);
        b.Add("Q_NPN", 16.51, 5.08, mirror: true);
        b.Add("R", 27.94, -19.05, value: "47k");
        b.Add("R", 48.26, -19.05, value: "47k");
        b.Add("C", 38.1, -2.54, rotation: 90, value: "10u");
        b.Add("C", 45.72, 17.78, rotation: 90, value: "10u");
        b.Add("Q_NPN", 59.69, 5.08);
        b.Add("LED", 60.96, -10.16, rotation: 270);
        b.Add("R", 60.96, -19.05, value: "470");
        b.Add("GND", 15.24, 12.7);
        b.Add("GND", 60.96, 12.7);
        // Alimentación arriba.
        b.Wire((-10.16, -5.08), (-10.16, -25.4), (60.96, -25.4), (60.96, -22.86));
        foreach (var x in new[] { 15.24, 27.94, 48.26 }) b.Wire((x, -25.4), (x, -22.86));
        b.Wire((-10.16, 5.08), (-10.16, 12.7));
        // Lado izquierdo: LED1 y Q1.
        b.Wire((15.24, -15.24), (15.24, -13.97));
        b.Wire((15.24, -6.35), (15.24, 0));
        b.Wire((15.24, 10.16), (15.24, 12.7));
        b.Wire((27.94, -15.24), (27.94, 5.08), (21.59, 5.08));
        // Lado derecho: LED2 y Q2.
        b.Wire((60.96, -15.24), (60.96, -13.97));
        b.Wire((60.96, -6.35), (60.96, 0));
        b.Wire((60.96, 10.16), (60.96, 12.7));
        b.Wire((48.26, -15.24), (48.26, 5.08), (54.61, 5.08));
        // Condensadores cruzados: colector de un transistor a la base del otro.
        b.Wire((15.24, -2.54), (34.29, -2.54));
        b.Wire((41.91, -2.54), (48.26, -2.54));
        b.Wire((60.96, -2.54), (66.04, -2.54), (66.04, 17.78), (49.53, 17.78));
        b.Wire((41.91, 17.78), (24.13, 17.78), (24.13, 5.08));
        b.Doc.TranStop = "4";
        b.Doc.TranStep = "2m";
        return b.Doc;
    }

    public static SchematicDocument CommonEmitterAmplifier(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "Amplificador en emisor común");
        b.Add("VSIN", 7.62, 5.08, value: "SIN(0 20m 1k)");
        b.Add("C", 22.86, 0, rotation: 90, value: "10u");
        b.Add("R", 30.48, -12.7, value: "47k");
        b.Add("R", 30.48, 12.7, value: "10k");
        b.Add("Q_NPN", 40.64, 0);
        b.Add("R", 41.91, -12.7, value: "4.7k");
        b.Add("R", 41.91, 12.7, value: "1k");
        b.Add("C", 50.8, 12.7, value: "100u");
        b.Add("C", 55.88, -6.35, rotation: 90, value: "10u");
        b.Add("R", 63.5, 1.27, value: "10k");
        b.Add("VDC", 76.2, 0, value: "12");
        foreach (var x in new[] { 7.62, 30.48, 41.91, 50.8, 63.5, 76.2 }) b.Add("GND", x, 22.86);
        // Entrada.
        b.Wire((7.62, 0), (19.05, 0));
        b.Wire((7.62, 10.16), (7.62, 22.86));
        b.Wire((26.67, 0), (30.48, 0), (35.56, 0));
        // Polarización de la base.
        b.Wire((30.48, -8.89), (30.48, 8.89));
        b.Wire((30.48, 16.51), (30.48, 22.86));
        // Colector, emisor y condensador de desacoplo.
        b.Wire((41.91, -8.89), (41.91, -5.08));
        b.Wire((41.91, 5.08), (41.91, 8.89));
        b.Wire((41.91, 16.51), (41.91, 22.86));
        b.Wire((41.91, 6.35), (50.8, 6.35), (50.8, 8.89));
        b.Wire((50.8, 16.51), (50.8, 22.86));
        // Salida.
        b.Wire((41.91, -6.35), (52.07, -6.35));
        b.Wire((59.69, -6.35), (63.5, -6.35), (63.5, -2.54));
        b.Wire((63.5, 5.08), (63.5, 22.86));
        // Alimentación.
        b.Wire((30.48, -16.51), (30.48, -21.59), (76.2, -21.59), (76.2, -5.08));
        b.Wire((41.91, -16.51), (41.91, -21.59));
        b.Wire((76.2, 5.08), (76.2, 22.86));
        b.Doc.Labels.Add(new NetLabel { Name = "ENTRADA", Position = new Vec2(12.7, 0) });
        b.Doc.Labels.Add(new NetLabel { Name = "SALIDA", Position = new Vec2(63.5, -6.35) });
        b.Doc.TranStop = "4m";
        b.Doc.TranStep = "2u";
        return b.Doc;
    }

    public static SchematicDocument RcFilter(ComponentCatalog catalog)
    {
        var b = new Builder(catalog, "Filtro RC paso bajo");
        b.Add("VSIN", 0, 0, value: "SIN(0 1 1k)");
        b.Add("R", 12.7, -10.16, rotation: 90, value: "1k");
        b.Add("C", 25.4, -3.81, value: "100n");
        b.Add("GND", 0, 12.7);
        b.Add("GND", 25.4, 12.7);
        b.Wire((0, -5.08), (0, -10.16), (8.89, -10.16));
        b.Wire((16.51, -10.16), (25.4, -10.16), (25.4, -7.62));
        b.Wire((25.4, 0), (25.4, 12.7));
        b.Wire((0, 5.08), (0, 12.7));
        b.Doc.Labels.Add(new NetLabel { Name = "ENTRADA", Position = new Vec2(0, -10.16) });
        b.Doc.Labels.Add(new NetLabel { Name = "SALIDA", Position = new Vec2(25.4, -10.16) });
        return b.Doc;
    }

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

        public void Add(string symbol, double x, double y, int rotation = 0, string? value = null, bool mirror = false)
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
                Mirror = mirror,
            });
        }

        public void Wire(params (double X, double Y)[] points)
        {
            for (int i = 1; i < points.Length; i++)
                Doc.Wires.Add(new Wire(new Vec2(points[i - 1].X, points[i - 1].Y), new Vec2(points[i].X, points[i].Y)));
        }
    }
}
