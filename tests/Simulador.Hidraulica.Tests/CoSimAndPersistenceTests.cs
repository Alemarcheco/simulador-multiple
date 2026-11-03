using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Hidraulica.CoSim;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Persistence;

namespace Simulador.Hidraulica.Tests;

public class CoSimAndPersistenceTests
{
    private sealed class FakeClock : ISimulationClock
    {
        public TimeSpan Now => TimeSpan.Zero;
        public TimeSpan Step => TimeSpan.FromMilliseconds(1);
        public event EventHandler<TimeSpan>? Tick { add { } remove { } }
        public void RequestEventAt(TimeSpan time) { }
    }

    private static SimContext Context() => new() { Clock = new FakeClock(), Bus = new SignalBus() };

    [Fact]
    public void Project_round_trips_through_json()
    {
        var net = Samples.PumpingToTank();
        var json = ProjectFile.Serialize(net);
        var back = ProjectFile.Deserialize(json);
        Assert.Contains("\"tipo\": \"bomba\"", json);
        Assert.Equal(net.Nodes.Count, back.Nodes.Count);
        Assert.Equal(net.Links.Count, back.Links.Count);
        Assert.Equal(ValveKind.Control, back.Links.OfType<Valve>().Single().Kind);
        Assert.Equal(ProjectFile.Serialize(back), json);
    }

    [Fact]
    public void Bill_of_materials_sums_pipe_lengths_by_diameter()
    {
        var bom = Report.BillOfMaterials(Samples.PumpingToTank());
        var pipe50 = bom.Single(l => l.Item == "Tubería" && l.Description.Contains("Ø50"));
        Assert.Equal(80, pipe50.Quantity);
        Assert.Contains(bom, l => l.Item == "Bomba centrífuga");
        Assert.Contains(bom, l => l.Item == "Tanque");
    }

    [Fact]
    public void Network_exposes_shaft_and_signals_and_follows_PLC_commands()
    {
        var red = new RedHidraulica("RED1", Samples.PumpingToTank());
        red.Initialize(Context());
        Assert.Equal(SimPhase.Physics, red.Phase);
        Assert.Equal(10, red.StepMultiple);
        var shaft = Assert.Single(red.PowerPorts);
        Assert.Equal("B1.Eje", shaft.Id);
        Assert.Equal(Causality.ImposesEffort, shaft.Causality);
        Assert.Contains(red.Signals, s => s.Id == "V1.Consigna" && s.Unit == SignalUnit.Percent);
        Assert.Contains(red.Signals, s => s.Id == "LT1.Medida" && s.Unit == SignalUnit.Meter);

        // El PLC para la bomba: deja de haber caudal y de haber par.
        red.Part("B1").RunCommand!.Value = 0;
        red.DoStep(TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
        Assert.False(red.Part("B1").Snapshot().Get<bool>(HydraulicKeys.Running, true));
        Assert.Equal(0, shaft.Effort);

        // Lo acciona un motor del banco: impone la velocidad y recibe par resistente.
        var pump = red.Part("B1");
        pump.ShaftConnected = true;
        shaft.Flow = 303.7;
        red.DoStep(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        Assert.True(shaft.Effort > 0);
        Assert.Equal(303.7, pump.Snapshot().Get<double>(HydraulicKeys.Speed), 6);

        // El transmisor de nivel publica el nivel del tanque.
        var level = red.Part("T1").Snapshot().Get<double>(HydraulicKeys.Level);
        Assert.Equal(level, red.Part("LT1").Measurement!.Value, 9);
        Assert.True(red.Snapshot().Values.ContainsKey("T1.level"));
    }

    [Fact]
    public void Factory_parts_assemble_into_a_network_with_implicit_junctions()
    {
        var f = new HydraulicComponentFactory();
        var parts = new[]
        {
            (HydraulicPart)f.Create("deposito", new Dictionary<string, object> { ["id"] = "R1" }),
            (HydraulicPart)f.Create("bomba", new Dictionary<string, object> { ["id"] = "B1" }),
            (HydraulicPart)f.Create("tuberia", new Dictionary<string, object> { ["id"] = "P1", ["longitud"] = 20.0 }),
            (HydraulicPart)f.Create("tanque", new Dictionary<string, object> { ["id"] = "T1", ["cota"] = 5.0 }),
            (HydraulicPart)f.Create("sensor-nivel", new Dictionary<string, object> { ["id"] = "LT1", ["objetivo"] = "T1" }),
        };
        var red = RedHidraulica.Assemble("RED1", parts,
        [
            new("R1", "Boca", "B1", "Entrada"),
            new("B1", "Salida", "P1", "Entrada"),   // brida directa bomba-tubería → unión implícita
            new("P1", "Salida", "T1", "Boca"),
        ]);
        red.Initialize(Context());
        Assert.Single(red.Model.Nodes.OfType<Junction>());
        Assert.Contains(f.Catalog, c => c.TypeId == "valvula-control");

        parts[1].RunCommand!.Value = 1;
        for (int i = 0; i < 100; i++) red.DoStep(TimeSpan.FromMilliseconds(10 * i), TimeSpan.FromMilliseconds(10));
        Assert.True(red.Part("P1").Snapshot().Get<double>(HydraulicKeys.Flow) > 0);
        Assert.Equal(1.0, red.Simulation.Time.TotalSeconds, 9);
    }
}
