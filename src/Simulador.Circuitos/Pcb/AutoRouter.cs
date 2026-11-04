using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

public sealed record AutoRouteReport(int Routed, int Failed, IReadOnlyList<string> FailedNets, int Jumpers = 0);

/// <summary>
/// Ruteador automático por laberinto (A* en una rejilla) para placas de una cara: traza en la capa de abajo
/// las conexiones pendientes, de la más corta a la más larga, respetando el ancho de pista y las separaciones
/// de las reglas. Admite tramos a 45° y penaliza los giros para que las pistas queden limpias.
/// </summary>
public static class AutoRouter
{
    public const double GridStep = 0.635;
    private const double TurnPenalty = 0.6;

    /// <summary>
    /// Rutea todas las conexiones pendientes. Si alguna no cabe, vuelve a empezar poniendo primero las que fallaron
    /// (las pistas que ya había en la placa no se tocan) y se queda con el mejor intento.
    /// </summary>
    /// <param name="allowJumpers">
    /// Si una conexión no cabe en el cobre, se resuelve con un puente de cable entre los dos pads (habitual en
    /// placas caseras de una cara).
    /// </param>
    public static AutoRouteReport Route(PcbDocument doc, string? layer = null, int attempts = 8, bool allowJumpers = false)
    {
        layer ??= Layers.Bottom;
        var fixedTracks = doc.Tracks.ToList();
        var connections = PcbAnalysis.ComputeRatsnest(doc).OrderBy(r => Geometry2D.Length(r.A - r.B)).ToList();
        List<PcbTrack>? best = null;
        AutoRouteReport? bestReport = null;
        var priority = new List<Ratsnest>();
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            doc.Tracks = fixedTracks.ToList();
            var order = priority.Concat(connections.Where(c => !priority.Contains(c))).ToList();
            var failed = RouteAll(doc, order, layer, out int routed);
            var report = new AutoRouteReport(routed, failed.Count, failed.Select(f => f.Net).Distinct().ToList());
            if (bestReport is null || report.Failed < bestReport.Failed)
            {
                bestReport = report;
                best = doc.Tracks.ToList();
            }
            if (report.Failed == 0) break;
            // Las que fallaron pasan delante, por si las anteriores les cerraron el paso.
            priority = failed.Concat(priority.Where(p => !failed.Contains(p))).ToList();
        }
        doc.Tracks = best ?? fixedTracks;
        bestReport ??= new AutoRouteReport(0, 0, []);
        if (!allowJumpers || bestReport.Failed == 0) return bestReport;

        int jumpers = 0;
        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).ToList();
        foreach (var r in PcbAnalysis.ComputeRatsnest(doc))
        {
            var a = pads.FirstOrDefault(p => Near(p.Center, r.A));
            var b = pads.FirstOrDefault(p => Near(p.Center, r.B));
            if (a is not { IsThroughHole: true } || b is not { IsThroughHole: true }) continue;
            doc.Tracks.Add(new PcbTrack { A = r.A, B = r.B, Width = 0.6, Layer = Layers.Jumper, Net = r.Net });
            jumpers++;
        }
        var left = PcbAnalysis.ComputeRatsnest(doc);
        return new AutoRouteReport(bestReport.Routed, left.Count, left.Select(l => l.Net).Distinct().ToList(), jumpers);
    }

    private static List<Ratsnest> RouteAll(PcbDocument doc, List<Ratsnest> connections, string layer, out int routed)
    {
        routed = 0;
        var failed = new List<Ratsnest>();
        foreach (var c in connections)
        {
            // La conexión pudo quedar hecha por una pista anterior de la misma red.
            if (Connected(doc, c)) continue;
            bool ok = false;
            foreach (var margin in new[] { 0.15, 0.45 })
            {
                var tracks = RouteOne(doc, c, layer, margin);
                if (tracks is null) break;
                if (Clean(doc, tracks)) { doc.Tracks.AddRange(tracks); ok = true; break; }
            }
            if (ok) routed++; else failed.Add(c);
        }
        return failed;
    }

    /// <summary>¿Los dos pads de la conexión ya están unidos por cobre?</summary>
    private static bool Connected(PcbDocument doc, Ratsnest c)
    {
        var pending = PcbAnalysis.ComputeRatsnest(doc).Where(r => r.Net == c.Net).ToList();
        if (pending.Count == 0) return true;
        // Si alguno de los dos pads aparece en una línea pendiente de su red, todavía falta.
        return !pending.Any(r => Near(r.A, c.A) || Near(r.B, c.A) || Near(r.A, c.B) || Near(r.B, c.B));
    }

    private static bool Near(Vec2 a, Vec2 b) => Geometry2D.Length(a - b) < 1e-6;

    /// <summary>Comprueba con la geometría exacta que las pistas nuevas no invaden cobre de otras redes ni el borde.</summary>
    private static bool Clean(PcbDocument doc, List<PcbTrack> tracks)
    {
        var rules = doc.Rules;
        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).ToList();
        foreach (var t in tracks)
        {
            foreach (var p in pads.Where(p => p.OnLayer(t.Layer) && p.Net != t.Net))
                if (Geometry2D.PadSegment(p, t.A, t.B) - t.Width / 2 < rules.Clearance - 1e-6) return false;
            foreach (var o in doc.Tracks.Where(o => o.Layer == t.Layer && o.Net != t.Net))
                if (Geometry2D.SegmentSegment(t.A, t.B, o.A, o.B) - (t.Width + o.Width) / 2 < rules.Clearance - 1e-6) return false;
            foreach (var e in new[] { t.A, t.B })
                if (Math.Min(Math.Min(e.X, e.Y), Math.Min(doc.BoardWidth - e.X, doc.BoardHeight - e.Y)) - t.Width / 2 < rules.EdgeClearance - 1e-6) return false;
        }
        return true;
    }

    private static List<PcbTrack>? RouteOne(PcbDocument doc, Ratsnest c, string layer, double margin)
    {
        var rules = doc.Rules;
        double w = rules.TrackWidth;
        int nx = (int)Math.Floor(doc.BoardWidth / GridStep) + 1, ny = (int)Math.Floor(doc.BoardHeight / GridStep) + 1;
        Vec2 At(int i, int j) => new(i * GridStep, j * GridStep);
        var blocked = new bool[nx, ny];

        // Borde de la placa.
        double edge = rules.EdgeClearance + w / 2;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var p = At(i, j);
                if (Math.Min(Math.Min(p.X, p.Y), Math.Min(doc.BoardWidth - p.X, doc.BoardHeight - p.Y)) < edge) blocked[i, j] = true;
            }

        double keep = rules.Clearance + w / 2 + margin;
        void Block(Vec2 min, Vec2 max, Func<Vec2, double> distance)
        {
            int i0 = Math.Max(0, (int)Math.Floor((min.X - keep) / GridStep)), i1 = Math.Min(nx - 1, (int)Math.Ceiling((max.X + keep) / GridStep));
            int j0 = Math.Max(0, (int)Math.Floor((min.Y - keep) / GridStep)), j1 = Math.Min(ny - 1, (int)Math.Ceiling((max.Y + keep) / GridStep));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (!blocked[i, j] && distance(At(i, j)) < keep) blocked[i, j] = true;
        }

        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).Where(p => p.OnLayer(layer)).ToList();
        foreach (var p in pads.Where(p => p.Net != c.Net || p.Net.Length == 0))
        {
            double r = Math.Max(p.Pad.Size.X, p.Pad.Size.Y) / 2;
            Block(new Vec2(p.Center.X - r, p.Center.Y - r), new Vec2(p.Center.X + r, p.Center.Y + r), q => Geometry2D.PadSegment(p, q, q));
        }
        foreach (var t in doc.Tracks.Where(t => t.Layer == layer && t.Net != c.Net))
            Block(new Vec2(Math.Min(t.A.X, t.B.X) - t.Width / 2, Math.Min(t.A.Y, t.B.Y) - t.Width / 2),
                  new Vec2(Math.Max(t.A.X, t.B.X) + t.Width / 2, Math.Max(t.A.Y, t.B.Y) + t.Width / 2),
                  q => Geometry2D.PointSegment(q, t.A, t.B) - t.Width / 2);

        (int, int) Cell(Vec2 p) => (Math.Clamp((int)Math.Round(p.X / GridStep), 0, nx - 1), Math.Clamp((int)Math.Round(p.Y / GridStep), 0, ny - 1));
        var start = Cell(c.A);
        var goal = Cell(c.B);
        // Dentro del propio pad de origen y destino siempre se puede pasar.
        var source = pads.FirstOrDefault(p => Near(p.Center, c.A));
        var target = pads.FirstOrDefault(p => Near(p.Center, c.B));
        bool Free(int i, int j)
        {
            if (!blocked[i, j]) return true;
            var q = At(i, j);
            return (source is not null && Geometry2D.PadSegment(source, q, q) <= 0) || (target is not null && Geometry2D.PadSegment(target, q, q) <= 0)
                || (i, j) == start || (i, j) == goal;
        }

        int[] dx = [1, 1, 0, -1, -1, -1, 0, 1], dy = [0, 1, 1, 1, 0, -1, -1, -1];
        int States(int i, int j, int d) => ((i * ny) + j) * 9 + d;
        var cost = new Dictionary<int, double>();
        var from = new Dictionary<int, int>();
        var open = new PriorityQueue<(int I, int J, int D), double>();
        double H(int i, int j)
        {
            int ax = Math.Abs(i - goal.Item1), ay = Math.Abs(j - goal.Item2);
            return Math.Max(ax, ay) + (Math.Sqrt(2) - 1) * Math.Min(ax, ay);
        }
        int s0 = States(start.Item1, start.Item2, 8);
        cost[s0] = 0;
        open.Enqueue((start.Item1, start.Item2, 8), H(start.Item1, start.Item2));
        int end = -1;
        int guard = 0;
        while (open.Count > 0 && guard++ < 2_000_000)
        {
            var (i, j, d) = open.Dequeue();
            int s = States(i, j, d);
            if ((i, j) == goal) { end = s; break; }
            double g = cost[s];
            for (int k = 0; k < 8; k++)
            {
                int ni = i + dx[k], nj = j + dy[k];
                if (ni < 0 || nj < 0 || ni >= nx || nj >= ny || !Free(ni, nj)) continue;
                // En diagonal, las dos celdas de las esquinas también deben estar libres.
                if (k % 2 == 1 && (!Free(i + dx[k], j) || !Free(i, j + dy[k]))) continue;
                double step = k % 2 == 1 ? Math.Sqrt(2) : 1;
                if (d != 8 && d != k) step += TurnPenalty;
                int ns = States(ni, nj, k);
                double ng = g + step;
                if (cost.TryGetValue(ns, out var old) && old <= ng) continue;
                cost[ns] = ng;
                from[ns] = s;
                open.Enqueue((ni, nj, k), ng + H(ni, nj));
            }
        }
        if (end < 0) return null;

        // Reconstruir el camino y unir los tramos rectos.
        var cells = new List<Vec2>();
        for (int s = end; ; s = from[s])
        {
            int cell = s / 9;
            cells.Add(At(cell / ny, cell % ny));
            if (!from.ContainsKey(s)) break;
        }
        cells.Reverse();
        cells[0] = c.A;
        cells[^1] = c.B;
        var pts = new List<Vec2> { cells[0] };
        for (int k = 1; k < cells.Count - 1; k++)
        {
            var a = pts[^1]; var b = cells[k]; var n = cells[k + 1];
            double cross = (b.X - a.X) * (n.Y - b.Y) - (b.Y - a.Y) * (n.X - b.X);
            if (Math.Abs(cross) > 1e-9) pts.Add(b);
        }
        pts.Add(cells[^1]);
        var tracks = new List<PcbTrack>();
        for (int k = 1; k < pts.Count; k++)
            if (!Near(pts[k - 1], pts[k]))
                tracks.Add(new PcbTrack { A = pts[k - 1], B = pts[k], Width = w, Layer = layer, Net = c.Net });
        return tracks;
    }

    /// <summary>
    /// Recorta la placa al tamaño justo de los componentes y pistas, dejando <paramref name="margin"/> mm
    /// alrededor, y mueve todo para que empiece en la esquina. Devuelve el área ahorrada en mm².
    /// </summary>
    public static double FitBoard(PcbDocument doc, double margin = 2)
    {
        if (doc.Footprints.Count == 0) return 0;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Grow(Vec2 a, Vec2 b) { minX = Math.Min(minX, a.X); minY = Math.Min(minY, a.Y); maxX = Math.Max(maxX, b.X); maxY = Math.Max(maxY, b.Y); }
        foreach (var f in doc.Footprints) { var (a, b) = FootprintTransform.Bounds(f); Grow(a, b); }
        foreach (var t in doc.Tracks)
        {
            double h = t.Width / 2;
            Grow(new Vec2(Math.Min(t.A.X, t.B.X) - h, Math.Min(t.A.Y, t.B.Y) - h), new Vec2(Math.Max(t.A.X, t.B.X) + h, Math.Max(t.A.Y, t.B.Y) + h));
        }
        margin = Math.Max(margin, doc.Rules.EdgeClearance);
        // Se desplaza en múltiplos de la rejilla de colocación para no descolocar nada.
        double grid = 1.27;
        double sx = Math.Round((margin - minX) / grid) * grid, sy = Math.Round((margin - minY) / grid) * grid;
        if (minX + sx < margin - 1e-6) sx += grid;
        if (minY + sy < margin - 1e-6) sy += grid;
        var shift = new Vec2(sx, sy);
        foreach (var f in doc.Footprints) f.Position += shift;
        foreach (var t in doc.Tracks) { t.A += shift; t.B += shift; }
        double before = doc.BoardWidth * doc.BoardHeight;
        doc.BoardWidth = Math.Ceiling((maxX + sx + margin) * 2) / 2;
        doc.BoardHeight = Math.Ceiling((maxY + sy + margin) * 2) / 2;
        return before - doc.BoardWidth * doc.BoardHeight;
    }
}
