using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Scenarios;

/// <summary>Una línea de la lista de conexiones: lo que se vería en el esquema de bornes o en el P&amp;ID.</summary>
public sealed record WireEntry(string Kind, string Terminal, string From, string To, string Detail);

/// <summary>Una magnitud que se dibuja en la gráfica de tendencias, en % de su escala.</summary>
public sealed record TrendSeries(string Name, Func<SimFrame, double> Percent);

/// <summary>Un mando que el usuario mueve durante la simulación (deslizador).</summary>
public sealed record ScenarioSlider(string Name, string Unit, double Min, double Max, double Initial, Action<double> Apply);

/// <summary>Un botón de maniobra o de fallo.</summary>
public sealed record ScenarioAction(string Name, Action Run);

/// <summary>
/// Un montaje del banco de pruebas: crea los componentes de cada módulo, los cablea con el bus de señales y los
/// puertos de potencia, y dice qué mostrar (conexiones, tendencias, mandos).
/// </summary>
public abstract class Scenario
{
    private readonly List<WireEntry> _wiring = [];

    protected Scenario()
    {
        Scheduler = new CoSimScheduler(fluids: new Dictionary<string, FluidProperties>
        {
            ["agua"] = new(Density: 998, Viscosity: 1.0e-3, BulkModulus: 2.2e9),
        });
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public CoSimScheduler Scheduler { get; }
    public IReadOnlyList<WireEntry> Wiring => _wiring;
    public List<TrendSeries> Trends { get; } = [];
    public List<ScenarioSlider> Sliders { get; } = [];
    public List<ScenarioAction> Actions { get; } = [];

    /// <summary>Lo que hay que hacer tras reiniciar (p. ej. poner el PLC en RUN).</summary>
    public virtual void AfterReset() { }

    /// <summary>Une una salida con una entrada a través de un borne y lo apunta en la lista de conexiones.</summary>
    protected void Wire(string terminal, ISignalPort from, ISignalPort to, string detail = "")
    {
        var r = Scheduler.Bus.Connect(from, to);
        _wiring.Add(new WireEntry("Señal", terminal, from.Id, to.Id,
            r.Warnings.Count > 0 ? $"{detail} ⚠ {string.Join(" ", r.Warnings)}".Trim() : detail));
    }

    /// <summary>Enlaza dos puertos de potencia (cable, eje, tubería de aire) y lo apunta.</summary>
    protected void Link(string kind, string label, IPowerPort a, IPowerPort b, string detail = "")
    {
        var r = Scheduler.Connect(a, b);
        if (!r.Connected) throw new InvalidOperationException($"{a.Id} ↔ {b.Id}: {string.Join(" ", r.Warnings)}");
        _wiring.Add(new WireEntry(kind, label, a.Id, b.Id, detail));
    }

    /// <summary>Apunta una tubería que resuelve la red hidráulica (no pasa por el bus).</summary>
    protected void Pipe(string label, string from, string to, string detail) =>
        _wiring.Add(new WireEntry("Tubería", label, from, to, detail));

    protected static double Get(SimFrame f, string component, string key) =>
        f[component] is { } s && s.Values.TryGetValue(key, out var v) ? Convert.ToDouble(v) : 0;

    protected static bool GetBool(SimFrame f, string component, string key) =>
        f[component] is { } s && s.Values.TryGetValue(key, out var v) && v is true;

    /// <summary>Todos los montajes de ejemplo, en el orden del selector.</summary>
    public static IReadOnlyList<(string Name, Func<Scenario> Create)> All { get; } =
    [
        ("Llenado de tanque: PLC, contactor, motor y bomba", () => new TankFillScenario()),
        ("Llenado de tanque con esquema eléctrico real (módulo Eléctrico)", () => new TankFillElectricScenario()),
        ("Turbogenerador en isla con regulador de velocidad", () => new TurboGeneratorScenario()),
        ("Turbocompresor y depósito de aire controlados por PLC", () => new TurboCompressorScenario()),
    ];
}
