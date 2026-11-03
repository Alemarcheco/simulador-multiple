using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Pcb;

public sealed record Ratsnest(Vec2 A, Vec2 B, string Net);

public sealed record DrcViolation(Vec2 Position, string Message);

/// <summary>Conexiones pendientes (ratsnest) y comprobación de reglas de diseño (DRC).</summary>
public static class PcbAnalysis
{
    /// <summary>Líneas de conexión que faltan por rutear: árbol mínimo entre los grupos ya unidos de cada red.</summary>
    public static List<Ratsnest> ComputeRatsnest(PcbDocument doc)
    {
        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).Where(p => p.Net.Length > 0).ToList();
        var groups = CopperGroups(doc, pads);
        var result = new List<Ratsnest>();

        foreach (var net in pads.GroupBy(p => p.Net))
        {
            // Agrupar los pads de la red por grupo de cobre conectado.
            var clusters = net.GroupBy(p => groups[p]).Select(g => g.ToList()).ToList();
            if (clusters.Count < 2) continue;

            // Prim sobre los grupos, usando la distancia mínima entre pads de grupos distintos.
            var inTree = new List<List<PlacedPad>> { clusters[0] };
            var outside = clusters.Skip(1).ToList();
            while (outside.Count > 0)
            {
                double best = double.MaxValue;
                (PlacedPad A, PlacedPad B, List<PlacedPad> C) pick = (inTree[0][0], outside[0][0], outside[0]);
                foreach (var t in inTree)
                    foreach (var o in outside)
                        foreach (var a in t)
                            foreach (var b in o)
                            {
                                double d = Geometry2D.Length(a.Center - b.Center);
                                if (d < best) { best = d; pick = (a, b, o); }
                            }
                result.Add(new Ratsnest(pick.A.Center, pick.B.Center, net.Key));
                inTree.Add(pick.C);
                outside.Remove(pick.C);
            }
        }
        return result;
    }

    /// <summary>Asigna a cada pad un número de grupo: pads unidos por pistas comparten grupo.</summary>
    private static Dictionary<PlacedPad, int> CopperGroups(PcbDocument doc, List<PlacedPad> pads)
    {
        int n = pads.Count + doc.Tracks.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }

        for (int t = 0; t < doc.Tracks.Count; t++)
        {
            var tr = doc.Tracks[t];
            int ti = pads.Count + t;
            for (int p = 0; p < pads.Count; p++)
                if (pads[p].OnLayer(tr.Layer) && Geometry2D.PadSegment(pads[p], tr.A, tr.B) <= 1e-6)
                    Union(ti, p);
            for (int u = t + 1; u < doc.Tracks.Count; u++)
            {
                var other = doc.Tracks[u];
                if (other.Layer != tr.Layer) continue;
                if (Geometry2D.SegmentSegment(tr.A, tr.B, other.A, other.B) <= (tr.Width + other.Width) / 2 - 1e-6)
                    Union(ti, pads.Count + u);
            }
        }
        var map = new Dictionary<PlacedPad, int>();
        for (int p = 0; p < pads.Count; p++) map[pads[p]] = Find(p);
        return map;
    }

    /// <summary>Comprueba separaciones entre cobre de redes distintas, pistas fuera de la placa y conexiones sin rutear.</summary>
    public static List<DrcViolation> CheckRules(PcbDocument doc)
    {
        var v = new List<DrcViolation>();
        var rules = doc.Rules;
        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).ToList();
        const double eps = 1e-6;

        static bool SameNet(string a, string b) => a.Length > 0 && a == b;

        foreach (var layer in doc.CopperLayers)
        {
            var tracks = doc.Tracks.Where(t => t.Layer == layer).ToList();
            var layerPads = pads.Where(p => p.OnLayer(layer)).ToList();

            for (int i = 0; i < tracks.Count; i++)
            {
                var a = tracks[i];
                for (int j = i + 1; j < tracks.Count; j++)
                {
                    var b = tracks[j];
                    if (SameNet(a.Net, b.Net)) continue;
                    double gap = Geometry2D.SegmentSegment(a.A, a.B, b.A, b.B) - (a.Width + b.Width) / 2;
                    if (gap < rules.Clearance - eps)
                        v.Add(new DrcViolation(Mid(a), gap <= 0
                            ? $"Cortocircuito entre pistas de {Name(a.Net)} y {Name(b.Net)}."
                            : $"Pistas de {Name(a.Net)} y {Name(b.Net)} a {gap:0.00} mm (mínimo {rules.Clearance} mm)."));
                }
                foreach (var p in layerPads)
                {
                    if (SameNet(a.Net, p.Net)) continue;
                    double gap = Geometry2D.PadSegment(p, a.A, a.B) - a.Width / 2;
                    if (gap < rules.Clearance - eps)
                        v.Add(new DrcViolation(p.Center, gap <= 0
                            ? $"La pista de {Name(a.Net)} toca el pad {p.Footprint.Reference}.{p.Pad.Number} ({Name(p.Net)})."
                            : $"Pista de {Name(a.Net)} a {gap:0.00} mm del pad {p.Footprint.Reference}.{p.Pad.Number} (mínimo {rules.Clearance} mm)."));
                }
                foreach (var end in new[] { a.A, a.B })
                {
                    double edge = Math.Min(Math.Min(end.X, end.Y), Math.Min(doc.BoardWidth - end.X, doc.BoardHeight - end.Y)) - a.Width / 2;
                    if (edge < rules.EdgeClearance - eps)
                    {
                        v.Add(new DrcViolation(end, edge < 0 ? "Pista fuera de la placa." : $"Pista a {edge:0.00} mm del borde (mínimo {rules.EdgeClearance} mm)."));
                        break;
                    }
                }
            }
        }

        for (int i = 0; i < pads.Count; i++)
            for (int j = i + 1; j < pads.Count; j++)
            {
                var p = pads[i]; var q = pads[j];
                if (SameNet(p.Net, q.Net) || ReferenceEquals(p.Footprint, q.Footprint) && p.Net == q.Net && p.Net.Length > 0) continue;
                if (Geometry2D.Length(p.Center - q.Center) > Math.Max(p.Pad.Size.X, p.Pad.Size.Y) + Math.Max(q.Pad.Size.X, q.Pad.Size.Y) + rules.Clearance) continue;
                double gap = Geometry2D.PadPad(p, q);
                if (gap < rules.Clearance - eps)
                    v.Add(new DrcViolation(p.Center, $"Pads {p.Footprint.Reference}.{p.Pad.Number} y {q.Footprint.Reference}.{q.Pad.Number} demasiado cerca ({gap:0.00} mm)."));
            }

        foreach (var f in doc.Footprints)
        {
            var (min, max) = FootprintTransform.Bounds(f);
            if (min.X < 0 || min.Y < 0 || max.X > doc.BoardWidth || max.Y > doc.BoardHeight)
                v.Add(new DrcViolation(f.Position, $"{f.Reference} se sale de la placa."));
        }

        foreach (var r in ComputeRatsnest(doc))
            v.Add(new DrcViolation(new Vec2((r.A.X + r.B.X) / 2, (r.A.Y + r.B.Y) / 2), $"Falta conectar la red {Name(r.Net)}."));

        return v;
    }

    private static Vec2 Mid(PcbTrack t) => new((t.A.X + t.B.X) / 2, (t.A.Y + t.B.Y) / 2);
    private static string Name(string net) => net.Length == 0 ? "(sin red)" : net == "0" ? "GND" : net;
}
