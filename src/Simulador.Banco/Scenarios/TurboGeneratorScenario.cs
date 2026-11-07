using Simulador.Banco.Generic;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Scenarios;

/// <summary>
/// Turbogenerador en isla: turbina de vapor y generador síncrono en el mismo eje, con un regulador de velocidad PI que
/// mueve la admisión para mantener 50 Hz cuando cambia la carga eléctrica.
/// </summary>
public sealed class TurboGeneratorScenario : Scenario
{
    public override string Name => "Turbogenerador";
    public override string Description =>
        "La turbina T1 mueve el generador G1 a 3000 rpm. El regulador GOV mide la velocidad y abre o cierra la " +
        "admisión para mantener 50 Hz. Cambia la carga y mira cómo cae la frecuencia y se recupera.";

    public Turbine T1 { get; }
    public SynchronousGenerator G1 { get; }
    public ElectricalLoad Load { get; }
    public PiController Governor { get; }

    public TurboGeneratorScenario()
    {
        var s = Scheduler;
        G1 = new SynchronousGenerator("G1", ratedKva: 600, voltage: 400, rpm: 3000);
        T1 = new Turbine("T1", TurbineKind.Steam, powerKw: 550, rpm: 3000, inertia: 9, loadInertia: 5, actuatorTime: 0.3);
        Load = new ElectricalLoad("CARGA", powerKw: 200);
        Governor = new PiController("GOV", SignalUnit.RadPerSecond, setpoint: G1.RatedSpeed, kp: 2500, ti: 3);

        s.Add(Governor); s.Add(T1); s.Add(G1); s.Add(Load);
        Wire("X2:1", G1.SpeedSignal, Governor.ProcessValue, "captador de velocidad");
        Wire("X2:2", Governor.Output, T1.AdmissionCommand, "0-100 % → válvula de admisión");
        Link("Eje", "Acoplamiento", T1.Shaft, G1.Shaft, "turbina impone velocidad, generador devuelve par");
        Link("Cable", "Barras 400 V", G1.Terminals, Load.Terminals, "generador impone tensión, la carga pide corriente");

        Trends.Add(new("Frecuencia 45–55 Hz", fr => (Get(fr, "G1", GenericKeys.Frequency) - 45) * 10));
        Trends.Add(new("Potencia G1 /600 kW", fr => Get(fr, "G1", GenericKeys.Power) / 6000));
        Trends.Add(new("Admisión T1", fr => Get(fr, "T1", GenericKeys.Admission) * 100));

        Sliders.Add(new("Carga eléctrica", "kW", 0, 500, 200, v => Load.PowerKw = v));
        Actions.Add(new("Rechazo de carga", () => Load.PowerKw = 0));
        Actions.Add(new("Escalón +150 kW", () => Load.PowerKw = Math.Min(500, Load.PowerKw + 150)));
        Actions.Add(new("Rearmar sobrevelocidad", () => T1.ResetTrip()));
    }

    public override void AfterReset() => Load.PowerKw = 200;
}
