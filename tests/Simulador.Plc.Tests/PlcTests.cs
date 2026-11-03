using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Tests;

public class PlcTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(10);

    private static PlcCpu Siemens(string st)
    {
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        cpu.LoadStructuredText(st);
        cpu.Start();
        return cpu;
    }

    private static ISignalPort Port(PlcCpu cpu, string address) => cpu.Signals.Single(s => s.Id == $"{cpu.Id}.{address}");

    /// <summary>Ejecuta ciclos durante <paramref name="duration"/> desde <paramref name="t"/>.</summary>
    private static TimeSpan Run(PlcCpu cpu, TimeSpan t, TimeSpan duration)
    {
        var end = t + duration;
        while (t < end)
        {
            t += Cycle;
            cpu.Scan(t);
        }
        return t;
    }

    private const string StartStop = """
        PROGRAM Marcha
        VAR
            Marcha AT %I0.0 : BOOL;
            Paro   AT %I0.1 : BOOL;   (* contacto NC: 24 V en reposo *)
            Motor  AT %Q0.0 : BOOL;
        END_VAR
        Motor := (Marcha OR Motor) AND Paro;
        END_PROGRAM
        """;

    [Fact]
    public void Start_stop_latch_in_structured_text()
    {
        var cpu = Siemens(StartStop);
        var t = TimeSpan.Zero;
        Port(cpu, "I0.1").Value = 24;
        t = Run(cpu, t, Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);

        Port(cpu, "I0.0").Value = 24;
        t = Run(cpu, t, Cycle);
        Port(cpu, "I0.0").Value = 0;
        t = Run(cpu, t, Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);

        Port(cpu, "I0.1").Value = 0;
        Run(cpu, t, Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
    }

    [Fact]
    public void Ladder_and_structured_text_behave_the_same()
    {
        var ladder = new LadderProgram(
            new Rung(
                new Series(
                    new ParallelBranch(new Contact(ContactKind.NormallyOpen, "I0.0"), new Contact(ContactKind.NormallyOpen, "Q0.0")),
                    new Contact(ContactKind.NormallyOpen, "I0.1")),
                new Coil(CoilKind.Normal, "Q0.0")));
        var ld = new PlcCpu("PLC2", new SiemensS71200Profile());
        ld.LoadLadder(ladder);
        ld.Start();
        var st = Siemens(StartStop);

        var t = TimeSpan.Zero;
        foreach (var (start, stop) in new[] { (0, 24), (24, 24), (0, 24), (0, 0), (0, 24) })
        {
            foreach (var cpu in new[] { ld, st })
            {
                Port(cpu, "I0.0").Value = start;
                Port(cpu, "I0.1").Value = stop;
            }
            t += Cycle;
            ld.Scan(t);
            st.Scan(t);
            Assert.Equal(Port(st, "Q0.0").Value, Port(ld, "Q0.0").Value);
        }
    }

    [Fact]
    public void Ton_switches_on_after_preset_in_simulated_time()
    {
        var cpu = Siemens("""
            VAR T1 : TON; END_VAR
            T1(IN := I0.0, PT := T#2s);
            Q0.0 := T1.Q;
            """);
        Port(cpu, "I0.0").Value = 24;
        var t = Run(cpu, TimeSpan.Zero, TimeSpan.FromMilliseconds(1990));
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
        Run(cpu, t, TimeSpan.FromMilliseconds(20));
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Assert.Equal(TimeSpan.FromSeconds(2), cpu.Read("T1.ET").AsTime);
    }

    [Fact]
    public void Ladder_counter_with_edge_and_compare_contact()
    {
        var ladder = new LadderProgram(
            new Rung(new Contact(ContactKind.NormallyOpen, "X0"), new CounterBox(CounterKind.CTU, "C1", 3, "X1")),
            new Rung(new CompareContact("C1.CV", ">=", "3"), new Coil(CoilKind.Normal, "Y0")),
            new Rung(new Contact(ContactKind.RisingEdge, "X0"), new Coil(CoilKind.Set, "M0")));
        var cpu = new PlcCpu("PLC1", new MitsubishiFx5uProfile());
        cpu.LoadLadder(ladder);
        cpu.Start();

        var t = TimeSpan.Zero;
        for (int i = 0; i < 3; i++)
        {
            Port(cpu, "X0").Value = 24;
            t = Run(cpu, t, Cycle);
            Port(cpu, "X0").Value = 0;
            t = Run(cpu, t, Cycle);
        }
        Assert.Equal(3, cpu.Read("C1.CV").AsInt);
        Assert.Equal(1, Port(cpu, "Y0").Value);
        Assert.True(cpu.Read("M0").AsBool);

        Port(cpu, "X1").Value = 24;
        Run(cpu, t, Cycle);
        Assert.Equal(0, Port(cpu, "Y0").Value);
    }

    [Fact]
    public void Control_structures_and_functions()
    {
        var cpu = Siemens("""
            VAR i, suma, modo : INT; r : REAL; END_VAR
            suma := 0;
            FOR i := 1 TO 10 DO
                IF i = 8 THEN EXIT; END_IF;
                suma := suma + i;
            END_FOR;
            modo := 2;
            CASE modo OF
                1: MW10 := 100;
                2..4: MW10 := 200;
            ELSE
                MW10 := -1;
            END_CASE;
            r := SQRT(16.0) + INT_TO_REAL(MAX(3, 7, 5));
            WHILE suma > 20 DO suma := suma - 5; END_WHILE;
            """);
        cpu.Scan(Cycle);
        Assert.Equal(PlcState.Run, cpu.State);
        Assert.Equal(18, cpu.Read("suma").AsInt);
        Assert.Equal(200, cpu.Read("MW10").AsInt);
        Assert.Equal(11.0, cpu.Read("r").AsReal);
    }

    [Fact]
    public void Infinite_loop_trips_watchdog_and_clears_outputs()
    {
        var cpu = Siemens("Q0.0 := TRUE; WHILE TRUE DO MW0 := MW0 + 1; END_WHILE;");
        cpu.Scan(Cycle);
        Assert.Equal(PlcState.Fault, cpu.State);
        Assert.Contains("guardián", cpu.FaultMessage);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
    }

    [Fact]
    public void Compile_errors_report_line()
    {
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        var ex = Assert.Throws<PlcCompileException>(() => cpu.LoadStructuredText("Q0.0 := TRUE;\nQ0.1 := Desconocida;"));
        Assert.Equal(2, ex.Line);
        Assert.Throws<PlcCompileException>(() => cpu.LoadStructuredText("IF I0.0 THEN Q0.0 := TRUE;"));
    }

    [Fact]
    public void Input_thresholds_and_overvoltage_damage()
    {
        var cpu = Siemens("Q0.0 := I0.0;");
        var input = Port(cpu, "I0.0");
        input.Value = 16;
        cpu.Scan(Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        input.Value = 10; // zona indefinida: mantiene el estado anterior
        cpu.Scan(2 * Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        input.Value = 3;
        cpu.Scan(3 * Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);

        input.Value = 230;
        cpu.Scan(4 * Cycle);
        input.Value = 24;
        cpu.Scan(5 * Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
        Assert.Single(cpu.Diagnostics);
    }

    [Fact]
    public void Analog_input_scales_like_siemens()
    {
        var cpu = Siemens("MW0 := IW64; QW64 := IW64;");
        Port(cpu, "IW64").Value = 5;
        cpu.Scan(Cycle);
        Assert.Equal(13824, cpu.Read("MW0").AsInt);
    }

    [Fact]
    public void Forcing_overrides_inputs_and_outputs()
    {
        var cpu = Siemens("Q0.0 := I0.0; Q0.1 := TRUE;");
        cpu.Force("I0.0", true);
        cpu.Force("Q0.1", false);
        cpu.Scan(Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Assert.Equal(0, Port(cpu, "Q0.1").Value);
    }

    [Fact]
    public void Outputs_reach_other_modules_through_signal_bus()
    {
        var cpu = Siemens("Q0.0 := TRUE;");
        var bus = new SignalBus();
        var coil = new SignalPort("KM1.A1", SignalKind.DI, SignalUnit.Bool, 24);
        var result = bus.Connect(Port(cpu, "Q0.0"), coil);
        cpu.Scan(Cycle);
        Assert.Empty(result.Warnings);
        Assert.Equal(1, coil.Value);
    }

    [Theory]
    [InlineData("siemens-s7-1200", "I0.7", "Q1.1", "M10.3")]
    [InlineData("allen-bradley-micrologix-1100", "I:0/9", "O:0/5", "B3:1/12")]
    [InlineData("schneider-m221", "%I0.8", "%Q0.6", "%M25")]
    [InlineData("mitsubishi-fx5u", "X17", "Y10", "M100")]
    [InlineData("delta-dvp-14ss2", "X7", "Y5", "M0")]
    [InlineData("omron-cp1e", "1.05", "101.03", "W2.15")]
    [InlineData("generico-iec", "%IX1.7", "%QX0.0", "%MX3.1")]
    public void Brand_addresses_round_trip(string profileId, params string[] addresses)
    {
        var p = PlcProfiles.Get(profileId);
        foreach (var text in addresses)
        {
            Assert.True(p.TryParse(text, out var a), text);
            Assert.Equal(text, p.Format(a));
        }
    }

    [Fact]
    public void Every_profile_exposes_its_physical_io_with_brand_names()
    {
        foreach (var p in PlcProfiles.All)
        {
            var cpu = new PlcCpu("PLC1", p);
            Assert.Equal(p.DigitalInputs, cpu.DigitalInputs.Count);
            Assert.Equal(p.DigitalOutputs, cpu.DigitalOutputs.Count);
            Assert.Equal(cpu.Signals.Count, cpu.Signals.Select(s => s.Id).Distinct().Count());
            foreach (var s in cpu.Signals)
                Assert.True(p.TryParse(s.Id["PLC1.".Length..], out _), s.Id);
        }
        Assert.Equal("PLC1.101.03", new PlcCpu("PLC1", new OmronCp1eProfile()).DigitalOutputs[^1].Id);
        Assert.Equal("PLC1.X17", new PlcCpu("PLC1", new MitsubishiFx5uProfile()).DigitalInputs[^1].Id);
    }

    [Fact]
    public void Factory_creates_running_plc_for_combined_bench()
    {
        var factory = new PlcComponentFactory();
        Assert.Equal(PlcProfiles.All.Count, factory.Catalog.Count());
        var c = (PlcCpu)factory.Create("schneider-m221", new Dictionary<string, object>
        {
            ["Id"] = "PLC7",
            ["Programa (ST)"] = "%Q0.0 := %I0.0;",
            ["Tiempo de ciclo"] = 5.0,
        });
        Assert.Equal(5, c.StepMultiple);
        Port(c, "%I0.0").Value = 24;
        c.DoStep(TimeSpan.Zero, TimeSpan.FromMilliseconds(5));
        Assert.Equal(1.0, c.Snapshot().Get<double>("PLC7.%Q0.0"));
    }
}
