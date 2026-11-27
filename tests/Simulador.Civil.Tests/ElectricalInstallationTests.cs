using Simulador.Civil.Model;
using Simulador.Civil.Model.Electrical;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

public class ElectricalInstallationTests
{
    private static Point2 P(double x, double y) => new(x, y);

    /// <summary>Cuarto de 4 × 3 m con tablero, una lámpara con su apagador y un contacto, cableados por el techo.</summary>
    private static (BuildingDocument Doc, PlacedFixture Panel, PlacedFixture Lamp, PlacedFixture Switch, PlacedFixture Outlet) Room()
    {
        var doc = new BuildingDocument();
        PlacedFixture q = null!, lamp = null!, s = null!, x = null!;
        doc.Edit(e => e.AddWallLoop("N0", [P(0, 0), P(4, 0), P(4, 3), P(0, 3)]));
        doc.Edit(e =>
        {
            q = FixturePlacer.Place(doc, e, FixtureCatalog.Tablero, "N0", P(0.5, 0.1))!;
            lamp = FixturePlacer.Place(doc, e, FixtureCatalog.Luminaria, "N0", P(2, 1.5))!;
            s = FixturePlacer.Place(doc, e, FixtureCatalog.Interruptor, "N0", P(3.9, 0.5))!;
            x = FixturePlacer.Place(doc, e, FixtureCatalog.Tomacorriente, "N0", P(2, 2.9))!;
            e.Electrical.Set(new FixtureElectrical(lamp.Id, ControlledBy: [s.Id]));
            ElectricalDesigner.AutoCircuits(doc, e);
            ElectricalDesigner.AutoConduits(doc, e);
        });
        return (doc, q, lamp, s, x);
    }

    [Fact]
    public void Wall_fixtures_go_on_the_inner_face_and_ceiling_ones_under_the_slab()
    {
        var (doc, q, lamp, s, x) = Room();
        Assert.Equal(1.50, q.Position.Z, 3);
        Assert.Equal(1.20, s.Position.Z, 3);
        Assert.Equal(0.30, x.Position.Z, 3);
        Assert.Equal(2.95, lamp.Position.Z, 3);
        Assert.NotNull(q.HostId);
        Assert.Null(lamp.HostId);
        Assert.InRange(s.Position.X, 3.9, 3.93); // cara interior del muro este (espesor 0,15)
    }

    [Fact]
    public void Auto_design_makes_lighting_and_outlet_circuits_and_routes_everything()
    {
        var (doc, q, lamp, s, x) = Room();
        var el = doc.Electrical;
        var light = el.CircuitOf(lamp.Id)!;
        var outlets = el.CircuitOf(x.Id)!;
        Assert.Equal(CircuitUse.Alumbrado, light.Use);
        Assert.Equal(15, light.BreakerA);
        Assert.Equal(14, light.WireAwg);
        Assert.Equal(CircuitUse.Contactos, outlets.Use);
        Assert.Equal(20, outlets.BreakerA);
        Assert.Equal(12, outlets.WireAwg);
        Assert.Equal(light.Id, el.CircuitOf(s.Id)!.Id);
        Assert.NotEqual(light.Phase, outlets.Phase); // se reparten entre las fases A y B
        Assert.Equal(3, doc.Routes.Count(r => r.ModuleId == "electrico"));

        var sch = LoadSchedule.Compute(doc);
        var panel = Assert.Single(sch.Panels);
        Assert.Equal(15 + 180, panel.TotalVA, 6);
        var lc = panel.Circuits.Single(c => c.Circuit.Id == light.Id);
        Assert.True(lc.Routed);
        Assert.Equal(15.0 / 127, lc.Current, 6);
        Assert.InRange(lc.DropPercent, 0, 0.5);
        Assert.DoesNotContain(sch.Checks, c => c.Severity == CheckSeverity.Error);
    }

    [Fact]
    public void Switch_turns_the_lamp_on_and_outlet_feeds_what_is_plugged_in()
    {
        var (doc, _, lamp, s, x) = Room();
        var sim = new InstallationSimulation(doc);
        Assert.Empty(sim.NotSimulated);
        sim.Run(0.1);
        Assert.Equal(0, sim.Brightness(lamp.Id), 3);
        sim.ClickSwitch(s.Id);
        sim.Run(0.1);
        Assert.InRange(sim.Brightness(lamp.Id), 0.97, 1.01);

        Assert.InRange(sim.VoltageAt(x.Id), 125, 127.5);
        var plancha = sim.SetOutletLoad(x.Id, Array.FindIndex(InstallationSimulation.PlugLoads, p => p.Name == "Plancha"));
        sim.Run(0.1);
        Assert.InRange(sim.PowerAt(x.Id), plancha.Watts * 0.95, plancha.Watts * 1.01);
        var c = doc.Electrical.CircuitOf(x.Id)!;
        Assert.InRange(sim.CircuitCurrent(c.Id), 9.0, 9.6);
    }

    [Fact]
    public void Overloaded_outlet_circuit_trips_its_breaker_and_can_be_reset()
    {
        var (doc, _, _, _, x) = Room();
        doc.Edit(e =>
        {
            var c = e.Electrical.CircuitOf(x.Id)!;
            e.Electrical.Set(c with { BreakerA = 6 });
        });
        var cid = doc.Electrical.CircuitOf(x.Id)!.Id;
        var sim = new InstallationSimulation(doc);
        sim.SetOutletLoad(x.Id, InstallationSimulation.PlugLoads.Length - 1); // calentador de 2000 W ≈ 15,7 A ≈ 2,6 In
        sim.Run(5);
        Assert.False(sim.BreakerStatus(cid).Tripped);
        sim.Run(30);
        Assert.True(sim.BreakerStatus(cid).Tripped);
        Assert.True(sim.VoltageAt(x.Id) < 5);
        sim.SetOutletLoad(x.Id, 0);
        sim.ToggleBreaker(cid);
        sim.Run(0.1);
        Assert.False(sim.BreakerStatus(cid).Tripped);
        Assert.True(sim.VoltageAt(x.Id) > 120);
    }

    [Fact]
    public void Three_way_switches_command_the_same_lamp_from_both_ends()
    {
        var doc = new BuildingDocument();
        doc.Edit(e => e.AddWallLoop("N0", [P(0, 0), P(8, 0), P(8, 1.5), P(0, 1.5)]));
        PlacedFixture lamp = null!, a = null!, b = null!;
        doc.Edit(e =>
        {
            FixturePlacer.Place(doc, e, FixtureCatalog.Tablero, "N0", P(4, 0.1));
            lamp = FixturePlacer.Place(doc, e, FixtureCatalog.Luminaria, "N0", P(4, 0.75))!;
            a = FixturePlacer.Place(doc, e, FixtureCatalog.Escalera, "N0", P(0.1, 0.75))!;
            b = FixturePlacer.Place(doc, e, FixtureCatalog.Escalera, "N0", P(7.9, 0.75))!;
            ElectricalDesigner.AutoSwitches(doc, e);
            ElectricalDesigner.AutoCircuits(doc, e);
        });
        Assert.Equal(2, doc.Electrical.Of(lamp.Id)!.ControlledBy!.Count);
        var sim = new InstallationSimulation(doc);
        // Empieza apagada; cada clic en cualquiera de los dos la cambia.
        sim.Run(0.05);
        Assert.Equal(0, sim.Brightness(lamp.Id), 2);
        sim.ClickSwitch(a.Id); sim.Run(0.05);
        Assert.True(sim.Brightness(lamp.Id) > 0.9);
        sim.ClickSwitch(b.Id); sim.Run(0.05);
        Assert.Equal(0, sim.Brightness(lamp.Id), 2);
        sim.ClickSwitch(b.Id); sim.Run(0.05);
        Assert.True(sim.Brightness(lamp.Id) > 0.9);
    }

    [Fact]
    public void Gfci_test_cuts_the_outlet_and_reset_restores_it()
    {
        var doc = new BuildingDocument();
        doc.Edit(e => e.AddWallLoop("N0", [P(0, 0), P(3, 0), P(3, 2), P(0, 2)]));
        PlacedFixture x = null!;
        doc.Edit(e =>
        {
            FixturePlacer.Place(doc, e, FixtureCatalog.Tablero, "N0", P(0.5, 0.1));
            x = FixturePlacer.Place(doc, e, FixtureCatalog.ContactoGfci, "N0", P(2.9, 1))!;
            ElectricalDesigner.AutoCircuits(doc, e);
        });
        var sim = new InstallationSimulation(doc);
        sim.Run(0.05);
        Assert.True(sim.VoltageAt(x.Id) > 120);
        var cid = doc.Electrical.CircuitOf(x.Id)!.Id;
        sim.PressGfci(x.Id, cid, test: true);
        sim.Run(0.1);
        Assert.True(sim.VoltageAt(x.Id) < 5);
        sim.PressGfci(x.Id, cid, test: false);
        sim.Run(0.1);
        Assert.True(sim.VoltageAt(x.Id) > 120);
    }

    [Fact]
    public void Undersized_breaker_cable_and_full_conduit_are_reported()
    {
        var (doc, _, _, _, x) = Room();
        doc.Edit(e =>
        {
            var c = e.Electrical.CircuitOf(x.Id)!;
            e.Electrical.Set(c with { BreakerA = 30, WireAwg = 12 });
            foreach (var r in doc.Routes) e.Electrical.Set(new ConduitSpec(r.Id, ConduitType.PvcLigero, 13, Conductors: 9, WireAwg: 10));
        });
        var checks = LoadSchedule.Compute(doc).Checks;
        Assert.Contains(checks, c => c.Severity == CheckSeverity.Error && c.Message.Contains("no protege el cable"));
        Assert.Contains(checks, c => c.Severity == CheckSeverity.Error && c.Message.Contains("lo llenan"));
    }

    [Fact]
    public void Columns_and_beams_carry_fixtures_and_survive_save_and_undo()
    {
        var doc = BuildingTemplates.NaveConInstalacionElectrica();
        Assert.Equal(12, doc.Columns.Count);
        Assert.Equal(6, doc.Beams.Count);
        var bells = doc.Fixtures.Where(f => f.Kind == FixtureCatalog.Campana.Kind).ToList();
        Assert.Equal(12, bells.Count);
        Assert.All(bells, b => Assert.StartsWith("T", b.HostId));
        Assert.All(bells, b => Assert.Equal(7.0 - 0.60 - 0.50, b.Position.Z, 3));
        Assert.Contains(doc.Fixtures, f => f.Kind == FixtureCatalog.ContactoTrifasico.Kind && f.HostId!.StartsWith("K"));
        // Tubería a la vista: metálica, bajo las vigas.
        var specs = doc.Routes.Where(r => r.ModuleId == "electrico").Select(r => doc.Electrical.ConduitOf(r.Id)).ToList();
        Assert.Contains(specs, s => s.Type == ConduitType.ParedDelgada);
        Assert.Contains(doc.Routes, r => RouteBuilder.GuessStyle(r) == RouteStyle.Structure);
        // Ni desbalance ni lámparas en otro circuito que su apagador: el diseño automático deja la nave sin avisos.
        Assert.DoesNotContain(LoadSchedule.Compute(doc).Checks, c => c.Severity != CheckSeverity.Info);

        var copy = BuildingDocument.FromJson(doc.ToJson());
        Assert.Equal(doc.Columns.Count, copy.Columns.Count);
        Assert.Equal(doc.Electrical.Circuits.Count, copy.Electrical.Circuits.Count);
        Assert.Equal(doc.Electrical.Fixtures.Count, copy.Electrical.Fixtures.Count);

        int circuits = doc.Electrical.Circuits.Count;
        doc.Edit(e => e.Delete(doc.Fixtures.First(f => f.Kind == "tablero").Id));
        Assert.Empty(doc.Electrical.Circuits);
        doc.Undo();
        Assert.Equal(circuits, doc.Electrical.Circuits.Count);

        var sch = LoadSchedule.Compute(doc);
        Assert.DoesNotContain(sch.Checks, c => c.Severity == CheckSeverity.Error);
        var sim = new InstallationSimulation(doc);
        Assert.Empty(sim.NotSimulated);
        sim.Run(0.1);
        var motor = doc.Fixtures.First(f => f.Kind == FixtureCatalog.Motor.Kind);
        Assert.InRange(sim.PowerAt(motor.Id), 5000, 5700);
    }

    [Fact]
    public void House_example_is_fully_designed_and_simulates()
    {
        var doc = BuildingTemplates.CasaConInstalaciones();
        var sch = LoadSchedule.Compute(doc);
        Assert.DoesNotContain(sch.Checks, c => c.Severity == CheckSeverity.Error);
        Assert.DoesNotContain(sch.Checks, c => c.Message.Contains("no está asignado"));
        var panel = Assert.Single(sch.Panels);
        Assert.True(panel.ImbalancePercent < 40);
        Assert.Contains(panel.Circuits, c => c.Circuit.GroundFault);
        Assert.Contains(panel.Circuits, c => c.Circuit.Poles == 2);
        var sim = new InstallationSimulation(doc);
        Assert.Empty(sim.NotSimulated);
        var entry = doc.Fixtures.First(f => f.Kind == FixtureCatalog.Doble.Kind);
        var porch = doc.Fixtures.First(f => f.Kind == FixtureCatalog.Arbotante.Kind);
        sim.Run(0.05);
        sim.ClickSwitch(entry.Id, 2);
        sim.Run(0.05);
        Assert.True(sim.Brightness(porch.Id) > 0.9);
        Assert.False(LoadSchedule.Compute(doc).ToCsv().Length < 100);
    }
}
