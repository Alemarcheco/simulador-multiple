using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;
using Simulador.Circuitos.Simulation;

namespace Simulador.Circuitos.Tests;

// Estas pruebas necesitan la biblioteca de ngspice (en CI se descarga; en Linux: apt install libngspice0).
public class NgSpiceTests
{
    [Fact]
    public void Divider_operating_point()
    {
        var catalog = new ComponentCatalog();
        var doc = Examples.Divider(catalog);   // 12 V, 10k arriba, 4.7k abajo, etiqueta SALIDA
        var netlist = SpiceNetlister.Build(doc, catalog, new AnalysisSettings().ToSpice());

        var result = NgSpice.Run(netlist.Text);

        Assert.True(result.Ok, string.Join("\n", result.Errors.Concat(result.Log)));
        var vout = result.Voltage("salida");
        Assert.NotNull(vout);
        Assert.Equal(12 * 4.7 / 14.7, vout![0], 3);
    }

    [Fact]
    public void Led_transistor_turns_on()
    {
        var catalog = new ComponentCatalog();
        var doc = Examples.LedTransistor(catalog);
        var netlist = SpiceNetlister.Build(doc, catalog, new AnalysisSettings().ToSpice());

        var result = NgSpice.Run(netlist.Text);

        Assert.True(result.Ok, string.Join("\n", result.Errors.Concat(result.Log)));
        // Corriente por la fuente de 9 V: el LED conduce (unos 10-15 mA). ngspice da la corriente entrando por +.
        var i = -result.Real["v1#branch"][0];
        Assert.InRange(i, 0.005, 0.03);
    }

    [Fact]
    public void Transient_and_ac_produce_vectors()
    {
        const string rc = """
            * RC
            V1 in 0 SIN(0 1 1k) AC 1
            R1 in out 1k
            C1 out 0 100n
            .end
            """;
        var tran = NgSpice.Run(rc.Replace(".end", ".tran 10u 2m\n.end"));
        Assert.True(tran.Ok, string.Join("\n", tran.Errors));
        Assert.True(tran.Real["time"].Length > 100);
        Assert.Equal(tran.Real["time"].Length, tran.Voltage("out")!.Length);

        var ac = NgSpice.Run(rc.Replace(".end", ".ac dec 10 10 100k\n.end"));
        Assert.True(ac.Ok, string.Join("\n", ac.Errors));
        var gain = ac.Complex["out"];
        Assert.True(gain[0].Magnitude > 0.99);        // 10 Hz: pasa
        Assert.True(gain[^1].Magnitude < 0.05);       // 100 kHz: atenuado (fc ≈ 1.6 kHz)
    }

    [Fact]
    public void Rc_example_attenuates_at_cutoff()
    {
        var catalog = new ComponentCatalog();
        var netlist = SpiceNetlister.Build(Examples.RcFilter(catalog), catalog,
            new AnalysisSettings { Kind = AnalysisKind.Ac, StartFrequency = "1591.55", StopFrequency = "15915.5", PointsPerDecade = 1 }.ToSpice());
        var r = NgSpice.Run(netlist.Text);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(1 / Math.Sqrt(2), r.Complex["salida"][0].Magnitude, 3);   // -3 dB en fc = 1/(2πRC)
    }

    [Fact]
    public void Bad_netlist_reports_error()
    {
        var r = NgSpice.Run("* malo\nR1 a 0 1k\nQ1 a b 0 NOEXISTE\n.op\n.end");
        Assert.False(r.Ok);
    }
}

public class EngineeringFormatTests
{
    [Theory]
    [InlineData("4.7k", 4700)]
    [InlineData("100n", 100e-9)]
    [InlineData("1meg", 1e6)]
    [InlineData("10", 10)]
    [InlineData("2.2u", 2.2e-6)]
    [InlineData("1e-3", 1e-3)]
    [InlineData("5m", 5e-3)]
    public void Parses_spice_suffixes(string text, double expected) =>
        Assert.Equal(expected, EngineeringFormat.ParseSpice(text)!.Value, 9);

    [Fact]
    public void Formats_with_prefixes()
    {
        Assert.Equal("4.7 kΩ", EngineeringFormat.Format(4700, "Ω").Replace(',', '.'));
        Assert.Equal("12 mA", EngineeringFormat.Format(0.012, "A").Replace(',', '.'));
    }
}
