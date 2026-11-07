using Simulador.Banco.Generic;
using Simulador.Banco.Scenarios;
using Simulador.Core.CoSim;
using Simulador.Plc.Runtime;
using Xunit.Abstractions;

namespace Simulador.Banco.Tests;

public class ScenarioTests(ITestOutputHelper output)
{
    private static double Level(TankFillScenario sc) =>
        (double)sc.Hydraulics.Snapshot().Values["TK1.levelRatio"];

    private void Log(Scenario sc, double seconds, double every, Func<string> line)
    {
        for (double t = 0; t < seconds; t += every)
        {
            sc.Scheduler.RunFor(TimeSpan.FromSeconds(every));
            output.WriteLine($"t={sc.Scheduler.Now.TotalSeconds,6:F1} {line()}");
        }
    }

    [Fact]
    public void Tank_fill_cycles_between_low_and_high_level()
    {
        var sc = new TankFillScenario();
        sc.Valve.OpeningCommand!.Value = 100;   // vaciar rápido hasta el 20 %
        Assert.Equal(PlcState.Run, sc.Plc.State);

        bool started = false, stopped = false;
        double peak = 0, minAfterStart = 1, maxLevel = 0;
        var limit = TimeSpan.FromMinutes(30);
        while (sc.Scheduler.Now < limit && !(started && stopped))
        {
            sc.Scheduler.RunFor(TimeSpan.FromSeconds(1));
            double lvl = Level(sc);
            maxLevel = Math.Max(maxLevel, lvl);
            peak = Math.Max(peak, sc.M1.PeakCurrent);
            if (sc.Km1.Closed && !started) { started = true; sc.Valve.OpeningCommand!.Value = 20; output.WriteLine($"arranque t={sc.Scheduler.Now.TotalSeconds:F0} s, nivel {lvl:P0}"); }
            if (started) minAfterStart = Math.Min(minAfterStart, lvl);
            if (started && !sc.Km1.Closed && !stopped) { stopped = true; output.WriteLine($"paro t={sc.Scheduler.Now.TotalSeconds:F0} s, nivel {lvl:P0}"); }
            if (sc.Scheduler.Now.TotalSeconds % 30 < 1)
                output.WriteLine($"t={sc.Scheduler.Now.TotalSeconds,5:F0} nivel={lvl:P1} KM1={sc.Km1.Closed} I={sc.M1.Terminals.Flow:F1} A w={sc.M1.Shaft.Flow:F0} par={sc.M1.Shaft.Effort:F2} Q={(double)sc.Hydraulics.Snapshot().Values["P1.flow"] * 1000:F2} L/s");
        }

        Assert.True(started, "El PLC debería arrancar la bomba al bajar del 20 %.");
        Assert.True(stopped, "El PLC debería parar la bomba al pasar del 80 %.");
        Assert.InRange(maxLevel, 0.79, 0.9);
        Assert.False(sc.Km1.Tripped);
        Assert.InRange(peak, 4.7 * 4, 4.7 * 7.5);
        Assert.InRange(sc.M1.Terminals.Flow, 0, 0.1); // parado
    }

    [Fact]
    public void Tank_fill_motor_runs_near_rated_current()
    {
        var sc = new TankFillScenario();
        sc.Valve.OpeningCommand!.Value = 100;
        while (!sc.Km1.Closed && sc.Scheduler.Now < TimeSpan.FromMinutes(20)) sc.Scheduler.RunFor(TimeSpan.FromSeconds(1));
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(10));
        output.WriteLine($"I={sc.M1.Terminals.Flow:F2} A, w={sc.M1.Shaft.Flow:F1} rad/s, par carga={sc.M1.Shaft.Effort:F2} N·m, Tn={sc.M1.RatedTorque:F2}");
        Assert.True(sc.Km1.Closed);
        Assert.InRange(sc.M1.Terminals.Flow, 1.5, 4.7 * 1.05);
        Assert.InRange(sc.M1.Shaft.Flow, 290, sc.M1.SyncSpeed);
    }

    [Fact]
    public void Overload_trips_and_plc_sees_it()
    {
        var sc = new TankFillScenario();
        sc.Valve.OpeningCommand!.Value = 100;
        while (!sc.Km1.Closed && sc.Scheduler.Now < TimeSpan.FromMinutes(20)) sc.Scheduler.RunFor(TimeSpan.FromSeconds(1));
        sc.Actions.First(a => a.Name.StartsWith("Térmico")).Run();
        sc.Scheduler.RunFor(TimeSpan.FromMinutes(5));
        Assert.True(sc.Km1.Tripped);
        Assert.False(sc.Km1.Closed);
        Assert.True(sc.Plc.Read("Alarma").AsBool);
    }

    [Fact]
    public void Turbogenerator_holds_50_hz_and_recovers_after_load_step()
    {
        var sc = new TurboGeneratorScenario();
        Log(sc, 60, 5, () => $"f={sc.G1.Frequency:F2} Hz P={sc.G1.ElectricalPower / 1000:F0} kW adm={sc.T1.Admission:P0}");
        Assert.InRange(sc.G1.Frequency, 49.5, 50.5);
        Assert.InRange(sc.G1.ElectricalPower, 190e3, 210e3);

        sc.Load.PowerKw = 350;
        double fmin = 100;
        for (int i = 0; i < 400; i++) { sc.Scheduler.RunFor(TimeSpan.FromMilliseconds(100)); fmin = Math.Min(fmin, sc.G1.Frequency); }
        output.WriteLine($"tras escalón: fmin={fmin:F2} Hz, f={sc.G1.Frequency:F2} Hz, adm={sc.T1.Admission:P0}");
        Assert.InRange(fmin, 45, 49.9);
        Assert.InRange(sc.G1.Frequency, 49.5, 50.5);
        Assert.False(sc.T1.Tripped);
    }

    [Fact]
    public void Turbogenerator_load_rejection_does_not_trip_overspeed()
    {
        var sc = new TurboGeneratorScenario();
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(60));
        sc.Load.PowerKw = 0;
        double fmax = 0;
        for (int i = 0; i < 300; i++) { sc.Scheduler.RunFor(TimeSpan.FromMilliseconds(100)); fmax = Math.Max(fmax, sc.G1.Frequency); }
        output.WriteLine($"rechazo: fmax={fmax:F2} Hz, f={sc.G1.Frequency:F2}, disparo={sc.T1.Tripped}");
        Assert.InRange(sc.G1.Frequency, 49, 51);
    }

    [Fact]
    public void Turbocompressor_plc_holds_7_bar()
    {
        var sc = new TurboCompressorScenario();
        Assert.Equal(PlcState.Run, sc.Plc.State);
        Log(sc, 120, 10, () => $"p={sc.Receiver.Pressure / 1e5:F2} bar adm={sc.Turbine.Admission:P0} w={sc.Turbine.Shaft.Flow:F0} q={sc.Compressor.Outlet.Flow:F3}");
        Assert.InRange(sc.Receiver.Pressure, 6.6e5, 7.4e5);

        sc.Receiver.Demand = 0.22;
        sc.Scheduler.RunFor(TimeSpan.FromSeconds(90));
        output.WriteLine($"consumo 0,22: p={sc.Receiver.Pressure / 1e5:F2} bar adm={sc.Turbine.Admission:P0}");
        Assert.InRange(sc.Receiver.Pressure, 6.5e5, 7.5e5);
        Assert.False(sc.Receiver.ReliefOpen);
    }

    [Fact]
    public void Every_scenario_resets_and_runs_again()
    {
        foreach (var (name, create) in Scenario.All)
        {
            var sc = create();
            sc.Scheduler.RunFor(TimeSpan.FromSeconds(5));
            var before = sc.Scheduler.Snapshot();
            sc.Scheduler.Reset();
            sc.AfterReset();
            sc.Scheduler.RunFor(TimeSpan.FromSeconds(5));
            var after = sc.Scheduler.Snapshot();
            Assert.Equal(before.Count, after.Count);
            Assert.NotEmpty(sc.Wiring);
            Assert.NotEmpty(sc.Trends);
            output.WriteLine($"{name}: {sc.Wiring.Count} conexiones, {sc.Trends.Count} tendencias");
            var frame = new SimFrame(sc.Scheduler.Now, after);
            foreach (var tr in sc.Trends) Assert.False(double.IsNaN(tr.Percent(frame)), $"{name}: {tr.Name} da NaN");
        }
    }
}
