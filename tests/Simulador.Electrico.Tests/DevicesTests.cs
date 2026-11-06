using System.Numerics;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Electrico.Tests;

/// <summary>Motor monofásico, transformadores, variador, PLC y corrientes por borne.</summary>
public class DevicesTests
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
    public void Single_phase_pump_runs_from_24V_control_transformer()
    {
        var s = Examples.SinglePhasePump();
        var sim = new Simulator(s);
        var coil = El(s, "KM1", "bobina_contactor");
        sim.Run(0.1);
        Assert.InRange(sim.Status(coil.Id).Voltage, 0, 0.5);
        var t1 = El(s, "T1", "trafo_1f");
        Assert.InRange(sim.TerminalVoltage(t1.Id, "2U1") > 0 ? Math.Abs(sim.NetVoltage(sim.NetOf(t1.Id, "2U1")).Ac.Magnitude - sim.NetVoltage(sim.NetOf(t1.Id, "2U2")).Ac.Magnitude) : 0, 0, 1000);

        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(0.1);
        Assert.True(sim.Status(coil.Id).Actuated, "KM1 debe actuar con 24 V");
        Assert.InRange(sim.Status(coil.Id).Voltage, 21, 25.5);
        sim.Run(3);
        var m = sim.Status(El(s, "M1", "motor_1f").Id);
        Assert.InRange(m.Rpm, 1380, 1500);
        // 0,75 kW a 230 V con rendimiento 0,72 y cos 0,92: unos 4,9 A.
        Assert.InRange(m.Current, 2.5, 6.5);
        Click(sim, El(s, "S1", "pulsador_nc"));
        sim.Run(0.1);
        Assert.False(sim.Status(coil.Id).Actuated);
    }

    [Fact]
    public void Single_phase_transformer_has_its_ratio_and_three_phase_Dyn_gives_neutral()
    {
        var b = new SchematicBuilder("trafos");
        var red = b.Add("red_3f", "XS1", 0, 0);
        var t1 = b.Add("trafo_1f", "T1", 0, 5, ("V1", 400), ("V2", 230), ("S", 250));
        b.Wire(red, "L1", t1, "1U1").Wire(red, "L2", t1, "1U2");
        var r = b.Add("resistencia", "R1", 0, 12, ("R", 230.0 * 230 / 200)); // 200 W
        b.Wire(t1, "2U1", r, "1").Wire(t1, "2U2", r, "2");
        var t3 = b.Add("trafo_3f", "T2", 10, 5, ("V1", 400), ("V2", 400), ("S", 10));
        b.Wire(red, "L1", t3, "1U").Wire(red, "L2", t3, "1V").Wire(red, "L3", t3, "1W");
        var lamp = b.Add("lampara", "E1", 10, 12, ("V", 230), ("P", 100));
        b.Wire(t3, "2U", lamp, "1").Wire(lamp, "2", t3, "2N");
        var sim = new Simulator(b.Schematic);
        sim.Run(0.05);
        Assert.InRange(sim.Status(r.Id).Voltage, 215, 232);
        var u = sim.NetVoltage(sim.NetOf(t3.Id, "2U")).Ac;
        var v = sim.NetVoltage(sim.NetOf(t3.Id, "2V")).Ac;
        var n = sim.NetVoltage(sim.NetOf(t3.Id, "2N")).Ac;
        Assert.InRange((u - v).Magnitude, 390, 405);
        Assert.InRange((u - n).Magnitude, 225, 234);
        Assert.True(sim.Status(lamp.Id).Brightness > 0.95);
    }

    [Fact]
    public void Vfd_ramps_up_with_low_current_and_reverses()
    {
        var s = Examples.VfdDrive();
        var sim = new Simulator(s);
        var u1 = El(s, "U1", "variador");
        var m1 = El(s, "M1", "motor_3f");
        var sa = El(s, "SA1", "selector_3p");
        sim.Run(0.2);
        Assert.Equal(0, sim.Status(u1.Id).Frequency);
        Assert.Equal(0, sim.Status(m1.Id).Rpm);

        sim.PointerDown(sa.Id); sim.PointerUp(sa.Id); // posición I: STF-SD
        sim.Run(1);
        Assert.InRange(sim.Status(u1.Id).Frequency, 9, 11);
        double peak = 0;
        for (var i = 0; i < 70; i++) { sim.Run(0.1); peak = Math.Max(peak, sim.Status(u1.Id).Current); }
        var st = sim.Status(m1.Id);
        Assert.Equal(50, sim.Status(u1.Id).Frequency, 3);
        Assert.InRange(st.Rpm, 1400, 1500);
        // Motor de 4 kW: unos 8 A de línea. Con rampa no pasa de ~1,6 veces (en directo serían más de 50 A).
        Assert.True(peak < 14, $"pico de corriente con variador {peak:0.0} A");
        Assert.Null(sim.Status(u1.Id).Fault);

        sim.PointerDown(sa.Id); sim.PointerUp(sa.Id); // posición II: STR-SD
        sim.Run(15);
        Assert.Equal(-50, sim.Status(u1.Id).Frequency, 3);
        Assert.InRange(sim.Status(m1.Id).Rpm, -1500, -1400);

        sim.PointerDown(sa.Id); sim.PointerUp(sa.Id); // posición 0: para con rampa
        sim.Run(8);
        Assert.Equal(0, sim.Status(u1.Id).Frequency);
        Assert.InRange(sim.Status(m1.Id).Rpm, -60, 60);
    }

    [Fact]
    public void Vfd_reference_can_change_while_running()
    {
        var s = Examples.VfdDrive();
        var sim = new Simulator(s);
        var u1 = El(s, "U1", "variador");
        var sa = El(s, "SA1", "selector_3p");
        sim.PointerDown(sa.Id); sim.PointerUp(sa.Id);
        sim.Run(7);
        sim.SetVfdReference(u1.Id, 25);
        sim.Run(5);
        Assert.Equal(25, sim.Status(u1.Id).Frequency, 3);
        Assert.InRange(sim.Status(El(s, "M1", "motor_3f").Id).Rpm, 690, 750);
    }

    [Fact]
    public void Plc_program_latches_the_contactor_through_its_relay_output()
    {
        var s = Examples.PlcStartStop();
        var sim = new Simulator(s);
        var plc = El(s, "KF1", "plc_compacto");
        var cpu = new PlcCpu("KF1", PlcProfiles.Get("generico-iec"));
        cpu.LoadStructuredText(plc.Texts["programa"]);
        cpu.Start();
        var warnings = sim.AttachController("KF1", cpu);
        Assert.Empty(warnings);

        var coil = El(s, "KM1", "bobina_contactor");
        sim.Run(0.3);
        Assert.True(sim.Status(plc.Id).Powered);
        Assert.False(sim.Status(coil.Id).Actuated);
        var inputs = sim.Status(plc.Id).Inputs;
        Assert.True(inputs[2], "I0.2 (paro NC) en reposo a 1");
        Assert.True(inputs[4], "I0.4 (Q1 cerrado) a 1");
        Assert.False(inputs[0]);

        Click(sim, El(s, "S1", "pulsador_na"));
        sim.Run(0.2);
        Assert.True(sim.Status(coil.Id).Actuated, "KM1 debe actuar por Q0.0");
        sim.Run(3);
        Assert.InRange(sim.Status(El(s, "M1", "motor_3f").Id).Rpm, 1400, 1500);
        Assert.True(sim.Status(El(s, "H1", "piloto").Id).Brightness > 0.9);

        Click(sim, El(s, "S2", "pulsador_nc"));
        sim.Run(0.2);
        Assert.False(sim.Status(coil.Id).Actuated);
    }

    [Fact]
    public void Plc_without_24V_keeps_outputs_open()
    {
        var s = Examples.PlcStartStop();
        s.Wires.RemoveAll(w => s.Find(w.A.Element)?.Type == "fuente_dc" || s.Find(w.B.Element)?.Type == "fuente_dc");
        var sim = new Simulator(s);
        var plc = El(s, "KF1", "plc_compacto");
        var cpu = new PlcCpu("KF1", PlcProfiles.Get("generico-iec"));
        cpu.LoadStructuredText("PROGRAM P VAR Q AT %QX0.0 : BOOL; END_VAR Q := TRUE; END_PROGRAM");
        cpu.Start();
        sim.AttachController("KF1", cpu);
        sim.Run(0.3);
        Assert.False(sim.Status(plc.Id).Powered);
        Assert.False(sim.Status(El(s, "KM1", "bobina_contactor").Id).Actuated);
    }

    [Fact]
    public void Terminal_currents_obey_Kirchhoff_and_match_the_motor_current()
    {
        var s = Examples.DirectOnLine();
        var sim = new Simulator(s);
        Click(sim, El(s, "S2", "pulsador_na"));
        sim.Run(3);
        var m = El(s, "M1", "motor_3f");
        var iu = sim.TerminalCurrent(m.Id, "U");
        var iv = sim.TerminalCurrent(m.Id, "V");
        var iw = sim.TerminalCurrent(m.Id, "W");
        Assert.True((iu.Ac + iv.Ac + iw.Ac).Magnitude < 0.01);
        Assert.Equal(sim.Status(m.Id).Current, new[] { iu.Ac.Magnitude, iv.Ac.Magnitude, iw.Ac.Magnitude }.Max(), 1);

        // En cada red, lo que entra en unos bornes sale por otros.
        var byNet = new Dictionary<int, Complex>();
        foreach (var e in s.Elements)
        {
            var type = Simulador.Electrico.Engine.Catalog.ElementCatalog.Default.Get(e.Type);
            foreach (var t in type.Terminals)
            {
                var net = sim.NetOf(e.Id, t.Id);
                byNet[net] = byNet.GetValueOrDefault(net) + sim.TerminalCurrent(e.Id, t.Id).Ac;
            }
        }
        Assert.All(byNet, kv => Assert.True(kv.Value.Magnitude < 0.05, $"red {kv.Key}: {kv.Value.Magnitude:0.000} A"));
    }

    [Fact]
    public void Vfd_output_wired_to_the_grid_is_reported()
    {
        var s = Examples.VfdDrive();
        var red = El(s, "XS1", "red_3f");
        var u1 = El(s, "U1", "variador");
        s.Wires.Add(new Wire { A = new TerminalRef(red.Id, "L1"), B = new TerminalRef(u1.Id, "U") });
        var sim = new Simulator(s);
        Assert.Contains(sim.Events, e => e.Text.Contains("salida U-V-W"));
    }
}
