using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Tests;

public class SchematicTests
{
    private readonly ComponentCatalog _catalog = new();

    private PlacedSymbol Place(SchematicDocument doc, string symbol, double x, double y, int rotation = 0, string? value = null)
    {
        var def = _catalog.FindSymbol("Basicos:" + symbol)!;
        var s = new PlacedSymbol
        {
            SymbolName = def.FullName,
            Symbol = def,
            Reference = def.IsPower ? doc.NextReference("#PWR") : doc.NextReference(def.Reference),
            Value = value ?? def.Value,
            Position = new Vec2(x, y),
            Rotation = rotation,
        };
        doc.Symbols.Add(s);
        return s;
    }

    private static Vec2 Pin(PlacedSymbol s, string number) =>
        SymbolTransform.Pins(s).Single(p => p.Pin.Number == number).Position;

    private static void Connect(SchematicDocument doc, Vec2 a, Vec2 b)
    {
        // Cable en L: primero vertical y luego horizontal.
        var corner = new Vec2(a.X, b.Y);
        if (a != corner) doc.Wires.Add(new Wire(a, corner));
        if (corner != b) doc.Wires.Add(new Wire(corner, b));
    }

    private SchematicDocument Divider()
    {
        var doc = new SchematicDocument { Title = "Divisor" };
        var v1 = Place(doc, "VDC", 0, 0, value: "10");
        var r1 = Place(doc, "R", 20, -10);
        var r2 = Place(doc, "R", 20, 10);
        var g1 = Place(doc, "GND", 0, 20);
        var g2 = Place(doc, "GND", 20, 20);
        Connect(doc, Pin(v1, "1"), Pin(r1, "1"));
        Connect(doc, Pin(r1, "2"), Pin(r2, "1"));
        Connect(doc, Pin(r2, "2"), Pin(g2, "1"));
        Connect(doc, Pin(v1, "2"), Pin(g1, "1"));
        return doc;
    }

    [Fact]
    public void Builtin_library_loads()
    {
        Assert.NotNull(_catalog.FindSymbol("Basicos:R"));
        Assert.NotNull(_catalog.FindSymbol("Q_NPN"));
        Assert.True(_catalog.FindSymbol("Basicos:GND")!.IsPower);
        Assert.NotNull(_catalog.FindModel("2N2222"));
    }

    [Fact]
    public void Rotation_moves_pins()
    {
        var doc = new SchematicDocument();
        var r = Place(doc, "R", 10, 10);
        Assert.Equal(new Vec2(10, 10 - 3.81), Pin(r, "1"), Vec2Comparer.Instance);
        r.Rotation = 90;
        Assert.Equal(new Vec2(10 - 3.81, 10), Pin(r, "1"), Vec2Comparer.Instance);
    }

    [Fact]
    public void Divider_has_three_nets_and_ground()
    {
        var conn = Connectivity.Analyze(Divider());
        Assert.Equal(3, conn.Nets.Count);
        Assert.Contains(conn.Nets, n => n.Name == "0" && n.Pins.Count == 4);
    }

    [Fact]
    public void Divider_netlist()
    {
        var doc = Divider();
        var netlist = SpiceNetlister.Build(doc, _catalog, [".op"]);
        var lines = netlist.Text.Split('\n', StringSplitOptions.TrimEntries);

        Assert.Empty(netlist.Warnings);
        var v1 = lines.Single(l => l.StartsWith("V1 "));
        var r1 = lines.Single(l => l.StartsWith("R1 "));
        var r2 = lines.Single(l => l.StartsWith("R2 "));
        Assert.EndsWith(" 0 DC 10", v1);
        Assert.Equal(v1.Split(' ')[1], r1.Split(' ')[1]);
        Assert.Equal(r1.Split(' ')[2], r2.Split(' ')[1]);
        Assert.Equal("R2 " + r2.Split(' ')[1] + " 0 1k", r2);
        Assert.Contains(".op", lines);
        Assert.Equal(".end", lines.Last(l => l.Length > 0));
    }

    [Fact]
    public void T_junction_connects_and_gets_a_dot()
    {
        var doc = new SchematicDocument();
        doc.Wires.Add(new Wire(new Vec2(0, 0), new Vec2(20, 0)));
        doc.Wires.Add(new Wire(new Vec2(10, 0), new Vec2(10, 10)));
        var r = Place(doc, "R", 10, 10 + 3.81);   // pin 1 en (10, 10)
        var g = Place(doc, "GND", 0, 0);          // masa al principio del cable horizontal

        var conn = Connectivity.Analyze(doc);
        Assert.Equal("0", conn.NetOf(r, "1")!.Name);
        Assert.Single(conn.Junctions);
        Assert.Equal(new Vec2(10, 0), conn.Junctions[0], Vec2Comparer.Instance);
        _ = g;
    }

    [Fact]
    public void Labels_join_nets()
    {
        var doc = new SchematicDocument();
        var r1 = Place(doc, "R", 0, 0);
        var r2 = Place(doc, "R", 50, 0);
        doc.Labels.Add(new NetLabel { Name = "SALIDA", Position = Pin(r1, "2") });
        doc.Labels.Add(new NetLabel { Name = "SALIDA", Position = Pin(r2, "1") });

        var conn = Connectivity.Analyze(doc);
        Assert.Same(conn.NetOf(r1, "2"), conn.NetOf(r2, "1"));
        Assert.Equal("SALIDA", conn.NetOf(r1, "2")!.Name);
    }

    [Fact]
    public void Netlist_includes_used_models_and_warns_on_missing_ground()
    {
        var doc = new SchematicDocument();
        var q = Place(doc, "Q_NPN", 0, 0);
        var netlist = SpiceNetlister.Build(doc, _catalog);

        Assert.Contains(".model 2N2222 NPN", netlist.Text);
        Assert.DoesNotContain("2N3906", netlist.Text);
        Assert.Contains(netlist.Warnings, w => w.Contains("masa"));
        Assert.Contains(netlist.Warnings, w => w.Contains("no está conectado"));
        _ = q;
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var doc = Divider();
        var loaded = SchematicDocument.FromJson(doc.ToJson());
        var missing = loaded.ResolveSymbols(_catalog.FindSymbol);

        Assert.Empty(missing);
        Assert.Equal(doc.Symbols.Count, loaded.Symbols.Count);
        Assert.Equal(doc.Wires.Count, loaded.Wires.Count);
        Assert.Equal(SpiceNetlister.Build(doc, _catalog).Text, SpiceNetlister.Build(loaded, _catalog).Text);
    }

    [Fact]
    public void Next_reference_counts_up()
    {
        var doc = new SchematicDocument();
        Place(doc, "R", 0, 0);
        Place(doc, "R", 0, 0);
        Assert.Equal("R3", doc.NextReference("R"));
        Assert.Equal("C1", doc.NextReference("C"));
    }

    private sealed class Vec2Comparer : IEqualityComparer<Vec2>
    {
        public static readonly Vec2Comparer Instance = new();
        public bool Equals(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        public int GetHashCode(Vec2 v) => 0;
    }
}

public class ExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Examples_are_fully_connected(int index)
    {
        var catalog = new ComponentCatalog();
        var doc = Examples.All[index].Build(catalog);
        var netlist = SpiceNetlister.Build(doc, catalog, [".op"]);
        Assert.Empty(netlist.Warnings);
    }

    [Fact]
    public void Led_example_netlist_has_expected_elements()
    {
        var catalog = new ComponentCatalog();
        var netlist = SpiceNetlister.Build(Examples.LedTransistor(catalog), catalog);
        Assert.Contains(".model LED_ROJO", netlist.Text);
        Assert.Contains(".model 2N2222", netlist.Text);
        // El emisor del transistor va a masa.
        Assert.Matches(@"(?m)^Q1 \S+ \S+ 0 2N2222\r?$", netlist.Text);
    }
}
