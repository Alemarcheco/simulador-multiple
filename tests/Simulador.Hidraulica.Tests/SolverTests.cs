using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.Tests;

public class SolverTests
{
    private const double G = LinkLaws.Gravity;

    private static HydraulicNetwork TwoReservoirs(double upper, double lower, params HydraulicLink[] chain)
    {
        var net = new HydraulicNetwork();
        net.Add(new Reservoir { Id = "R1", Elevation = upper });
        net.Add(new Reservoir { Id = "R2", Elevation = lower });
        string prev = "R1";
        for (int i = 0; i < chain.Length; i++)
        {
            string next = i == chain.Length - 1 ? "R2" : $"N{i + 1}";
            if (next != "R2") net.Add(new Junction { Id = next, Elevation = lower });
            chain[i].Id = $"L{i + 1}";
            net.Connect(chain[i], prev, next);
            prev = next;
        }
        return net;
    }

    [Fact]
    public void Laminar_flow_matches_Hagen_Poiseuille()
    {
        var net = TwoReservoirs(0.1, 0, new Pipe { Length = 10, Diameter = 0.002 });
        var sim = new HydraulicSimulation(net);
        double nu = net.Fluid.KinematicViscosity;
        double expected = 0.1 * G * Math.PI * Math.Pow(0.002, 4) / (128 * nu * 10);
        Assert.Equal(expected, sim.FlowIn("L1"), expected * 1e-4);
    }

    [Fact]
    public void Swamee_Jain_matches_Moody_chart()
    {
        // Re = 1e5, e/D = 1e-4 → f ≈ 0,0185 (Colebrook)
        Assert.Equal(0.0185, LinkLaws.SwameeJain(1e5, 1e-5, 0.1), 3);
    }

    [Fact]
    public void Turbulent_pipe_head_loss_equals_available_head()
    {
        var net = TwoReservoirs(10, 0, new Pipe { Length = 100, Diameter = 0.05, Roughness = 0.045 });
        var sim = new HydraulicSimulation(net);
        double q = sim.FlowIn("L1");
        Assert.True(q > 0);
        Assert.Equal(10, LinkLaws.PipeLoss((Pipe)net.Links[0], q, net.Fluid), 6);
    }

    [Fact]
    public void Pipes_in_series_conserve_flow_and_split_head()
    {
        var net = TwoReservoirs(20, 0,
            new Pipe { Length = 50, Diameter = 0.05 },
            new Pipe { Length = 50, Diameter = 0.05 });
        var sim = new HydraulicSimulation(net);
        Assert.Equal(sim.FlowIn("L1"), sim.FlowIn("L2"), 9);
        Assert.Equal(10, sim.HeadAt("N1"), 4); // dos tramos iguales: la mitad de la altura en el medio
    }

    [Fact]
    public void Closed_valve_stops_flow_and_check_valve_blocks_reverse()
    {
        var closed = TwoReservoirs(10, 0, new Pipe(), new Valve { InitialOpening = 0 });
        Assert.True(Math.Abs(new HydraulicSimulation(closed).FlowIn("L1")) < 1e-6);

        var reverse = TwoReservoirs(0, 10, new Pipe(), new Valve { Kind = ValveKind.Check });
        Assert.True(Math.Abs(new HydraulicSimulation(reverse).FlowIn("L1")) < 1e-6);
    }

    [Fact]
    public void Junction_demand_satisfies_continuity()
    {
        var net = new HydraulicNetwork();
        net.Add(new Reservoir { Id = "R1", Elevation = 30 });
        net.Add(new Junction { Id = "N1", Elevation = 0, Demand = 0.002 });
        net.Add(new Junction { Id = "N2", Elevation = 0, Demand = 0.001 });
        net.Connect(new Pipe { Id = "P1", Length = 100, Diameter = 0.08 }, "R1", "N1");
        net.Connect(new Pipe { Id = "P2", Length = 100, Diameter = 0.05 }, "N1", "N2");
        var sim = new HydraulicSimulation(net);
        Assert.Equal(0.003, sim.FlowIn("P1"), 8);
        Assert.Equal(0.001, sim.FlowIn("P2"), 8);
        Assert.True(sim.PressureAt("N2") < sim.PressureAt("N1"));
    }

    private static HydraulicNetwork PumpToTank(out Pump pump, out Tank tank)
    {
        var net = new HydraulicNetwork();
        net.Add(new Reservoir { Id = "R1", Elevation = 0 });
        net.Add(new Junction { Id = "N1", Elevation = 0 });
        tank = net.Add(new Tank { Id = "T1", Elevation = 10, Diameter = 2, MaxLevel = 3, InitialLevel = 0.5 });
        pump = net.Connect(new Pump { Id = "B1", ShutoffHead = 30, RatedFlow = 0.005, RatedHead = 22 }, "R1", "N1");
        net.Connect(new Pipe { Id = "P1", Length = 30, Diameter = 0.05 }, "N1", "T1");
        return net;
    }

    [Fact]
    public void Pump_operates_where_its_curve_meets_the_system_curve()
    {
        var net = PumpToTank(out var pump, out _);
        var sim = new HydraulicSimulation(net);
        double q = sim.FlowIn("B1");
        double systemHead = 10 + 0.5 + LinkLaws.PipeLoss((Pipe)net.FindLink("P1")!, q, net.Fluid);
        Assert.Equal(pump.HeadAt(q, 1), systemHead, 4);
        Assert.Equal(pump.HeadAt(q, 1), sim.PumpHead("B1"), 4);
        // Par = potencia / velocidad
        Assert.Equal(sim.PumpShaftPower("B1") / pump.RatedSpeed, sim.PumpTorque("B1"), 9);
    }

    [Fact]
    public void Tank_level_rises_by_pumped_volume()
    {
        var net = PumpToTank(out _, out var tank);
        var sim = new HydraulicSimulation(net);
        double v0 = tank.Volume, pumped = 0;
        for (int i = 0; i < 600; i++)
        {
            double qBefore = sim.FlowIn("B1");
            sim.Step(TimeSpan.FromSeconds(0.5));
            pumped += 0.5 * (qBefore + sim.FlowIn("B1")) / 2;
        }
        Assert.Equal(300, sim.Time.TotalSeconds, 6);
        Assert.Equal(v0 + pumped, tank.Volume, 2);
        Assert.True(tank.Level > 0.5);
    }

    [Fact]
    public void Full_tank_overflows_and_stays_at_max_level()
    {
        var net = PumpToTank(out _, out var tank);
        tank.InitialLevel = 2.99;
        var sim = new HydraulicSimulation(net);
        sim.Step(TimeSpan.FromSeconds(120));
        Assert.Equal(tank.MaxLevel, tank.Level, 9);
        int t = sim.NodeIndex("T1");
        Assert.Equal(sim.FlowIn("P1"), sim.Overflow[t], 9);
    }

    [Fact]
    public void Draining_tank_empties_without_going_negative()
    {
        var net = new HydraulicNetwork();
        var tank = net.Add(new Tank { Id = "T1", Elevation = 5, Diameter = 0.5, MaxLevel = 2, InitialLevel = 1 });
        net.Add(new Reservoir { Id = "R1", Elevation = 0 });
        net.Connect(new Pipe { Id = "P1", Length = 5, Diameter = 0.05 }, "T1", "R1");
        var sim = new HydraulicSimulation(net);
        double last = tank.Level;
        for (int i = 0; i < 200; i++)
        {
            sim.Step(TimeSpan.FromSeconds(1));
            Assert.True(tank.Level <= last + 1e-12);
            last = tank.Level;
        }
        Assert.Equal(0, tank.Level, 9);
        Assert.True(Math.Abs(sim.FlowIn("P1")) < 1e-5, $"Caudal con tanque vacío: {sim.FlowIn("P1")}");
    }

    [Fact]
    public void Pump_cannot_empty_tank_below_zero()
    {
        var net = new HydraulicNetwork();
        var tank = net.Add(new Tank { Id = "T1", Elevation = 0, Diameter = 0.5, MaxLevel = 2, InitialLevel = 0.2 });
        net.Add(new Junction { Id = "N1" });
        net.Add(new Reservoir { Id = "R1", Elevation = 5 });
        net.Connect(new Pipe { Id = "P1", Length = 2, Diameter = 0.05 }, "T1", "N1");
        net.Connect(new Pump { Id = "B1" }, "N1", "R1");
        var sim = new HydraulicSimulation(net);
        sim.Step(TimeSpan.FromSeconds(300));
        Assert.Equal(0, tank.Level, 9);
        Assert.True(Math.Abs(sim.FlowIn("B1")) < 1e-5);
    }

    [Fact]
    public void Stopped_pump_and_shaft_speed_follow_affinity_laws()
    {
        var net = PumpToTank(out var pump, out _);
        var sim = new HydraulicSimulation(net);
        sim.SetPumpRunning("B1", false);
        sim.Step(TimeSpan.FromSeconds(1));
        Assert.True(Math.Abs(sim.FlowIn("B1")) < 1e-6);

        sim.SetPumpShaftSpeed("B1", pump.RatedSpeed * 0.8);
        sim.Step(TimeSpan.FromMilliseconds(10));
        Assert.Equal(0.8, pump.RelativeSpeed, 9);
        Assert.Equal(pump.HeadAt(sim.FlowIn("B1"), 0.8), sim.PumpHead("B1"), 4);
    }

    [Fact]
    public void Valve_moves_at_its_stroke_rate()
    {
        var net = TwoReservoirs(10, 0, new Pipe(), new Valve { Kind = ValveKind.Control, InitialOpening = 0, StrokeTime = 10 });
        var sim = new HydraulicSimulation(net);
        sim.SetValveOpening("L2", 1);
        sim.Step(TimeSpan.FromSeconds(5));
        Assert.Equal(0.5, ((Valve)net.Links[1]).Opening, 6);
        Assert.True(sim.FlowIn("L2") > 0);
    }

    [Fact]
    public void Validation_reports_missing_reference_and_dangling_links()
    {
        var net = new HydraulicNetwork();
        net.Add(new Junction { Id = "N1" });
        net.Connect(new Pipe { Id = "P1" }, "N1", "X");
        var r = net.Validate();
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("depósito o tanque"));
        Assert.Contains(r.Errors, e => e.Contains("P1"));
    }
}
