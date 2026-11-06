using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;
using Simulador.Circuitos.Simulation;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Tests;

public class CurrentFlowTests
{
    private readonly ComponentCatalog _catalog = new();

    private (SchematicDocument Doc, SpiceNetlist Netlist, SimulationResult Result) Run(SchematicDocument doc, string analysis = ".op")
    {
        var netlist = SpiceNetlister.Build(doc, _catalog, [analysis], currentProbes: true);
        var result = NgSpice.Run(netlist.Text);
        Assert.True(result.Ok, string.Join("\n", result.Errors.Concat(result.Log)) + "\n" + netlist.Text);
        return (doc, netlist, result);
    }

    [Fact]
    public void Probes_do_not_change_voltages()
    {
        var (_, _, result) = Run(Examples.Divider(_catalog));
        Assert.Equal(12 * 4.7 / 14.7, result.Voltage("salida")![0], 3);
        // La corriente entra por el pin 1 de R1 (arriba) y sale por el 2: 12 V / 14.7 kΩ.
        Assert.Equal(12 / 14.7e3, result.Real[SpiceNetlister.ProbeVector("R1", "1")][0], 6);
        Assert.Equal(-12 / 14.7e3, result.Real[SpiceNetlister.ProbeVector("R1", "2")][0], 6);
        Assert.DoesNotContain(result.Real.Keys, k => k.StartsWith("salida", StringComparison.Ordinal) && k != "salida");
    }

    [Fact]
    public void Every_wire_in_a_series_loop_carries_the_same_current()
    {
        var (doc, netlist, result) = Run(Examples.Divider(_catalog));
        var anim = FlowAnimation.From(doc, netlist.Connectivity, result)!;
        double i = 12 / 14.7e3;

        // Todos los tramos de cable (no los pines) llevan la corriente del lazo.
        var wires = anim.Segments.Select((s, k) => (s, c: anim.Currents[0][k])).Where(x => x.s.Terms.Count > 0 && !IsStub(doc, x.s)).ToList();
        Assert.NotEmpty(wires);
        Assert.All(wires, x => Assert.Equal(i, Math.Abs(x.c), 6));

        // El cable de arriba va del + de la fuente (0,-5.08) hacia R1: la corriente sube y luego va a la derecha.
        var top = anim.Segments.Select((s, k) => (s, c: anim.Currents[0][k])).Single(x => Same(x.s, new Vec2(0, -15.24), new Vec2(20.32, -15.24)));
        double dir = top.s.A.X < top.s.B.X ? 1 : -1;
        Assert.True(top.c * dir > 0, "la corriente debe ir de la fuente hacia la resistencia");
    }

    [Fact]
    public void Led_current_is_reported()
    {
        var (doc, netlist, result) = Run(Examples.LedTransistor(_catalog));
        var anim = FlowAnimation.From(doc, netlist.Connectivity, result)!;
        Assert.InRange(anim.LedCurrents["D1"][0], 0.005, 0.03);
        Assert.Equal(9, anim.MaxVoltage, 3);
    }

    private static bool IsStub(SchematicDocument doc, FlowSegment s) =>
        !doc.Wires.Any(w => OnWire(w, s.A) && OnWire(w, s.B));

    private static bool OnWire(Wire w, Vec2 p)
    {
        double cross = (w.B.X - w.A.X) * (p.Y - w.A.Y) - (w.B.Y - w.A.Y) * (p.X - w.A.X);
        return Math.Abs(cross) < 1e-6 && p.X >= Math.Min(w.A.X, w.B.X) - 1e-6 && p.X <= Math.Max(w.A.X, w.B.X) + 1e-6
            && p.Y >= Math.Min(w.A.Y, w.B.Y) - 1e-6 && p.Y <= Math.Max(w.A.Y, w.B.Y) + 1e-6;
    }

    private static bool Same(FlowSegment s, Vec2 a, Vec2 b) =>
        (Near(s.A, a) && Near(s.B, b)) || (Near(s.A, b) && Near(s.B, a));

    private static bool Near(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
}

public class ExampleSimulationTests
{
    private readonly ComponentCatalog _catalog = new();

    private FlowAnimation Animate(Func<ComponentCatalog, SchematicDocument> build)
    {
        var doc = build(_catalog);
        var outcome = LiveRun.Run(doc, _catalog, LiveRun.Suggest(doc));
        Assert.True(outcome.Result?.Ok, outcome.Message + "\n" + outcome.Netlist.Text + string.Join("\n", outcome.Result?.Log ?? []));
        Assert.NotNull(outcome.Animation);
        return outcome.Animation!;
    }

    [Fact]
    public void Astable_blinks_both_leds()
    {
        var anim = Animate(Examples.AstableMultivibrator);
        Assert.False(anim.IsStatic);
        foreach (var led in new[] { "D1", "D2" })
        {
            var i = anim.LedCurrents[led];
            // Después del arranque cada LED pasa por apagado y por encendido.
            var late = i.Where((_, k) => anim.Times[k] > 1).ToArray();
            Assert.True(late.Min() < 0.001, $"{led} nunca se apaga (mín {late.Min()})");
            Assert.True(late.Max() > 0.008, $"{led} nunca se enciende (máx {late.Max()})");
        }
    }

    [Fact]
    public void Rectifier_output_stays_high()
    {
        var anim = Animate(Examples.HalfWaveRectifier);
        var v = anim.Voltages["SALIDA"];
        var late = v.Where((_, k) => anim.Times[k] > 0.05).ToArray();
        Assert.InRange(late.Min(), 8, 12);
        Assert.InRange(late.Max(), 10.5, 12);
    }

    [Fact]
    public void Amplifier_has_gain()
    {
        var anim = Animate(Examples.CommonEmitterAmplifier);
        var vin = anim.Voltages["ENTRADA"];
        var vout = anim.Voltages["SALIDA"];
        double Pp(double[] v) => v.Where((_, k) => anim.Times[k] > 0.002).Max() - v.Where((_, k) => anim.Times[k] > 0.002).Min();
        Assert.True(Pp(vout) > 10 * Pp(vin), $"ganancia {Pp(vout) / Pp(vin):0.0}");
    }

    [Fact]
    public void Capacitor_charges_and_discharges()
    {
        var anim = Animate(Examples.CapacitorCharge);
        var vc = anim.Voltages["CONDENSADOR"];
        Assert.InRange(vc[anim.IndexAt(0.0099)], 4.8, 5.01);
        Assert.InRange(vc[anim.IndexAt(0.0199)], -0.01, 0.2);
    }

    [Fact]
    public void Sine_source_suggests_three_periods()
    {
        var doc = Examples.RcFilter(_catalog);
        var s = LiveRun.Suggest(doc);
        Assert.Equal(AnalysisKind.Transient, s.Kind);
        Assert.Equal(3e-3, EngineeringFormat.ParseSpice(s.StopTime)!.Value, 9);
    }
}

public class NewComponentTests
{
    private readonly ComponentCatalog _catalog = new();

    private (FlowAnimation Anim, SimulationResult Result) Run(Func<ComponentCatalog, SchematicDocument> build)
    {
        var doc = build(_catalog);
        var outcome = LiveRun.Run(doc, _catalog, LiveRun.Suggest(doc));
        Assert.True(outcome.Result?.Ok, outcome.Message + "\n" + outcome.Netlist.Text + string.Join("\n", outcome.Result?.Log ?? []));
        return (outcome.Animation!, outcome.Result!);
    }

    [Fact]
    public void New_parts_have_symbols_models_and_footprints()
    {
        foreach (var (sym, model, fp) in new[] { ("NE555", "NE555", "DIP-8_W7.62mm"), ("OPAMP", "UA741", "DIP-8_W7.62mm"), ("REG_7805", "UA7805", "TO-220_Vertical"), ("RELE", "RELE_5V", "Rele_SRD") })
        {
            Assert.NotNull(_catalog.FindSymbol("Basicos:" + sym));
            Assert.NotNull(_catalog.FindSubcircuit(model));
            Assert.NotNull(_catalog.FindFootprint("Basicos:" + fp));
        }
    }

    [Fact]
    public void Blinker_555_oscillates_near_1_5_hz()
    {
        var (anim, _) = Run(Examples.Blinker555);
        var v = anim.Voltages["SALIDA"];
        // Cuenta los flancos de subida después del primer ciclo.
        int rises = 0;
        for (int k = 1; k < v.Length; k++)
            if (anim.Times[k] > 0.5 && v[k - 1] < 4.5 && v[k] >= 4.5) rises++;
        Assert.InRange(rises, 4, 7);
        Assert.InRange(anim.LedCurrents["D1"].Max(), 0.008, 0.03);
    }

    [Fact]
    public void Regulator_gives_five_volts()
    {
        var (anim, _) = Run(Examples.Regulator7805);
        Assert.Equal(5, anim.Voltages["SALIDA_5V"][0], 1);
        Assert.Equal(12, anim.Voltages["ENTRADA_12V"][0], 3);
        // La corriente del LED sale de la entrada de 12 V.
        Assert.InRange(anim.LedCurrents["D1"][0], 0.005, 0.015);
    }

    [Fact]
    public void Opamp_amplifies_eleven_times()
    {
        var (anim, _) = Run(Examples.OpAmpNonInverting);
        double Pp(double[] v) => v.Where((_, k) => anim.Times[k] > 0.001).Max() - v.Where((_, k) => anim.Times[k] > 0.001).Min();
        Assert.InRange(Pp(anim.Voltages["SALIDA"]) / Pp(anim.Voltages["ENTRADA"]), 10.5, 11.5);
    }

    [Fact]
    public void Relay_switches_the_leds()
    {
        var (anim, _) = Run(Examples.RelayDriver);
        double At(string led, double t) => anim.LedCurrents[led][anim.IndexAt(t)];
        // Con la señal a 5 V (0,2 s a 0,7 s) luce el LED del contacto NA; luego, el del NC.
        Assert.True(At("D2", 0.5) > 0.004, $"NA a 0,5 s: {At("D2", 0.5)}");
        Assert.True(At("D3", 0.5) < 0.0005);
        Assert.True(At("D3", 0.9) > 0.004, $"NC a 0,9 s: {At("D3", 0.9)}");
        Assert.True(At("D2", 0.9) < 0.0005);
    }
}
