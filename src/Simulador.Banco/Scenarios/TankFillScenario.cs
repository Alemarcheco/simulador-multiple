using Simulador.Banco.Generic;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Hidraulica.CoSim;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Banco.Scenarios;

/// <summary>
/// PLC → contactor con térmico → motor asíncrono → bomba centrífuga → tanque elevado, con el nivel realimentado al
/// PLC por un transmisor 0–10 V. Usa el PLC real (texto estructurado) y la red hidráulica real del módulo Hidráulica.
/// </summary>
public sealed class TankFillScenario : Scenario
{
    public const string Program = """
        PROGRAM Llenado
        VAR
            Nivel   AT %IW64  : INT;    (* transmisor LT1: 0-10 V = 0-3 m *)
            Termico AT %IX0.1 : BOOL;   (* 97-98 del relé térmico de KM1 *)
            Bomba   AT %QX0.0 : BOOL;   (* bobina de KM1 *)
            Alarma  AT %QX0.1 : BOOL;
            Porcentaje : REAL;
        END_VAR

        Porcentaje := INT_TO_REAL(Nivel) * 100.0 / 27648.0;
        IF Termico THEN
            Bomba := FALSE;
        ELSIF Porcentaje < 20.0 THEN
            Bomba := TRUE;
        ELSIF Porcentaje > 80.0 THEN
            Bomba := FALSE;
        END_IF;
        Alarma := Termico OR (Porcentaje > 95.0);
        END_PROGRAM
        """;

    public override string Name => "Llenado de tanque";
    public override string Description =>
        "El PLC arranca la bomba bajo el 20 % de nivel y la para sobre el 80 %. El contactor KM1 alimenta el motor M1, " +
        "que mueve la bomba P1 y llena el tanque TK1 elevado 4 m. La válvula V1 es el consumo.";

    public PlcCpu Plc { get; }
    public GridSource Grid { get; }
    public Contactor Km1 { get; }
    public InductionMotorGeneric M1 { get; }
    public RedHidraulica Hydraulics { get; }
    public HydraulicPart Pump { get; }
    public HydraulicPart Valve { get; }
    public Transmitter Lt1Transmitter { get; }

    public TankFillScenario()
    {
        var s = Scheduler;
        Plc = new PlcCpu("PLC1", PlcProfiles.Get("generico-iec")) { CycleTime = TimeSpan.FromMilliseconds(10) };
        Plc.LoadStructuredText(Program);

        Grid = new GridSource("RED");
        Km1 = new Contactor("KM1", overloadSetting: 5.0);
        // 2,2 kW, 400 V, 2900 rpm, 4,7 A. La bomba declara 0,01 kg·m² de inercia: la suma el motor.
        M1 = new InductionMotorGeneric("M1", powerKw: 2.2, voltage: 400, rpm: 2900, ratedCurrent: 4.7,
            startCurrentRatio: 6.5, breakdownTorqueRatio: 2.8, inertia: 0.006, loadInertia: 0.01);

        Hydraulics = CreateHydraulics();
        Pump = Hydraulics.Part("P1");
        Pump.ShaftConnected = true;
        Valve = Hydraulics.Part("V1");
        Valve.OpeningCommand!.Value = 35;

        Lt1Transmitter = new Transmitter("LT1-T", SignalUnit.Meter, 0, 3, SignalUnit.Volt, 0, 10);

        s.Add(Grid); s.Add(Lt1Transmitter); s.Add(Plc); s.Add(Km1); s.Add(M1); s.Add(Hydraulics);

        Wire("X1:1", Plc.DigitalOutputs[0], Km1.Coil, "salida a transistor 24 V → bobina A1-A2");
        Wire("X1:2", Km1.TripContact, Plc.DigitalInputs[1], "contacto 97-98 del térmico, 24 V");
        Wire("X1:3", Hydraulics.Part("LT1").Measurement!, Lt1Transmitter.Input, "sonda de nivel");
        Wire("X1:4", Lt1Transmitter.Output, Plc.AnalogInputs[0], "0-10 V = 0-3 m");
        Link("Cable", "W1 4×2,5 mm²", Grid.L, Km1.Line, "acometida 400 V");
        Link("Cable", "W2 4×1,5 mm²", Km1.Load, M1.Terminals, "KM1 → bornes U-V-W del motor");
        Link("Eje", "Acoplamiento", M1.Shaft, Pump.Shaft!, "motor impone velocidad, bomba devuelve par");
        Pipe("T1", "CIS", "P1", "aspiración DN50, 4 m");
        Pipe("T2", "P1", "TK1", "impulsión DN50, 18 m, sube 4 m");
        Pipe("T3 + V1", "TK1", "DES", "consumo DN40 con válvula de control");

        Trends.Add(new("Nivel TK1", fr => Get(fr, "agua", "TK1.levelRatio") * 100));
        Trends.Add(new("Corriente M1 /35 A", fr => Get(fr, "M1", GenericKeys.Current) / 35 * 100));
        Trends.Add(new("Velocidad M1", fr => Get(fr, "M1", GenericKeys.Speed) / M1.SyncSpeed * 100));
        Trends.Add(new("Caudal P1 /8 L/s", fr => Get(fr, "agua", "P1.flow") / 0.008 * 100));

        Sliders.Add(new("Apertura de V1 (consumo)", "%", 0, 100, 35, v => Valve.OpeningCommand!.Value = v));
        Actions.Add(new("Corte de red / reponer", () => Grid.Outage = !Grid.Outage));
        Actions.Add(new("Térmico mal ajustado (0,8 A)", () => Km1.OverloadSetting = 0.8));
        Actions.Add(new("Rearmar térmico", () => { Km1.OverloadSetting = 5.0; Km1.ResetOverload(); }));

        AfterReset();
    }

    /// <summary>Cisterna → bomba P1 → tanque elevado TK1 → válvula de consumo V1 → desagüe, con la sonda LT1.</summary>
    internal static RedHidraulica CreateHydraulics()
    {
        var f = new HydraulicComponentFactory();
        HydraulicPart Part(string type, Dictionary<string, object> p) => (HydraulicPart)f.Create(type, p);
        var parts = new List<HydraulicPart>
        {
            Part("deposito", new() { ["id"] = "CIS", ["cota"] = 0.0, ["nivel"] = 1.5 }),
            Part("tuberia", new() { ["id"] = "T1", ["longitud"] = 4.0, ["diametro"] = 0.05 }),
            Part("bomba", new() { ["id"] = "P1" }),
            Part("tuberia", new() { ["id"] = "T2", ["longitud"] = 18.0, ["diametro"] = 0.05, ["perdidasLocales"] = 3.0 }),
            Part("tanque", new() { ["id"] = "TK1", ["cota"] = 4.0, ["diametro"] = 1.2, ["alturaMax"] = 3.0, ["nivelInicial"] = 0.9 }),
            Part("tuberia", new() { ["id"] = "T3", ["longitud"] = 12.0, ["diametro"] = 0.04 }),
            Part("valvula-control", new() { ["id"] = "V1", ["diametro"] = 0.04, ["tiempoRecorrido"] = 3.0 }),
            Part("deposito", new() { ["id"] = "DES", ["cota"] = 0.0, ["nivel"] = 0.0 }),
            Part("sensor-nivel", new() { ["id"] = "LT1", ["objetivo"] = "TK1" }),
        };
        return RedHidraulica.Assemble("agua", parts,
        [
            new("CIS", "Boca", "T1", "Entrada"),
            new("T1", "Salida", "P1", "Entrada"),
            new("P1", "Salida", "T2", "Entrada"),
            new("T2", "Salida", "TK1", "Boca"),
            new("TK1", "Boca", "T3", "Entrada"),
            new("T3", "Salida", "V1", "Entrada"),
            new("V1", "Salida", "DES", "Boca"),
        ]);
    }

    public override void AfterReset()
    {
        Valve.OpeningCommand!.Value = 35;
        Plc.Start();
    }
}
