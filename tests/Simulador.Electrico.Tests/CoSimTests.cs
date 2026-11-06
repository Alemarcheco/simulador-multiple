using Simulador.Core.CoSim;
using Simulador.Electrico.Engine.CoSim;
using Simulador.Electrico.Engine.Model;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Electrico.Tests;

/// <summary>La red eléctrica como un solo componente del banco de pruebas combinado.</summary>
public class CoSimTests
{
    [Fact]
    public void Plc_in_the_bench_drives_a_contactor_coil_and_the_motor_shaft_feels_the_load()
    {
        var factory = new ElectricalComponentFactory();
        var index = Examples.All.ToList().FindIndex(x => x.Name.StartsWith("Arranque directo"));
        var net = (ElectricalNetwork)factory.Create($"ejemplo:{index}", new Dictionary<string, object> { ["Id"] = "RED1", ["Inercia carga M1"] = 0.1 });
        Assert.Contains(net.Signals, s => s.Id == "RED1.KM1.bobina");
        Assert.Contains(net.Signals, s => s.Id == "RED1.KM1.13-14");
        var shaft = Assert.Single(net.PowerPorts);
        Assert.Equal("RED1.M1.eje", shaft.Id);

        var cpu = new PlcCpu("PLC1", PlcProfiles.Get("generico-iec"));
        cpu.LoadStructuredText("PROGRAM P VAR Marcha AT %IX0.0 : BOOL; KM AT %QX0.0 : BOOL; END_VAR KM := Marcha; END_PROGRAM");
        cpu.Start();

        var bench = new CoSimScheduler();
        bench.Add(net);
        bench.Add(cpu);
        bench.Initialize();
        var r = bench.Connect(cpu.DigitalOutputs[0].Id, "RED1.KM1.bobina");
        Assert.True(r.Connected);

        Run(bench, 0.5);
        Assert.Equal(0, net.Signals.Single(s => s.Id == "RED1.KM1.13-14").Value);
        cpu.Force("%IX0.0", true);
        Run(bench, 3);
        Assert.Equal(24, net.Signals.Single(s => s.Id == "RED1.KM1.13-14").Value);
        var free = shaft.Flow;
        Assert.InRange(free, 145, 158); // ~1450 rpm

        shaft.Effort = 30; // la carga del banco frena con 30 N·m más
        Run(bench, 3);
        Assert.True(shaft.Flow < free - 1, $"con más carga debe bajar la velocidad ({shaft.Flow:0.0} rad/s)");
        Assert.True(net.Snapshot().Get<double>("M1.corriente") > 9);

        cpu.Force("%IX0.0", false);
        Run(bench, 0.3);
        // El esquema tiene autorretención (KM1 13-14): al quitar la orden del PLC, el contactor sigue enclavado
        // por su propio circuito, igual que en la realidad. S1 lo para.
        Assert.True(net.Snapshot().Get<bool>("KM1.actuado"));
        var s1 = net.Schematic.Elements.Single(e => e.Tag == "S1");
        net.Simulator.PointerDown(s1.Id);
        Run(bench, 0.2);
        net.Simulator.PointerUp(s1.Id);
        Assert.False(net.Snapshot().Get<bool>("KM1.actuado"));
    }

    [Fact]
    public void Factory_lists_every_example_with_its_ports()
    {
        var f = new ElectricalComponentFactory();
        var cat = f.Catalog.ToList();
        Assert.Equal(Examples.All.Count + 1, cat.Count);
        var plc = cat.Single(c => c.Name.Contains("PLC"));
        Assert.Contains(plc.Ports, p => p.Name == "KF1.Q0.0");
        Assert.Contains(plc.Ports, p => p.Name == "M1.eje");
    }

    private static void Run(CoSimScheduler bench, double seconds)
    {
        bench.RunFor(TimeSpan.FromSeconds(seconds));
    }
}
