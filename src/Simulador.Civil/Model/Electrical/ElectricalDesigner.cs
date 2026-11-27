using Simulador.Core.Building;

namespace Simulador.Civil.Model.Electrical;

/// <summary>
/// Ayudas para quien no es electricista: reparte los elementos en circuitos, enlaza cada luminaria con el apagador de su
/// habitación y traza la tubería desde el tablero. Todo queda editable después.
/// </summary>
public static class ElectricalDesigner
{
    /// <summary>Carga máxima que se pone en un circuito de alumbrado de 15 A (VA).</summary>
    public const double MaxLightingVA = 1200;
    /// <summary>Contactos por circuito de 20 A (10 × 180 VA = 1800 VA).</summary>
    public const int MaxOutletsPerCircuit = 10;

    /// <summary>Asigna a un circuito todo lo que aún no tiene (crea los circuitos necesarios en el tablero más cercano).</summary>
    public static int AutoCircuits(BuildingDocument doc, BuildingDocument.Editor e)
    {
        var el = e.Electrical;
        var panels = doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Panel).ToList();
        if (panels.Count == 0) return 0;
        int assigned = 0;
        var phaseVA = new Dictionary<string, double[]>();
        foreach (var p in panels) phaseVA[p.Id] = new double[3];
        foreach (var c in el.Circuits)
        {
            var va = el.Fixtures.Where(f => f.CircuitId == c.Id).Sum(f => doc.Fixtures.FirstOrDefault(x => x.Id == f.FixtureId) is { } fx ? LoadSchedule.PowerOf(doc, fx) : 0);
            if (phaseVA.TryGetValue(c.PanelId, out var ph)) for (int i = 0; i < c.Poles; i++) ph[((int)c.Phase + i) % 3] += va / c.Poles;
        }

        PlacedFixture NearestPanel(PlacedFixture f) => panels.MinBy(p => p.Position.DistanceTo(f.Position) + (p.LevelId == f.LevelId ? 0 : 50))!;
        Phase LightestPhase(string panelId, int poles)
        {
            var ph = phaseVA[panelId];
            if (poles >= 3) return Phase.A;
            return ph[0] <= ph[1] ? Phase.A : Phase.B;
        }
        var created = new List<string>();
        Circuit NewCircuit(PlacedFixture panel, string name, CircuitUse use, double breaker, int poles, int awg, bool gfci = false)
        {
            var c = new Circuit(el.NewCircuitId(), name, panel.Id, use, breaker, poles, LightestPhase(panel.Id, poles), awg, gfci);
            el.Circuits.Add(c);
            created.Add(c.Id);
            return c;
        }
        void Assign(PlacedFixture f, Circuit c)
        {
            var cur = el.Of(f.Id) ?? new FixtureElectrical(f.Id);
            el.Set(cur with { CircuitId = c.Id });
            double va = LoadSchedule.PowerOf(doc, f);
            var ph = phaseVA[c.PanelId];
            for (int i = 0; i < c.Poles; i++) ph[((int)c.Phase + i) % 3] += va / c.Poles;
            assigned++;
        }
        bool Unassigned(PlacedFixture f) => el.Of(f.Id)?.CircuitId is not { } id || el.Circuits.All(c => c.Id != id);

        // Equipos fijos: un circuito propio cada uno, con interruptor al 125 % de su corriente.
        foreach (var f in doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Load && Unassigned(f)).ToList())
        {
            var t = FixtureCatalog.ByKind(f.Kind)!;
            var panel = NearestPanel(f);
            int poles = t.Phases;
            double i = LoadSchedule.CurrentOf(new Circuit("", "", "", Poles: poles), LoadSchedule.PowerOf(doc, f)) * 1.25;
            double breaker = ElectricalTables.Breakers.FirstOrDefault(b => b >= i, 100);
            int awg = ElectricalTables.Awg.FirstOrDefault(a => ElectricalTables.Ampacity(a) >= breaker, 2);
            Assign(f, NewCircuit(panel, $"{t.Name} {f.ComponentId}", poles == 3 ? CircuitUse.Fuerza : CircuitUse.Especial, breaker, poles, awg));
        }

        // Contactos de 220 V y trifásicos: un circuito de 20 A por contacto.
        foreach (var f in doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Outlet && FixtureCatalog.ByKind(f.Kind)!.Phases > 1 && Unassigned(f)).ToList())
        {
            var t = FixtureCatalog.ByKind(f.Kind)!;
            Assign(f, NewCircuit(NearestPanel(f), $"{t.Name} {f.ComponentId}", CircuitUse.Especial, 20, t.Phases, 12));
        }

        // Contactos de 127 V: los de zonas húmedas en circuitos con GFCI; el resto por nivel, de 10 en 10.
        var outlets = doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Outlet && FixtureCatalog.ByKind(f.Kind)!.Phases == 1 && Unassigned(f)).ToList();
        foreach (var grp in outlets.GroupBy(f => (Panel: NearestPanel(f).Id, f.LevelId, Wet: IsWet(doc, f))))
        {
            var panel = panels.First(p => p.Id == grp.Key.Panel);
            var lvl = doc.GetLevel(grp.Key.LevelId);
            var list = grp.OrderBy(f => f.Position.X).ThenBy(f => f.Position.Y).ToList();
            for (int i = 0; i < list.Count; i += MaxOutletsPerCircuit)
            {
                var c = NewCircuit(panel, grp.Key.Wet ? $"Contactos cocina y baño ({lvl.Name})" : $"Contactos {lvl.Name}", CircuitUse.Contactos, 20, 1, 12, grp.Key.Wet);
                foreach (var f in list.Skip(i).Take(MaxOutletsPerCircuit)) Assign(f, c);
            }
        }

        // Alumbrado por nivel hasta 1200 VA por circuito, con sus apagadores.
        var lights = doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Light && Unassigned(f)).ToList();
        foreach (var grp in lights.GroupBy(f => (Panel: NearestPanel(f).Id, f.LevelId)))
        {
            var panel = panels.First(p => p.Id == grp.Key.Panel);
            var lvl = doc.GetLevel(grp.Key.LevelId);
            Circuit? c = null;
            double va = 0;
            // Las lámparas de un mismo apagador van juntas en un circuito (si caben), para no mezclar circuitos en la caja.
            string SwitchKey(PlacedFixture f) => string.Join(",", (el.Of(f.Id)?.ControlledBy ?? [])
                .Select(ElectricalInstallation.SwitchBase).Distinct().Order());
            var bunches = grp.GroupBy(SwitchKey)
                .Select(b => b.OrderBy(f => f.Position.X).ThenBy(f => f.Position.Y).ToList())
                .OrderBy(b => b.Min(f => f.Position.X)).ThenBy(b => b.Min(f => f.Position.Y));
            foreach (var bunch in bunches)
            {
                double total = bunch.Sum(f => LoadSchedule.PowerOf(doc, f));
                if (c is not null && va + total > MaxLightingVA && total <= MaxLightingVA) c = null;
                foreach (var f in bunch)
                {
                    double p = LoadSchedule.PowerOf(doc, f);
                    if (c is null || va + p > MaxLightingVA)
                    {
                        c = NewCircuit(panel, $"Alumbrado {lvl.Name}", CircuitUse.Alumbrado, 15, 1, 14);
                        va = 0;
                    }
                    Assign(f, c);
                    va += p;
                }
            }
        }

        // Apagadores: al circuito de las lámparas que mandan.
        foreach (var s in doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Switch && Unassigned(f)).ToList())
        {
            var lamp = el.Fixtures.FirstOrDefault(x => x.ControlledBy?.Any(k => ElectricalInstallation.SwitchBase(k) == s.Id) == true && x.CircuitId is not null);
            var c = lamp is not null ? el.Circuits.FirstOrDefault(x => x.Id == lamp.CircuitId) : null;
            c ??= el.Circuits.Where(x => x.Use == CircuitUse.Alumbrado && x.PanelId == NearestPanel(s).Id).FirstOrDefault();
            if (c is not null) Assign(s, c);
        }

        Rebalance(doc, el, created);
        return assigned;
    }

    /// <summary>
    /// Reparte entre las fases los circuitos de 127 V recién creados, de mayor a menor carga, cada uno a la fase menos
    /// cargada (A y B; también C si el tablero ya tiene circuitos trifásicos).
    /// </summary>
    private static void Rebalance(BuildingDocument doc, ElectricalInstallation el, List<string> created)
    {
        double Va(Circuit c) => el.Fixtures.Where(f => f.CircuitId == c.Id)
            .Sum(f => doc.Fixtures.FirstOrDefault(x => x.Id == f.FixtureId) is { } fx ? LoadSchedule.PowerOf(doc, fx) : 0);
        foreach (var panel in el.Circuits.Select(c => c.PanelId).Distinct().ToList())
        {
            var mine = el.Circuits.Where(c => c.PanelId == panel).ToList();
            var movable = mine.Where(c => c.Poles == 1 && created.Contains(c.Id)).OrderByDescending(Va).ToList();
            var ph = new double[3];
            foreach (var c in mine.Except(movable))
                for (int i = 0; i < c.Poles; i++) ph[((int)c.Phase + i) % 3] += Va(c) / c.Poles;
            int phases = mine.Any(c => c.Poles == 3) ? 3 : 2;
            foreach (var c in movable)
            {
                int best = Enumerable.Range(0, phases).MinBy(i => ph[i]);
                ph[best] += Va(c);
                el.Set(c with { Phase = (Phase)best });
            }
        }
    }

    /// <summary>Sube el calibre de los circuitos cuya caída de tensión pasa del 3 % (hasta 6 AWG).</summary>
    public static int AutoSizeWires(BuildingDocument doc, BuildingDocument.Editor e)
    {
        int n = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            foreach (var cr in LoadSchedule.Compute(doc).Panels.SelectMany(p => p.Circuits))
            {
                if (cr.DropPercent <= 3) continue;
                int i = Array.IndexOf(ElectricalTables.Awg, cr.Circuit.WireAwg);
                if (i < 0 || i + 1 >= ElectricalTables.Awg.Length || ElectricalTables.Awg[i + 1] < 6) continue;
                e.Electrical.Set(cr.Circuit with { WireAwg = ElectricalTables.Awg[i + 1] });
                n++;
                changed = true;
            }
            if (!changed) break;
        }
        return n;
    }

    private static bool IsWet(BuildingDocument doc, PlacedFixture f) =>
        f.Kind == FixtureCatalog.ContactoGfci.Kind || doc.SpaceAt(f.LevelId, f.Position.Xy)?.Use is "baño" or "cocina" or "exterior" or null;

    /// <summary>
    /// Cada luminaria sin apagador queda mandada por el apagador de su habitación (el más cercano); si en la habitación
    /// hay dos apagadores de escalera, por los dos.
    /// </summary>
    public static int AutoSwitches(BuildingDocument doc, BuildingDocument.Editor e)
    {
        var el = e.Electrical;
        int n = 0;
        var switches = doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Switch).ToList();
        foreach (var lamp in doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Light && f.Kind != FixtureCatalog.Emergencia.Kind))
        {
            var data = el.Of(lamp.Id) ?? new FixtureElectrical(lamp.Id);
            if (data.ControlledBy is { Count: > 0 }) continue;
            var space = doc.SpaceAt(lamp.LevelId, lamp.Position.Xy);
            var here = switches.Where(s => s.LevelId == lamp.LevelId && (space is null
                ? doc.SpaceAt(s.LevelId, s.Position.Xy) is null
                : doc.SpaceAt(s.LevelId, s.Position.Xy)?.Id == space.Id)).ToList();
            if (here.Count == 0) here = switches.Where(s => s.LevelId == lamp.LevelId && s.Position.Xy.DistanceTo(lamp.Position.Xy) < 6).ToList();
            if (here.Count == 0) continue;
            var threeWay = here.Where(s => FixtureCatalog.ByKind(s.Kind)?.Switch == SwitchKind.ThreeWay).ToList();
            List<string> cb;
            if (threeWay.Count >= 2)
            {
                var pair = threeWay.OrderBy(s => s.Position.Xy.DistanceTo(lamp.Position.Xy)).Take(2).ToList();
                var mids = here.Where(s => FixtureCatalog.ByKind(s.Kind)?.Switch == SwitchKind.FourWay).Select(s => s.Id);
                cb = [pair[0].Id, .. mids, pair[1].Id];
            }
            else
            {
                var s = here.Where(s => FixtureCatalog.ByKind(s.Kind)?.Switch != SwitchKind.FourWay).MinBy(s => s.Position.Xy.DistanceTo(lamp.Position.Xy));
                if (s is null) continue;
                cb = [s.Id];
            }
            el.Set(data with { ControlledBy = cb });
            n++;
        }
        return n;
    }

    /// <summary>
    /// Traza la tubería que falta: por cada circuito, un árbol desde el tablero que une cada elemento con el más cercano
    /// ya conectado (los apagadores con su lámpara). Devuelve cuántas rutas se crearon.
    /// </summary>
    public static int AutoConduits(BuildingDocument doc, BuildingDocument.Editor e, ConduitType type = ConduitType.PvcLigero)
    {
        var el = e.Electrical;
        int created = 0;
        var fixtures = doc.Fixtures.ToDictionary(f => f.Id);
        var connected = new HashSet<(string, string)>();
        foreach (var r in doc.Routes.Where(r => r.ModuleId == "electrico"))
        {
            connected.Add((r.FromFixtureId, r.ToFixtureId));
            connected.Add((r.ToFixtureId, r.FromFixtureId));
        }
        bool industrial = doc.Type == BuildingType.Industria;
        RouteStyle StyleFor(PlacedFixture a, PlacedFixture b)
        {
            var ma = FixtureCatalog.ByKind(a.Kind)?.Mount;
            var mb = FixtureCatalog.ByKind(b.Kind)?.Mount;
            if (ma == MountKind.Floor || mb == MountKind.Floor) return RouteStyle.Floor;
            return industrial ? RouteStyle.Structure : RouteStyle.Ceiling;
        }
        void Link(PlacedFixture a, PlacedFixture b)
        {
            if (a.Id == b.Id || connected.Contains((a.Id, b.Id))) return;
            var style = StyleFor(a, b);
            var t = industrial && style == RouteStyle.Structure && type == ConduitType.PvcLigero ? ConduitType.ParedDelgada : type;
            var r = e.AddRoute(new Route(e.NewId("R"), "electrico", ElectricalTables.RouteKind(t), a.Id, b.Id, RouteBuilder.Build(doc, a, b, style)));
            el.Set(new ConduitSpec(r.Id, t, 16));
            connected.Add((a.Id, b.Id));
            connected.Add((b.Id, a.Id));
            created++;
        }

        foreach (var c in el.Circuits)
        {
            if (!fixtures.TryGetValue(c.PanelId, out var panel)) continue;
            var members = el.Fixtures.Where(f => f.CircuitId == c.Id && fixtures.ContainsKey(f.FixtureId)).Select(f => fixtures[f.FixtureId]).ToList();
            // Los que ya tienen ruta con alguno del circuito o con el tablero cuentan como conectados.
            var tree = new List<PlacedFixture> { panel };
            var pending = members.ToList();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var m in pending.ToList())
                    if (tree.Any(t => connected.Contains((t.Id, m.Id)))) { tree.Add(m); pending.Remove(m); progress = true; }
            }
            // Los apagadores primero se unen a su lámpara (sale el retorno), el resto al más cercano del árbol.
            while (pending.Count > 0)
            {
                PlacedFixture? bestM = null, bestT = null;
                double best = double.MaxValue;
                foreach (var m in pending)
                    foreach (var t in tree)
                    {
                        double d = Distance(m, t);
                        if (FixtureCatalog.RoleOf(m) == ElecRole.Switch && Controls(el, m.Id, t.Id)) d *= 0.3;
                        if (d < best) { best = d; bestM = m; bestT = t; }
                    }
                Link(bestT!, bestM!);
                tree.Add(bestM!);
                pending.Remove(bestM!);
            }
        }
        return created;
    }

    private static bool Controls(ElectricalInstallation el, string switchId, string lampId) =>
        el.Of(lampId)?.ControlledBy?.Any(k => ElectricalInstallation.SwitchBase(k) == switchId) == true;

    private static double Distance(PlacedFixture a, PlacedFixture b) =>
        Math.Abs(a.Position.X - b.Position.X) + Math.Abs(a.Position.Y - b.Position.Y) + (a.LevelId == b.LevelId ? 0 : 20);

    /// <summary>Ajusta el diámetro de cada tubo al mínimo que admite sus conductores.</summary>
    public static int AutoSizeConduits(BuildingDocument doc, BuildingDocument.Editor e)
    {
        int n = 0;
        foreach (var r in LoadSchedule.Compute(doc).Conduits)
        {
            var spec = e.Electrical.ConduitOf(r.RouteId);
            if (spec.Type == ConduitType.Charola || r.Conductors == 0 || spec.DiameterMm == r.SuggestedMm) continue;
            if (r.FillPercent <= r.MaxFillPercent && spec.DiameterMm <= r.SuggestedMm) continue;
            e.Electrical.Set(spec with { DiameterMm = r.SuggestedMm });
            n++;
        }
        return n;
    }

    /// <summary>Todo de una vez: apagadores, circuitos, tubería y diámetros.</summary>
    public static string DesignAll(BuildingDocument doc)
    {
        int s = 0, c = 0, r = 0, d = 0, w = 0;
        doc.Edit(e =>
        {
            s = AutoSwitches(doc, e);
            c = AutoCircuits(doc, e);
            r = AutoConduits(doc, e);
            w = AutoSizeWires(doc, e);
            d = AutoSizeConduits(doc, e);
        });
        return $"{s} luminaria(s) enlazadas con su apagador, {c} elemento(s) asignados a circuitos, {r} tubo(s) trazados, {w} calibre(s) subidos por caída de tensión, {d} diámetro(s) de tubo ajustados.";
    }
}
