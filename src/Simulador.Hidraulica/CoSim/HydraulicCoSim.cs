// Contrato: /mnt/project-files/contratos/co-simulacion.md, sección "Ajustes acordados con hidráulica".
using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.CoSim;

/// <summary>Claves de <see cref="ComponentState"/> que publica cada pieza hidráulica.</summary>
public static class HydraulicKeys
{
    public const string Level = "level";          // m (tanque)
    public const string LevelRatio = "levelRatio"; // 0–1 (tanque)
    public const string Volume = "volume";        // m³ (tanque)
    public const string Overflow = "overflow";    // m³/s (tanque)
    public const string Pressure = "pressure";    // Pa (nudo, tanque: fondo)
    public const string Head = "head";            // m (nudo) o altura de bomba
    public const string Flow = "flow";            // m³/s (enlace)
    public const string Velocity = "velocity";    // m/s (tubería, válvula)
    public const string Opening = "opening";      // 0–1 (válvula)
    public const string Speed = "speed";          // rad/s (bomba)
    public const string Torque = "torque";        // N·m (bomba)
    public const string Power = "power";          // W en el eje (bomba)
    public const string Running = "running";      // bool (bomba)
    public const string Value = "value";          // medida del sensor (SI)
}

/// <summary>Puerto de potencia sencillo (eje de bomba, vástago de cilindro).</summary>
public sealed class PowerPort(string id, PowerDomain domain, Causality causality) : IPowerPort
{
    public string Id { get; } = id;
    public PowerDomain Domain { get; } = domain;
    public double Effort { get; set; }
    public double Flow { get; set; }
    public Causality Causality { get; } = causality;
}

/// <summary>
/// Una pieza del catálogo hidráulico en el banco de pruebas. No avanza sola: la resuelve la
/// <see cref="RedHidraulica"/> que la contiene, y su <see cref="Snapshot"/> lee el estado de esa red.
/// </summary>
public sealed class HydraulicPart : ISimComponent
{
    /// <summary>Puertos de tubería (no son IPowerPort: dentro de una red se unen por nombre).</summary>
    public const string Inlet = "Entrada", Outlet = "Salida", Mouth = "Boca";

    private readonly List<ISignalPort> _signals = [];
    private readonly List<IPowerPort> _power = [];

    public string Id { get; }
    public string ModuleId => HidraulicaModule.ModuleId;
    /// <summary>Nudo, enlace o <see cref="Sensor"/> del modelo.</summary>
    public object Element { get; }
    public RedHidraulica? Network { get; internal set; }

    public IReadOnlyList<ISignalPort> Signals => _signals;
    public IReadOnlyList<IPowerPort> PowerPorts => _power;
    public int StepMultiple => RedHidraulica.DefaultStepMultiple;
    public SimPhase Phase => SimPhase.Physics;

    /// <summary>Eje de la bomba: el motor impone la velocidad (Flow, rad/s) y la bomba devuelve el par (Effort, N·m).</summary>
    public PowerPort? Shaft { get; }
    /// <summary>Marcha de la bomba (DI Bool) cuando no la acciona un motor del banco.</summary>
    public SignalPort? RunCommand { get; }
    /// <summary>Consigna de apertura 0–100 % de una válvula de control (la escribe una AO del PLC).</summary>
    public SignalPort? OpeningCommand { get; }
    /// <summary>Bobina de una electroválvula (DI Bool).</summary>
    public SignalPort? Coil { get; }
    /// <summary>Medida del instrumento (AO en Pascal, Meter o CubicMeterPerSecond).</summary>
    public SignalPort? Measurement { get; }

    /// <summary>true cuando el eje está acoplado a un motor en el banco; entonces se ignora <see cref="RunCommand"/>.</summary>
    public bool ShaftConnected { get; set; }

    public HydraulicPart(object element)
    {
        Element = element;
        switch (element)
        {
            case HydraulicNode n: Id = n.Id; break;
            case Pump p:
                Id = p.Id;
                Shaft = Add(new PowerPort($"{Id}.Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort));
                RunCommand = Add(new SignalPort($"{Id}.Marcha", SignalKind.DI, SignalUnit.Bool));
                RunCommand.Value = p.InitiallyOn ? 1 : 0;
                break;
            case Valve { Kind: ValveKind.Control } v:
                Id = v.Id;
                OpeningCommand = Add(new SignalPort($"{Id}.Consigna", SignalKind.AI, SignalUnit.Percent));
                OpeningCommand.Value = v.InitialOpening * 100;
                break;
            case Valve { Kind: ValveKind.Solenoid } v:
                Id = v.Id;
                Coil = Add(new SignalPort($"{Id}.Bobina", SignalKind.DI, SignalUnit.Bool));
                Coil.Value = v.InitialOpening > 0.5 ? 1 : 0;
                break;
            case HydraulicLink l: Id = l.Id; break;
            case Sensor s:
                Id = s.Id;
                var unit = s.Kind switch
                {
                    SensorKind.Pressure => SignalUnit.Pascal,
                    SensorKind.Level => SignalUnit.Meter,
                    _ => SignalUnit.CubicMeterPerSecond,
                };
                Measurement = Add(new SignalPort($"{Id}.Medida", SignalKind.AO, unit));
                break;
            default: throw new ArgumentException($"Elemento no válido: {element.GetType().Name}");
        }
    }

    private T Add<T>(T port) where T : class
    {
        if (port is ISignalPort s) _signals.Add(s);
        if (port is IPowerPort p) _power.Add(p);
        return port;
    }

    public void Initialize(SimContext context) { foreach (var s in _signals) context.Bus.Register(s); }
    public void DoStep(TimeSpan now, TimeSpan dt) { }
    public void Reset() { }

    public ComponentState Snapshot() =>
        Network?.PartSnapshot(this) ?? new ComponentState(Id, TimeSpan.Zero, new Dictionary<string, object>());
}

/// <summary>
/// Solver de una red de tuberías conectada (subsistema del banco de pruebas, fase Physics, cada 10 ms).
/// Contiene todas sus piezas y solo expone ejes de bomba y señales.
/// </summary>
public sealed class RedHidraulica : ISimComponent
{
    public const int DefaultStepMultiple = 10;

    private readonly List<HydraulicPart> _parts;
    private HydraulicSimulation _sim;

    public string Id { get; }
    public string ModuleId => HidraulicaModule.ModuleId;
    public HydraulicNetwork Model { get; }
    public HydraulicSimulation Simulation => _sim;
    public IReadOnlyList<HydraulicPart> Parts => _parts;
    public IReadOnlyList<ISignalPort> Signals { get; }
    public IReadOnlyList<IPowerPort> PowerPorts { get; }
    public int StepMultiple { get; set; } = DefaultStepMultiple;
    public SimPhase Phase => SimPhase.Physics;

    /// <summary>Crea el solver para un diseño hecho en la pestaña Hidráulica.</summary>
    public RedHidraulica(string id, HydraulicNetwork network, IEnumerable<HydraulicPart>? parts = null)
    {
        Id = id;
        Model = network;
        _parts = parts?.ToList() ?? network.Nodes.Cast<object>().Concat(network.Links).Concat(network.Sensors)
            .Select(e => new HydraulicPart(e)).ToList();
        foreach (var p in _parts) p.Network = this;
        Signals = _parts.SelectMany(p => p.Signals).ToList();
        PowerPorts = _parts.SelectMany(p => p.PowerPorts).ToList();
        _sim = new HydraulicSimulation(network);
    }

    public HydraulicPart Part(string id) => _parts.First(p => p.Id == id);

    public void Initialize(SimContext context)
    {
        if (context.Fluids.TryGetValue(Id, out var f) || context.Fluids.TryGetValue(Model.Name, out f))
            // FluidProperties.Viscosity se interpreta como dinámica (Pa·s, SI); el modelo usa la cinemática.
            Model.Fluid = new Fluid(Model.Fluid.Name, f.Density, f.Viscosity / f.Density, f.BulkModulus);
        foreach (var s in Signals) context.Bus.Register(s);
        Reset();
    }

    public void Reset()
    {
        _sim.Reset();
        PublishOutputs();
    }

    public void DoStep(TimeSpan now, TimeSpan dt)
    {
        foreach (var part in _parts)
            switch (part.Element)
            {
                case Pump p when part.ShaftConnected: _sim.SetPumpShaftSpeed(p.Id, part.Shaft!.Flow); break;
                case Pump p: _sim.SetPumpRunning(p.Id, part.RunCommand!.Value > 0.5); break;
                case Valve { Kind: ValveKind.Control } v: _sim.SetValveOpening(v.Id, part.OpeningCommand!.Value / 100); break;
                case Valve { Kind: ValveKind.Solenoid } v: _sim.SetValveOpening(v.Id, part.Coil!.Value > 0.5 ? 1 : 0); break;
            }
        _sim.Step(dt);
        PublishOutputs();
    }

    private void PublishOutputs()
    {
        foreach (var part in _parts)
        {
            if (part.Element is Pump p)
            {
                part.Shaft!.Effort = _sim.PumpTorque(p.Id);
                if (!part.ShaftConnected) part.Shaft.Flow = p.RelativeSpeed * p.RatedSpeed;
            }
            if (part.Element is Sensor s) part.Measurement!.Value = _sim.SensorValue(s);
        }
    }

    /// <summary>Estado de toda la red con claves "Pieza.clave".</summary>
    public ComponentState Snapshot()
    {
        var values = new Dictionary<string, object>();
        foreach (var part in _parts)
            foreach (var (k, v) in PartSnapshot(part).Values)
                values[$"{part.Id}.{k}"] = v;
        return new ComponentState(Id, _sim.Time, values);
    }

    internal ComponentState PartSnapshot(HydraulicPart part)
    {
        var v = new Dictionary<string, object>();
        switch (part.Element)
        {
            case Tank t:
                v[HydraulicKeys.Level] = t.Level;
                v[HydraulicKeys.LevelRatio] = t.MaxLevel > 0 ? t.Level / t.MaxLevel : 0;
                v[HydraulicKeys.Volume] = t.Volume;
                v[HydraulicKeys.Overflow] = _sim.Overflow[_sim.NodeIndex(t.Id)];
                v[HydraulicKeys.Pressure] = _sim.PressureAt(t.Id);
                break;
            case HydraulicNode n:
                v[HydraulicKeys.Pressure] = _sim.PressureAt(n.Id);
                v[HydraulicKeys.Head] = _sim.HeadAt(n.Id);
                break;
            case Pump p:
                v[HydraulicKeys.Flow] = _sim.FlowIn(p.Id);
                v[HydraulicKeys.Head] = _sim.PumpHead(p.Id);
                v[HydraulicKeys.Speed] = p.RelativeSpeed * p.RatedSpeed;
                v[HydraulicKeys.Torque] = _sim.PumpTorque(p.Id);
                v[HydraulicKeys.Power] = _sim.PumpShaftPower(p.Id);
                v[HydraulicKeys.Running] = p.RelativeSpeed > 0.01;
                break;
            case Valve va:
                v[HydraulicKeys.Flow] = _sim.FlowIn(va.Id);
                v[HydraulicKeys.Velocity] = _sim.VelocityIn(va.Id);
                v[HydraulicKeys.Opening] = va.Opening;
                break;
            case HydraulicLink l:
                v[HydraulicKeys.Flow] = _sim.FlowIn(l.Id);
                v[HydraulicKeys.Velocity] = _sim.VelocityIn(l.Id);
                break;
            case Sensor s:
                v[HydraulicKeys.Value] = _sim.SensorValue(s);
                break;
        }
        return new ComponentState(part.Id, _sim.Time, v);
    }

    /// <summary>Unión entre dos bocas de piezas en el banco: ("B1", "Salida") ↔ ("P1", "Entrada").</summary>
    public sealed record PipeConnection(string PartA, string PortA, string PartB, string PortB);

    /// <summary>
    /// Agrupa piezas sueltas del banco en una red. Las bocas unidas entre sí forman un nudo; si en el grupo hay un
    /// tanque, depósito o nudo, ese es el nudo; si no, se crea una unión implícita.
    /// </summary>
    public static RedHidraulica Assemble(string id, IReadOnlyList<HydraulicPart> parts, IEnumerable<PipeConnection> connections)
    {
        var parent = new Dictionary<(string, string), (string, string)>();
        (string, string) Find((string, string) x)
        {
            if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; }
            return p == x ? x : parent[x] = Find(p);
        }
        // Todas las bocas de un mismo nudo son el mismo punto.
        foreach (var part in parts) Find((part.Id, HydraulicPart.Mouth));
        foreach (var c in connections)
        {
            var a = Find((c.PartA, Normalize(parts, c.PartA, c.PortA)));
            var b = Find((c.PartB, Normalize(parts, c.PartB, c.PortB)));
            if (a != b) parent[a] = b;
        }

        var net = new HydraulicNetwork { Name = id };
        var nodeOfGroup = new Dictionary<(string, string), string>();
        foreach (var part in parts.Where(p => p.Element is HydraulicNode))
        {
            net.Nodes.Add((HydraulicNode)part.Element);
            nodeOfGroup[Find((part.Id, HydraulicPart.Mouth))] = part.Id;
        }
        string NodeFor(string partId, string port)
        {
            var root = Find((partId, port));
            if (!nodeOfGroup.TryGetValue(root, out var nodeId))
            {
                nodeId = net.NextId("U");
                net.Nodes.Add(new Junction { Id = nodeId });
                nodeOfGroup[root] = nodeId;
            }
            return nodeId;
        }
        foreach (var part in parts.Where(p => p.Element is HydraulicLink))
        {
            var link = (HydraulicLink)part.Element;
            link.From = NodeFor(part.Id, HydraulicPart.Inlet);
            link.To = NodeFor(part.Id, HydraulicPart.Outlet);
            net.Links.Add(link);
        }
        foreach (var part in parts.Where(p => p.Element is Sensor)) net.Sensors.Add((Sensor)part.Element);
        return new RedHidraulica(id, net, parts);
    }

    private static string Normalize(IReadOnlyList<HydraulicPart> parts, string partId, string port) =>
        parts.FirstOrDefault(p => p.Id == partId)?.Element is HydraulicNode ? HydraulicPart.Mouth : port;
}

/// <summary>Catálogo hidráulico para el banco de pruebas combinado.</summary>
public sealed class HydraulicComponentFactory : ISimComponentFactory
{
    public string ModuleId => HidraulicaModule.ModuleId;

    private static readonly PortDescriptor In = new(HydraulicPart.Inlet, "Hydraulic", "Pa / m³/s");
    private static readonly PortDescriptor Out = new(HydraulicPart.Outlet, "Hydraulic", "Pa / m³/s");
    private static readonly PortDescriptor Mouth = new(HydraulicPart.Mouth, "Hydraulic", "Pa / m³/s");

    public IEnumerable<ComponentDescriptor> Catalog { get; } =
    [
        new("deposito", "Depósito de nivel constante", "🌊", [Mouth], [new("cota", "m", 0.0), new("nivel", "m", 2.0)]),
        new("tanque", "Tanque", "🛢", [Mouth], [new("cota", "m", 0.0), new("diametro", "m", 2.0), new("alturaMax", "m", 3.0), new("nivelInicial", "m", 1.0)]),
        new("nudo", "Unión / consumo", "•", [Mouth], [new("cota", "m", 0.0), new("consumo", "m³/s", 0.0)]),
        new("tuberia", "Tubería", "━", [In, Out], [new("longitud", "m", 10.0), new("diametro", "m", 0.05), new("rugosidad", "mm", 0.0015), new("perdidasLocales", "", 0.0)]),
        new("bomba", "Bomba centrífuga", "⟳", [In, Out, new("Eje", "MechanicalRotational", "N·m / rad/s"), new("Marcha", "DI", "Bool")],
            [new("alturaCero", "m", 30.0), new("caudalNominal", "m³/s", 0.005), new("alturaNominal", "m", 22.0), new("velocidadNominal", "rad/s", 303.7), new("rendimiento", "", 0.65), new("inercia", "kg·m²", 0.01)]),
        new("valvula", "Válvula manual", "⋈", [In, Out], [new("diametro", "m", 0.05), new("apertura", "0–1", 1.0)]),
        new("valvula-control", "Válvula de control", "⋈", [In, Out, new("Consigna", "AI", "Percent")], [new("diametro", "m", 0.05), new("tiempoRecorrido", "s", 10.0)]),
        new("electrovalvula", "Electroválvula", "⋈", [In, Out, new("Bobina", "DI", "Bool")], [new("diametro", "m", 0.05)]),
        new("antirretorno", "Válvula antirretorno", "▷", [In, Out], [new("diametro", "m", 0.05)]),
        new("sensor-presion", "Transmisor de presión", "P", [new("Medida", "AO", "Pascal")], [new("objetivo", "", "")]),
        new("sensor-nivel", "Transmisor de nivel", "L", [new("Medida", "AO", "Meter")], [new("objetivo", "", "")]),
        new("caudalimetro", "Caudalímetro", "F", [new("Medida", "AO", "CubicMeterPerSecond")], [new("objetivo", "", "")]),
    ];

    public ISimComponent Create(string typeId, IDictionary<string, object> parameters)
    {
        double D(string key, double fallback) =>
            parameters.TryGetValue(key, out var v) ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        string id = parameters.TryGetValue("id", out var idv) ? Convert.ToString(idv) ?? typeId : typeId;
        string target = parameters.TryGetValue("objetivo", out var t) ? Convert.ToString(t) ?? "" : "";

        object element = typeId switch
        {
            "deposito" => new Reservoir { Id = id, Elevation = D("cota", 0), Level = D("nivel", 2) },
            "tanque" => new Tank { Id = id, Elevation = D("cota", 0), Diameter = D("diametro", 2), MaxLevel = D("alturaMax", 3), InitialLevel = D("nivelInicial", 1) },
            "nudo" => new Junction { Id = id, Elevation = D("cota", 0), Demand = D("consumo", 0) },
            "tuberia" => new Pipe { Id = id, Length = D("longitud", 10), Diameter = D("diametro", 0.05), Roughness = D("rugosidad", 0.0015), MinorLoss = D("perdidasLocales", 0) },
            "bomba" => new Pump
            {
                Id = id, ShutoffHead = D("alturaCero", 30), RatedFlow = D("caudalNominal", 0.005), RatedHead = D("alturaNominal", 22),
                RatedSpeed = D("velocidadNominal", 303.7), Efficiency = D("rendimiento", 0.65), Inertia = D("inercia", 0.01), InitiallyOn = false,
            },
            "valvula" => new Valve { Id = id, Diameter = D("diametro", 0.05), InitialOpening = D("apertura", 1) },
            "valvula-control" => new Valve { Id = id, Kind = ValveKind.Control, Diameter = D("diametro", 0.05), StrokeTime = D("tiempoRecorrido", 10), InitialOpening = 0 },
            "electrovalvula" => new Valve { Id = id, Kind = ValveKind.Solenoid, Diameter = D("diametro", 0.05), InitialOpening = 0 },
            "antirretorno" => new Valve { Id = id, Kind = ValveKind.Check, Diameter = D("diametro", 0.05) },
            "sensor-presion" => new Sensor { Id = id, Kind = SensorKind.Pressure, Target = target },
            "sensor-nivel" => new Sensor { Id = id, Kind = SensorKind.Level, Target = target },
            "caudalimetro" => new Sensor { Id = id, Kind = SensorKind.Flow, Target = target },
            _ => throw new ArgumentException($"Tipo hidráulico desconocido: '{typeId}'."),
        };
        return new HydraulicPart(element);
    }
}
