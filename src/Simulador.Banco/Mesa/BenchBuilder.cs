using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Hidraulica.CoSim;
using Simulador.Hidraulica.Model;

namespace Simulador.Banco.Mesa;

/// <summary>Un montaje de la mesa listo para simular, con los avisos que salieron al montarlo.</summary>
public sealed class BuiltBench
{
    private readonly Dictionary<ISimComponent, int> _index = [];

    internal BuiltBench(BenchProject project, CoSimScheduler scheduler, Dictionary<string, BuiltPart> parts,
        List<RedHidraulica> networks, List<BenchIssue> issues)
    {
        Project = project;
        Scheduler = scheduler;
        Parts = parts;
        Networks = networks;
        Issues = issues;
        var comps = scheduler.Components;
        for (int i = 0; i < comps.Count; i++) _index[comps[i]] = i;
    }

    public BenchProject Project { get; }
    public CoSimScheduler Scheduler { get; }
    public IReadOnlyDictionary<string, BuiltPart> Parts { get; }
    public IReadOnlyList<RedHidraulica> Networks { get; }
    public List<BenchIssue> Issues { get; }
    public bool CanRun => Issues.All(i => i.Level != IssueLevel.Error) && Parts.Count > 0;

    public IEnumerable<InstrumentComponent> Instruments => Parts.Values.Select(p => p.Model).OfType<InstrumentComponent>();

    /// <summary>Los valores de una pieza dentro de una instantánea del banco.</summary>
    public PartReader Reader(SimFrame? frame, string partId)
    {
        if (frame is null || !Parts.TryGetValue(partId, out var bp) || bp.SnapshotSource is null) return PartReader.Empty;
        if (!_index.TryGetValue(bp.SnapshotSource, out int i) || i >= frame.Components.Count) return PartReader.Empty;
        return new PartReader(frame.Components[i], bp.SnapshotPrefix);
    }

    /// <summary>Lo que hay que hacer tras reiniciar: poner los PLC en RUN, recargar los programas de las placas…</summary>
    public void AfterReset()
    {
        foreach (var p in Parts.Values) p.AfterReset?.Invoke();
    }
}

/// <summary>
/// Convierte un <see cref="BenchProject"/> en un banco que se puede simular: crea cada pieza con la fábrica de su
/// módulo, agrupa las tuberías en redes hidráulicas, une señales y potencias, conecta las sondas de los
/// instrumentos y apunta todo lo que esté mal o falte, explicado.
/// </summary>
public static class BenchBuilder
{
    public static readonly FluidProperties Water = new(Density: 998, Viscosity: 1.0e-3, BulkModulus: 2.2e9);

    public static BuiltBench Build(BenchProject project)
    {
        var issues = new List<BenchIssue>();
        var parts = new Dictionary<string, BuiltPart>(StringComparer.Ordinal);
        var hydraulic = new Dictionary<string, (BenchPart Part, PartType Type)>();
        var instruments = new List<(BenchPart Part, PartType Type)>();

        if (project.Parts.Count == 0)
            issues.Add(new(IssueLevel.Info, null, "La mesa está vacía.", "Elige una pieza en la lista de la izquierda y haz clic en la mesa para ponerla."));

        // 1. Piezas
        foreach (var part in project.Parts)
        {
            if (parts.ContainsKey(part.Id) || hydraulic.ContainsKey(part.Id) || instruments.Any(x => x.Part.Id == part.Id))
            {
                issues.Add(new(IssueLevel.Error, part.Id, $"Hay dos piezas llamadas {part.Id}.", "Cambia el nombre de una de ellas en su ficha."));
                continue;
            }
            var type = BenchCatalog.Get(part.Type);
            if (type is null)
            {
                issues.Add(new(IssueLevel.Error, part.Id, $"{part.Id} es de un tipo que este simulador no conoce («{part.Type}»).", "Bórrala o abre el montaje con una versión más nueva."));
                continue;
            }
            if (type.HydraulicType is not null) { hydraulic[part.Id] = (part, type); continue; }
            if (type.Instrument is not null) { instruments.Add((part, type)); continue; }
            try
            {
                parts[part.Id] = type.Create!(part);
            }
            catch (Exception ex)
            {
                issues.Add(new(IssueLevel.Error, part.Id, $"{part.Id} no se pudo preparar: {Clean(ex.Message)}",
                    type.TypeId is "plc" or "arduino-io" ? "Corrige el programa en la ficha de la pieza." : "Revisa sus parámetros en la ficha."));
            }
        }

        // 2. Cables: que existan los dos extremos y que sean compatibles
        var wires = new List<(BenchWire Wire, PortDef A, PortDef B)>();
        foreach (var w in project.Wires)
        {
            var pa = project.Part(w.FromPart);
            var pb = project.Part(w.ToPart);
            var a = pa is null ? null : BenchCatalog.Get(pa.Type)?.Port(pa, w.FromPort);
            var b = pb is null ? null : BenchCatalog.Get(pb.Type)?.Port(pb, w.ToPort);
            if (a is null || b is null)
            {
                issues.Add(new(IssueLevel.Warning, pa?.Id ?? pb?.Id, $"La conexión {w} apunta a un borne que no existe; se ignora.", "Bórrala y vuelve a tirarla."));
                continue;
            }
            var why = BenchRules.Pair(a, $"{w.FromPart}.{a.Text}", b, $"{w.ToPart}.{b.Text}");
            if (why is not null) { issues.Add(new(IssueLevel.Error, w.ToPart, why, "Selecciona la conexión y bórrala con Supr.")); continue; }
            wires.Add((w, a, b));
        }
        CheckFanIn(project, wires, issues);

        // 3. Redes hidráulicas: cada grupo de piezas unidas por tuberías es una red con su propio solver
        var networks = BuildHydraulics(project, hydraulic, wires, parts, issues);

        // 4. Planificador con todos los componentes
        var fluids = networks.ToDictionary(n => n.Id, _ => Water);
        var scheduler = new CoSimScheduler(fluids: fluids);
        foreach (var bp in parts.Values) if (bp.Component is not null) scheduler.Add(bp.Component);
        foreach (var n in networks) scheduler.Add(n);

        // 5. Señales y potencias
        foreach (var (w, a, b) in wires)
        {
            if (!parts.TryGetValue(w.FromPart, out var ba) || !parts.TryGetValue(w.ToPart, out var bb)) continue;
            if (a.Kind is PortKind.SignalIn or PortKind.SignalOut && b.Kind is PortKind.SignalIn or PortKind.SignalOut)
            {
                var (outPart, outPort, inPart, inPort) = a.Kind == PortKind.SignalOut ? (ba, w.FromPort, bb, w.ToPort) : (bb, w.ToPort, ba, w.FromPort);
                var src = outPart.Signal(outPort);
                var dst = inPart.Signal(inPort);
                if (src is null || dst is null) continue;
                var r = scheduler.Bus.Connect(src, dst);
                foreach (var warn in r.Warnings)
                    issues.Add(new(IssueLevel.Warning, inPart.Part.Id, $"{src.Id} → {dst.Id}: {ExplainBus(warn, src, dst)}",
                        "Funciona en la simulación, pero en la realidad podrías dañar la entrada o no activarla. Revisa tensiones y tipos de señal."));
            }
            else if (a.Kind == PortKind.Power && b.Kind == PortKind.Power)
            {
                var pa = ba.Power(w.FromPort);
                var pb = bb.Power(w.ToPort);
                if (pa is null || pb is null) continue;
                var r = scheduler.Connect(pa, pb);
                if (!r.Connected) issues.Add(new(IssueLevel.Error, w.ToPart, string.Join(" ", r.Warnings)));
                foreach (var hp in new[] { ba.Model, bb.Model }.OfType<HydraulicPart>()) hp.ShaftConnected = true;
            }
        }

        // 6. Instrumentos y sus sondas
        foreach (var (part, type) in instruments)
        {
            var defs = type.PortsFor(part);
            var probes = defs.Select(d =>
            {
                var w = project.WiresAt(part.Id, d.Name).FirstOrDefault();
                if (w is null) return null;
                var (op, oport) = w.Other(part.Id, d.Name);
                return ResolveProbe(project, parts, op, oport);
            }).ToList();
            int every = type.Instrument == InstrumentKind.Multimeter ? 10
                : type.Instrument == InstrumentKind.Oscilloscope ? (int)Math.Clamp(part.Number("muestreo", 2), 1, 100) : 1;
            var inst = new InstrumentComponent(part.Id, type.Instrument!.Value, probes, every);
            scheduler.Add(inst);
            var bp = new BuiltPart { Part = part, Type = type, Component = inst, SnapshotSource = inst, Model = inst };
            bp.Look = _ =>
            {
                var last = inst.LastReadings();
                var lines = type.Instrument == InstrumentKind.Multimeter
                    ? (last.Length > 0 && last[0].Length > 0 ? last[0].Select(x => x.ToString()).ToList() : ["sin punta"])
                    : last.Select((r, i) => r.Length > 0 ? $"{defs[i].Name}: {r[0]}" : null).OfType<string>().Take(4).ToList();
                if (lines.Count == 0) lines.Add("sin sondas");
                return new PartLook(lines, PartState.On);
            };
            parts[part.Id] = bp;
            if (probes.All(p => p is null))
                issues.Add(new(IssueLevel.Info, part.Id, $"{part.Id} no tiene ninguna punta puesta.", "Tira un cable desde su punta hasta el borne que quieras medir."));
        }

        // 7. Bornes que deberían estar conectados
        foreach (var part in project.Parts)
        {
            var type = BenchCatalog.Get(part.Type);
            if (type is null) continue;
            foreach (var d in type.PortsFor(part))
                if (d.IfMissing is not null && !project.WiresAt(part.Id, d.Name).Any())
                    issues.Add(new(d.MissingLevel, part.Id, $"{part.Id} ({d.Text}): {d.IfMissing}"));
            if (type.HydraulicType == "bomba" && !project.WiresAt(part.Id, "Eje").Any() && !project.WiresAt(part.Id, "Marcha").Any())
                issues.Add(new(IssueLevel.Info, part.Id, $"{part.Id} no tiene motor ni señal de marcha.", "Acopla un motor a su eje, une su Marcha a un contacto, o arráncala a mano desde su ficha mientras simulas."));
        }

        var built = new BuiltBench(project, scheduler, parts, networks, issues);
        built.AfterReset();
        return built;
    }

    private static void CheckFanIn(BenchProject project, List<(BenchWire Wire, PortDef A, PortDef B)> wires, List<BenchIssue> issues)
    {
        var ends = wires.SelectMany(x => new[] { (x.Wire.FromPart, x.A, Other: x.B), (x.Wire.ToPart, x.B, Other: x.A) })
            .Where(e => e.Other.Kind != PortKind.Probe && e.Item2.Kind is PortKind.SignalIn or PortKind.Power or PortKind.Tap or PortKind.Probe)
            .GroupBy(e => (e.Item1, e.Item2.Name));
        foreach (var g in ends.Where(g => g.Count() > 1))
        {
            var d = g.First().Item2;
            string what = d.Kind switch
            {
                PortKind.SignalIn => "recibe señal de varias salidas a la vez. Una entrada tiene una sola fuente.",
                PortKind.Power => "está unido a varios equipos. Para repartir potencia usa un Embarrado.",
                PortKind.Probe => "tiene la punta en varios sitios.",
                _ => "está puesto en varios sitios.",
            };
            issues.Add(new(IssueLevel.Error, g.Key.Item1, $"{g.Key.Item1}.{d.Text} {what}", "Deja una sola conexión en ese borne."));
        }
    }

    private static List<RedHidraulica> BuildHydraulics(BenchProject project, Dictionary<string, (BenchPart Part, PartType Type)> hydraulic,
        List<(BenchWire Wire, PortDef A, PortDef B)> wires, Dictionary<string, BuiltPart> parts, List<BenchIssue> issues)
    {
        var networks = new List<RedHidraulica>();
        if (hydraulic.Count == 0) return networks;
        var factory = new HydraulicComponentFactory();

        // Grupos de piezas unidas por tuberías o tomas
        var parent = hydraulic.Keys.ToDictionary(k => k, k => k);
        string Find(string x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        var pipeWires = new List<BenchWire>();
        var taps = new Dictionary<string, string>();
        foreach (var (w, a, b) in wires)
        {
            bool pipe = a.Kind == PortKind.Pipe && b.Kind == PortKind.Pipe;
            bool tap = (a.Kind == PortKind.Tap && b.Kind == PortKind.Pipe) || (a.Kind == PortKind.Pipe && b.Kind == PortKind.Tap);
            if (!pipe && !tap) continue;
            if (!hydraulic.ContainsKey(w.FromPart) || !hydraulic.ContainsKey(w.ToPart)) continue;
            parent[Find(w.FromPart)] = Find(w.ToPart);
            if (pipe) pipeWires.Add(w);
            else if (a.Kind == PortKind.Tap) taps[w.FromPart] = w.ToPart;
            else taps[w.ToPart] = w.FromPart;
        }

        int n = 0;
        foreach (var group in hydraulic.Keys.GroupBy(Find))
        {
            n++;
            string netId = $"RH{n}";
            var members = group.ToList();
            var hparts = new List<HydraulicPart>();
            bool ok = true;
            foreach (var id in members)
            {
                var (part, type) = hydraulic[id];
                var prm = new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = id };
                foreach (var d in type.Params) prm[d.Key] = part.Number(d.Key, double.TryParse(d.Default, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : 0);
                if (type.HydraulicType!.StartsWith("sensor") || type.HydraulicType == "caudalimetro")
                {
                    if (!taps.TryGetValue(id, out var target)) { ok = false; continue; }
                    var why = TargetProblem(type.HydraulicType, hydraulic[target].Type.HydraulicType!, id, target);
                    if (why is not null) { issues.Add(new(IssueLevel.Error, id, why)); ok = false; continue; }
                    prm["objetivo"] = target;
                }
                try
                {
                    var hp = (HydraulicPart)factory.Create(type.HydraulicType, prm);
                    hparts.Add(hp);
                }
                catch (Exception ex)
                {
                    issues.Add(new(IssueLevel.Error, id, $"{id} no se pudo preparar: {Clean(ex.Message)}"));
                    ok = false;
                }
            }
            if (!hparts.Any(h => h.Element is Reservoir or Tank))
            {
                issues.Add(new(IssueLevel.Error, members[0], $"La red de tuberías de {string.Join(", ", members.Take(4))}{(members.Count > 4 ? "…" : "")} no tiene ningún depósito ni tanque: el agua no tiene de dónde salir ni adónde llegar.",
                    "Añade un «Depósito de nivel constante» o un «Tanque» y únelo con tuberías."));
                ok = false;
            }
            if (!ok) continue;

            try
            {
                var connections = pipeWires.Where(w => members.Contains(w.FromPart))
                    .Select(w => new RedHidraulica.PipeConnection(w.FromPart, w.FromPort, w.ToPart, w.ToPort));
                var net = RedHidraulica.Assemble(netId, hparts, connections);
                var report = net.Model.Validate();
                foreach (var w in report.Warnings) issues.Add(new(IssueLevel.Warning, members[0], w));
                if (report.Errors.Count > 0)
                {
                    foreach (var e in report.Errors) issues.Add(new(IssueLevel.Error, PartOf(e, members), e, "Une los dos extremos de cada tubería, bomba o válvula."));
                    continue;
                }
                networks.Add(net);
                foreach (var hp in hparts)
                {
                    var (part, type) = hydraulic[hp.Id];
                    var bp = new BuiltPart
                    {
                        Part = part, Type = type, SnapshotSource = net, SnapshotPrefix = hp.Id + ".", Model = hp,
                        Signal = name => hp.Signals.FirstOrDefault(s => s.Id == $"{hp.Id}.{name}"),
                        Power = name => hp.PowerPorts.FirstOrDefault(s => s.Id == $"{hp.Id}.{name}"),
                    };
                    HydraulicLooks.Decorate(bp, hp, port => project.WiresAt(part.Id, port).Any());
                    parts[hp.Id] = bp;
                }
            }
            catch (Exception ex)
            {
                issues.Add(new(IssueLevel.Error, members[0], $"La red de tuberías {netId} no se puede calcular: {Clean(ex.Message)}", "Revisa que todas las tuberías tengan sus dos extremos unidos."));
            }
        }
        return networks;
    }

    /// <summary>Pieza a la que se refiere un mensaje del módulo Hidráulica ("T1: …").</summary>
    private static string PartOf(string message, List<string> members)
    {
        int i = message.IndexOf(':');
        string id = i > 0 ? message[..i] : "";
        return members.Contains(id) ? id : members[0];
    }

    private static string? TargetProblem(string sensor, string target, string id, string targetId)
    {
        bool node = target is "deposito" or "tanque" or "nudo";
        return sensor switch
        {
            "sensor-nivel" when target != "tanque" && target != "deposito" =>
                $"El transmisor de nivel {id} está puesto en {targetId}, que no tiene nivel. Ponlo en un tanque.",
            "sensor-presion" when !node =>
                $"El transmisor de presión {id} está en {targetId}. Ponlo en un tanque, un depósito o una unión (la presión se mide en un punto, no a lo largo de un tubo).",
            "caudalimetro" when node =>
                $"El caudalímetro {id} está en {targetId}. Ponlo en una tubería, una bomba o una válvula (el caudal pasa por un tubo).",
            _ => null,
        };
    }

    private static Probe? ResolveProbe(BenchProject project, Dictionary<string, BuiltPart> parts, string partId, string port)
    {
        if (!parts.TryGetValue(partId, out var bp)) return null;
        var def = bp.Type.Port(bp.Part, port);
        return def?.Kind switch
        {
            PortKind.SignalIn or PortKind.SignalOut => bp.Signal(port) is { } s ? Probe.ForSignal(s) : null,
            PortKind.Power => bp.Power(port) is { } p ? Probe.ForPower(p) : null,
            PortKind.Pipe when bp.Model is HydraulicPart hp => Probe.ForHydraulic($"{partId}.{port}", hp),
            _ => null,
        };
    }

    private static string ExplainBus(string warning, ISignalPort src, ISignalPort dst)
    {
        if (warning.StartsWith("Nivel nominal", StringComparison.Ordinal))
            return $"la salida trabaja a {src.NominalLevel:0.#} V y la entrada espera {dst.NominalLevel:0.#} V.";
        if (warning.StartsWith("Unidades", StringComparison.Ordinal))
            return $"la salida da {src.Unit} y la entrada espera {dst.Unit}. Pon un convertidor de señal entre las dos.";
        if (warning.Contains("alterna", StringComparison.Ordinal))
            return "se mezclan una señal de alterna y otra de continua.";
        return warning;
    }

    private static string Clean(string message) => message.Replace("\r", "").Replace("\n", " ").Trim();
}
