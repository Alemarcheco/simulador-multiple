using Simulador.Core.Signals;
using Simulador.Hidraulica.Neumatica;
using Simulador.Hidraulica.Neumatica.View3D;

namespace Simulador.Hidraulica.Tests;

public class PneumaticTests
{
    private const double Bar = 1e5;

    public static TheoryData<string> SampleNames()
    {
        var d = new TheoryData<string>();
        foreach (var s in PneumaticSamples.All) d.Add(s.Name);
        return d;
    }

    [Fact]
    public void Iso6358_is_choked_below_b_and_zero_without_pressure_difference()
    {
        double c = 1e-8, p1 = 7e5;
        Assert.Equal(c * p1, PneumaticSimulation.Iso6358(c, 0.3, p1, 1e5), 12);
        Assert.Equal(c * p1, PneumaticSimulation.Iso6358(c, 0.3, p1, 0.3 * p1), 12);
        double mid = PneumaticSimulation.Iso6358(c, 0.3, p1, 0.65 * p1);
        Assert.InRange(mid, 0.1 * c * p1, c * p1);
        Assert.Equal(0, PneumaticSimulation.Iso6358(c, 0.3, p1, p1), 12);
        // Monótono: más contrapresión, menos caudal.
        Assert.True(PneumaticSimulation.Iso6358(c, 0.3, p1, 0.9 * p1) < mid);
    }

    [Fact]
    public void Single_acting_cylinder_extends_while_pressed_and_springs_back()
    {
        var sim = new PneumaticSimulation(PneumaticSamples.SingleActing());
        Assert.Equal(0, sim.Position("1A1"));
        sim.SetPressed("1V1", true);
        sim.Step(0.5);
        Assert.Equal(0.1, sim.Position("1A1"), 4);
        Assert.InRange(sim.PressureAt("1A1", "A"), 5.9 * Bar, 6.01 * Bar);
        sim.SetPressed("1V1", false);
        sim.Step(1.5);
        Assert.Equal(0, sim.Position("1A1"), 4);
        Assert.InRange(sim.PressureAt("1A1", "A"), -0.01 * Bar, 0.05 * Bar);
    }

    private static double StrokeTime(double opening)
    {
        var c = PneumaticSamples.SpeedControl();
        c.Get<FlowControl>("1V2").Opening = opening;
        c.Get<FlowControl>("1V3").Opening = opening;
        var sim = new PneumaticSimulation(c);
        sim.SetPressed("1V1", true); // palanca: queda accionada
        while (sim.Position("1A1") < 0.199 && sim.Time < 20) sim.Step(0.005);
        return sim.Time;
    }

    [Fact]
    public void Lever_valve_holds_and_flow_controls_slow_the_stroke()
    {
        double fast = StrokeTime(1.0), slow = StrokeTime(0.15);
        Assert.True(fast < 1.0, $"rápido {fast}");
        Assert.True(slow > 2 * fast, $"lento {slow}, rápido {fast}");

        var c = PneumaticSamples.SpeedControl();
        var sim = new PneumaticSimulation(c);
        sim.SetPressed("1V1", true);
        sim.Step(3);
        Assert.True(sim.IsActuated("1V1"));
        Assert.Equal(c.Get<Cylinder>("1A1").Stroke, sim.Position("1A1"), 4);
        sim.SetPressed("1V1", true); // segundo clic: vuelve
        sim.Step(5);
        Assert.False(sim.IsActuated("1V1"));
        Assert.Equal(0, sim.Position("1A1"), 4);
    }

    [Fact]
    public void Two_hand_control_needs_both_buttons()
    {
        var sim = new PneumaticSimulation(PneumaticSamples.TwoHand());
        sim.SetPressed("1S1", true);
        sim.Step(1);
        Assert.Equal(0, sim.Position("1A1"), 4);
        Assert.False(sim.IsActuated("1V1"));
        sim.SetPressed("1S2", true);
        sim.Step(1);
        Assert.True(sim.Position("1A1") > 0.14);
        sim.SetPressed("1S1", false);
        sim.Step(1.5);
        Assert.Equal(0, sim.Position("1A1"), 4);
    }

    [Fact]
    public void Or_valve_passes_air_from_either_input()
    {
        var c = PneumaticSamples.TwoHand();
        c.Get<LogicValve>("1V2").Kind = LogicKind.Or;
        var sim = new PneumaticSimulation(c);
        sim.SetPressed("1S2", true);
        sim.Step(1);
        Assert.True(sim.IsActuated("1V1"));
        Assert.True(sim.Position("1A1") > 0.14);
    }

    [Fact]
    public void Rollers_make_the_cylinder_oscillate_until_the_start_lever_is_off()
    {
        var sim = new PneumaticSimulation(PneumaticSamples.Oscillation());
        sim.Step(1);
        Assert.Equal(0, sim.Position("1A1"), 4);
        sim.SetPressed("1S3", true);
        int ends = 0;
        bool atEnd = false;
        for (int i = 0; i < 1200; i++)
        {
            sim.Step(0.005);
            bool e = sim.Position("1A1") > 0.198;
            if (e && !atEnd) ends++;
            atEnd = e;
        }
        Assert.True(ends >= 2, $"llegadas al final: {ends}");
        sim.SetPressed("1S3", true); // quita la marcha
        sim.Step(6);
        Assert.Equal(0, sim.Position("1A1"), 3);
        sim.Step(2);
        Assert.Equal(0, sim.Position("1A1"), 3);
    }

    [Fact]
    public void Double_solenoid_valve_remembers_and_sensors_report_the_ends()
    {
        var sim = new PneumaticSimulation(PneumaticSamples.ElectroPneumatic());
        Assert.True(sim.SensorActive("1B1"));
        Assert.False(sim.SensorActive("1B2"));
        sim.SetCoil("Y1", true);
        sim.Step(0.05);
        sim.SetCoil("Y1", false); // impulso: la biestable sigue accionada
        sim.Step(2);
        Assert.True(sim.IsActuated("1V1"));
        Assert.True(sim.SensorActive("1B2"));
        Assert.False(sim.SensorActive("1B1"));
        sim.SetCoil("Y2", true);
        sim.Step(2);
        Assert.True(sim.SensorActive("1B1"));
    }

    [Fact]
    public void Compressor_stops_at_cut_out_and_restarts_when_consumption_drops_the_pressure()
    {
        var c = PneumaticSamples.CompressorAndReceiver();
        var sim = new PneumaticSimulation(c);
        sim.Step(150);
        Assert.False(sim.IsCompressorRunning("0P1"));
        Assert.InRange(sim.PressureAt("0Z2", "1"), 7.8 * Bar, 8.1 * Bar);
        double before = sim.PressureAt("0Z2", "1");
        for (int k = 0; k < 8; k++) { sim.SetPressed("1V1", true); sim.Step(2.5); }
        Assert.True(sim.PressureAt("0Z2", "1") < before - 1 * Bar);
        Assert.True(sim.IsCompressorRunning("0P1"));
        Assert.True(sim.AirConsumed > 0.01);
    }

    [Fact]
    public void Load_bigger_than_the_piston_force_stops_the_cylinder()
    {
        var c = PneumaticSamples.SpeedControl();
        var cyl = c.Get<Cylinder>("1A1");
        cyl.Load = 6 * Bar * cyl.PistonArea * 1.2;
        var sim = new PneumaticSimulation(c);
        sim.SetPressed("1V1", true);
        sim.Step(3);
        Assert.Equal(0, sim.Position("1A1"), 4);
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Every_sample_is_valid_round_trips_and_builds_a_3D_scene(string name)
    {
        var c = PneumaticSamples.All.Single(s => s.Name == name).Create();
        var (errors, warnings) = c.Validate();
        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.False(string.IsNullOrWhiteSpace(c.Description));
        var json = PneumaticFile.Serialize(c);
        var back = PneumaticFile.Deserialize(json);
        Assert.Equal(json, PneumaticFile.Serialize(back));
        var sim = new PneumaticSimulation(back);
        sim.Step(2);
        foreach (var cy in back.Components.OfType<Cylinder>()) Assert.InRange(sim.Position(cy.Id), 0, cy.Stroke);
        var scene = PneumaticSceneBuilder.Build(back, sim, back.Components[0].Id);
        Assert.True(scene.Triangles.Count > 200);
        Assert.Contains(scene.Labels, l => l.Highlight);
        Assert.Contains("|", PneumaticFile.BillOfMaterials(back));
    }

    [Fact]
    public void Validation_reports_missing_supply_and_bad_roller()
    {
        var c = PneumaticSamples.Oscillation();
        c.Get<DirectionalValve>("1S1").RollerCylinder = "X";
        c.Remove("0Z1");
        var (errors, _) = c.Validate();
        Assert.Contains(errors, e => e.Contains("fuente"));
        Assert.Contains(errors, e => e.Contains("1S1"));
        Assert.True(c.Rename("1A1", "2A1"));
        Assert.Equal("2A1", c.Get<DirectionalValve>("1S2").RollerCylinder);
    }

    [Fact]
    public void Ports_rotate_with_the_component()
    {
        var f = new FlowControl { X = 100, Y = 100 };
        Assert.Equal((100.0, 130.0), f.PortPosition("1"));
        f.Rotation = 180;
        Assert.Equal((100.0, 70.0), f.PortPosition("1"));
        f.Rotation = 90;
        Assert.Equal((70.0, 100.0), f.PortPosition("1"));
    }

    [Fact]
    public void Circuit_component_takes_coils_from_the_PLC_and_publishes_sensors()
    {
        var part = new PneumaticCircuitComponent("NEU1", PneumaticSamples.ElectroPneumatic());
        Assert.Contains(part.Signals, s => s.Id == "NEU1.Y1" && s.Kind == SignalKind.DI);
        Assert.Contains(part.Signals, s => s.Id == "NEU1.1B2" && s.Kind == SignalKind.DO);
        Assert.Contains(part.Signals, s => s.Id == "NEU1.PI1.Medida" && s.Unit == SignalUnit.Pascal);
        part.Reset();
        Assert.Equal(1, part.Signal("1B1").Value);
        part.Signal("Y1").Value = 1;
        for (int i = 0; i < 200; i++) part.DoStep(TimeSpan.FromMilliseconds(10 * i), TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, part.Signal("1B2").Value);
        Assert.Equal(0, part.Signal("1B1").Value);
        Assert.True(part.Snapshot().Get<double>("1A1.position") > 0.15);
    }
}
