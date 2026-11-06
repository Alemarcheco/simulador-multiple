using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Generic;

/// <summary>
/// Base de los equipos genéricos del banco de pruebas: modelos sencillos con parámetros de placa, pensados para
/// combinar dominios mientras cada módulo publica sus propios modelos detallados.
/// </summary>
public abstract class GenericComponent(string id) : ISimComponent
{
    private readonly List<ISignalPort> _signals = [];
    private readonly List<IPowerPort> _power = [];

    public string Id { get; } = id;
    public virtual string ModuleId => BancoModule.ModuleId;
    public IReadOnlyList<ISignalPort> Signals => _signals;
    public IReadOnlyList<IPowerPort> PowerPorts => _power;
    public virtual int StepMultiple => 1;
    public abstract SimPhase Phase { get; }
    /// <summary>Instante al final del último paso.</summary>
    protected TimeSpan Time { get; private set; }
    protected SimContext? Context { get; private set; }

    protected SignalPort Signal(string name, SignalKind kind, SignalUnit unit, double nominal = 0)
    {
        var s = new SignalPort($"{Id}.{name}", kind, unit, nominal);
        _signals.Add(s);
        return s;
    }

    protected PowerPort Power(string name, PowerDomain domain, Causality causality)
    {
        var p = new PowerPort($"{Id}.{name}", domain, causality);
        _power.Add(p);
        return p;
    }

    public virtual void Initialize(SimContext context) => Context = context;

    public void DoStep(TimeSpan now, TimeSpan dt)
    {
        Step(dt.TotalSeconds);
        Time = now + dt;
    }

    protected abstract void Step(double dt);

    public ComponentState Snapshot()
    {
        var v = new Dictionary<string, object>();
        Fill(v);
        return new ComponentState(Id, Time, v);
    }

    protected abstract void Fill(Dictionary<string, object> values);

    public virtual void Reset() => Time = TimeSpan.Zero;
}
