// Contrato: /mnt/project-files/contratos/co-simulacion.md ("Solvers de dominio" y "Causalidad fijada").
using System.Globalization;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Engine.CoSim;

/// <summary>
/// Subsistema "red eléctrica" para el banco de pruebas combinado: un esquema eléctrico entero (mando y potencia)
/// resuelto por su simulador, con un solo componente hacia fuera. Expone solo lo que cruza de dominio:
/// <list type="bullet">
/// <item><c>Id.KM1.bobina</c> (DI, Bool): excita la bobina KM1 desde una salida de PLC del banco.</item>
/// <item><c>Id.KM1.13-14</c> (DO, Volt 24): 24 V mientras ese contacto auxiliar está cerrado (también 95-96, 97-98 del térmico).</item>
/// <item><c>Id.KF1.I0.0</c> / <c>Id.KF1.Q0.0</c>: bornes de los PLC dibujados en el esquema (ver bus-senales.md).</item>
/// <item><c>Id.M1.eje</c> (MechanicalRotational, ImposesFlow): el motor impone la velocidad (Flow, rad/s) y lee el par
/// resistente de la carga (Effort, N·m). La inercia de la carga se pasa con el parámetro "Inercia carga M1".</item>
/// </list>
/// </summary>
public sealed class ElectricalNetwork : ISimComponent
{
    public const string ModuleIdValue = "electrico";

    private readonly Simulator _sim;
    private readonly List<ISignalPort> _signals = [];
    private readonly List<IPowerPort> _power = [];
    private readonly List<(SignalPort Port, string Tag)> _coilInputs = [];
    private readonly List<(SignalPort Port, Guid Element)> _contactOutputs = [];
    private readonly List<(PowerPort Port, Guid Motor, double Inertia)> _shafts = [];

    public ElectricalNetwork(string id, Schematic schematic, IDictionary<string, object>? parameters = null, ElementCatalog? catalog = null)
    {
        Id = id;
        Schematic = schematic;
        _sim = new Simulator(schematic, catalog);
        var tagsWithCoil = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (e, t) in _sim.Elements)
        {
            var tag = string.IsNullOrWhiteSpace(e.Tag) ? null : e.Tag.Trim();
            if (tag is null) continue;
            if (t.IsCoil && tagsWithCoil.Add(tag))
            {
                var p = new SignalPort($"{id}.{tag}.bobina", SignalKind.DI, SignalUnit.Bool, 24);
                _coilInputs.Add((p, tag));
                _signals.Add(p);
            }
            else if (t.Behavior is Behavior.ContactNO or Behavior.ContactNC or Behavior.TimedContactNO or Behavior.TimedContactNC)
            {
                var terminals = e.Text("Bornes", t.DefaultTerminalText);
                var name = $"{id}.{tag}.{terminals}";
                if (_signals.Any(s => s.Id == name)) name += $"#{_contactOutputs.Count + 1}";
                var p = new SignalPort(name, SignalKind.DO, SignalUnit.Volt, 24);
                _contactOutputs.Add((p, e.Id));
                _signals.Add(p);
            }
            else if (t.IsMotor)
            {
                var port = new PowerPort($"{id}.{tag}.eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow);
                var inertia = parameters is not null && parameters.TryGetValue($"Inercia carga {tag}", out var v) ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
                _shafts.Add((port, e.Id, inertia));
                _power.Add(port);
            }
        }
        // Bornes de PLC del esquema, renombrados con el Id del componente delante.
        foreach (var inner in _sim.Signals)
        {
            var outer = new SignalPort($"{id}.{inner.Id}", inner.Kind, inner.Unit, inner.NominalLevel, inner.IsAc, inner.Common);
            if (inner.Kind is SignalKind.DI or SignalKind.AI) outer.Changed += (_, v) => inner.Value = v;
            else inner.Changed += (_, v) => outer.Value = v;
            _signals.Add(outer);
        }
    }

    public string Id { get; }
    public string ModuleId => ModuleIdValue;
    public Schematic Schematic { get; }
    /// <summary>El simulador interno, para vistas o pruebas que quieran accionar pulsadores.</summary>
    public Simulator Simulator => _sim;
    public IReadOnlyList<ISignalPort> Signals => _signals;
    public IReadOnlyList<IPowerPort> PowerPorts => _power;
    public int StepMultiple => 1;
    public SimPhase Phase => SimPhase.Physics;

    public void Initialize(SimContext context)
    {
        foreach (var s in _signals) context.Bus.Register(s);
    }

    public void DoStep(TimeSpan now, TimeSpan dt)
    {
        foreach (var (p, tag) in _coilInputs) _sim.SetCoilCommand(tag, p.Value >= 0.5);
        foreach (var (p, motor, inertia) in _shafts) _sim.SetShaftLoad(motor, Math.Abs(p.Effort), inertia);
        var seconds = dt.TotalSeconds;
        var steps = Math.Max(1, (int)Math.Round(seconds / Simulator.DefaultStep));
        for (var i = 0; i < steps; i++) _sim.Step(seconds / steps);
        foreach (var (p, el) in _contactOutputs)
        {
            var st = _sim.Status(el);
            p.Value = st.Paths.Length > 0 && st.Paths[0] ? 24 : 0;
        }
        foreach (var (p, motor, _) in _shafts) p.Flow = _sim.ShaftSpeed(motor);
    }

    public ComponentState Snapshot()
    {
        var values = new Dictionary<string, object>();
        foreach (var (e, t) in _sim.Elements)
        {
            if (string.IsNullOrWhiteSpace(e.Tag)) continue;
            var st = _sim.Status(e.Id);
            var tag = e.Tag.Trim();
            if (t.IsCoil) values[$"{tag}.{Keys.Actuated}"] = st.Actuated;
            if (t.IsMotor)
            {
                values[$"{tag}.{Keys.Rpm}"] = st.Rpm;
                values[$"{tag}.{Keys.Current}"] = st.Current;
                values[$"{tag}.{Keys.Torque}"] = st.Torque;
            }
            if (t.PoleCount > 0) values[$"{tag}.{Keys.Tripped}"] = st.Tripped;
            if (t.Behavior is Behavior.Lamp or Behavior.Pilot) values[$"{tag}.{Keys.Brightness}"] = st.Brightness;
            if (t.Behavior == Behavior.Vfd) values[$"{tag}.{Keys.Frequency}"] = st.Frequency;
        }
        foreach (var s in _signals) values[s.Id] = s.Value;
        return new ComponentState(Id, TimeSpan.FromSeconds(_sim.Time), values);
    }

    public void Reset()
    {
        _sim.Reset();
        foreach (var (p, _) in _contactOutputs) p.Value = 0;
        foreach (var (p, _, _) in _shafts) { p.Flow = 0; p.Effort = 0; }
    }

    /// <summary>Claves de <see cref="Snapshot"/>, precedidas de la designación ("M1.rpm").</summary>
    public static class Keys
    {
        public const string Actuated = "actuado";
        public const string Rpm = "rpm";
        public const string Current = "corriente";
        public const string Torque = "par";
        public const string Tripped = "disparado";
        public const string Brightness = "brillo";
        public const string Frequency = "frecuencia";
    }
}

/// <summary>
/// Catálogo eléctrico del banco de pruebas: "red_electrica" con un esquema .selx propio, o uno de los ejemplos
/// incluidos ("ejemplo:0", "ejemplo:1"…) para empezar sin dibujar nada.
/// </summary>
public sealed class ElectricalComponentFactory : ISimComponentFactory
{
    public string ModuleId => ElectricalNetwork.ModuleIdValue;

    private static readonly ParameterDescriptor[] CommonParams =
    [
        new("Id", "", "RED1"),
        new("Inercia carga M1", "kg·m²", 0.0),
    ];

    public IEnumerable<ComponentDescriptor> Catalog
    {
        get
        {
            yield return new ComponentDescriptor("red_electrica", "Red eléctrica (esquema .selx)", "🔌", [],
                [new("Esquema", "ruta .selx", ""), .. CommonParams]);
            for (var i = 0; i < Examples.All.Count; i++)
            {
                var net = new ElectricalNetwork("RED", Examples.All[i].Create());
                var ports = net.Signals.Select(s => new PortDescriptor(s.Id["RED.".Length..], s.Kind.ToString(), s.Unit.ToString()))
                    .Concat(net.PowerPorts.Select(p => new PortDescriptor(p.Id["RED.".Length..], p.Domain.ToString(), "rad/s · N·m")))
                    .ToList();
                yield return new ComponentDescriptor($"ejemplo:{i}", Examples.All[i].Name, "🔌", ports, CommonParams);
            }
        }
    }

    public ISimComponent Create(string typeId, IDictionary<string, object> parameters)
    {
        var id = parameters.TryGetValue("Id", out var v) && v is string s && s.Length > 0 ? s : "RED1";
        Schematic schematic;
        if (typeId.StartsWith("ejemplo:", StringComparison.Ordinal)
            && int.TryParse(typeId["ejemplo:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0 && index < Examples.All.Count)
            schematic = Examples.All[index].Create();
        else if (typeId == "red_electrica" && parameters.TryGetValue("Esquema", out var p) && p is string path && path.Length > 0)
            schematic = path.TrimStart().StartsWith('{') ? Schematic.FromJson(path) : Schematic.FromJson(File.ReadAllText(path));
        else
            throw new ArgumentException($"Tipo eléctrico desconocido o sin esquema: {typeId}.");
        return new ElectricalNetwork(id, schematic, parameters);
    }
}
