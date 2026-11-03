using Simulador.Banco.Generic;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Banco.Scenarios;

/// <summary>
/// Turbocompresor: turbina de gas y compresor en el mismo eje llenan un depósito de aire. El PLC lee la presión por una
/// analógica y regula la admisión de la turbina con un PI programado en texto estructurado.
/// </summary>
public sealed class TurboCompressorScenario : Scenario
{
    public const string Program = """
        PROGRAM Presion
        VAR
            Presion  AT %IW64  : INT;    (* PT1: 0-10 V = 0-10 bar *)
            Mando    AT %QW64  : INT;    (* 0-10 V = 0-100 % de admisión *)
            EnMarcha AT %QX0.0 : BOOL;   (* piloto *)
            Alta     AT %QX0.1 : BOOL;   (* alarma de presión alta *)
            Consigna : REAL := 7.0;
            Bar, Error, Integral, Salida : REAL;
        END_VAR

        Bar := INT_TO_REAL(Presion) * 10.0 / 27648.0;
        Error := Consigna - Bar;
        (* PI: Kp = 30 %/bar, integral 0,5 %/(bar·s) con ciclo de 10 ms, sin integrar en saturación *)
        Salida := 30.0 * Error + Integral;
        IF NOT ((Salida >= 100.0 AND Error > 0.0) OR (Salida <= 0.0 AND Error < 0.0)) THEN
            Integral := LIMIT(0.0, Integral + Error * 0.005, 100.0);
        END_IF;
        Salida := LIMIT(0.0, 30.0 * Error + Integral, 100.0);
        Mando := REAL_TO_INT(Salida * 276.48);
        EnMarcha := Salida > 1.0;
        Alta := Bar > 9.0;
        END_PROGRAM
        """;

    public override string Name => "Turbocompresor";
    public override string Description =>
        "La turbina de gas TC-T mueve el compresor TC-C, que llena el depósito D1. El PLC mantiene 7 bar ajustando la " +
        "admisión. Sube el consumo de aire y mira cómo responde.";

    public PlcCpu Plc { get; }
    public Turbine Turbine { get; }
    public Compressor Compressor { get; }
    public AirReceiver Receiver { get; }

    public TurboCompressorScenario()
    {
        var s = Scheduler;
        Plc = new PlcCpu("PLC2", PlcProfiles.Get("generico-iec")) { CycleTime = TimeSpan.FromMilliseconds(10) };
        Plc.LoadStructuredText(Program);
        Compressor = new Compressor("TC-C", ratedFlow: 0.5, maxPressure: 14e5, rpm: 3000);
        Turbine = new Turbine("TC-T", TurbineKind.Gas, powerKw: 260, rpm: 3000, inertia: 4, loadInertia: 2, actuatorTime: 0.5);
        Receiver = new AirReceiver("D1", volume: 4, reliefPressure: 9.5e5);
        var pt = new Transmitter("PT1", SignalUnit.Pascal, 0, 10e5, SignalUnit.Volt, 0, 10);
        var conv = new Transmitter("Y1", SignalUnit.Volt, 0, 10, SignalUnit.Percent, 0, 100);

        s.Add(pt); s.Add(Plc); s.Add(conv); s.Add(Turbine); s.Add(Compressor); s.Add(Receiver);
        Wire("X3:1", Receiver.PressureSignal, pt.Input, "toma de presión del depósito");
        Wire("X3:2", pt.Output, Plc.AnalogInputs[0], "0-10 V = 0-10 bar");
        Wire("X3:3", Plc.AnalogOutputs[0], conv.Input, "salida analógica 0-10 V");
        Wire("X3:4", conv.Output, Turbine.AdmissionCommand, "posicionador de la válvula de combustible");
        Link("Eje", "Acoplamiento", Turbine.Shaft, Compressor.Shaft, "turbina impone velocidad, compresor devuelve par");
        Link("Tubería de aire", "DN100", Compressor.Outlet, Receiver.Inlet, "el depósito impone la presión");

        Trends.Add(new("Presión D1 0–10 bar", fr => Get(fr, "D1", GenericKeys.Pressure) / 1e4));
        Trends.Add(new("Admisión TC-T", fr => Get(fr, "TC-T", GenericKeys.Admission) * 100));
        Trends.Add(new("Velocidad TC-T", fr => Get(fr, "TC-T", GenericKeys.Speed) / Turbine.RatedSpeed * 100));
        Trends.Add(new("Caudal /0,5 m³/s", fr => Get(fr, "TC-C", GenericKeys.Flow) / 0.005));

        Sliders.Add(new("Consumo de aire", "m³/s", 0, 0.35, 0.15, v => Receiver.Demand = v));
        Actions.Add(new("Fuga grande", () => Receiver.Demand = 0.33));
        Actions.Add(new("Rearmar sobrevelocidad", () => Turbine.ResetTrip()));
        AfterReset();
    }

    public override void AfterReset()
    {
        Receiver.Demand = 0.15;
        Plc.Start();
    }
}
