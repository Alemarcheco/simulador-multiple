// Contrato: /mnt/project-files/contratos/co-simulacion.md (responsable: hilo "Integrar simulaciones en un entorno combinado").
using Simulador.Core.Signals;

namespace Simulador.Core.CoSim;

/// <summary>
/// Reloj común y planificador del banco de pruebas.
/// <list type="bullet">
/// <item>Paso maestro fijo (1 ms por defecto). Nunca usa el reloj de pared.</item>
/// <item>Cada componente se ejecuta cada <see cref="ISimComponent.StepMultiple"/> pasos maestros,
/// en orden de fase (Inputs, Logic, Physics, Outputs) y, dentro de cada fase, en orden de registro.</item>
/// <item><see cref="RequestEventAt"/> acorta el paso para caer exactamente en el instante pedido;
/// en ese instante se ejecutan todos los componentes (punto de sincronización).</item>
/// <item>Los puertos de potencia enlazados se intercambian justo después de que su dueño avance:
/// el lado que impone esfuerzo copia el esfuerzo al otro, y el que impone flujo copia el flujo.</item>
/// </list>
/// Determinista: mismo montaje y mismas entradas dan el mismo resultado.
/// No es seguro entre hilos: úsalo desde un solo hilo (ver <see cref="SimRunner"/>).
/// </summary>
public sealed class CoSimScheduler : ISimulationClock
{
    private sealed class Entry(ISimComponent component, int order)
    {
        public ISimComponent Component { get; } = component;
        public int Order { get; } = order;
        public TimeSpan LastTime;
        public TimeSpan NextDue;
    }

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<IPowerPort, List<IPowerPort>> _powerLinks = [];
    private readonly SortedSet<TimeSpan> _events = [];
    private Entry[] _ordered = [];
    private bool _initialized;

    public CoSimScheduler(TimeSpan? step = null, ISignalBus? bus = null,
        IReadOnlyDictionary<string, FluidProperties>? fluids = null)
    {
        Step = step ?? TimeSpan.FromMilliseconds(1);
        if (Step <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(step), "El paso debe ser positivo.");
        Bus = bus ?? new SignalBus();
        Context = new SimContext
        {
            Clock = this,
            Bus = Bus,
            Fluids = fluids ?? new Dictionary<string, FluidProperties>(),
        };
    }

    public TimeSpan Now { get; private set; }
    public TimeSpan Step { get; }
    public ISignalBus Bus { get; }
    public SimContext Context { get; }
    public long TickCount { get; private set; }
    public IReadOnlyList<ISimComponent> Components => _entries.Select(e => e.Component).ToList();

    public event EventHandler<TimeSpan>? Tick;

    /// <summary>Añade un componente y registra sus señales en el bus.</summary>
    public void Add(ISimComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.StepMultiple < 1)
            throw new ArgumentException($"{component.Id}: StepMultiple debe ser 1 o más.", nameof(component));
        if (_byId.ContainsKey(component.Id))
            throw new InvalidOperationException($"Ya hay un componente con Id '{component.Id}'.");

        var entry = new Entry(component, _entries.Count) { LastTime = Now, NextDue = Now };
        _entries.Add(entry);
        _byId[component.Id] = entry;
        foreach (var s in component.Signals) Bus.Register(s);
        _ordered = [.. _entries.OrderBy(e => (int)e.Component.Phase).ThenBy(e => e.Order)];
        if (_initialized) InitializeEntry(entry);
    }

    public ISimComponent Get(string id) => _byId[id].Component;

    /// <summary>
    /// Enlaza dos puertos de potencia. Deben ser del mismo dominio y de causalidad opuesta:
    /// uno impone esfuerzo y el otro flujo. Devuelve avisos si no, pero no enlaza.
    /// </summary>
    public ConnectionResult Connect(IPowerPort a, IPowerPort b)
    {
        var w = new List<string>();
        if (a.Domain != b.Domain) w.Add($"Dominios distintos: {a.Domain} ↔ {b.Domain}.");
        if (a.Causality == b.Causality)
            w.Add($"Causalidad incompatible: {a.Id} y {b.Id} imponen ambos {(a.Causality == Causality.ImposesEffort ? "esfuerzo" : "flujo")}.");
        if (w.Count > 0) return new ConnectionResult(false, w);

        Link(a, b);
        Link(b, a);
        Transfer(a);
        Transfer(b);
        return ConnectionResult.Ok;
    }

    public void Disconnect(IPowerPort a, IPowerPort b)
    {
        Unlink(a, b);
        Unlink(b, a);
    }

    /// <summary>Atajo para conectar señales: <c>Connect("PLC1.Q0.0", "KM1.A1")</c>.</summary>
    public ConnectionResult Connect(string sourceSignalId, string targetSignalId)
    {
        var src = Bus.Ports.First(p => p.Id == sourceSignalId);
        var dst = Bus.Ports.First(p => p.Id == targetSignalId);
        return Bus.Connect(src, dst);
    }

    public void RequestEventAt(TimeSpan time)
    {
        if (time > Now) _events.Add(time);
    }

    /// <summary>Llama a Initialize en todos los componentes. Se hace solo en el primer paso.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        foreach (var e in _ordered) InitializeEntry(e);
    }

    /// <summary>
    /// Avanza un paso maestro (o hasta el próximo evento, si cae antes).
    /// Mientras los componentes avanzan, <see cref="Now"/> sigue siendo el inicio del paso.
    /// </summary>
    public void StepOnce()
    {
        Initialize();

        var target = Now + Step;
        bool isEvent = false;
        if (_events.Count > 0 && _events.Min <= target)
        {
            target = _events.Min;
            _events.Remove(target);
            isEvent = true;
        }

        foreach (var e in _ordered)
        {
            if (!isEvent && target < e.NextDue) continue;
            var dt = target - e.LastTime;
            if (dt <= TimeSpan.Zero) continue;
            e.Component.DoStep(e.LastTime, dt);
            e.LastTime = target;
            e.NextDue = target + Step * e.Component.StepMultiple;
            foreach (var port in e.Component.PowerPorts) Transfer(port);
        }

        Now = target;
        TickCount++;
        Tick?.Invoke(this, Now);
    }

    /// <summary>Avanza hasta <paramref name="time"/> (incluido).</summary>
    public void RunUntil(TimeSpan time)
    {
        while (Now < time)
        {
            if (Now + Step > time) RequestEventAt(time);
            StepOnce();
        }
    }

    public void RunFor(TimeSpan duration) => RunUntil(Now + duration);

    /// <summary>Instantáneas de todos los componentes, para animar o graficar.</summary>
    public IReadOnlyList<ComponentState> Snapshot() => _entries.Select(e => e.Component.Snapshot()).ToList();

    /// <summary>Vuelve a t = 0 y reinicia todos los componentes.</summary>
    public void Reset()
    {
        Now = TimeSpan.Zero;
        TickCount = 0;
        _events.Clear();
        foreach (var e in _entries)
        {
            e.Component.Reset();
            e.LastTime = TimeSpan.Zero;
            e.NextDue = TimeSpan.Zero;
        }
        _initialized = false;
    }

    private void InitializeEntry(Entry e)
    {
        e.Component.Initialize(Context);
        e.LastTime = Now;
        e.NextDue = Now;
    }

    private void Link(IPowerPort from, IPowerPort to)
    {
        if (!_powerLinks.TryGetValue(from, out var list)) _powerLinks[from] = list = [];
        if (!list.Contains(to)) list.Add(to);
    }

    private void Unlink(IPowerPort from, IPowerPort to)
    {
        if (_powerLinks.TryGetValue(from, out var list) && list.Remove(to) && list.Count == 0)
            _powerLinks.Remove(from);
    }

    private void Transfer(IPowerPort port)
    {
        if (!_powerLinks.TryGetValue(port, out var peers)) return;
        foreach (var peer in peers)
        {
            if (port.Causality == Causality.ImposesEffort) peer.Effort = port.Effort;
            else peer.Flow = port.Flow;
        }
    }
}
