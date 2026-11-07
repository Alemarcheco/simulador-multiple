using Simulador.Banco.Generic;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Electrico.Engine.CoSim;
using Simulador.Electrico.Engine.Model;
using Simulador.Hidraulica.CoSim;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Banco.Scenarios;

/// <summary>
/// El mismo llenado de tanque, pero con la parte eléctrica resuelta por el módulo Eléctrico: un esquema real
/// (red → guardamotor Q1 → contactor KM1 → motor M1) metido en el banco como un solo componente
/// <see cref="ElectricalNetwork"/>. El PLC excita la bobina de KM1 y lee el contacto 13-14 de Q1.
/// </summary>
public sealed class TankFillElectricScenario : Scenario
{
    public const string NetworkId = "RED1";

    public const string Program = """
        PROGRAM Llenado
        VAR
            Nivel   AT %IW64  : INT;    (* transmisor LT1: 0-10 V = 0-3 m *)
            Q1_ok   AT %IX0.1 : BOOL;   (* 13-14 del guardamotor Q1: cerrado = en servicio *)
            Bomba   AT %QX0.0 : BOOL;   (* bobina de KM1 *)
            Alarma  AT %QX0.1 : BOOL;
            Porcentaje : REAL;
        END_VAR

        Porcentaje := INT_TO_REAL(Nivel) * 100.0 / 27648.0;
        IF NOT Q1_ok THEN
            Bomba := FALSE;
        ELSIF Porcentaje < 20.0 THEN
            Bomba := TRUE;
        ELSIF Porcentaje > 80.0 THEN
            Bomba := FALSE;
        END_IF;
        Alarma := (NOT Q1_ok) OR (Porcentaje > 95.0);
        END_PROGRAM
        """;

    /// <summary>Esquema de potencia: red 400 V, guardamotor, contactor y motor 2,2 kW. La bobina y el 13-14 de Q1 van al PLC del banco.</summary>
    public static Schematic CreateSchematic()
    {
        var b = new SchematicBuilder("Bomba P1 (banco)", "Arranque directo de la bomba del tanque, mandado por el PLC del banco de pruebas.");
        var red = b.Add("red_3f", "XS1", 0, 0);
        var q1 = b.Add("guardamotor", "Q1", 0, 5, ("Ir", 5));
        b.Wire(red, "L1", q1, "1").Wire(red, "L2", q1, "3").Wire(red, "L3", q1, "5");
        var km = b.Add("contactos_principales", "KM1", 0, 12, ("In", 9));
        b.Wire(q1, "2", km, "1").Wire(q1, "4", km, "3").Wire(q1, "6", km, "5");
        // 2,2 kW, 230/400 V en estrella, 2900 rpm. Sin carga propia: el par lo pone la bomba por el eje.
        var m = b.Add("motor_3f", "M1", 0, 20, ("Pn", 2.2), ("Uw", 230), ("rpm", 2900), ("cos", 0.85), ("eta", 0.84),
            ("kI", 6.5), ("J", 0.006), ("carga", 0), ("tipoCarga", 1));
        b.Wire(km, "2", m, "U").Wire(km, "4", m, "V").Wire(km, "6", m, "W");
        b.Add("bobina_contactor", "KM1", 20, 12);
        var aux = b.Add("contacto_na", "Q1", 24, 5);
        b.Text(aux, "Bornes", "13-14");
        return b.Schematic;
    }

    public override string Name => "Llenado de tanque (esquema eléctrico real)";
    public override string Description =>
        "Igual que el llenado de tanque, pero la red, el guardamotor Q1, el contactor KM1 y el motor M1 son un esquema del módulo Eléctrico. " +
        "Abre Q1 y el PLC lo ve por su contacto 13-14 y deja la bomba parada.";

    public PlcCpu Plc { get; }
    public ElectricalNetwork Electrical { get; }
    public RedHidraulica Hydraulics { get; }
    public HydraulicPart Pump { get; }
    public HydraulicPart Valve { get; }
    public Transmitter Lt1Transmitter { get; }

    public ISignalPort Coil => Port("KM1.bobina");
    public ISignalPort Q1Contact => Port("Q1.13-14");
    public IPowerPort MotorShaft => Electrical.PowerPorts.Single(p => p.Id == $"{NetworkId}.M1.eje");

    private ISignalPort Port(string name) => Electrical.Signals.Single(s => s.Id == $"{NetworkId}.{name}");

    public TankFillElectricScenario()
    {
        var s = Scheduler;
        Plc = new PlcCpu("PLC1", PlcProfiles.Get("generico-iec")) { CycleTime = TimeSpan.FromMilliseconds(10) };
        Plc.LoadStructuredText(Program);
        Electrical = new ElectricalNetwork(NetworkId, CreateSchematic(), new Dictionary<string, object> { ["Inercia carga M1"] = 0.01 });

        Hydraulics = TankFillScenario.CreateHydraulics();
        Pump = Hydraulics.Part("P1");
        Pump.ShaftConnected = true;
        Valve = Hydraulics.Part("V1");
        Valve.OpeningCommand!.Value = 35;
        Lt1Transmitter = new Transmitter("LT1-T", SignalUnit.Meter, 0, 3, SignalUnit.Volt, 0, 10);

        s.Add(Lt1Transmitter); s.Add(Plc); s.Add(Electrical); s.Add(Hydraulics);

        Wire("X1:1", Plc.DigitalOutputs[0], Coil, "salida a transistor 24 V → bobina A1-A2 de KM1");
        Wire("X1:2", Q1Contact, Plc.DigitalInputs[1], "contacto auxiliar 13-14 del guardamotor Q1, 24 V");
        Wire("X1:3", Hydraulics.Part("LT1").Measurement!, Lt1Transmitter.Input, "sonda de nivel");
        Wire("X1:4", Lt1Transmitter.Output, Plc.AnalogInputs[0], "0-10 V = 0-3 m");
        Link("Eje", "Acoplamiento", MotorShaft, Pump.Shaft!, "el motor del esquema impone la velocidad, la bomba devuelve el par");
        Pipe("T1", "CIS", "P1", "aspiración DN50, 4 m");
        Pipe("T2", "P1", "TK1", "impulsión DN50, 18 m, sube 4 m");
        Pipe("T3 + V1", "TK1", "DES", "consumo DN40 con válvula de control");

        Trends.Add(new("Nivel TK1", fr => Get(fr, "agua", "TK1.levelRatio") * 100));
        Trends.Add(new("Corriente M1 /35 A", fr => Get(fr, NetworkId, "M1." + ElectricalNetwork.Keys.Current) / 35 * 100));
        Trends.Add(new("Velocidad M1", fr => Get(fr, NetworkId, "M1." + ElectricalNetwork.Keys.Rpm) / 3000 * 100));
        Trends.Add(new("Caudal P1 /8 L/s", fr => Get(fr, "agua", "P1.flow") / 0.008 * 100));

        Sliders.Add(new("Apertura de V1 (consumo)", "%", 0, 100, 35, v => Valve.OpeningCommand!.Value = v));
        Actions.Add(new("Abrir / rearmar guardamotor Q1", ToggleQ1));

        AfterReset();
    }

    /// <summary>Lo mismo que un clic sobre Q1 en el editor eléctrico.</summary>
    public void ToggleQ1()
    {
        var q1 = Electrical.Schematic.Elements.First(e => e.Tag == "Q1" && e.Type == "guardamotor");
        Electrical.Simulator.PointerDown(q1.Id);
        Electrical.Simulator.PointerUp(q1.Id);
    }

    public override void AfterReset()
    {
        Valve.OpeningCommand!.Value = 35;
        Plc.Start();
    }
}
