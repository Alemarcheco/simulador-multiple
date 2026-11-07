// Contrato: /mnt/project-files/contratos/co-simulacion.md
using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Hidraulica.Neumatica;

/// <summary>
/// Un circuito neumático completo en el banco de pruebas combinado. Expone las bobinas como entradas digitales
/// (las mueve el PLC), los detectores de los cilindros como salidas digitales y los manómetros como salidas analógicas.
/// </summary>
public sealed class PneumaticCircuitComponent : ISimComponent
{
    private readonly PneumaticSimulation _sim;
    private readonly Dictionary<string, SignalPort> _coils = [];
    private readonly Dictionary<string, SignalPort> _sensors = [];
    private readonly Dictionary<string, SignalPort> _gauges = [];

    public string Id { get; }
    public string ModuleId => NeumaticaModule.ModuleId;
    public PneumaticCircuit Circuit { get; }
    public PneumaticSimulation Simulation => _sim;
    public IReadOnlyList<ISignalPort> Signals { get; }
    public IReadOnlyList<IPowerPort> PowerPorts { get; } = [];
    public int StepMultiple { get; set; } = 1;
    public SimPhase Phase => SimPhase.Physics;

    public PneumaticCircuitComponent(string id, PneumaticCircuit circuit)
    {
        Id = id;
        Circuit = circuit;
        _sim = new PneumaticSimulation(circuit);
        foreach (var coil in _sim.Coils.Distinct()) _coils[coil] = new SignalPort($"{id}.{coil}", SignalKind.DI, SignalUnit.Bool, 24);
        foreach (var s in _sim.Sensors.Distinct()) _sensors[s] = new SignalPort($"{id}.{s}", SignalKind.DO, SignalUnit.Bool, 24, common: CommonType.PNP);
        foreach (var g in circuit.Components.OfType<Gauge>()) _gauges[g.Id] = new SignalPort($"{id}.{g.Id}.Medida", SignalKind.AO, SignalUnit.Pascal);
        Signals = [.. _coils.Values, .. _sensors.Values, .. _gauges.Values];
    }

    public SignalPort Signal(string name) => _coils.GetValueOrDefault(name) ?? _sensors.GetValueOrDefault(name) ?? _gauges[name];

    public void Initialize(SimContext context)
    {
        foreach (var s in Signals) context.Bus.Register(s);
        Reset();
    }

    public void Reset()
    {
        _sim.Reset();
        Publish();
    }

    public void DoStep(TimeSpan now, TimeSpan dt)
    {
        foreach (var (coil, port) in _coils) _sim.SetCoil(coil, port.Value > 0.5);
        _sim.Step(dt);
        Publish();
    }

    private void Publish()
    {
        foreach (var (name, port) in _sensors) port.Value = _sim.SensorActive(name) ? 1 : 0;
        foreach (var (gauge, port) in _gauges) port.Value = _sim.PressureAt(gauge, "1");
    }

    public ComponentState Snapshot()
    {
        var v = new Dictionary<string, object>();
        foreach (var c in Circuit.Components.OfType<Cylinder>())
        {
            v[$"{c.Id}.position"] = _sim.Position(c.Id);
            v[$"{c.Id}.velocity"] = _sim.Velocity(c.Id);
        }
        foreach (var valve in Circuit.Components.OfType<DirectionalValve>()) v[$"{valve.Id}.actuated"] = _sim.IsActuated(valve.Id);
        return new ComponentState(Id, TimeSpan.FromSeconds(_sim.Time), v);
    }
}
