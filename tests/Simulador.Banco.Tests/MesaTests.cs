using Simulador.Banco.Generic;
using Simulador.Banco.Mesa;
using Simulador.Core.CoSim;

namespace Simulador.Banco.Tests;

public class MesaTests
{
    private static BenchPart P(BuiltBench b, string id) => b.Project.Part(id)!;
    private static T Model<T>(BuiltBench b, string id) => (T)b.Parts[id].Model!;
    private static double D(BuiltBench b, string id, string key) =>
        b.Reader(new SimFrame(b.Scheduler.Now, b.Scheduler.Snapshot()), id).D(key);

    [Fact]
    public void Every_catalog_part_can_be_placed_and_built_alone()
    {
        foreach (var t in BenchCatalog.All)
        {
            var project = new BenchProject();
            project.Parts.Add(t.NewPart(t.Prefix + "1", 0, 0));
            var built = BenchBuilder.Build(project);
            Assert.True(built.Parts.ContainsKey(t.Prefix + "1") || built.Issues.Any(i => i.Level == IssueLevel.Error), t.TypeId);
            built.Scheduler.RunFor(TimeSpan.FromMilliseconds(50));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Examples_build_without_errors_and_run(int index)
    {
        var built = BenchBuilder.Build(BenchExamples.All[index].Create());
        Assert.True(built.CanRun, string.Join("\n", built.Issues.Where(i => i.Level == IssueLevel.Error).Select(i => i.Message)));
        built.Scheduler.RunFor(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Examples_survive_a_save_and_open_roundtrip()
    {
        foreach (var ex in BenchExamples.All)
        {
            var p = ex.Create();
            var back = BenchProject.Deserialize(BenchProject.Serialize(p));
            Assert.Equal(p.Parts.Count, back.Parts.Count);
            Assert.Equal(p.Wires.Count, back.Wires.Count);
            Assert.Equal(p.Parts[0].Params, back.Parts[0].Params);
            Assert.True(BenchBuilder.Build(back).CanRun);
        }
    }

    [Fact]
    public void Plc_start_stop_runs_the_motor_and_instruments_record_it()
    {
        var b = BenchBuilder.Build(BenchExamples.MotorStartStop());
        var s1 = Model<PanelSwitch>(b, "S1");
        var s2 = Model<PanelSwitch>(b, "S2");
        var m1 = Model<InductionMotorGeneric>(b, "M1");
        b.Scheduler.RunFor(TimeSpan.FromSeconds(0.5));
        Assert.True(D(b, "M1", GenericKeys.Speed) < 1);

        s1.Actuated = true;
        b.Scheduler.RunFor(TimeSpan.FromSeconds(0.2));
        s1.Actuated = false;
        b.Scheduler.RunFor(TimeSpan.FromSeconds(3));
        Assert.True(D(b, "M1", GenericKeys.Speed) > 0.95 * m1.SyncSpeed);
        Assert.True(m1.PeakCurrent > 5 * 4.7, $"pico {m1.PeakCurrent}");
        Assert.True(P(b, "H1") is not null && b.Reader(new SimFrame(b.Scheduler.Now, b.Scheduler.Snapshot()), "H1").B(PanelKeys.Lit));

        var osc = Model<InstrumentComponent>(b, "OSC1");
        var samples = osc.Samples(0);
        Assert.True(samples.Count > 1000);
        Assert.True(samples.Max(x => x.Values[0]) > 25, "el osciloscopio ve el pico de arranque en CH1");
        var la = Model<InstrumentComponent>(b, "LA1");
        Assert.Contains(la.Samples(0), x => x.Values[0] > 15);
        var mm = Model<InstrumentComponent>(b, "MM1").LastReadings()[0];
        Assert.Equal("corriente", mm[0].Name);
        Assert.Contains(mm, r => r.Name == "tensión" && Math.Abs(r.Value - 400) < 1);

        s2.Actuated = true; // paro (NC): abre
        b.Scheduler.RunFor(TimeSpan.FromSeconds(0.2));
        s2.Actuated = false;
        b.Scheduler.RunFor(TimeSpan.FromSeconds(0.5));
        Assert.True(D(b, "KM1", GenericKeys.Current) < 0.01);
    }

    [Fact]
    public void Arduino_board_drives_the_contactor()
    {
        var b = BenchBuilder.Build(BenchExamples.ArduinoStartStop());
        Assert.True(b.CanRun);
        var s1 = Model<PanelSwitch>(b, "S1");
        b.Scheduler.RunFor(TimeSpan.FromSeconds(0.3));
        s1.Actuated = true;
        b.Scheduler.RunFor(TimeSpan.FromSeconds(0.3));
        s1.Actuated = false;
        b.Scheduler.RunFor(TimeSpan.FromSeconds(2));
        Assert.True(Model<Contactor>(b, "KM1").Closed);
    }

    [Fact]
    public void Tank_fill_example_starts_the_pump_and_raises_the_level()
    {
        var project = BenchExamples.TankFill();
        project.Part("TK1")!["nivelInicial"] = "0.3"; // 10 %: por debajo del 20 % el PLC arranca la bomba
        var b = BenchBuilder.Build(project);
        Assert.True(b.CanRun, string.Join("\n", b.Issues.Select(i => i.Message)));
        double start = D(b, "TK1", "level");
        Model<SetpointSource>(b, "R1").Value = 0; // consumo cerrado
        b.Scheduler.RunFor(TimeSpan.FromSeconds(30));
        Assert.True(Model<Contactor>(b, "KM1").Closed);
        Assert.True(D(b, "P1", "flow") > 0.001, $"caudal {D(b, "P1", "flow")}");
        Assert.True(D(b, "TK1", "level") > start + 0.05, $"nivel {D(b, "TK1", "level")} desde {start}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Examples_run_three_minutes_without_runtime_warnings(int index)
    {
        var b = BenchBuilder.Build(BenchExamples.All[index].Create());
        Assert.DoesNotContain(b.Issues, i => i.Level != IssueLevel.Info);
        var mon = new BenchMonitor(b);
        for (int i = 0; i < 360; i++)
        {
            b.Scheduler.RunFor(TimeSpan.FromSeconds(0.5));
            mon.Update(new SimFrame(b.Scheduler.Now, b.Scheduler.Snapshot()));
        }
        Assert.True(mon.Log.Count == 0, string.Join("\n", mon.Log.Select(l => $"{l.Time:0.0} s {l.Issue.Message}")));
    }

    [Fact]
    public void Tank_example_cycles_between_twenty_and_eighty_percent()
    {
        var b = BenchBuilder.Build(BenchExamples.TankFill());
        double min = 1, max = 0;
        for (int i = 0; i < 1200; i++)
        {
            b.Scheduler.RunFor(TimeSpan.FromSeconds(1));
            double r = D(b, "TK1", "levelRatio");
            if (i > 60) { min = Math.Min(min, r); max = Math.Max(max, r); }
        }
        Assert.InRange(min, 0.12, 0.25);
        Assert.InRange(max, 0.75, 0.9);
    }

    [Fact]
    public void Turbogenerator_example_holds_frequency()
    {
        var b = BenchBuilder.Build(BenchExamples.TurboGenerator());
        b.Scheduler.RunFor(TimeSpan.FromSeconds(40));
        Assert.InRange(D(b, "G1", GenericKeys.Frequency), 49, 51);
        Assert.InRange(D(b, "B1", GenericKeys.Current), 400, 700); // 350 kW a 400 V repartidos en dos cargas
    }

    [Fact]
    public void Wiring_two_outputs_together_is_refused_with_an_explanation()
    {
        var p = new BenchProject();
        p.Parts.Add(BenchCatalog.Get("pulsador")!.NewPart("S1", 0, 0));
        p.Parts.Add(BenchCatalog.Get("pulsador")!.NewPart("S2", 0, 0));
        var why = BenchRules.CanConnect(p, "S1", "13-14", "S2", "13-14");
        Assert.NotNull(why);
        Assert.Contains("dos salidas", why);
    }

    [Fact]
    public void Two_machines_on_one_shaft_and_two_sources_are_refused()
    {
        var p = new BenchProject();
        p.Parts.Add(BenchCatalog.Get("motor")!.NewPart("M1", 0, 0));
        p.Parts.Add(BenchCatalog.Get("turbina")!.NewPart("T1", 0, 0));
        p.Parts.Add(BenchCatalog.Get("red")!.NewPart("RED", 0, 0));
        p.Parts.Add(BenchCatalog.Get("generador")!.NewPart("G1", 0, 0));
        Assert.Contains("mismo eje", BenchRules.CanConnect(p, "M1", "Eje", "T1", "Eje"));
        Assert.Contains("dan tensión", BenchRules.CanConnect(p, "RED", "L", "G1", "Bornes"));
        Assert.Contains("eje", BenchRules.CanConnect(p, "M1", "Eje", "RED", "L"));
    }

    [Fact]
    public void A_second_load_on_a_grid_needs_a_busbar()
    {
        var p = new BenchProject();
        p.Parts.Add(BenchCatalog.Get("red")!.NewPart("RED", 0, 0));
        p.Parts.Add(BenchCatalog.Get("carga")!.NewPart("C1", 0, 0));
        p.Parts.Add(BenchCatalog.Get("carga")!.NewPart("C2", 0, 0));
        p.Wires.Add(new("RED", "L", "C1", "Bornes"));
        Assert.Contains("Embarrado", BenchRules.CanConnect(p, "RED", "L", "C2", "Bornes"));
        // Una punta de medida sí se puede poner aunque el borne ya esté ocupado.
        p.Parts.Add(BenchCatalog.Get("multimetro")!.NewPart("MM1", 0, 0));
        Assert.Null(BenchRules.CanConnect(p, "MM1", "Punta", "RED", "L"));
    }

    [Fact]
    public void Missing_supply_and_tankless_networks_are_errors()
    {
        var p = new BenchProject();
        p.Parts.Add(BenchCatalog.Get("contactor")!.NewPart("KM1", 0, 0));
        p.Parts.Add(BenchCatalog.Get("h-tuberia")!.NewPart("TB1", 0, 0));
        p.Parts.Add(BenchCatalog.Get("h-bomba")!.NewPart("P1", 0, 0));
        p.Wires.Add(new("TB1", "Salida", "P1", "Entrada"));
        var b = BenchBuilder.Build(p);
        Assert.False(b.CanRun);
        Assert.Contains(b.Issues, i => i.Level == IssueLevel.Error && i.PartId == "KM1" && i.Message.Contains("alimentación"));
        Assert.Contains(b.Issues, i => i.Level == IssueLevel.Error && i.Message.Contains("depósito ni tanque"));
    }

    [Fact]
    public void Level_transmitter_on_a_pipe_is_explained()
    {
        var p = BenchExamples.TankFill();
        p.Wires.RemoveAll(w => w.FromPart == "LT1" && w.FromPort == "Toma");
        p.Wires.Add(new("LT1", "Toma", "TB2", "Salida"));
        var b = BenchBuilder.Build(p);
        Assert.Contains(b.Issues, i => i.PartId == "LT1" && i.Message.Contains("Ponlo en un tanque"));
    }

    [Fact]
    public void Plc_program_errors_are_reported_on_the_plc()
    {
        var p = new BenchProject();
        var plc = BenchCatalog.Get("plc")!.NewPart("PLC1", 0, 0);
        plc["programa"] = "PROGRAM X\nVAR a : BOOL; END_VAR\na := ;\nEND_PROGRAM";
        p.Parts.Add(plc);
        var b = BenchBuilder.Build(p);
        Assert.Contains(b.Issues, i => i.Level == IssueLevel.Error && i.PartId == "PLC1");
    }

    [Fact]
    public void Monitor_explains_a_thermal_trip()
    {
        var p = BenchExamples.MotorStartStop();
        p.Part("KM1")!["termico"] = "0.8";
        var b = BenchBuilder.Build(p);
        var monitor = new BenchMonitor(b);
        Model<PanelSwitch>(b, "S1").Actuated = true;
        for (int i = 0; i < 100 && monitor.Log.Count(l => l.Issue.Message.Contains("térmico")) == 0; i++)
        {
            b.Scheduler.RunFor(TimeSpan.FromSeconds(0.5));
            monitor.Update(new SimFrame(b.Scheduler.Now, b.Scheduler.Snapshot()));
        }
        var trip = Assert.Single(monitor.Log, l => l.Issue.Message.Contains("relé térmico"));
        Assert.Equal(IssueLevel.Error, trip.Issue.Level);
        Assert.Equal("KM1", trip.Issue.PartId);
        Assert.NotNull(trip.Issue.Hint);
    }

    [Fact]
    public void Monitor_waits_for_the_hold_time_of_a_stalled_motor()
    {
        var p = new BenchProject();
        p.Parts.Add(BenchCatalog.Get("red")!.NewPart("RED", 0, 0));
        var m = BenchCatalog.Get("motor")!.NewPart("M1", 0, 0);
        m["inerciaCarga"] = "1000"; // imposible de acelerar en segundos
        p.Parts.Add(m);
        p.Wires.Add(new("RED", "L", "M1", "Bornes"));
        var b = BenchBuilder.Build(p);
        var mon = new BenchMonitor(b);
        b.Scheduler.RunFor(TimeSpan.FromSeconds(1));
        mon.Update(new SimFrame(b.Scheduler.Now, b.Scheduler.Snapshot()));
        Assert.DoesNotContain(mon.Active, a => a.Issue.Message.Contains("no consigue arrancar"));
        b.Scheduler.RunFor(TimeSpan.FromSeconds(5));
        mon.Update(new SimFrame(b.Scheduler.Now, b.Scheduler.Snapshot()));
        Assert.Contains(mon.Active, a => a.Issue.Message.Contains("no consigue arrancar"));
    }

    [Fact]
    public void Rename_updates_wires_and_rejects_bad_ids()
    {
        var p = BenchExamples.MotorStartStop();
        var km = p.Part("KM1")!;
        Assert.NotNull(p.Rename(km, "K M"));
        Assert.NotNull(p.Rename(km, "M1"));
        Assert.Null(p.Rename(km, "KM9"));
        Assert.Contains(p.Wires, w => w.ToPart == "KM9" && w.ToPort == "A1");
        Assert.DoesNotContain(p.Wires, w => w.FromPart == "KM1" || w.ToPart == "KM1");
    }
}
