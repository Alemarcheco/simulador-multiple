using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Tests;

public class NewExamplesTests
{
    private static PlacedElement El(Schematic s, string tag, string type) => s.Elements.Single(e => e.Tag == tag && e.Type == type);

    private static void Click(Simulator sim, PlacedElement e, double hold = 0.1)
    {
        sim.PointerDown(e.Id);
        sim.Run(hold);
        sim.PointerUp(e.Id);
        sim.Run(0.1);
    }

    [Fact]
    public void All_examples_build_without_warnings()
    {
        foreach (var (name, create) in Examples.All)
        {
            var sim = new Simulator(create());
            sim.Run(0.2);
            Assert.True(sim.Events.All(e => e.Severity == EventSeverity.Info), $"{name}: {string.Join(" | ", sim.Events.Select(e => e.Text))}");
        }
    }

    [Fact]
    public void Float_pump_manual_and_automatic()
    {
        var s = Examples.FloatPump();
        var sim = new Simulator(s);
        var coil = El(s, "KM1", "bobina_contactor");
        var sa = El(s, "SA1", "selector_3p");
        Click(sim, El(s, "S2", "pulsador_na"));
        Assert.False(sim.Status(coil.Id).Actuated, "en 0 no arranca");

        Click(sim, sa); // I: manual
        Click(sim, El(s, "S2", "pulsador_na"));
        Assert.True(sim.Status(coil.Id).Actuated);
        sim.Run(2);
        Assert.InRange(sim.Status(El(s, "M1", "motor_3f").Id).Rpm, 1400, 1500);

        Click(sim, sa); // II: automático; sale la autorretención
        Assert.False(sim.Status(coil.Id).Actuated);
        var b1 = El(s, "B1", "sensor_na");
        Click(sim, b1);
        Assert.True(sim.Status(coil.Id).Actuated, "el flotador arranca la bomba");
        Click(sim, b1);
        Assert.False(sim.Status(coil.Id).Actuated);
    }

    [Fact]
    public void Cascade_starts_second_belt_after_delay_and_stops_both()
    {
        var s = Examples.CascadeConveyors();
        var sim = new Simulator(s);
        var k1 = El(s, "KM1", "bobina_contactor");
        var k2 = El(s, "KM2", "bobina_contactor");
        Click(sim, El(s, "S2", "pulsador_na"));
        Assert.True(sim.Status(k1.Id).Actuated);
        Assert.False(sim.Status(k2.Id).Actuated);
        sim.Run(5);
        Assert.True(sim.Status(k2.Id).Actuated);
        sim.Run(2);
        Assert.InRange(sim.Status(El(s, "M2", "motor_3f").Id).Rpm, 1400, 1500);
        Click(sim, El(s, "S1", "pulsador_nc"));
        Assert.False(sim.Status(k1.Id).Actuated);
        Assert.False(sim.Status(k2.Id).Actuated);
    }

    [Fact]
    public void Garage_door_stops_at_limit_switch()
    {
        var s = Examples.GarageDoor();
        var sim = new Simulator(s);
        var k1 = El(s, "KM1", "bobina_contactor");
        Click(sim, El(s, "S2", "pulsador_na"));
        Assert.True(sim.Status(k1.Id).Actuated);
        Assert.True(sim.Status(El(s, "H1", "piloto").Id).Brightness > 0.9);
        sim.Run(2);
        Click(sim, El(s, "SQ1", "final_carrera_nc"));
        Assert.False(sim.Status(k1.Id).Actuated, "arriba del todo se para");
        Click(sim, El(s, "S2", "pulsador_na"));
        Assert.False(sim.Status(k1.Id).Actuated, "no puede seguir subiendo");
        Click(sim, El(s, "S3", "pulsador_na"));
        Assert.True(sim.Status(El(s, "KM2", "bobina_contactor").Id).Actuated, "sí puede bajar");
    }

    [Fact]
    public void Household_leak_trips_rcd()
    {
        var s = Examples.HouseholdPanel();
        var sim = new Simulator(s);
        var e1 = El(s, "E1", "lampara");
        Click(sim, El(s, "S1", "interruptor"));
        Click(sim, El(s, "S2", "interruptor"));
        Assert.True(sim.Status(e1.Id).Brightness > 0.9);
        Assert.InRange(sim.Status(El(s, "Q3", "magnetotermico_2p").Id).Current, 8, 9.5);
        var q1 = El(s, "Q1", "diferencial_2p");
        Assert.False(sim.Status(q1.Id).Tripped);
        Click(sim, El(s, "S3", "interruptor"));
        Assert.True(sim.Status(q1.Id).Tripped);
        Assert.True(sim.Status(e1.Id).Brightness < 0.1);
        Assert.Contains(sim.Events, e => e.Text.Contains("fuga"));
    }
}
