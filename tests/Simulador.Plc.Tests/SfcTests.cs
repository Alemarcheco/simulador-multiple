using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Tests;

public class SfcTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(10);

    private static PlcCpu Load(string sfc)
    {
        var cpu = new PlcCpu("PLC1", new GenericIecProfile());
        cpu.LoadSfc(sfc);
        cpu.Start();
        return cpu;
    }

    private static ISignalPort Port(PlcCpu cpu, string a) => cpu.Signals.Single(s => s.Id == "PLC1." + a);

    private const string Tank = """
        PROGRAM Deposito
        VAR
            Marcha AT %IX0.0 : BOOL;
            Lleno  AT %IX0.1 : BOOL;
            Valvula AT %QX0.0 : BOOL;
            Agitador AT %QX0.1 : BOOL;
            Vaciar AT %QX0.2 : BOOL;
            Ciclos : INT;
        END_VAR

        INITIAL_STEP Reposo: END_STEP
        STEP Llenar: Valvula(N); END_STEP
        STEP Mezclar: Agitador(N); Contar(P); END_STEP
        STEP Vaciado: Vaciar(N); END_STEP

        TRANSITION FROM Reposo TO Llenar := Marcha; END_TRANSITION
        TRANSITION FROM Llenar TO Mezclar := Lleno; END_TRANSITION
        TRANSITION FROM Mezclar TO Vaciado := Mezclar.T >= T#3s; END_TRANSITION
        TRANSITION FROM Vaciado TO Reposo := NOT Lleno; END_TRANSITION

        ACTION Contar:
            Ciclos := Ciclos + 1;
        END_ACTION
        END_PROGRAM
        """;

    [Fact]
    public void Sequence_advances_with_conditions_and_step_time()
    {
        var cpu = Load(Tank);
        var t = TimeSpan.Zero;
        void Run(int n) { for (int i = 0; i < n; i++) cpu.Scan(t += Cycle); }

        Run(3);
        Assert.True(cpu.Read("Reposo.X").AsBool);
        Port(cpu, "%IX0.0").Value = 24;
        Run(2);
        Port(cpu, "%IX0.0").Value = 0;
        Assert.Equal(1, Port(cpu, "%QX0.0").Value);
        Assert.False(cpu.Read("Reposo.X").AsBool);

        Port(cpu, "%IX0.1").Value = 24;
        Run(2);
        Assert.Equal(0, Port(cpu, "%QX0.0").Value);
        Assert.Equal(1, Port(cpu, "%QX0.1").Value);

        Run(290);
        Assert.Equal(1, Port(cpu, "%QX0.1").Value);
        Run(20);
        Assert.Equal(0, Port(cpu, "%QX0.1").Value);
        Assert.Equal(1, Port(cpu, "%QX0.2").Value);

        Port(cpu, "%IX0.1").Value = 0;
        Run(2);
        Assert.True(cpu.Read("Reposo.X").AsBool);
        Assert.Equal(1, cpu.Read("Ciclos").AsInt); // la acción P se ejecuta una sola vez por activación
    }

    [Fact]
    public void Simultaneous_divergence_and_convergence()
    {
        var cpu = Load("""
            INITIAL_STEP S0: END_STEP
            STEP A1: %QX0.0(N); END_STEP
            STEP B1: %QX0.1(N); END_STEP
            STEP A2: END_STEP
            STEP B2: END_STEP
            STEP Fin: %QX0.2(S); END_STEP
            TRANSITION FROM S0 TO (A1, B1) := %IX0.0; END_TRANSITION
            TRANSITION FROM A1 TO A2 := %IX0.1; END_TRANSITION
            TRANSITION FROM B1 TO B2 := %IX0.2; END_TRANSITION
            TRANSITION FROM (A2, B2) TO Fin := TRUE; END_TRANSITION
            """);
        var t = TimeSpan.Zero;
        void Run() { cpu.Scan(t += Cycle); cpu.Scan(t += Cycle); }

        Port(cpu, "%IX0.0").Value = 24;
        Run();
        Assert.Equal(1, Port(cpu, "%QX0.0").Value);
        Assert.Equal(1, Port(cpu, "%QX0.1").Value);
        Port(cpu, "%IX0.1").Value = 24;
        Run();
        Assert.Equal(0, Port(cpu, "%QX0.0").Value);
        Assert.False(cpu.Read("Fin.X").AsBool); // espera a la otra rama
        Port(cpu, "%IX0.2").Value = 24;
        Run();
        Assert.True(cpu.Read("Fin.X").AsBool);
        Assert.Equal(1, Port(cpu, "%QX0.2").Value);
    }

    [Fact]
    public void Delayed_and_limited_actions()
    {
        var cpu = Load("""
            INITIAL_STEP S0: %QX0.0(D, T#1s); %QX0.1(L, T#1s); END_STEP
            """);
        var t = TimeSpan.Zero;
        for (int i = 0; i < 50; i++) cpu.Scan(t += Cycle);
        Assert.Equal(0, Port(cpu, "%QX0.0").Value);
        Assert.Equal(1, Port(cpu, "%QX0.1").Value);
        for (int i = 0; i < 60; i++) cpu.Scan(t += Cycle);
        Assert.Equal(1, Port(cpu, "%QX0.0").Value);
        Assert.Equal(0, Port(cpu, "%QX0.1").Value);
    }

    [Theory]
    [InlineData("STEP A: END_STEP", "inicial")]
    [InlineData("INITIAL_STEP A: END_STEP\nTRANSITION FROM A TO B := TRUE; END_TRANSITION", "'B'")]
    [InlineData("INITIAL_STEP A: X(Q); END_STEP", "Calificador")]
    [InlineData("INITIAL_STEP A: X(D); END_STEP", "tiempo")]
    public void Invalid_charts_are_explained(string sfc, string fragment)
    {
        var cpu = new PlcCpu("PLC1", new GenericIecProfile());
        var ex = Assert.Throws<PlcCompileException>(() => cpu.LoadSfc(sfc));
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Transition_text_is_kept_for_the_diagram()
    {
        var chart = Parser.ParseSfc(Tank);
        Assert.Equal(4, chart.Steps.Count);
        Assert.Equal("Mezclar . T >= T#3s", chart.Transitions[2].ConditionText);
    }
}
