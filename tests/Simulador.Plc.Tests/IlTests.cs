using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Tests;

public class IlTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(10);

    private static PlcCpu Load(string il, PlcProfile? profile = null)
    {
        var cpu = new PlcCpu("PLC1", profile ?? new SiemensS71200Profile());
        cpu.LoadInstructionList(il);
        cpu.Start();
        return cpu;
    }

    private static ISignalPort Port(PlcCpu cpu, string a) => cpu.Signals.Single(s => s.Id == "PLC1." + a);

    [Fact]
    public void Start_stop_with_parenthesis()
    {
        var cpu = Load("""
            (* Marcha-paro en AWL *)
            LD   I0.0
            OR   Q0.0
            AND( I0.1
            ANDN I0.2
            )
            ST   Q0.0
            """);
        Port(cpu, "I0.1").Value = 24;
        Port(cpu, "I0.0").Value = 24;
        cpu.Scan(Cycle);
        Port(cpu, "I0.0").Value = 0;
        cpu.Scan(2 * Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Port(cpu, "I0.2").Value = 24;
        cpu.Scan(3 * Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
    }

    [Fact]
    public void Timer_call_and_arithmetic_with_declarations()
    {
        var cpu = Load("""
            PROGRAM Prueba
            VAR
                T1 : TON;
                Cuenta : INT := 5;
            END_VAR
            CAL  T1(IN := I0.0, PT := T#1s)
            LD   T1.Q
            ST   Q0.0
            LD   Cuenta
            ADD  2
            MUL  3
            ST   Cuenta    // 21
            ST   MW10      // el acumulador conserva 21 aunque Cuenta cambie
            LD   Cuenta
            GT   20
            ST   Q0.1
            END_PROGRAM
            """);
        Port(cpu, "I0.0").Value = 24;
        for (int i = 1; i <= 101; i++) cpu.Scan(i * Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Assert.Equal(1, Port(cpu, "Q0.1").Value);
        Assert.True(cpu.Read("Cuenta").AsInt > 21); // se va sumando en cada ciclo
        Assert.Equal(cpu.Read("Cuenta").AsInt, cpu.Read("MW10").AsInt);
    }

    [Fact]
    public void Store_negated_keeps_original_accumulator()
    {
        var cpu = Load("""
            LD  M0.0
            STN M0.0
            ST  M0.1
            """);
        cpu.Scan(Cycle);
        Assert.True(cpu.Read("M0.0").AsBool);
        Assert.False(cpu.Read("M0.1").AsBool);
    }

    [Fact]
    public void Conditional_jumps_and_set_reset()
    {
        var cpu = Load("""
                  LD    I0.0
                  JMPC  Manual
                  LD    I0.1
                  S     Q0.0
                  JMP   Fin
            Manual:
                  LD    TRUE
                  R     Q0.0
                  ST    Q0.1
            Fin:  LD    I0.2
                  RETC
                  LD    TRUE
                  ST    Q0.2
            """);
        Port(cpu, "I0.1").Value = 24;
        cpu.Scan(Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Assert.Equal(0, Port(cpu, "Q0.1").Value);
        Assert.Equal(1, Port(cpu, "Q0.2").Value);

        Port(cpu, "I0.0").Value = 24;
        Port(cpu, "I0.2").Value = 24;
        cpu.Scan(2 * Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
        Assert.Equal(1, Port(cpu, "Q0.1").Value);
        Assert.Equal(PlcState.Run, cpu.State);
    }

    [Fact]
    public void Backward_jump_loop_trips_watchdog()
    {
        var cpu = Load("""
            Bucle: LD TRUE
                   JMP Bucle
            """);
        cpu.Scan(Cycle);
        Assert.Equal(PlcState.Fault, cpu.State);
    }

    [Fact]
    public void Mitsubishi_addresses_work_in_il()
    {
        var cpu = Load("LD X0\nANI X1\nOUT Y0", new MitsubishiFx5uProfile());
        Port(cpu, "X0").Value = 24;
        cpu.Scan(Cycle);
        Assert.Equal(1, Port(cpu, "Y0").Value);
    }

    [Fact]
    public void Siemens_awl_mnemonics()
    {
        var cpu = Load("""
            U   I0.0
            UN  I0.1
            O   M0.0
            =   Q0.0
            """);
        Port(cpu, "I0.0").Value = 24;
        cpu.Scan(Cycle);
        Assert.Equal(1, Port(cpu, "Q0.0").Value);
        Port(cpu, "I0.1").Value = 24;
        cpu.Scan(2 * Cycle);
        Assert.Equal(0, Port(cpu, "Q0.0").Value);
    }

    [Theory]
    [InlineData("LD I0.0\nFOO Q0.0", 2)]
    [InlineData("ST Q0.0", 1)]
    [InlineData("LD I0.0\nJMP Nada", 2)]
    [InlineData("LD I0.0\nAND( I0.1\nST Q0.0", 3)]
    public void Errors_report_their_line(string il, int line)
    {
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        var ex = Assert.Throws<PlcCompileException>(() => cpu.LoadInstructionList(il));
        Assert.Equal(line, ex.Line);
    }
}
