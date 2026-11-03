// Contrato: /mnt/project-files/contratos/co-simulacion.md (responsable: hilo "Integrar simulaciones en un entorno combinado").
// Proponer cambios a ese hilo antes de modificar estos tipos.
using Simulador.Core.Signals;

namespace Simulador.Core.CoSim;

/// <summary>Todo lo que se coloca en el banco de pruebas combinado.</summary>
public interface ISimComponent
{
    /// <summary>Identificador en el banco, p. ej. "KM1", "M1", "PCB1".</summary>
    string Id { get; }
    /// <summary>Módulo que lo aporta: "circuitos", "electrico", "plc", "hidraulica"…</summary>
    string ModuleId { get; }
    IReadOnlyList<ISignalPort> Signals { get; }
    IReadOnlyList<IPowerPort> PowerPorts { get; }
    /// <summary>Se ejecuta cada N ticks del reloj común.</summary>
    int StepMultiple { get; }
    SimPhase Phase { get; }

    void Initialize(SimContext context);
    /// <summary>Avanza de <c>now</c> a <c>now + dt</c>. Puede usar subpasos internos.</summary>
    void DoStep(TimeSpan now, TimeSpan dt);
    /// <summary>Estado para animar la vista; la vista nunca toca el estado interno.</summary>
    ComponentState Snapshot();
    void Reset();
}

/// <summary>Cada módulo registra la suya para ofrecer su catálogo al banco de pruebas.</summary>
public interface ISimComponentFactory
{
    string ModuleId { get; }
    IEnumerable<ComponentDescriptor> Catalog { get; }
    ISimComponent Create(string typeId, IDictionary<string, object> parameters);
}

public sealed record PortDescriptor(string Name, string Kind, string Unit);

public sealed record ParameterDescriptor(string Name, string Unit, object? DefaultValue);

public sealed record ComponentDescriptor(
    string TypeId,
    string Name,
    string Icon,
    IReadOnlyList<PortDescriptor> Ports,
    IReadOnlyList<ParameterDescriptor> Parameters);

/// <summary>Propiedades de un fluido por red (hidráulica/neumática).</summary>
public sealed record FluidProperties(
    double Density, double Viscosity, double BulkModulus, double? GasConstant = null, double? HeatCapacityRatio = null);

/// <summary>Contexto que el banco de pruebas pasa a cada componente al iniciar.</summary>
public sealed class SimContext
{
    public required ISimulationClock Clock { get; init; }
    public required ISignalBus Bus { get; init; }
    /// <summary>Fluidos por nombre de red.</summary>
    public IReadOnlyDictionary<string, FluidProperties> Fluids { get; init; } = new Dictionary<string, FluidProperties>();
}

/// <summary>Instantánea de estado con claves string (cada módulo publica sus claves como constantes).</summary>
public sealed class ComponentState(string componentId, TimeSpan time, IReadOnlyDictionary<string, object> values)
{
    public string ComponentId { get; } = componentId;
    public TimeSpan Time { get; } = time;
    public IReadOnlyDictionary<string, object> Values { get; } = values;

    public T Get<T>(string key, T fallback = default!) =>
        Values.TryGetValue(key, out var v) && v is T t ? t : fallback;
}
