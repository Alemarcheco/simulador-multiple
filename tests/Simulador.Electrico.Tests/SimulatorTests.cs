using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Tests;

public class SimulatorTests
{
    private static PlacedElement El(Schematic s, string tag, string type) =>
        s.Elements.Single(e => e.Tag == tag && e.Type == type);

    private static void Click(Simulator sim, PlacedElement e, double hold = 0.1)
    {
        sim.PointerDown(e.Id);
        sim.Run(hold);
        sim.PointerUp(e.Id);
    }

    [Fact]
    public void Direct_on_line_latches_runs_and_stops()
    {
        var s = Examples.DirectOnLine();
        var sim = new Simulator(s);
        var coil = El(s, "KM1", "bobina_contactor");
        var motor = El(s, "M1", "motor_3f");
        sim.Run(0.2);
        Assert.False(sim.Status(coil.Id).Actuated);

        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(0.05);
        var start = sim.Status(motor.Id).Current;
        sim.Run(3);
        var st = sim.Status(motor.Id);
        Assert.True(sim.Status(coil.Id).Actuated, "KM1 debe quedar enclavado al soltar S2");
        Assert.InRange(st.Rpm, 1400, 1500);
        // Corriente de arranque de varias veces la nominal (~8 A) y vuelta a valores normales.
        Assert.True(start > 30, $"corriente de arranque {start:0.0} A");
        Assert.InRange(st.Current, 5, 9.5);
        Assert.True(sim.Status(El(s, "H1", "piloto").Id).Brightness > 0.9);

        Click(sim, El(s, "S1", "pulsador_nc"));
        sim.Run(0.2);
        Assert.False(sim.Status(coil.Id).Actuated);
        sim.Run(10);
        Assert.True(sim.Status(motor.Id).Rpm < 100);
    }

    [Fact]
    public void Emergency_stop_latches_open()
    {
        var s = Examples.DirectOnLine();
        var sim = new Simulator(s);
        Click(sim, El(s, "S2", "pulsador_na"));
        var seta = El(s, "S0", "seta_emergencia");
        sim.PointerDown(seta.Id);
        sim.PointerUp(seta.Id);
        sim.Run(0.2);
        Assert.False(sim.Status(El(s, "KM1", "bobina_contactor").Id).Actuated);
        Click(sim, El(s, "S2", "pulsador_na"));
        Assert.False(sim.Status(El(s, "KM1", "bobina_contactor").Id).Actuated);
    }

    [Fact]
    public void Overload_trips_motor_breaker_and_lights_fault_pilot()
    {
        var s = Examples.DirectOnLine();
        var motor = El(s, "M1", "motor_3f");
        motor.Params["carga"] = 200;
        var sim = new Simulator(s);
        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(300);
        var q1 = El(s, "Q1", "guardamotor");
        Assert.True(sim.Status(q1.Id).Tripped, "el guardamotor debe disparar con un 200 % de carga");
        Assert.Contains(sim.Events, e => e.Severity == EventSeverity.Fault && e.Text.Contains("Q1"));
        Assert.True(sim.Status(El(s, "H2", "piloto").Id).Brightness > 0.9);
    }

    [Fact]
    public void Reversing_swaps_direction_and_interlocks()
    {
        var s = Examples.Reversing();
        var sim = new Simulator(s);
        var motor = El(s, "M1", "motor_3f");
        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(3);
        Assert.InRange(sim.Status(motor.Id).Rpm, 1400, 1500);

        // S3 no hace nada mientras KM1 está dentro (enclavamiento).
        Click(sim, El(s, "S3", "pulsador_na"));
        Assert.False(sim.Status(El(s, "KM2", "bobina_contactor").Id).Actuated);

        Click(sim, El(s, "S1", "pulsador_nc"));
        sim.Run(10);
        Click(sim, El(s, "S3", "pulsador_na"));
        sim.Run(4);
        Assert.InRange(sim.Status(motor.Id).Rpm, -1500, -1400);
    }

    private static double PeakCurrent(Simulator sim, Guid id, double seconds)
    {
        var peak = 0.0;
        for (var t = 0.0; t < seconds; t += Simulator.DefaultStep)
        {
            sim.Step();
            peak = Math.Max(peak, sim.Status(id).Current);
        }
        return peak;
    }

    [Fact]
    public void Star_delta_switches_after_timer_with_lower_start_current()
    {
        var s = Examples.StarDelta();
        var sim = new Simulator(s);
        var f2 = El(s, "F2", "rele_termico");
        sim.PointerDown(El(s, "S2", "pulsador_na").Id);
        var starPeak = PeakCurrent(sim, f2.Id, 0.1);
        sim.PointerUp(El(s, "S2", "pulsador_na").Id);
        Assert.True(sim.Status(El(s, "KM2", "bobina_contactor").Id).Actuated, "arranca en estrella");
        Assert.False(sim.Status(El(s, "KM3", "bobina_contactor").Id).Actuated);
        sim.Run(4.5);
        Assert.False(sim.Status(El(s, "KM2", "bobina_contactor").Id).Actuated);
        Assert.True(sim.Status(El(s, "KM3", "bobina_contactor").Id).Actuated, "pasa a triángulo");
        sim.Run(3);
        Assert.InRange(sim.Status(El(s, "M1", "motor_3f_6b").Id).Rpm, 1400, 1500);

        // El mismo motor arrancado directamente en triángulo: la corriente de línea en estrella es ~1/3.
        var motor = El(s, "M1", "motor_3f_6b");
        var b = new SchematicBuilder("Directo en triángulo");
        var red = b.Add("red_3f", "XS1", 0, 0);
        var m = b.Add("motor_3f", "M1", 0, 6, [.. motor.Params.Select(kv => (kv.Key, kv.Value)), ("conexion", 1)]);
        b.Wire(red, "L1", m, "U").Wire(red, "L2", m, "V").Wire(red, "L3", m, "W");
        var dol = new Simulator(b.Schematic);
        var deltaPeak = PeakCurrent(dol, m.Id, 0.1) * Math.Sqrt(3); // corriente de devanado → de línea
        Assert.InRange(starPeak / deltaPeak, 0.25, 0.42);
    }

    [Fact]
    public void Three_way_lighting_toggles_from_each_point()
    {
        var s = Examples.ThreeWayLighting();
        var sim = new Simulator(s);
        var lamp = El(s, "E1", "lampara");
        sim.Run(0.05);
        var states = new List<bool> { sim.Status(lamp.Id).Brightness > 0.9 };
        foreach (var tag in new[] { "S1", "S2", "S3", "S1" })
        {
            var sw = s.Elements.Single(e => e.Tag == tag);
            sim.PointerDown(sw.Id);
            sim.PointerUp(sw.Id);
            sim.Run(0.05);
            states.Add(sim.Status(lamp.Id).Brightness > 0.9);
        }
        for (var i = 1; i < states.Count; i++) Assert.NotEqual(states[i - 1], states[i]);
    }

    [Fact]
    public void Rcd_test_button_trips()
    {
        var s = Examples.ThreeWayLighting();
        var sim = new Simulator(s);
        var rcd = El(s, "Q2", "diferencial_2p");
        sim.Run(0.05);
        sim.PointerDown(rcd.Id, alt: true);
        sim.Run(0.1);
        sim.PointerUp(rcd.Id);
        Assert.True(sim.Status(rcd.Id).Tripped);
    }

    [Fact]
    public void Short_circuit_trips_breaker_magnetically()
    {
        var b = new SchematicBuilder("Corto");
        var src = b.Add("fuente_ac1", "XS1", 0, 0);
        var q = b.Add("magnetotermico_1p", "Q1", 0, 4, ("In", 10), ("curva", 1));
        var sw = b.Add("interruptor", "S1", 0, 10);
        b.Wire(src, "L", q, "1").Wire(q, "2", sw, "1").Wire(sw, "2", src, "N");
        var sim = new Simulator(b.Schematic);
        sim.Run(0.1);
        Assert.False(sim.Status(q.Id).Tripped);
        sim.PointerDown(sw.Id);
        sim.Run(0.05);
        Assert.True(sim.Status(q.Id).Tripped);
        Assert.Contains(sim.Events, e => e.Text.Contains("magnético"));
    }

    [Fact]
    public void Impulse_relay_toggles_and_stair_timer_switches_off()
    {
        var s = Examples.ImpulseAndStairTimer();
        var sim = new Simulator(s);
        var e1 = El(s, "E1", "lampara");
        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(0.1);
        Assert.True(sim.Status(e1.Id).Brightness > 0.9);
        Click(sim, El(s, "S3", "pulsador_na"));
        sim.Run(0.1);
        Assert.True(sim.Status(e1.Id).Brightness < 0.1);

        var e2 = El(s, "E2", "lampara");
        Click(sim, El(s, "S4", "pulsador_na"), 0.5);
        sim.Run(5);
        Assert.True(sim.Status(e2.Id).Brightness > 0.9);
        sim.Run(6);
        Assert.True(sim.Status(e2.Id).Brightness < 0.1);
    }

    [Fact]
    public void Phase_loss_prevents_start_and_thermal_relay_trips()
    {
        var s = Examples.Reversing();
        // Se quita la fase L3 del guardamotor.
        var q1 = El(s, "Q1", "guardamotor");
        var red = El(s, "XS1", "red_3f");
        s.Wires.RemoveAll(w => w.A.Element == red.Id && w.A.Terminal == "L3");
        var sim = new Simulator(s);
        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(2);
        Assert.True(Math.Abs(sim.Status(El(s, "M1", "motor_3f").Id).Rpm) < 50, "con dos fases no arranca");
        Assert.Contains(sim.Events, e => e.Text.Contains("falta una fase"));
        sim.Run(60);
        Assert.True(sim.Status(El(s, "F2", "rele_termico").Id).Tripped || sim.Status(q1.Id).Tripped);
    }

    [Fact]
    public void Wrong_coil_voltage_is_reported()
    {
        var b = new SchematicBuilder("Bobina 24 V en 230 V");
        var src = b.Add("fuente_ac1", "XS1", 0, 0);
        var k = b.Add("bobina_rele", "KA1", 0, 4, ("V", 24), ("ac", 1));
        b.Wire(src, "L", k, "A1").Wire(k, "A2", src, "N");
        var sim = new Simulator(b.Schematic);
        sim.Run(0.05);
        Assert.Contains(sim.Events, e => e.Text.Contains("Se quemaría"));
    }

    [Fact]
    public void Schematic_roundtrips_through_json()
    {
        var s = Examples.StarDelta();
        var back = Schematic.FromJson(s.ToJson());
        Assert.Equal(s.Elements.Count, back.Elements.Count);
        Assert.Equal(s.Wires.Count, back.Wires.Count);
        Assert.Equal(s.Elements[0].Params.Count, back.Elements[0].Params.Count);
    }
}

public class ManufacturerCatalogTests
{
    [Fact]
    public void Imports_csv_and_reports_bad_lines()
    {
        var csv = """
            Fabricante;Referencia;Tipo;Descripción;Parámetros
            # comentario
            Marca;KM-9;contactos_principales;9 A;In=9
            Marca;X;no_existe;;
            Marca;G1;guardamotor;"Guardamotor; 6-10 A";Ir=9,5 Im=13
            Marca;G2;guardamotor;;Foo=1
            """;
        var cat = new Simulador.Electrico.Engine.Catalog.ManufacturerCatalog();
        var r = cat.ImportCsv(csv, Simulador.Electrico.Engine.Catalog.ElementCatalog.Default);
        Assert.Equal(2, r.Imported);
        Assert.Equal(2, r.Errors.Count);
        var g1 = cat.ForType("guardamotor").Single();
        Assert.Equal(9.5, g1.Params["Ir"]);
        Assert.Equal("Guardamotor; 6-10 A", g1.Description);
    }

    [Fact]
    public void Bundled_catalog_is_valid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalogos", "fabricantes-ejemplo.csv");
        var cat = new Simulador.Electrico.Engine.Catalog.ManufacturerCatalog();
        var r = cat.ImportCsv(File.ReadAllText(path), Simulador.Electrico.Engine.Catalog.ElementCatalog.Default);
        Assert.Empty(r.Errors);
        Assert.True(r.Imported > 20);
    }
}
