using Simulador.Banco.Scenarios;
using Simulador.Electrico.Engine.CoSim;
using Simulador.Plc.Runtime;
using Xunit.Abstractions;

namespace Simulador.Banco.Tests;

public class ElectricScenarioTests(ITestOutputHelper output)
{
    private static double Level(TankFillElectricScenario sc) => (double)sc.Hydraulics.Snapshot().Values["TK1.levelRatio"];
    private static object Net(TankFillElectricScenario sc, string key) => sc.Electrical.Snapshot().Values[key];
    private static bool Km1(TankFillElectricScenario sc) => (bool)Net(sc, "KM1." + ElectricalNetwork.Keys.Actuated);
    private static double Current(TankFillElectricScenario sc) => Convert.ToDouble(Net(sc, "M1." + ElectricalNetwork.Keys.Current));
    private static double Rpm(TankFillElectricScenario sc) => Convert.ToDouble(Net(sc, "M1." + ElectricalNetwork.Keys.Rpm));

    [Fact]
    public void Real_schematic_fills_the_tank_between_limits()
    {
        var sc = new TankFillElectricScenario();
        Assert.Equal(PlcState.Run, sc.Plc.State);
        sc.Valve.OpeningCommand!.Value = 100;
        bool started = false, stopped = false;
        double maxLevel = 0;
        while (sc.Scheduler.Now < TimeSpan.FromMinutes(30) && !(started && stopped))
        {
            sc.Scheduler.RunFor(TimeSpan.FromSeconds(1));
            double lvl = Level(sc);
            maxLevel = Math.Max(maxLevel, lvl);
            if (Km1(sc) && !started) { started = true; sc.Valve.OpeningCommand!.Value = 20; }
            if (started && !Km1(sc)) stopped = true;
            if (sc.Scheduler.Now.TotalSeconds % 30 < 1)
                output.WriteLine($"t={sc.Scheduler.Now.TotalSeconds,5:F0} nivel={lvl:P1} KM1={Km1(sc)} I={Current(sc):F1} A n={Rpm(sc):F0} rpm par={sc.MotorShaft.Effort:F2} Q={(double)sc.Hydraulics.Snapshot().Values["P1.flow"] * 1000:F2} L/s");
        }
        Assert.True(started, "El PLC debería arrancar la bomba al bajar del 20 %.");
        Assert.True(stopped, "El PLC debería parar la bomba al pasar del 80 %.");
        Assert.InRange(maxLevel, 0.79, 0.9);
        Assert.False((bool)Net(sc, "Q1." + ElectricalNetwork.Keys.Tripped));
    }

    [Fact]
    public void Motor_from_schematic_drives_the_pump_near_rated_point()
    {
        var sc = new TankFillElectricScenario();
        sc.Valve.OpeningCommand!.Value = 100;
        while (!Km1(sc) && sc.Scheduler.Now < TimeSpan.FromMinutes(20)) sc.Scheduler.RunFor(TimeSpan.FromSeconds(1));
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(10));
        double q = (double)sc.Hydraulics.Snapshot().Values["P1.flow"];
        output.WriteLine($"I={Current(sc):F2} A, n={Rpm(sc):F0} rpm, par={sc.MotorShaft.Effort:F2} N·m, Q={q * 1000:F2} L/s");
        Assert.InRange(Rpm(sc), 2700, 3000);
        Assert.InRange(Current(sc), 1.5, 5.5);
        Assert.True(q > 0.001);
    }

    [Fact]
    public void Opening_Q1_is_seen_by_the_plc_and_stops_the_pump()
    {
        var sc = new TankFillElectricScenario();
        sc.Valve.OpeningCommand!.Value = 100;
        while (!Km1(sc) && sc.Scheduler.Now < TimeSpan.FromMinutes(20)) sc.Scheduler.RunFor(TimeSpan.FromSeconds(1));
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(5));
        Assert.True(sc.Q1Contact.Value > 15);
        Assert.True(sc.Plc.Read("Q1_ok").AsBool);

        sc.ToggleQ1();
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(3));
        Assert.True(sc.Q1Contact.Value < 1);
        Assert.False(sc.Plc.Read("Q1_ok").AsBool);
        Assert.False(sc.Plc.Read("Bomba").AsBool);
        Assert.True(sc.Plc.Read("Alarma").AsBool);
        Assert.False(Km1(sc));
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(10));
        Assert.True(Rpm(sc) < 1000, $"el motor debería ir frenando ({Rpm(sc):F0} rpm)");

        sc.ToggleQ1();
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(3));
        Assert.True(sc.Plc.Read("Q1_ok").AsBool);
    }
}
