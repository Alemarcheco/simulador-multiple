using Simulador.Hidraulica.CoSim;
using Simulador.Hidraulica.Model;
using K = Simulador.Hidraulica.CoSim.HydraulicKeys;

namespace Simulador.Banco.Mesa;

/// <summary>Lo que muestran, los mandos y los avisos de las piezas hidráulicas en la mesa.</summary>
internal static class HydraulicLooks
{
    private static string Ls(double q) => $"{q * 1000:0.00} L/s";

    public static void Decorate(BuiltPart b, HydraulicPart hp, Func<string, bool> wired)
    {
        string id = hp.Id;
        switch (hp.Element)
        {
            case Tank t:
                b.Look = r => new([$"{r.D(K.Level):0.00} m de {t.MaxLevel:0.#} m"], r.D(K.Overflow) > 1e-6 ? PartState.Warning : PartState.On, Fill: r.D(K.LevelRatio));
                b.Watches = r =>
                [
                    new("overflow", r.D(K.Overflow) > 1e-6, IssueLevel.Warning, $"{id} está rebosando: entra más agua de la que sale.",
                        "Para la bomba antes de llegar arriba (con un PLC y un transmisor de nivel) o abre más el consumo.", Hold: 0.3),
                    new("empty", r.D(K.LevelRatio) < 0.02, IssueLevel.Warning, $"{id} se ha quedado sin agua.",
                        "Si una bomba aspira de aquí, trabajará en seco. Arranca el llenado antes o cierra el consumo.", Hold: 1),
                ];
                break;
            case Reservoir res:
                b.Look = _ => new([$"nivel {res.Level:0.#} m"], PartState.On, Fill: 0.6);
                break;
            case Junction:
                b.Look = r => new([$"{r.D(K.Pressure) / 1e5:0.00} bar"], PartState.On);
                break;
            case Pump p:
                if (!wired("Eje") && !wired("Marcha"))
                    b.Controls.Add(new("Marcha a mano", LiveKind.Toggle, v => hp.RunCommand!.Value = v > 0.5 ? 1 : 0, () => hp.RunCommand!.Value));
                b.Look = r => new([Ls(r.D(K.Flow)), $"{r.D(K.Head):0.0} m"], r.B(K.Running) ? PartState.On : PartState.Idle, Spin: r.D(K.Speed) / p.RatedSpeed);
                b.Watches = r =>
                [
                    new("deadhead", r.B(K.Running) && r.D(K.Speed) > 0.5 * p.RatedSpeed && Math.Abs(r.D(K.Flow)) < 0.05 * p.RatedFlow, IssueLevel.Warning,
                        $"{id} gira pero casi no mueve agua (a válvula cerrada): el agua de dentro se calienta y la bomba sufre.",
                        "Abre la válvula de impulsión o para la bomba cuando no haya consumo.", Hold: 5),
                    new("reverse", r.D(K.Flow) < -0.02 * p.RatedFlow, IssueLevel.Warning,
                        $"El agua vuelve hacia atrás por {id}.", "Pon una válvula antirretorno en la impulsión.", Hold: 1),
                ];
                break;
            case Valve v:
                if (v.Kind == ValveKind.Control && !wired("Consigna"))
                    b.Controls.Add(new("Apertura", LiveKind.Slider, x => hp.OpeningCommand!.Value = x, () => hp.OpeningCommand!.Value, 0, 100, "%"));
                if (v.Kind == ValveKind.Solenoid && !wired("Bobina"))
                    b.Controls.Add(new("Bobina", LiveKind.Toggle, x => hp.Coil!.Value = x > 0.5 ? 1 : 0, () => hp.Coil!.Value));
                b.Look = r => new([$"abierta {r.D(K.Opening) * 100:0} %", Ls(r.D(K.Flow))], r.D(K.Opening) > 0.01 ? PartState.On : PartState.Idle);
                break;
            case Pipe pipe:
                b.Look = r => new([Ls(r.D(K.Flow)), $"{r.D(K.Velocity):0.00} m/s"], Math.Abs(r.D(K.Flow)) > 1e-5 ? PartState.On : PartState.Idle);
                b.Watches = r =>
                [
                    new("velocity", Math.Abs(r.D(K.Velocity)) > 3, IssueLevel.Warning,
                        $"El agua va a {Math.Abs(r.D(K.Velocity)):0.0} m/s por {id}: es demasiado rápido (ruido, desgaste y mucha pérdida de carga).",
                        $"Usa una tubería más gruesa que {pipe.Diameter * 1000:0} mm; lo normal es ir a 1-2 m/s.", Hold: 3),
                ];
                break;
            case Sensor s:
                string unit = s.Kind switch { SensorKind.Pressure => "bar", SensorKind.Level => "m", _ => "L/s" };
                double k = s.Kind switch { SensorKind.Pressure => 1e-5, SensorKind.Flow => 1000, _ => 1 };
                b.Look = r => new([$"{r.D(K.Value) * k:0.00} {unit}"], PartState.On);
                break;
            default:
                b.Look = _ => new([], PartState.On);
                break;
        }
    }
}
