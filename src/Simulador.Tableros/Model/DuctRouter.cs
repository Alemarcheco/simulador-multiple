namespace Simulador.Tableros.Model;

/// <summary>Recorrido de un conductor por las canaletas, en coordenadas de la placa (mm).</summary>
/// <remarks>
/// En un armario de doble frente, <see cref="Points"/> es el tramo por la placa delantera y <see cref="RearPoints"/>
/// el de la trasera; un hilo que pasa de una cara a otra tiene los dos y cruza por la esquina superior izquierda.
/// </remarks>
public sealed record WireRoute(PanelWire Wire, IReadOnlyList<(double X, double Y)> Points, bool FromDoor, bool ToDoor, IReadOnlyList<Duct> Ducts)
{
    public IReadOnlyList<(double X, double Y)> RearPoints { get; init; } = [];

    /// <summary>Tramo que se ve en una cara.</summary>
    public IReadOnlyList<(double X, double Y)> PointsOn(bool rear) => rear ? RearPoints : Points;
}

/// <summary>
/// Genera los conductores a partir de los potenciales del esquema y los lleva por las canaletas por el camino más
/// corto (Dijkstra sobre el eje de las canaletas). Da la longitud de cada hilo y el llenado de cada canaleta.
/// </summary>
public static class DuctRouter
{
    /// <summary>Reserva por cada punta para pelar, curvar y peinar (mm).</summary>
    public const double EndAllowance = 60;
    /// <summary>Bucle de la manguera de la puerta, por la bisagra (mm).</summary>
    public const double DoorLoop = 300;

    /// <summary>Rehace <see cref="PanelDocument.Wires"/> desde el esquema guardado y calcula longitudes.</summary>
    public static List<WireRoute> Rebuild(PanelDocument doc)
    {
        if (doc.Schematic is not null)
        {
            var analysis = SchematicImport.Analyze(doc.Schematic);
            doc.Wires = BuildWires(doc, analysis.Nets);
        }
        return Route(doc);
    }

    /// <summary>
    /// Conductores de cada potencial: une los bornes en cadena empezando por el de más arriba y yendo siempre al más
    /// cercano, para que ningún borne reciba más de dos hilos.
    /// </summary>
    public static List<PanelWire> BuildWires(PanelDocument doc, IEnumerable<PanelNet> nets)
    {
        var wires = new List<PanelWire>();
        foreach (var net in nets)
        {
            var points = net.Ends.Select(e => (End: e, P: Position(doc, e))).Where(p => p.P is not null)
                .Select(p => (p.End, P: p.P!.Value)).ToList();
            if (points.Count < 2) continue;
            var chain = new List<(WireEnd End, (double X, double Y) P)>();
            var start = points.OrderBy(p => p.P.Y).ThenBy(p => p.P.X).First();
            chain.Add(start);
            points.Remove(start);
            while (points.Count > 0)
            {
                var last = chain[^1].P;
                var next = points.OrderBy(p => Math.Abs(p.P.X - last.X) + Math.Abs(p.P.Y - last.Y)).First();
                chain.Add(next);
                points.Remove(next);
            }
            for (var i = 0; i + 1 < chain.Count; i++)
                wires.Add(new PanelWire
                {
                    Number = net.Number,
                    From = chain[i].End,
                    To = chain[i + 1].End,
                    Kind = net.Kind,
                    Section = net.Section,
                    Color = SchematicImport.ColorFor(net.Kind),
                });
        }
        return wires;
    }

    /// <summary>Punto del borne en la placa. Los de la puerta salen por el lado de la bisagra (izquierda).</summary>
    public static (double X, double Y)? Position(PanelDocument doc, WireEnd end)
    {
        var d = doc.FindTag(end.Tag);
        if (d is null || d.Location == DeviceLocation.Field) return null;
        var t = d.Terminal(end.Terminal);
        if (t is null) return (d.X + d.Width / 2, d.Y);
        var (x, y) = d.TerminalPoint(t);
        if (d.Location == DeviceLocation.Door) return (-1, Math.Clamp(y - doc.Enclosure.PlateMargin, 0, doc.Enclosure.PlateHeight));
        return (x, y);
    }

    private sealed class Graph
    {
        public readonly List<(double X, double Y)> Nodes = [];
        public readonly List<List<(int To, double Cost, Duct? Duct)>> Edges = [];

        public int Add((double X, double Y) p)
        {
            for (var i = 0; i < Nodes.Count; i++)
                if (Math.Abs(Nodes[i].X - p.X) < 0.5 && Math.Abs(Nodes[i].Y - p.Y) < 0.5) return i;
            Nodes.Add(p);
            Edges.Add([]);
            return Nodes.Count - 1;
        }

        public void Link(int a, int b, Duct? duct)
        {
            if (a == b) return;
            var c = Math.Abs(Nodes[a].X - Nodes[b].X) + Math.Abs(Nodes[a].Y - Nodes[b].Y);
            Edges[a].Add((b, c, duct));
            Edges[b].Add((a, c, duct));
        }
    }

    /// <summary>Calcula el recorrido y la longitud de todos los conductores.</summary>
    public static List<WireRoute> Route(PanelDocument doc)
    {
        var routes = new List<WireRoute>();
        foreach (var w in doc.Wires)
        {
            var a = Position(doc, w.From);
            var b = Position(doc, w.To);
            if (a is null || b is null) { w.Length = 0; continue; }
            var da = doc.FindTag(w.From.Tag)!;
            var db = doc.FindTag(w.To.Tag)!;
            var fromDoor = da.Location == DeviceLocation.Door;
            var toDoor = db.Location == DeviceLocation.Door;
            var len = 0.0;
            WireRoute route;
            if (da.Rear == db.Rear)
            {
                var (pts, used) = Leg(doc, a.Value, b.Value, da.Rear);
                len += Length(pts);
                route = da.Rear ? new WireRoute(w, [], fromDoor, toDoor, used) { RearPoints = pts } : new WireRoute(w, pts, fromDoor, toDoor, used);
            }
            else
            {
                // De una cara a la otra: por la esquina superior izquierda, rodeando el canto de las placas.
                var (p1, u1) = Leg(doc, a.Value, CrossPoint, da.Rear);
                var (p2, u2) = Leg(doc, CrossPoint, b.Value, db.Rear);
                len += Length(p1) + Length(p2) + CrossLength(doc);
                var (front, rear) = da.Rear ? (p2, p1) : (p1, p2);
                route = new WireRoute(w, front, fromDoor, toDoor, [.. u1, .. u2.Except(u1)]) { RearPoints = rear };
            }
            if (fromDoor) len += DoorLeg(doc, w.From);
            if (toDoor) len += DoorLeg(doc, w.To);
            w.Length = Math.Round(len + 2 * EndAllowance);
            routes.Add(route);
        }
        return routes;
    }

    /// <summary>Punto de la placa por el que los hilos pasan de una cara a la otra.</summary>
    public static readonly (double X, double Y) CrossPoint = (0, 0);

    /// <summary>Lo que mide el paso de una placa a la otra: el fondo entre las dos caras más el canto.</summary>
    public static double CrossLength(PanelDocument doc) => doc.Enclosure.Depth / 2 + 100;

    private static double Length(List<(double X, double Y)> pts)
    {
        var len = 0.0;
        for (var i = 0; i + 1 < pts.Count; i++) len += Math.Abs(pts[i].X - pts[i + 1].X) + Math.Abs(pts[i].Y - pts[i + 1].Y);
        return len;
    }

    private static (List<(double X, double Y)> Points, List<Duct> Ducts) Leg(PanelDocument doc, (double X, double Y) a, (double X, double Y) b, bool rear) =>
        doc.DuctsOf(rear).Any() ? Path(doc, a, b, rear) : (Manhattan(a, b), []);

    /// <summary>Tramo en la puerta: del aparato a la bisagra, más el bucle para poder abrirla.</summary>
    private static double DoorLeg(PanelDocument doc, WireEnd end)
    {
        var d = doc.FindTag(end.Tag)!;
        return d.X + d.Width / 2 + DoorLoop;
    }

    private static List<(double X, double Y)> Manhattan((double X, double Y) a, (double X, double Y) b) =>
        [a, (a.X, b.Y), b];

    private static (List<(double X, double Y)> Points, List<Duct> Ducts) Path(PanelDocument doc, (double X, double Y) a, (double X, double Y) b, bool rear)
    {
        var g = new Graph();
        var ducts = doc.DuctsOf(rear).ToList();
        // Nudos sobre el eje de cada canaleta: extremos, cruces con las demás y entradas de los dos bornes.
        var onDuct = ducts.ToDictionary(k => k, _ => new List<int>());
        foreach (var k in ducts)
        {
            onDuct[k].Add(g.Add(Start(k)));
            onDuct[k].Add(g.Add(End(k)));
        }
        foreach (var k in ducts)
            foreach (var o in ducts)
            {
                if (k == o || k.Vertical == o.Vertical) continue;
                var (h, v) = k.Vertical ? (o, k) : (k, o);
                // Se tocan si el eje de una cae dentro (o al borde) de la otra, con tolerancia de medio ancho.
                var x = v.CenterX;
                var y = h.CenterY;
                if (x >= h.X - v.Width && x <= h.X + h.Length + v.Width && y >= v.Y - h.Width && y <= v.Y + v.Length + h.Width)
                {
                    var p = (Math.Clamp(x, h.X, h.X + h.Length), Math.Clamp(y, v.Y, v.Y + v.Length));
                    var ph = g.Add((p.Item1, h.CenterY));
                    var pv = g.Add((v.CenterX, p.Item2));
                    onDuct[h].Add(ph);
                    onDuct[v].Add(pv);
                    g.Link(ph, pv, null);
                }
            }

        var ia = Entry(doc, g, onDuct, a, rear);
        var ib = Entry(doc, g, onDuct, b, rear);

        foreach (var (k, list) in onDuct)
        {
            var sorted = list.Distinct().OrderBy(i => k.Vertical ? g.Nodes[i].Y : g.Nodes[i].X).ToList();
            for (var i = 0; i + 1 < sorted.Count; i++) g.Link(sorted[i], sorted[i + 1], k);
        }

        // Dijkstra.
        var n = g.Nodes.Count;
        var dist = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        var prev = new int[n];
        var prevDuct = new Duct?[n];
        Array.Fill(prev, -1);
        dist[ia] = 0;
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(ia, 0);
        while (queue.TryDequeue(out var u, out var du))
        {
            if (du > dist[u]) continue;
            if (u == ib) break;
            foreach (var (v, c, k) in g.Edges[u])
                if (du + c < dist[v])
                {
                    dist[v] = du + c;
                    prev[v] = u;
                    prevDuct[v] = k;
                    queue.Enqueue(v, dist[v]);
                }
        }
        if (double.IsInfinity(dist[ib])) return (Manhattan(a, b), []);
        var path = new List<(double X, double Y)>();
        var used = new List<Duct>();
        for (var v = ib; v != -1; v = prev[v])
        {
            path.Add(g.Nodes[v]);
            if (prevDuct[v] is { } k && !used.Contains(k)) used.Add(k);
        }
        path.Reverse();
        path.Insert(0, a);
        path.Add(b);
        return (Simplify(path), used);
    }

    private static (double, double) Start(Duct k) => k.Vertical ? (k.CenterX, k.Y) : (k.X, k.CenterY);
    private static (double, double) End(Duct k) => k.Vertical ? (k.CenterX, k.Y + k.Length) : (k.X + k.Length, k.CenterY);

    /// <summary>
    /// Entrada de un borne a la canaleta: los de arriba de un aparato suben a la canaleta horizontal de encima, los
    /// de abajo bajan a la de debajo; si no hay, a la más cercana. Los de la puerta entran por la canaleta izquierda.
    /// </summary>
    private static int Entry(PanelDocument doc, Graph g, Dictionary<Duct, List<int>> onDuct, (double X, double Y) p, bool rear)
    {
        Duct? best = null;
        (double X, double Y) proj = default;
        var bestCost = double.PositiveInfinity;
        var isTopOfDevice = doc.PlateOf(rear).Any(d => Math.Abs(d.Y - p.Y) < 0.5 && p.X >= d.X - 0.5 && p.X <= d.X + d.Width + 0.5);
        var isBottom = doc.PlateOf(rear).Any(d => Math.Abs(d.Y + d.Height - p.Y) < 0.5 && p.X >= d.X - 0.5 && p.X <= d.X + d.Width + 0.5);
        foreach (var k in onDuct.Keys)
        {
            var q = k.Vertical
                ? (k.CenterX, Math.Clamp(p.Y, k.Y, k.Y + k.Length))
                : (Math.Clamp(p.X, k.X, k.X + k.Length), k.CenterY);
            var cost = Math.Abs(q.Item1 - p.X) + Math.Abs(q.Item2 - p.Y);
            // Penaliza ir hacia el lado contrario (un borne de arriba no baja cruzando su propio aparato).
            if (!k.Vertical && isTopOfDevice && q.Item2 > p.Y && !isBottom) cost += 1000;
            if (!k.Vertical && isBottom && q.Item2 < p.Y && !isTopOfDevice) cost += 1000;
            if (k.Vertical && p.X > 0 && (isTopOfDevice || isBottom)) cost += 200;
            if (cost < bestCost) { bestCost = cost; best = k; proj = q; }
        }
        var node = g.Add(proj);
        onDuct[best!].Add(node);
        return node;
    }

    private static List<(double X, double Y)> Simplify(List<(double X, double Y)> pts)
    {
        var r = new List<(double X, double Y)>();
        foreach (var p in pts)
        {
            if (r.Count > 0 && Math.Abs(r[^1].X - p.X) < 0.01 && Math.Abs(r[^1].Y - p.Y) < 0.01) continue;
            if (r.Count >= 2)
            {
                var a = r[^2];
                var b = r[^1];
                if ((Math.Abs(a.X - b.X) < 0.01 && Math.Abs(b.X - p.X) < 0.01) || (Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(b.Y - p.Y) < 0.01))
                    r[^1] = p;
                else r.Add(p);
            }
            else r.Add(p);
        }
        return r;
    }

    /// <summary>Diámetro exterior aproximado de un conductor flexible H07V-K (mm).</summary>
    public static double OuterDiameter(double section) => section switch
    {
        <= 0.5 => 2.1,
        <= 0.75 => 2.4,
        <= 1 => 2.6,
        <= 1.5 => 3.1,
        <= 2.5 => 3.7,
        <= 4 => 4.4,
        <= 6 => 5.0,
        <= 10 => 6.4,
        <= 16 => 7.6,
        _ => 9.0,
    };

    /// <summary>Llenado de cada canaleta (0–1): sección de los conductores que pasan / sección útil.</summary>
    public static Dictionary<Duct, double> Fill(PanelDocument doc, IEnumerable<WireRoute> routes)
    {
        var fill = doc.Ducts.ToDictionary(k => k, _ => 0.0);
        foreach (var r in routes)
        {
            var d = OuterDiameter(r.Wire.Section);
            var area = Math.PI * d * d / 4;
            foreach (var k in r.Ducts) fill[k] += area;
            // Los hilos de la puerta pasan todos por la canaleta vertical izquierda.
            if ((r.FromDoor || r.ToDoor) && doc.DuctsOf(false).Where(k => k.Vertical).OrderBy(k => k.X).FirstOrDefault() is { } left && !r.Ducts.Contains(left))
                fill[left] += area;
        }
        foreach (var k in doc.Ducts) fill[k] /= Math.Max(1, k.Width * k.Depth);
        return fill;
    }
}
