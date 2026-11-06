using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Persistence;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.Tests;

public class Phase2Tests
{
    private const double Bar = 1e5;

    /// <summary>Depósito a cota h sobre un aspersor, con una tubería corta y ancha (pérdida despreciable).</summary>
    private static (HydraulicNetwork, Junction) EmitterUnderHead(double headM, double q1)
    {
        var net = new HydraulicNetwork();
        net.Add(new Reservoir { Id = "R1", Elevation = headM, Level = 0 });
        var j = net.Add(new Junction { Id = "A1", Elevation = 0, DischargeAt1Bar = q1 });
        net.Connect(new Pipe { Id = "P1", Length = 1, Diameter = 0.3 }, "R1", "A1");
        return (net, j);
    }

    [Fact]
    public void Emitter_gives_its_rated_flow_at_one_bar_and_double_at_four()
    {
        double h1 = Bar / (998.2 * LinkLaws.Gravity);
        foreach (var (bar, factor) in new[] { (1.0, 1.0), (4.0, 2.0) })
        {
            var (net, _) = EmitterUnderHead(h1 * bar, 0.001);
            net.Fluid = net.Fluid with { Density = 998.2 };
            var sim = new HydraulicSimulation(net);
            Assert.True(sim.LastSolveConverged);
            Assert.Equal(0.001 * factor, sim.EmittedAt("A1"), 0.00002);
            Assert.Equal(sim.EmittedAt("A1"), sim.FlowIn("P1"), 1e-6);
        }
    }

    [Fact]
    public void Emitter_above_the_supply_gives_no_water()
    {
        var (net, j) = EmitterUnderHead(5, 0.001);
        j.Elevation = 8;
        var sim = new HydraulicSimulation(net);
        Assert.InRange(sim.EmittedAt("A1"), 0, 1e-6);
    }

    [Fact]
    public void Closed_tank_compresses_its_air_isothermally()
    {
        var t = new Tank { Closed = true, MaxLevel = 1, InitialLevel = 0.2, InitialGasPressure = 1 * Bar };
        Assert.Equal(1 * Bar, t.GasPressureAt(0.2), 1);
        // pV = cte: el aire pasa de 0,8 a 0,4 m → presión absoluta doble.
        double pAbs = (1 * Bar + Tank.AtmosphericPressure) * 2;
        Assert.Equal(pAbs - Tank.AtmosphericPressure, t.GasPressureAt(0.6), 1);
        Assert.Equal(0, new Tank { Closed = false }.GasPressureAt(0.5));
    }

    [Fact]
    public void Pressure_switch_cycles_the_pump_in_the_pressure_group()
    {
        var net = Samples.PressureGroup();
        var sim = new HydraulicSimulation(net);
        var pump = (Pump)net.FindLink("B1")!;
        int starts = 0, stops = 0;
        bool last = pump.RelativeSpeed > 0;
        double pMax = 0, pMin = double.MaxValue;
        for (int i = 0; i < 600; i++)
        {
            sim.Step(TimeSpan.FromSeconds(1));
            bool on = pump.RelativeSpeed > 0;
            if (on && !last) starts++;
            if (!on && last) stops++;
            last = on;
            if (i > 60) { pMax = Math.Max(pMax, sim.PressureAt("N4")); pMin = Math.Min(pMin, sim.PressureAt("N4")); }
        }
        Assert.True(starts >= 2 && stops >= 2, $"arranques {starts}, paros {stops}");
        Assert.InRange(pMax, 3.8 * Bar, 4.3 * Bar);
        Assert.InRange(pMin, 2.2 * Bar, 2.7 * Bar);
        Assert.Empty(sim.Warnings);
    }

    [Fact]
    public void Sprinklers_further_up_get_less_water()
    {
        var net = Samples.SprinklerIrrigation();
        var sim = new HydraulicSimulation(net);
        var q = new[] { "A1", "A2", "A3", "A4" }.Select(sim.EmittedAt).ToArray();
        for (int i = 1; i < q.Length; i++) Assert.True(q[i] < q[i - 1]);
        Assert.Equal(q.Sum(), sim.FlowIn("B1"), 1e-6);
    }

    private static (HydraulicNetwork, HydraulicSimulation) Line(double length = 1000, double opening = 0.1)
    {
        var net = new HydraulicNetwork();
        net.Add(new Reservoir { Id = "R1", Elevation = 50, Level = 0 });
        net.Add(new Junction { Id = "N1", Elevation = 0 });
        net.Add(new Junction { Id = "N2", Elevation = 0 });
        net.Add(new Reservoir { Id = "R2", Elevation = 0, Level = 0 });
        net.Connect(new Pipe { Id = "P1", Length = length, Diameter = 0.2, Material = "Acero", WallThickness = 0.006 }, "R1", "N1");
        net.Connect(new Valve { Id = "V1", Kind = ValveKind.Control, Diameter = 0.2, InitialOpening = opening }, "N1", "N2");
        net.Connect(new Pipe { Id = "P2", Length = 2, Diameter = 0.2 }, "N2", "R2");
        return (net, new HydraulicSimulation(net));
    }

    [Fact]
    public void Instant_closure_matches_Joukowsky_and_slow_closure_is_much_gentler()
    {
        var (net, sim) = Line();
        var fast = WaterHammer.Analyze(net, sim, "V1", 0);
        Assert.InRange(fast.WaveSpeed, 1000, 1400);
        Assert.True(fast.FastClosure);
        Assert.Equal(fast.SteadyPressure + fast.JoukowskyRise, fast.MaxPressure, fast.JoukowskyRise * 0.12);

        var slow = WaterHammer.Analyze(net, sim, "V1", 20 * fast.CriticalTime);
        Assert.False(slow.FastClosure);
        Assert.True(slow.MaxPressure - slow.SteadyPressure < 0.25 * fast.JoukowskyRise);
    }

    [Fact]
    public void Water_hammer_oscillates_with_period_four_L_over_a()
    {
        var (net, sim) = Line(opening: 0.05);
        var r = WaterHammer.Analyze(net, sim, "V1", 0);
        // Instantes en que la presión en la válvula cruza la de régimen subiendo.
        var ups = new List<double>();
        for (int i = 1; i < r.Times.Length; i++)
            if (r.ValvePressure[i - 1] < r.SteadyPressure && r.ValvePressure[i] >= r.SteadyPressure) ups.Add(r.Times[i]);
        Assert.True(ups.Count >= 3);
        double period = (ups[^1] - ups[0]) / (ups.Count - 1);
        Assert.Equal(4 * r.Length / r.WaveSpeed, period, 4 * r.Length / r.WaveSpeed * 0.05);
    }

    [Fact]
    public void Water_hammer_rejects_branched_lines()
    {
        var (net, sim) = Line();
        ((Junction)net.FindNode("N1")!).Demand = 0.001;
        sim.Refresh();
        Assert.Throws<InvalidOperationException>(() => WaterHammer.Analyze(net, sim, "V1", 1));
    }

    [Fact]
    public void Validation_catches_bad_pressure_switch_and_full_closed_tank()
    {
        var net = Samples.PressureGroup();
        var b = (Pump)net.FindLink("B1")!;
        b.SwitchOnPressure = 5 * Bar;
        Assert.Contains(net.Validate().Errors, e => e.Contains("presostato"));
        b.SwitchOnPressure = 2 * Bar;
        b.PressureSwitchNode = "X9";
        Assert.Contains(net.Validate().Errors, e => e.Contains("X9"));
        b.PressureSwitchNode = "N4";
        ((Tank)net.FindNode("T1")!).InitialLevel = 0.99;
        Assert.Contains(net.Validate().Errors, e => e.Contains("aire"));
        Assert.True(net.Rename("N4", "NP"));
        Assert.Equal("NP", b.PressureSwitchNode);
    }

    [Fact]
    public void New_fields_survive_the_project_file()
    {
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(Samples.PressureGroup()));
        var t = (Tank)back.FindNode("T1")!;
        Assert.True(t.Closed);
        Assert.Equal(2.4e5, t.InitialGasPressure);
        var b = (Pump)back.FindLink("B1")!;
        Assert.Equal("N4", b.PressureSwitchNode);
        Assert.Equal(4e5, b.SwitchOffPressure);
        var s = ProjectFile.Deserialize(ProjectFile.Serialize(Samples.SprinklerIrrigation()));
        Assert.Equal(0.0004, ((Junction)s.FindNode("A3")!).DischargeAt1Bar);
        var w = ProjectFile.Deserialize(ProjectFile.Serialize(Samples.WaterHammerLine()));
        Assert.Equal(0.005, ((Pipe)w.FindLink("P1")!).WallThickness);
    }
}
