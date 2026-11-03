using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Schematic;

/// <summary>Una red eléctrica: todos los pines unidos entre sí.</summary>
public sealed class Net
{
    public required string Name { get; set; }
    public List<(PlacedSymbol Symbol, SymbolPin Pin)> Pins { get; } = [];
}

public sealed class ConnectivityResult
{
    public List<Net> Nets { get; } = [];
    /// <summary>Red de cada pin, por (Id del símbolo, número de pin).</summary>
    public Dictionary<(Guid, string), Net> PinNets { get; } = [];
    /// <summary>Puntos donde se unen tres o más conexiones (se dibuja un punto).</summary>
    public List<Vec2> Junctions { get; } = [];

    public Net? NetOf(PlacedSymbol s, string pinNumber) => PinNets.GetValueOrDefault((s.Id, pinNumber));
}

/// <summary>Calcula qué está conectado con qué en un esquema.</summary>
public static class Connectivity
{
    public const string GroundNet = "0";
    private const double Tolerance = 0.01; // mm

    public static ConnectivityResult Analyze(SchematicDocument doc)
    {
        var uf = new UnionFind();
        var pinPoints = new List<(PlacedSymbol Symbol, SymbolPin Pin, long Key)>();
        var labelOrPower = new List<(long Key, string Name)>();

        foreach (var s in doc.Symbols)
        {
            foreach (var (pin, pos) in SymbolTransform.Pins(s))
            {
                long key = uf.Point(pos);
                pinPoints.Add((s, pin, key));
                if (s.Symbol?.IsPower == true)
                    labelOrPower.Add((key, PowerNetName(s)));
            }
        }
        foreach (var l in doc.Labels)
            labelOrPower.Add((uf.Point(l.Position), l.Name.Trim()));

        // Cada cable une sus extremos y cualquier punto que caiga en medio (unión en T).
        var interiorHits = new Dictionary<long, int>();
        var allPoints = uf.Keys.ToList();
        foreach (var w in doc.Wires)
        {
            long a = uf.Point(w.A), b = uf.Point(w.B);
            uf.Union(a, b);
            allPoints.Add(a);
            allPoints.Add(b);
        }
        foreach (var w in doc.Wires)
        {
            long a = uf.Point(w.A);
            foreach (var p in allPoints.Distinct())
            {
                var v = uf.PositionOf(p);
                if (OnSegmentInterior(v, w.A, w.B))
                {
                    uf.Union(a, p);
                    interiorHits[p] = interiorHits.GetValueOrDefault(p) + 1;
                }
            }
        }

        // Etiquetas y símbolos de alimentación con el mismo nombre comparten red.
        foreach (var group in labelOrPower.Where(l => l.Name.Length > 0).GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First().Key;
            foreach (var item in group) uf.Union(first, item.Key);
        }

        // Nombrar redes: masa → "0"; si no, etiqueta o alimentación; si no, N1, N2…
        var result = new ConnectivityResult();
        var byRoot = new Dictionary<long, Net>();
        var namesByRoot = labelOrPower
            .Where(l => l.Name.Length > 0)
            .GroupBy(l => uf.Find(l.Key))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());
        int auto = 1;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (symbol, pin, key) in pinPoints)
        {
            long root = uf.Find(key);
            if (!byRoot.TryGetValue(root, out var net))
            {
                string name;
                if (namesByRoot.TryGetValue(root, out var names))
                    name = names.Any(IsGround) ? GroundNet : names.Order(StringComparer.OrdinalIgnoreCase).First();
                else
                {
                    do name = "N" + auto++; while (usedNames.Contains(name));
                }
                usedNames.Add(name);
                net = new Net { Name = name };
                byRoot[root] = net;
                result.Nets.Add(net);
            }
            net.Pins.Add((symbol, pin));
            result.PinNets[(symbol.Id, pin.Number)] = net;
        }

        // Puntos de unión: 3 o más conexiones en el mismo sitio.
        var degree = new Dictionary<long, int>();
        foreach (var w in doc.Wires)
        {
            degree[uf.Point(w.A)] = degree.GetValueOrDefault(uf.Point(w.A)) + 1;
            degree[uf.Point(w.B)] = degree.GetValueOrDefault(uf.Point(w.B)) + 1;
        }
        foreach (var (_, _, key) in pinPoints) degree[key] = degree.GetValueOrDefault(key) + 1;
        foreach (var (key, hits) in interiorHits) degree[key] = degree.GetValueOrDefault(key) + 2 * hits;
        foreach (var (key, d) in degree)
            if (d >= 3) result.Junctions.Add(uf.PositionOf(key));

        return result;
    }

    public static string PowerNetName(PlacedSymbol s) => IsGround(s.Value) ? GroundNet : s.Value.Trim();

    public static bool IsGround(string name) =>
        name.Trim().ToUpperInvariant() is "GND" or "0" or "MASA" or "GNDA" or "GNDD" or "EARTH";

    private static bool OnSegmentInterior(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < Tolerance * Tolerance) return false;
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        if (t <= 0 || t >= 1) return false;
        double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
        if (cx * cx + cy * cy > Tolerance * Tolerance) return false;
        // Excluir los extremos (ya unidos por igualdad de punto).
        double ea = (p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y);
        double eb = (p.X - b.X) * (p.X - b.X) + (p.Y - b.Y) * (p.Y - b.Y);
        return ea > Tolerance * Tolerance && eb > Tolerance * Tolerance;
    }

    /// <summary>Unión-búsqueda sobre puntos redondeados a la tolerancia.</summary>
    private sealed class UnionFind
    {
        private readonly Dictionary<long, long> _parent = [];
        private readonly Dictionary<long, Vec2> _positions = [];

        public IEnumerable<long> Keys => _parent.Keys;

        public long Point(Vec2 v)
        {
            long x = (long)Math.Round(v.X / Tolerance), y = (long)Math.Round(v.Y / Tolerance);
            long key = (x << 32) ^ (y & 0xFFFFFFFFL);
            if (_parent.TryAdd(key, key)) _positions[key] = v;
            return key;
        }

        public Vec2 PositionOf(long key) => _positions[key];

        public long Find(long k)
        {
            while (_parent[k] != k)
            {
                _parent[k] = _parent[_parent[k]];
                k = _parent[k];
            }
            return k;
        }

        public void Union(long a, long b)
        {
            long ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[rb] = ra;
        }
    }
}
