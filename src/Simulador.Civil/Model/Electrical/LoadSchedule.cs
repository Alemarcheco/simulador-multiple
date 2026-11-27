using Simulador.Core.Building;

namespace Simulador.Civil.Model.Electrical;

/// <summary>Resultado de un circuito en el cuadro de cargas.</summary>
public sealed record CircuitReport(
    Circuit Circuit, int Lights, int Outlets, int Loads, int Switches, double LoadVA, double Current,
    double LongestM, double DropPercent, bool Routed, IReadOnlyList<string> Members);

/// <summary>Resumen de un centro de carga.</summary>
public sealed record PanelReport(
    PlacedFixture Panel, IReadOnlyList<CircuitReport> Circuits, double[] PhaseVA, double ImbalancePercent,
    double TotalVA, double DemandVA, double MainCurrent, double SuggestedMainA);

/// <summary>Conductores y relleno de una canalización.</summary>
public sealed record ConduitReport(string RouteId, int Conductors, int WireAwg, double FillPercent, double MaxFillPercent, int SuggestedMm);

/// <summary>
/// Cuadro de cargas y comprobaciones de la instalación eléctrica (orientativas, según la NOM-001-SEDE-2012:
/// 180 VA por contacto, protección de conductores, caída de tensión ≤ 3 % en circuitos derivados, GFCI en zonas
/// húmedas, relleno de tubos, desbalance entre fases ≤ 5 %). No sustituye el cálculo de un perito.
/// </summary>
public sealed class LoadSchedule
{
    public IReadOnlyList<PanelReport> Panels { get; }
    public IReadOnlyList<ConduitReport> Conduits { get; }
    public IReadOnlyList<CheckResult> Checks { get; }

    private LoadSchedule(IReadOnlyList<PanelReport> panels, IReadOnlyList<ConduitReport> conduits, IReadOnlyList<CheckResult> checks)
    {
        Panels = panels; Conduits = conduits; Checks = checks;
    }

    /// <summary>Potencia de cálculo de un elemento (W o VA).</summary>
    public static double PowerOf(BuildingDocument doc, PlacedFixture f)
    {
        var t = FixtureCatalog.ByKind(f.Kind);
        if (t is null) return 0;
        var data = doc.Electrical.Of(f.Id);
        return t.Role switch
        {
            ElecRole.Light or ElecRole.Outlet or ElecRole.Load => data?.PowerW ?? t.PowerW,
            _ => 0,
        };
    }

    /// <summary>Corriente de un circuito con esa carga (A).</summary>
    public static double CurrentOf(Circuit c, double va) => c.Poles switch
    {
        3 => va / (Math.Sqrt(3) * 220),
        2 => va / 220,
        _ => va / 127,
    };

    public static LoadSchedule Compute(BuildingDocument doc)
    {
        var el = doc.Electrical;
        var fixtures = doc.Fixtures.ToDictionary(f => f.Id);
        var checks = new List<CheckResult>();
        var graph = new RouteGraph(doc);
        var routeLoad = new Dictionary<string, (int Conductors, int Awg)>();
        void AddConductors(string routeId, int n, int awg)
        {
            var cur = routeLoad.GetValueOrDefault(routeId, (0, 14));
            routeLoad[routeId] = (cur.Item1 + n, Math.Min(cur.Item2, awg));
        }

        var panels = new List<PanelReport>();
        foreach (var panel in doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Panel))
        {
            var reports = new List<CircuitReport>();
            var phaseVA = new double[3];
            var dist = graph.ShortestFrom(panel.Id);
            foreach (var c in el.Circuits.Where(c => c.PanelId == panel.Id))
            {
                var members = el.Fixtures.Where(x => x.CircuitId == c.Id && fixtures.ContainsKey(x.FixtureId))
                    .Select(x => fixtures[x.FixtureId]).ToList();
                int lights = 0, outlets = 0, loads = 0, switches = 0;
                double va = 0, moment = 0, longest = 0;
                bool routed = true;
                var usedRoutes = new HashSet<string>();
                foreach (var m in members)
                {
                    var t = FixtureCatalog.ByKind(m.Kind)!;
                    double p = PowerOf(doc, m);
                    switch (t.Role)
                    {
                        case ElecRole.Light: lights++; break;
                        case ElecRole.Outlet: outlets++; break;
                        case ElecRole.Load: loads++; break;
                        case ElecRole.Switch: switches++; break;
                    }
                    double len;
                    if (dist.TryGetValue(m.Id, out var d))
                    {
                        len = d.Length;
                        foreach (var r in graph.PathRoutes(dist, m.Id)) usedRoutes.Add(r);
                    }
                    else
                    {
                        // Sin tubería trazada: estimación por recorrido ortogonal (subir, cruzar y bajar).
                        len = Math.Abs(m.Position.X - panel.Position.X) + Math.Abs(m.Position.Y - panel.Position.Y)
                            + Math.Abs(m.Position.Z - panel.Position.Z) + 2;
                        if (t.Role is ElecRole.Light or ElecRole.Outlet or ElecRole.Load) routed = false;
                    }
                    longest = Math.Max(longest, len);
                    va += p;
                    moment += CurrentOf(c, p) * len;

                    if (t.Phases > c.Poles && t.Role is ElecRole.Outlet or ElecRole.Load)
                        checks.Add(new(CheckSeverity.Error, m.Id, $"{m.ComponentId} es de {t.Volts:0} V ({t.Phases} fases) pero el circuito {c.Id} es de {c.Volts:0} V con {c.Poles} polo(s)."));
                }
                double current = CurrentOf(c, va);
                double s = ElectricalTables.SectionMm2(c.WireAwg);
                double k = c.Poles == 3 ? Math.Sqrt(3) : 2;
                double drop = k * ElectricalTables.CopperResistivity * moment / s / c.Volts * 100;
                reports.Add(new(c, lights, outlets, loads, switches, va, current, longest, drop, routed, members.Select(m => m.Id).ToList()));

                // Fases ocupadas.
                int first = (int)c.Phase;
                for (int i = 0; i < c.Poles; i++) phaseVA[(first + i) % 3] += va / c.Poles;

                // Conductores por las rutas que usa el circuito: fases + neutro (127 V) + tierra común.
                int baseCond = c.Poles == 1 ? 2 : c.Poles;
                foreach (var r in usedRoutes) AddConductors(r, baseCond, c.WireAwg);

                // Comprobaciones del circuito.
                string name = $"Circuito {c.Id} ({c.Name})";
                if (members.Count == 0)
                    checks.Add(new(CheckSeverity.Info, panel.Id, $"{name}: no tiene elementos."));
                if (c.BreakerA > ElectricalTables.Ampacity(c.WireAwg) + 1e-6)
                    checks.Add(new(CheckSeverity.Error, panel.Id, $"{name}: el termomagnético de {c.BreakerA:0} A no protege el cable {c.WireAwg} AWG (admite {ElectricalTables.Ampacity(c.WireAwg):0} A). Sube el calibre o baja el interruptor."));
                if (current > c.BreakerA + 1e-6)
                    checks.Add(new(CheckSeverity.Error, panel.Id, $"{name}: {current:0.0} A con todo conectado, más que su interruptor de {c.BreakerA:0} A. Divide el circuito."));
                else if (current > 0.8 * c.BreakerA + 1e-6)
                    checks.Add(new(CheckSeverity.Warning, panel.Id, $"{name}: {current:0.0} A, más del 80 % de su interruptor de {c.BreakerA:0} A (cargas continuas al 125 %)."));
                if (drop > 3 + 1e-6)
                    checks.Add(new(CheckSeverity.Warning, panel.Id, $"{name}: caída de tensión de {drop:0.0} % (máximo recomendado 3 % en circuitos derivados). Sube a {NextAwg(c.WireAwg)} AWG o acorta el recorrido."));
                if (!routed)
                    checks.Add(new(CheckSeverity.Info, panel.Id, $"{name}: hay elementos sin tubería hasta el tablero; la longitud se ha estimado."));
                double fixedLoads = members.Where(m => FixtureCatalog.RoleOf(m) == ElecRole.Load).Sum(m => PowerOf(doc, m));
                if (fixedLoads > 0 && lights + outlets > 0 && fixedLoads > 0.5 * c.BreakerA * c.Volts)
                    checks.Add(new(CheckSeverity.Warning, panel.Id, $"{name}: los equipos fijos pasan del 50 % del circuito, que también alimenta luces o contactos (NOM 210-23). Dales circuito propio."));
                foreach (var m in members.Where(m => FixtureCatalog.RoleOf(m) == ElecRole.Outlet))
                {
                    var sp = doc.SpaceAt(m.LevelId, m.Position.Xy);
                    bool wet = sp?.Use is "baño" or "cocina" or "exterior" or "cochera" or "lavanderia" || sp is null;
                    if (wet && !c.GroundFault && m.Kind != FixtureCatalog.ContactoGfci.Kind)
                        checks.Add(new(CheckSeverity.Warning, m.Id, $"{m.ComponentId}{(sp is null ? " (fuera de las habitaciones)" : $" en {sp.Name}")}: debe tener protección GFCI (NOM 210-8). Usa un contacto GFCI o activa GFCI en el circuito {c.Id}."));
                }
            }

            double total = phaseVA.Sum();
            var used = phaseVA.Where((v, i) => i < 2 || v > 0).ToArray();
            double max = used.Max(), min = used.Min();
            double imbalance = max > 0 ? (max - min) / max * 100 : 0;
            // Demanda de vivienda (NOM 220-42): primeros 3000 VA al 100 %, el resto al 35 %. En otros usos, al 100 %.
            double demand = doc.Type == BuildingType.Vivienda ? Math.Min(total, 3000) + Math.Max(0, total - 3000) * 0.35 : total;
            bool threePh = phaseVA[2] > 0;
            double main = threePh ? demand / (Math.Sqrt(3) * 220) : demand / 220;
            double suggested = ElectricalTables.Breakers.FirstOrDefault(b => b >= main * 1.25, 100);
            // Con pocos circuitos no siempre se puede repartir mejor: si la diferencia es pequeña solo se informa.
            var usedPh = phaseVA.Take(threePh ? 3 : 2).ToArray();
            double spread = usedPh.Max() - usedPh.Min();
            if (imbalance > 5 && total > 0 && reports.Count > 1)
                checks.Add(new(spread > 500 ? CheckSeverity.Warning : CheckSeverity.Info, panel.Id, $"Tablero {panel.ComponentId}: desbalance entre fases de {imbalance:0} % (recomendado ≤ 5 %). Cambia de fase algún circuito."));
            panels.Add(new(panel, reports, phaseVA, imbalance, total, demand, main, suggested));
        }

        // Elementos sin circuito y apagadores.
        var circuitIds = el.Circuits.Select(c => c.Id).ToHashSet();
        foreach (var f in doc.Fixtures)
        {
            var role = FixtureCatalog.RoleOf(f);
            if (role is not (ElecRole.Light or ElecRole.Outlet or ElecRole.Load or ElecRole.Switch)) continue;
            var data = el.Of(f.Id);
            if (data?.CircuitId is null || !circuitIds.Contains(data.CircuitId))
                checks.Add(new(CheckSeverity.Warning, f.Id, $"{f.ComponentId}: no está asignado a ningún circuito del tablero."));
            if (role == ElecRole.Light && f.Kind != FixtureCatalog.Emergencia.Kind && (data?.ControlledBy is not { Count: > 0 }))
                checks.Add(new(CheckSeverity.Info, f.Id, $"{f.ComponentId}: no tiene apagador; en la simulación queda siempre encendida."));
            if (role == ElecRole.Light && data?.ControlledBy is { Count: > 0 } cb)
            {
                var kinds = cb.Select(s => fixtures.TryGetValue(ElectricalInstallation.SwitchBase(s), out var sw) ? FixtureCatalog.ByKind(sw.Kind)?.Switch : null).ToList();
                if (cb.Count >= 2 && kinds.Count(k => k == SwitchKind.ThreeWay) != 2)
                    checks.Add(new(CheckSeverity.Warning, f.Id, $"{f.ComponentId}: se manda desde {cb.Count} lugares; hacen falta dos apagadores de escalera (3 vías){(cb.Count > 2 ? " y de 4 vías en medio" : "")}."));
                foreach (var s in cb)
                    if (fixtures.TryGetValue(ElectricalInstallation.SwitchBase(s), out var sw) && el.Of(sw.Id)?.CircuitId is { } sc && data.CircuitId is { } lc && sc != lc)
                        checks.Add(new(CheckSeverity.Warning, f.Id, $"{f.ComponentId} es del circuito {lc} pero su apagador {sw.ComponentId} está en el {sc}."));
            }
        }

        // Retornos y viajeros por las rutas entre apagadores y lámparas.
        foreach (var f in el.Fixtures.Where(x => x.ControlledBy is { Count: > 0 }))
        {
            if (!fixtures.ContainsKey(f.FixtureId)) continue;
            foreach (var s in f.ControlledBy!.Select(ElectricalInstallation.SwitchBase).Distinct())
            {
                if (!fixtures.ContainsKey(s)) continue;
                var d = graph.ShortestFrom(s);
                if (d.ContainsKey(f.FixtureId))
                    foreach (var r in graph.PathRoutes(d, f.FixtureId)) AddConductors(r, f.ControlledBy!.Count >= 2 ? 2 : 1, 14);
            }
        }

        var conduits = new List<ConduitReport>();
        foreach (var r in doc.Routes.Where(r => r.ModuleId == "electrico"))
        {
            var spec = el.ConduitOf(r.Id);
            var (n, awg) = routeLoad.GetValueOrDefault(r.Id, (0, 14));
            if (n > 0) n += 1; // conductor de puesta a tierra
            n = spec.Conductors ?? n;
            awg = spec.WireAwg ?? awg;
            double area = n * ElectricalTables.InsulatedAreaMm2(awg);
            double fill = spec.Type == ConduitType.Charola ? 0 : area / ElectricalTables.ConduitInnerAreaMm2(spec.DiameterMm) * 100;
            double maxFill = ElectricalTables.MaxFill(n) * 100;
            int suggested = ElectricalTables.ConduitSizes.Select(x => x.Mm).Where(mm => mm >= 16)
                .FirstOrDefault(mm => area <= ElectricalTables.MaxFill(n) * ElectricalTables.ConduitInnerAreaMm2(mm), 78);
            conduits.Add(new(r.Id, n, awg, fill, maxFill, suggested));
            if (n > 0 && fill > maxFill + 1e-6 && spec.Type != ConduitType.Charola)
                checks.Add(new(CheckSeverity.Error, r.Id, $"Tubo {r.Id} de {ElectricalTables.Inch(spec.DiameterMm)}: {n} conductores {awg} AWG lo llenan al {fill:0} % (máximo {maxFill:0} %). Usa {ElectricalTables.Inch(suggested)}."));
            if (r.Segments.Count(sg => sg.Start.Xy.DistanceTo(sg.End.Xy) > 1e-6 || Math.Abs(sg.Start.Z - sg.End.Z) > 1e-6) - 1 > 4)
                checks.Add(new(CheckSeverity.Warning, r.Id, $"Tubo {r.Id}: más de 4 curvas de 90° seguidas (360°); pon una caja de paso."));
        }

        return new LoadSchedule(panels, conduits, checks);
    }

    private static int NextAwg(int awg)
    {
        int i = Array.IndexOf(ElectricalTables.Awg, awg);
        return i >= 0 && i + 1 < ElectricalTables.Awg.Length ? ElectricalTables.Awg[i + 1] : awg;
    }

    /// <summary>Texto del cuadro de cargas para guardarlo como CSV (separado por punto y coma).</summary>
    public string ToCsv()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Tablero;Circuito;Nombre;Uso;Polos;Fase;Interruptor (A);Calibre (AWG);Luminarias;Contactos;Equipos;Carga (VA);Corriente (A);Longitud máx. (m);Caída (%)");
        foreach (var p in Panels)
        {
            foreach (var c in p.Circuits)
                sb.AppendLine(string.Join(';', p.Panel.ComponentId, c.Circuit.Id, c.Circuit.Name, c.Circuit.Use, c.Circuit.Poles,
                    PhaseText(c.Circuit), c.Circuit.BreakerA, c.Circuit.WireAwg, c.Lights, c.Outlets, c.Loads,
                    c.LoadVA.ToString("0"), c.Current.ToString("0.0"), c.LongestM.ToString("0.0"), c.DropPercent.ToString("0.00")));
            sb.AppendLine($"{p.Panel.ComponentId};TOTAL;;;;;;;;;;{p.TotalVA:0};;;");
            sb.AppendLine($"{p.Panel.ComponentId};Fase A {p.PhaseVA[0]:0} VA · Fase B {p.PhaseVA[1]:0} VA · Fase C {p.PhaseVA[2]:0} VA · Desbalance {p.ImbalancePercent:0.0} %;;;;;;;;;;;;;");
            sb.AppendLine($"{p.Panel.ComponentId};Demanda {p.DemandVA:0} VA · Interruptor principal sugerido {p.SuggestedMainA:0} A;;;;;;;;;;;;;");
        }
        return sb.ToString();
    }

    public static string PhaseText(Circuit c) => c.Poles switch
    {
        1 => c.Phase.ToString(),
        2 => $"{c.Phase}{(Phase)(((int)c.Phase + 1) % 3)}",
        _ => "ABC",
    };
}

/// <summary>Grafo de las rutas eléctricas (los elementos son los nudos) para medir recorridos reales de cable.</summary>
internal sealed class RouteGraph
{
    private readonly Dictionary<string, List<(string To, string RouteId, double Length)>> _adj = [];

    public RouteGraph(BuildingDocument doc)
    {
        foreach (var r in doc.Routes.Where(r => r.ModuleId == "electrico"))
        {
            Add(r.FromFixtureId, r.ToFixtureId, r.Id, r.Length);
            Add(r.ToFixtureId, r.FromFixtureId, r.Id, r.Length);
        }
    }

    private void Add(string a, string b, string id, double len)
    {
        if (!_adj.TryGetValue(a, out var l)) _adj[a] = l = [];
        l.Add((b, id, len));
    }

    public Dictionary<string, (double Length, string? Prev, string? Route)> ShortestFrom(string start)
    {
        var dist = new Dictionary<string, (double Length, string? Prev, string? Route)> { [start] = (0, null, null) };
        var pq = new PriorityQueue<string, double>();
        pq.Enqueue(start, 0);
        while (pq.TryDequeue(out var u, out var du))
        {
            if (du > dist[u].Length + 1e-9) continue;
            if (!_adj.TryGetValue(u, out var edges)) continue;
            foreach (var (v, rid, len) in edges)
            {
                double nd = du + len;
                if (!dist.TryGetValue(v, out var cur) || nd < cur.Length - 1e-9)
                {
                    dist[v] = (nd, u, rid);
                    pq.Enqueue(v, nd);
                }
            }
        }
        return dist;
    }

    public IEnumerable<string> PathRoutes(Dictionary<string, (double Length, string? Prev, string? Route)> dist, string target)
    {
        var cur = target;
        while (dist.TryGetValue(cur, out var d) && d.Prev is not null)
        {
            yield return d.Route!;
            cur = d.Prev;
        }
    }
}
