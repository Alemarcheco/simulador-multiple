using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Fbd;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Tests;

public class FbdTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(10);

    private static ISignalPort Port(PlcCpu cpu, string a) => cpu.Signals.Single(s => s.Id == "PLC1." + a);

    /// <summary>Marcha-paro: ≥1(I0.0, Q0.0) → &amp;(…, I0.1) → Q0.0.</summary>
    private static FbdProgram StartStop() => new()
    {
        Networks =
        {
            new FbdNetwork
            {
                Root = new FbdGate(GateKind.And,
                    new FbdGate(GateKind.Or, new FbdOperand("I0.0"), new FbdOperand("Q0.0")),
                    new FbdOperand("I0.1")),
                Outputs = { new FbdAssignment("Q0.0") },
            },
        },
    };

    [Fact]
    public void Start_stop_network()
    {
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        cpu.LoadFbd(StartStop());
        cpu.Start();
        Port(cpu, "I0.1").Value = 24;
        Port(cpu, "I0.0").Value = 24;
        cpu.Scan(Cycle);
        Port(cpu, "I0.0").Value = 0;
        cpu.Scan(2 * Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Port(cpu, "I0.1").Value = 0;
        cpu.Scan(3 * Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
    }

    [Fact]
    public void Intermediate_values_are_visible_for_live_view()
    {
        var program = StartStop();
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        cpu.LoadFbd(program);
        cpu.Start();
        Port(cpu, "I0.0").Value = 24;
        cpu.Scan(Cycle);
        var or = ((FbdGate)program.Networks[0].Root!).Inputs[0];
        Assert.NotNull(or.RuntimeName);
        Assert.True(cpu.Read(or.RuntimeName!).AsBool);
        Assert.False(cpu.Read(program.Networks[0].Root!.RuntimeName!).AsBool);
    }

    [Fact]
    public void Timer_block_comparison_and_set_reset()
    {
        var timer = new FbdInstance("TON", "T1") { Inputs = { ["IN"] = new FbdOperand("I0.0"), ["PT"] = new FbdOperand("T#1s") } };
        var program = new FbdProgram
        {
            Networks =
            {
                new FbdNetwork { Root = timer, Outputs = { new FbdAssignment("Q0.0") } },
                new FbdNetwork
                {
                    Root = new FbdGate(GateKind.Ge, new FbdOperand("T1.ET"), new FbdOperand("T#500ms")),
                    Outputs = { new FbdAssignment("Q0.1", FbdAssignKind.Set) },
                },
                new FbdNetwork { Root = new FbdOperand("I0.1"), Outputs = { new FbdAssignment("Q0.1", FbdAssignKind.Reset) } },
            },
        };
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        cpu.LoadFbd(program);
        cpu.Start();
        Port(cpu, "I0.0").Value = 24;
        var t = TimeSpan.Zero;
        for (int i = 0; i < 60; i++) cpu.Scan(t += Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
        Assert.Equal(1, Port(cpu, "Q0.1").Value);
        for (int i = 0; i < 50; i++) cpu.Scan(t += Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Port(cpu, "I0.0").Value = 0;
        Port(cpu, "I0.1").Value = 24;
        cpu.Scan(t += Cycle);
        Assert.Equal(0, Port(cpu, "Q0.1").Value);
    }

    [Fact]
    public void Arithmetic_and_select()
    {
        var program = new FbdProgram
        {
            Networks =
            {
                new FbdNetwork
                {
                    Root = new FbdGate(GateKind.Sel, new FbdOperand("I0.0"),
                        new FbdGate(GateKind.Add, new FbdOperand("10"), new FbdOperand("5")),
                        new FbdGate(GateKind.Mul, new FbdOperand("4"), new FbdOperand("2.5"))),
                    Outputs = { new FbdAssignment("MW10") },
                },
            },
        };
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        cpu.LoadFbd(program);
        cpu.Start();
        cpu.Scan(Cycle);
        Assert.Equal(15, cpu.Read("MW10").AsInt);
        Port(cpu, "I0.0").Value = 24;
        cpu.Scan(2 * Cycle);
        Assert.Equal(10, cpu.Read("MW10").AsInt);
    }

    [Fact]
    public void File_round_trip_keeps_the_program()
    {
        var timer = new FbdInstance("CTU", "C1") { Inputs = { ["CU"] = new FbdOperand("X0"), ["R"] = new FbdOperand("X1", true), ["PV"] = new FbdOperand("3") } };
        var program = new FbdProgram { Profile = "mitsubishi-fx5u", Networks = { new FbdNetwork { Root = new FbdGate(GateKind.Not, timer) { Negated = false }, Outputs = { new FbdAssignment("Y0") }, Comment = "Contador" } } };
        var json = FbdCompiler.Save(program);
        var loaded = FbdCompiler.Load(json);
        Assert.Equal(json, FbdCompiler.Save(loaded));
        Assert.Equal("mitsubishi-fx5u", loaded.Profile);
        var cpu = new PlcCpu("PLC1", new MitsubishiFx5uProfile());
        cpu.LoadFbd(loaded);
        Assert.Throws<FormatException>(() => FbdCompiler.Load("{\"networks\":[{\"root\":{\"node\":\"nada\"}}]}"));
    }

    [Fact]
    public void Errors_name_the_network()
    {
        var program = new FbdProgram
        {
            Networks =
            {
                new FbdNetwork { Root = new FbdOperand("I0.0"), Outputs = { new FbdAssignment("Q0.0") } },
                new FbdNetwork { Root = new FbdGate(GateKind.Gt, new FbdOperand("MW0")), Outputs = { new FbdAssignment("Q0.1") } },
            },
        };
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        var ex = Assert.Throws<PlcCompileException>(() => cpu.LoadFbd(program));
        Assert.Equal(2, ex.Line);
    }
}
