using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Simulation;

/// <summary>
/// Tramo del esquema por el que circula corriente. La corriente de A hacia B es la suma de las corrientes
/// de los pines de <see cref="Terms"/> (cada una es la corriente que entra al componente por ese pin).
/// </summary>
public sealed record FlowSegment(Vec2 A, Vec2 B, string Net, IReadOnlyList<string> Terms)
{
    public double Evaluate(Func<string, double> pinCurrent) => Terms.Sum(pinCurrent);
}

/// <summary>
/// Reparte por los cables la corriente que entra a cada pin, para animarla como en un simulador didáctico.
/// Cada red de cables se recorre como un árbol: la corriente por un tramo es la que consumen los pines
/// que quedan "aguas abajo". Si una red tiene bucles de cable, el tramo que cierra el bucle se dibuja sin corriente.
/// </summary>
public static class CurrentFlow
{
    /// <summary>Clave de un pin: "R1:2".</summary>
    public static string Key(string reference, string pin) => reference + ":" + pin;

    public static List<FlowSegment> Build(SchematicDocument doc, ConnectivityResult conn)
    {
        static (long, long) K(Vec2 p) => ((long)Math.Round(p.X * 100), (long)Math.Round(p.Y * 100));

        // Puntos donde se puede partir un cable: extremos, pines y etiquetas.
        var demands = new Dictionary<(long, long), List<string>>();
        var ports = new HashSet<(long, long)>();
        var stubs = new List<FlowSegment>();
        var special = new List<Vec2>();
        foreach (var s in doc.Symbols)
        {
            foreach (var (pin, pos) in SymbolTransform.Pins(s))
            {
                special.Add(pos);
                if (s.Symbol!.IsPower) { ports.Add(K(pos)); continue; }
                var key = Key(s.Reference, pin.Number);
                if (!demands.TryGetValue(K(pos), out var list)) demands[K(pos)] = list = [];
                list.Add(key);
                var body = SymbolTransform.ToWorld(s, pin.BodyEnd);
                if (Distance(body, pos) > 0.05)
                    stubs.Add(new FlowSegment(pos, body, conn.NetOf(s, pin.Number)?.Name ?? "", [key]));
            }
        }
        foreach (var l in doc.Labels) { special.Add(l.Position); ports.Add(K(l.Position)); }
        foreach (var w in doc.Wires) { special.Add(w.A); special.Add(w.B); }

        // Cables partidos en tramos simples.
        var adj = new Dictionary<(long, long), List<(Vec2 From, Vec2 To)>>();
        void Edge(Vec2 a, Vec2 b)
        {
            if (K(a) == K(b)) return;
            if (!adj.TryGetValue(K(a), out var la)) adj[K(a)] = la = [];
            if (!adj.TryGetValue(K(b), out var lb)) adj[K(b)] = lb = [];
            la.Add((a, b));
            lb.Add((b, a));
        }
        foreach (var w in doc.Wires)
        {
            var d = w.B - w.A;
            double len2 = d.X * d.X + d.Y * d.Y;
            if (len2 < 1e-9) continue;
            var cuts = special
                .Select(p => (P: p, T: ((p.X - w.A.X) * d.X + (p.Y - w.A.Y) * d.Y) / len2))
                .Where(c => c.T > 1e-6 && c.T < 1 - 1e-6 && Distance(c.P, new Vec2(w.A.X + d.X * c.T, w.A.Y + d.Y * c.T)) < 0.01)
                .OrderBy(c => c.T).Select(c => c.P).DistinctBy(K).ToList();
            var prev = w.A;
            foreach (var c in cuts) { Edge(prev, c); prev = c; }
            Edge(prev, w.B);
        }

        string NetAt(Vec2 p) =>
            conn.Nets.FirstOrDefault(n => n.Pins.Any(pp => K(SymbolTransform.ToWorld(pp.Symbol, pp.Pin.Position)) == K(p)))?.Name ?? "";

        var segments = new List<FlowSegment>();
        var visited = new HashSet<(long, long)>();
        // Primero las islas que tienen una etiqueta o símbolo de alimentación: ahí se "cierra" la corriente que viene de otra isla.
        var starts = adj.Keys.OrderBy(k => ports.Contains(k) ? 0 : 1).ToList();
        foreach (var root in starts)
        {
            if (!visited.Add(root)) continue;
            var order = new List<(long, long)>();
            var parent = new Dictionary<(long, long), (Vec2 From, Vec2 To)>();
            var stack = new Stack<(long, long)>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                order.Add(n);
                foreach (var (from, to) in adj[n])
                    if (visited.Add(K(to))) { parent[K(to)] = (from, to); stack.Push(K(to)); }
            }
            string net = order.Select(k => adj[k][0].From).Select(NetAt).FirstOrDefault(n => n != "") ?? "";
            var subtree = order.ToDictionary(k => k, k => demands.GetValueOrDefault(k)?.ToList() ?? []);
            for (int i = order.Count - 1; i > 0; i--)
            {
                var n = order[i];
                var (from, to) = parent[n];
                segments.Add(new FlowSegment(from, to, net, subtree[n]));
                subtree[K(from)].AddRange(subtree[n]);
            }
        }
        segments.AddRange(stubs);
        return segments;
    }

    private static double Distance(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>Corrientes y tensiones a lo largo del tiempo, listas para animar en el esquema.</summary>
public sealed class FlowAnimation
{
    public required IReadOnlyList<FlowSegment> Segments { get; init; }
    /// <summary>Instantes simulados (un único 0 para el punto de trabajo).</summary>
    public required double[] Times { get; init; }
    /// <summary>[instante][tramo] corriente de A a B en amperios.</summary>
    public required double[][] Currents { get; init; }
    /// <summary>Tensión de cada red en cada instante.</summary>
    public required Dictionary<string, double[]> Voltages { get; init; }
    /// <summary>Corriente por cada LED (referencia → corriente de ánodo a cátodo en cada instante).</summary>
    public required Dictionary<string, double[]> LedCurrents { get; init; }
    public double MaxCurrent { get; init; }
    public double MaxVoltage { get; init; }
    public bool IsStatic => Times.Length <= 1;

    /// <summary>Índice del instante más cercano por debajo de <paramref name="t"/>.</summary>
    public int IndexAt(double t)
    {
        int i = Array.BinarySearch(Times, t);
        if (i < 0) i = Math.Max(0, ~i - 1);
        return Math.Min(i, Times.Length - 1);
    }

    public static FlowAnimation? From(SchematicDocument doc, ConnectivityResult conn, SimulationResult result)
    {
        var segments = CurrentFlow.Build(doc, conn);
        double[] times = result.Real.GetValueOrDefault("time") ?? [0.0];
        int frames = times.Length;
        var pinVec = new Dictionary<string, double[]>();
        foreach (var s in doc.Symbols.Where(s => s.Symbol is { IsPower: false }))
            foreach (var (pin, _) in SymbolTransform.Pins(s))
                if (result.Real.TryGetValue(SpiceNetlister.ProbeVector(s.Reference, pin.Number), out var v) && v.Length == frames)
                    pinVec[CurrentFlow.Key(s.Reference, pin.Number)] = v;
        if (pinVec.Count == 0) return null;

        var currents = new double[frames][];
        double max = 0;
        for (int f = 0; f < frames; f++)
        {
            int ff = f;
            currents[f] = segments.Select(seg => seg.Evaluate(k => pinVec.TryGetValue(k, out var v) ? v[ff] : 0)).ToArray();
            foreach (var c in currents[f]) max = Math.Max(max, Math.Abs(c));
        }

        var volts = new Dictionary<string, double[]>();
        foreach (var n in conn.Nets)
        {
            if (n.Name == Schematic.Connectivity.GroundNet) volts[n.Name] = new double[frames];
            else if (result.Real.TryGetValue(n.Name, out var v) && v.Length == frames) volts[n.Name] = v;
        }
        var leds = new Dictionary<string, double[]>();
        foreach (var s in doc.Symbols.Where(s => s.Symbol is { } d && (d.Name.Contains("LED", StringComparison.OrdinalIgnoreCase) || s.Value.Contains("LED", StringComparison.OrdinalIgnoreCase))))
        {
            var anode = SymbolTransform.Pins(s).FirstOrDefault(p => p.Pin.Name.Equals("A", StringComparison.OrdinalIgnoreCase)).Pin;
            if (anode is not null && pinVec.TryGetValue(CurrentFlow.Key(s.Reference, anode.Number), out var v)) leds[s.Reference] = v;
        }
        return new FlowAnimation
        {
            Segments = segments,
            Times = times,
            Currents = currents,
            Voltages = volts,
            LedCurrents = leds,
            MaxCurrent = max,
            MaxVoltage = volts.Values.SelectMany(v => v).Select(Math.Abs).DefaultIfEmpty(0).Max(),
        };
    }
}
