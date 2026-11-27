using Simulador.Core.Building;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Civil.Model.Electrical;

/// <summary>Aparato que se puede enchufar en un contacto durante la simulación.</summary>
public sealed record PlugLoad(string Name, double Watts);

/// <summary>
/// Simulación de la instalación de la edificación con el motor del módulo Instalaciones eléctricas: se genera el
/// esquema (acometida 220/127 V 60 Hz, termomagnéticos, cable con su resistencia según la longitud real de la tubería,
/// apagadores sencillos, de escalera y de 4 vías, lámparas, contactos y equipos) y se avanza en el tiempo.
/// </summary>
public sealed class InstallationSimulation
{
    public const double OffResistance = 1e9;
    public const double MotionHoldSeconds = 20;

    /// <summary>Aparatos que se ofrecen al hacer clic en un contacto (en orden; el último vuelve a «nada»).</summary>
    public static readonly PlugLoad[] PlugLoads =
    [
        new("Nada enchufado", 0), new("Cargador de celular", 10), new("Televisión", 100), new("Computadora", 250),
        new("Licuadora", 600), new("Refrigerador", 400), new("Horno de microondas", 1200), new("Plancha", 1200),
        new("Secadora de pelo", 1800), new("Calentador portátil", 2000),
    ];

    private readonly BuildingDocument _doc;
    private readonly Simulator _sim;
    private readonly Dictionary<string, List<Guid>> _lamps = [];          // luminaria → lámparas del esquema
    private readonly Dictionary<string, Guid> _emergency = [];            // luminaria de emergencia → sensor de tensión
    private readonly Dictionary<string, List<Guid>> _switches = [];       // "F12" / "F12#2" → elementos con esa designación
    private readonly Dictionary<string, List<(Guid Id, double Ron)>> _dimmers = [];
    private readonly Dictionary<string, Guid> _breakers = [];             // circuito → termomagnético
    private readonly Dictionary<string, Guid> _circuitGfci = [];          // circuito → diferencial
    private readonly Dictionary<string, Guid> _outletGfci = [];           // contacto GFCI → diferencial propio
    private readonly Dictionary<Guid, Guid> _leaks = [];                  // diferencial → resistencia de fuga a tierra (botón TEST)
    private readonly Dictionary<string, List<(Guid Id, double Factor)>> _outlets = []; // contacto → resistencias (R = factor / W)
    private readonly Dictionary<string, List<(Guid Id, double Ron)>> _loads = []; // equipo fijo → resistencias
    private readonly Dictionary<string, int> _plugIndex = [];
    private readonly Dictionary<string, bool> _loadOn = [];
    private readonly Dictionary<string, int> _dimLevel = [];
    private readonly Dictionary<string, double> _motionLeft = [];
    private readonly Dictionary<string, double> _circuitVolts = [];
    private readonly HashSet<string> _threeWayLast = [];

    public Schematic Schematic { get; }
    public double Time => _sim.Time;
    public IReadOnlyList<SimEvent> Events => _sim.Events;
    /// <summary>Elementos que no se simulan (sin circuito) para avisar al usuario.</summary>
    public IReadOnlyList<string> NotSimulated { get; }

    public InstallationSimulation(BuildingDocument doc)
    {
        _doc = doc;
        var notSim = new List<string>();
        Schematic = BuildSchematic(notSim);
        NotSimulated = notSim;
        _sim = new Simulator(Schematic);
        // Los de escalera empiezan en posiciones contrarias: la luz arranca apagada.
        foreach (var k in _threeWayLast) Toggle(k);
        _sim.Run(0.02);
    }

    // ------------------------------------------------------------------ construcción del esquema

    private Schematic BuildSchematic(List<string> notSim)
    {
        var el = _doc.Electrical;
        var b = new SchematicBuilder($"Instalación de {_doc.Name}", "Generado desde la pestaña Edificación (planta con tuberías y circuitos).");
        var fixtures = _doc.Fixtures.ToDictionary(f => f.Id);
        var schedule = LoadSchedule.Compute(_doc);
        int col = 0;

        foreach (var panelReport in schedule.Panels)
        {
            var panel = panelReport.Panel;
            int x0 = col * 16;
            var src = b.Add("red_3f", $"XS{panel.ComponentId}", x0, 0, ("V", 220), ("f", 60), ("Zsc", 0.05));
            b.Text(src, "rotulo", $"Acometida 220/127 V del tablero {panel.ComponentId}");

            foreach (var rep in panelReport.Circuits)
            {
                var c = rep.Circuit;
                int x = col * 16;
                int y = 8;
                col++;
                string[] lines = c.Poles switch
                {
                    1 => [PhaseTerm(c.Phase)],
                    2 => [PhaseTerm(c.Phase), PhaseTerm((Phase)(((int)c.Phase + 1) % 3))],
                    _ => ["L1", "L2", "L3"],
                };
                var q = b.Add($"magnetotermico_{Math.Clamp(c.Poles, 1, 3)}p", $"{panel.ComponentId}-{c.Id}", x, y, ("In", c.BreakerA), ("curva", 1));
                b.Text(q, "rotulo", c.Name);
                _breakers[c.Id] = q.Id;
                y += 8;

                // Cable: resistencia equivalente a la longitud media ponderada por la carga.
                double leff = EffectiveLength(rep, fixtures, panel);
                double r1 = ElectricalTables.CopperResistivity * leff / ElectricalTables.SectionMm2(c.WireAwg);
                var bus = new (PlacedElement E, string T)[lines.Length];
                for (int i = 0; i < lines.Length; i++)
                {
                    b.Wire(src, lines[i], q, (2 * i + 1).ToString());
                    // En 127 V el neutro también tiene resistencia: se suma en el conductor de fase (ida y vuelta).
                    var rw = b.Add("resistencia", $"W{c.Id}.{i + 1}", x + 2 * i, y, ("R", Math.Max(1e-4, c.Poles == 1 ? 2 * r1 : r1)));
                    b.Text(rw, "rotulo", $"Cable {c.WireAwg} AWG, {leff:0.0} m");
                    b.Wire(q, (2 * i + 2).ToString(), rw, "1");
                    bus[i] = (rw, "2");
                }
                (PlacedElement E, string T) neutral = (src, "N");
                y += 8;

                if (c.GroundFault && c.Poles == 1)
                {
                    var rcd = b.Add("diferencial_2p", $"GFCI-{c.Id}", x, y, ("In", 40), ("Idn", 10));
                    b.Wire(bus[0].E, bus[0].T, rcd, "1");
                    b.Wire(neutral.E, neutral.T, rcd, "3");
                    bus[0] = (rcd, "2");
                    neutral = (rcd, "4");
                    _circuitGfci[c.Id] = rcd.Id;
                    _leaks[rcd.Id] = Leak(b, rcd, src, x + 4, y);
                    y += 8;
                }
                _circuitVolts[c.Id] = c.Volts;

                var members = rep.Members.Select(id => fixtures[id]).ToList();
                // Lámparas agrupadas por los apagadores que las mandan.
                var lightGroups = members.Where(m => FixtureCatalog.RoleOf(m) == ElecRole.Light && m.Kind != FixtureCatalog.Emergencia.Kind)
                    .GroupBy(m => string.Join(",", el.Of(m.Id)?.ControlledBy ?? []));
                foreach (var g in lightGroups)
                {
                    var switches = g.Key.Length == 0 ? [] : g.Key.Split(',');
                    var (outE, outT) = SwitchChain(b, switches, bus[0], fixtures, x, ref y, g.Sum(m => LoadSchedule.PowerOf(_doc, m)));
                    foreach (var m in g)
                    {
                        double p = Math.Max(0.5, LoadSchedule.PowerOf(_doc, m));
                        var lamp = b.Add("lampara", m.ComponentId, x + 6, y, ("V", 127), ("P", p));
                        b.Wire(outE, outT, lamp, "1");
                        b.Wire(lamp, "2", neutral.E, neutral.T);
                        Add(_lamps, m.Id, lamp.Id);
                        y += 6;
                    }
                }
                foreach (var m in members.Where(m => m.Kind == FixtureCatalog.Emergencia.Kind))
                {
                    var sense = b.Add("resistencia", m.ComponentId, x + 6, y, ("R", 1e6));
                    b.Text(sense, "rotulo", "Cargador de la luminaria de emergencia");
                    b.Wire(bus[0].E, bus[0].T, sense, "1");
                    b.Wire(sense, "2", neutral.E, neutral.T);
                    _emergency[m.Id] = sense.Id;
                    y += 6;
                }

                foreach (var m in members.Where(m => FixtureCatalog.RoleOf(m) is ElecRole.Outlet or ElecRole.Load))
                {
                    var t = FixtureCatalog.ByKind(m.Kind)!;
                    bool load = t.Role == ElecRole.Load;
                    double watts = load ? Math.Max(1, LoadSchedule.PowerOf(_doc, m)) : 0;
                    var phaseBus = bus;
                    var n = neutral;
                    if (m.Kind == FixtureCatalog.ContactoGfci.Kind && c.Poles == 1 && !c.GroundFault)
                    {
                        var rcd = b.Add("diferencial_2p", $"GFCI-{m.ComponentId}", x + 10, y, ("In", 20), ("Idn", 10));
                        b.Wire(bus[0].E, bus[0].T, rcd, "1");
                        b.Wire(neutral.E, neutral.T, rcd, "3");
                        phaseBus = [(rcd, "2")];
                        n = (rcd, "4");
                        _outletGfci[m.Id] = rcd.Id;
                        _leaks[rcd.Id] = Leak(b, rcd, src, x + 14, y);
                    }
                    var parts = new List<(Guid, double)>();
                    // Monofásico 127 V entre fase y neutro; 220 V entre dos fases; trifásico en triángulo.
                    var pairs = new List<((PlacedElement E, string T) A, (PlacedElement E, string T) B, double V, double Share)>();
                    int needed = Math.Min(t.Phases, c.Poles);
                    if (needed <= 1) pairs.Add((phaseBus[0], n, 127, 1));
                    else if (needed == 2) pairs.Add((phaseBus[0], phaseBus[1], 220, 1));
                    else for (int i = 0; i < 3; i++) pairs.Add((phaseBus[i], phaseBus[(i + 1) % 3], 220, 1.0 / 3));
                    foreach (var (a, bb, v, share) in pairs)
                    {
                        double factor = v * v / share; // R = V² / (P·share)
                        var r = b.Add("resistencia", pairs.Count > 1 ? $"{m.ComponentId}.{parts.Count + 1}" : m.ComponentId, x + 10, y,
                            ("R", load ? factor / watts : OffResistance));
                        b.Text(r, "rotulo", load ? t.Name : $"{t.Name} (aparato enchufado)");
                        b.Wire(a.E, a.T, r, "1");
                        b.Wire(r, "2", bb.E, bb.T);
                        parts.Add((r.Id, factor));
                        y += 6;
                    }
                    if (load) { _loads[m.Id] = parts.Select(p => (p.Item1, p.Item2 / watts)).ToList(); _loadOn[m.Id] = true; }
                    else { _outlets[m.Id] = parts; _plugIndex[m.Id] = 0; }
                }
            }
        }

        foreach (var f in _doc.Fixtures)
        {
            var role = FixtureCatalog.RoleOf(f);
            if (role is ElecRole.Light or ElecRole.Outlet or ElecRole.Load && !_lamps.ContainsKey(f.Id) && !_outlets.ContainsKey(f.Id)
                && !_loads.ContainsKey(f.Id) && !_emergency.ContainsKey(f.Id))
                notSim.Add(f.Id);
        }
        return b.Schematic;
    }

    /// <summary>Resistencia de fuga de la salida del GFCI a tierra (fuera del toroidal); abierta salvo al probarlo.</summary>
    private static Guid Leak(SchematicBuilder b, PlacedElement rcd, PlacedElement src, int x, int y)
    {
        var r = b.Add("resistencia", $"Fuga-{rcd.Tag}", x, y, ("R", OffResistance));
        b.Text(r, "rotulo", "Botón TEST: fuga a tierra de 20 mA");
        b.Wire(rcd, "2", r, "1");
        b.Wire(r, "2", src, "PE");
        return r.Id;
    }

    private static string PhaseTerm(Phase p) => p switch { Phase.B => "L2", Phase.C => "L3", _ => "L1" };

    private double EffectiveLength(CircuitReport rep, Dictionary<string, PlacedFixture> fixtures, PlacedFixture panel)
    {
        double i = LoadSchedule.CurrentOf(rep.Circuit, rep.LoadVA);
        if (i < 1e-9) return rep.LongestM;
        double drop = rep.DropPercent / 100 * rep.Circuit.Volts;
        double k = rep.Circuit.Poles == 3 ? Math.Sqrt(3) : 2;
        // De ΔV = k·ρ·Σ(I·L)/S se despeja la longitud equivalente con toda la corriente.
        return drop * ElectricalTables.SectionMm2(rep.Circuit.WireAwg) / (k * ElectricalTables.CopperResistivity * i);
    }

    /// <summary>
    /// Mando de un grupo de lámparas: un apagador en serie, dos de escalera (3 vías) con sus viajeros y apagadores de
    /// 4 vías entre ellos. Devuelve el borne del retorno, que va a las lámparas.
    /// </summary>
    private (PlacedElement, string) SwitchChain(SchematicBuilder b, string[] keys, (PlacedElement E, string T) feed,
        Dictionary<string, PlacedFixture> fixtures, int x, ref int y, double groupWatts)
    {
        if (keys.Length == 0) return feed;
        var kinds = keys.Select(k => fixtures.TryGetValue(ElectricalInstallation.SwitchBase(k), out var f) ? FixtureCatalog.ByKind(f.Kind)?.Switch ?? SwitchKind.Single : SwitchKind.Single).ToArray();
        string Tag(string key)
        {
            var f = fixtures.GetValueOrDefault(ElectricalInstallation.SwitchBase(key));
            int k = ElectricalInstallation.SwitchKey(key);
            return (f?.ComponentId ?? key) + (k > 1 ? $".{k}" : "");
        }

        bool threeWay = keys.Length >= 2 && kinds[0] == SwitchKind.ThreeWay && kinds[^1] == SwitchKind.ThreeWay
            && kinds.Skip(1).Take(keys.Length - 2).All(k => k == SwitchKind.FourWay);
        if (threeWay)
        {
            var first = b.Add("conmutador", Tag(keys[0]), x + 2, y);
            Add(_switches, keys[0], first.Id);
            b.Wire(feed.E, feed.T, first, "C");
            (PlacedElement E, string T) t1 = (first, "1"), t2 = (first, "2");
            y += 6;
            for (int i = 1; i < keys.Length - 1; i++)
            {
                var mid = b.Add("cruzamiento", Tag(keys[i]), x + 2, y);
                Add(_switches, keys[i], mid.Id);
                b.Wire(t1.E, t1.T, mid, "1");
                b.Wire(t2.E, t2.T, mid, "2");
                t1 = (mid, "3"); t2 = (mid, "4");
                y += 6;
            }
            var last = b.Add("conmutador", Tag(keys[^1]), x + 2, y);
            Add(_switches, keys[^1], last.Id);
            _threeWayLast.Add(keys[^1]);
            b.Wire(t1.E, t1.T, last, "1");
            b.Wire(t2.E, t2.T, last, "2");
            y += 6;
            return (last, "C");
        }

        // Apagadores sencillos (o mezclas) en serie: la luz enciende cuando todos están cerrados.
        var cur = feed;
        for (int i = 0; i < keys.Length; i++)
        {
            PlacedElement s;
            if (kinds[i] == SwitchKind.Dimmer)
            {
                double rLamps = 127.0 * 127.0 / Math.Max(1, groupWatts);
                s = b.Add("resistencia", Tag(keys[i]), x + 2, y, ("R", 1e-3));
                b.Text(s, "rotulo", "Atenuador");
                if (!_dimmers.TryGetValue(keys[i], out var dl)) _dimmers[keys[i]] = dl = [];
                dl.Add((s.Id, rLamps));
                _dimLevel[keys[i]] = 0;
            }
            else
            {
                s = b.Add("interruptor", Tag(keys[i]), x + 2, y);
                Add(_switches, keys[i], s.Id);
            }
            b.Wire(cur.E, cur.T, s, "1");
            cur = (s, "2");
            y += 6;
        }
        return cur;
    }

    private static void Add<T>(Dictionary<string, List<T>> d, string key, T v)
    {
        if (!d.TryGetValue(key, out var l)) d[key] = l = [];
        l.Add(v);
    }

    // ------------------------------------------------------------------ avance y mandos

    /// <summary>Avanza la simulación (los sensores de presencia se apagan solos al cumplir su tiempo).</summary>
    public void Run(double seconds)
    {
        foreach (var k in _motionLeft.Keys.ToList())
        {
            _motionLeft[k] -= seconds;
            if (_motionLeft[k] <= 0)
            {
                _motionLeft.Remove(k);
                if (SwitchOn(k)) Toggle(k);
            }
        }
        _sim.Run(seconds);
    }

    /// <summary>Teclas del apagador que existen en el esquema ("F12", "F12#2").</summary>
    public IEnumerable<string> KeysOf(string switchFixtureId) =>
        _switches.Keys.Concat(_dimmers.Keys).Where(k => ElectricalInstallation.SwitchBase(k) == switchFixtureId).Distinct().OrderBy(k => k);

    /// <summary>Clic en un apagador. En el doble, <paramref name="key"/> = 2 para la segunda tecla.</summary>
    public string ClickSwitch(string fixtureId, int key = 1)
    {
        string k = key > 1 ? $"{fixtureId}#{key}" : fixtureId;
        var f = _doc.Fixtures.FirstOrDefault(x => x.Id == fixtureId);
        var kind = f is null ? SwitchKind.None : FixtureCatalog.ByKind(f.Kind)?.Switch ?? SwitchKind.None;
        if (_dimmers.TryGetValue(k, out var dims))
        {
            int level = _dimLevel[k] = (_dimLevel[k] + 1) % 4;
            // 100 %, 60 %, 30 % de tensión, apagado: R serie = R_lámparas · (1/v − 1).
            foreach (var (id, rl) in dims)
                _sim.SetParameter(id, "R", level switch { 0 => 1e-3, 1 => rl * (1 / 0.6 - 1), 2 => rl * (1 / 0.3 - 1), _ => OffResistance });
            return level switch { 0 => "al 100 %", 1 => "al 60 %", 2 => "al 30 %", _ => "apagado" };
        }
        if (!_switches.ContainsKey(k)) return "sin lámparas conectadas";
        if (kind == SwitchKind.Motion)
        {
            if (!SwitchOn(k)) Toggle(k);
            _motionLeft[k] = MotionHoldSeconds;
            _sim.Run(0.02);
            return $"detecta movimiento: luz encendida {MotionHoldSeconds:0} s";
        }
        Toggle(k);
        _sim.Run(0.02);
        return SwitchOn(k) ? "posición 1" : "posición 0";
    }

    private void Toggle(string key)
    {
        if (_switches.TryGetValue(key, out var ids) && ids.Count > 0) _sim.PointerDown(ids[0]);
    }

    private bool SwitchOn(string key) => _switches.TryGetValue(key, out var ids) && ids.Count > 0 && _sim.Status(ids[0]).Position == 1;

    /// <summary>Posición del apagador (para dibujarlo): true = palanca arriba.</summary>
    public bool SwitchPosition(string fixtureId, int key = 1)
    {
        string k = key > 1 ? $"{fixtureId}#{key}" : fixtureId;
        if (_dimLevel.TryGetValue(k, out var lvl)) return lvl < 3;
        return SwitchOn(k);
    }

    /// <summary>Clic en un contacto: enchufa el siguiente aparato de la lista. Devuelve el aparato enchufado.</summary>
    public PlugLoad CycleOutlet(string fixtureId) =>
        SetOutletLoad(fixtureId, ((_plugIndex.GetValueOrDefault(fixtureId)) + 1) % PlugLoads.Length);

    public PlugLoad SetOutletLoad(string fixtureId, int index)
    {
        if (!_outlets.TryGetValue(fixtureId, out var parts)) return PlugLoads[0];
        var pl = PlugLoads[Math.Clamp(index, 0, PlugLoads.Length - 1)];
        _plugIndex[fixtureId] = index;
        foreach (var (id, factor) in parts) _sim.SetParameter(id, "R", pl.Watts <= 0 ? OffResistance : factor / pl.Watts);
        return pl;
    }

    public PlugLoad OutletLoad(string fixtureId) => PlugLoads[_plugIndex.GetValueOrDefault(fixtureId)];

    public bool IsOutlet(string fixtureId) => _outlets.ContainsKey(fixtureId);
    public bool IsLoad(string fixtureId) => _loads.ContainsKey(fixtureId);

    /// <summary>Enciende o apaga un equipo fijo (minisplit, bomba, motor).</summary>
    public bool ToggleLoad(string fixtureId)
    {
        if (!_loads.TryGetValue(fixtureId, out var parts)) return false;
        bool on = _loadOn[fixtureId] = !_loadOn[fixtureId];
        foreach (var (id, ron) in parts) _sim.SetParameter(id, "R", on ? ron : OffResistance);
        return on;
    }

    public bool LoadOn(string fixtureId) => _loadOn.GetValueOrDefault(fixtureId);

    /// <summary>Brillo de una luminaria (0 = apagada, 1 = tensión nominal).</summary>
    public double Brightness(string fixtureId)
    {
        if (_emergency.TryGetValue(fixtureId, out var sense))
            return _sim.Status(sense).Voltage < 0.6 * 127 ? 1 : 0; // con batería al faltar la red
        return _lamps.TryGetValue(fixtureId, out var ids) ? ids.Max(id => _sim.Status(id).Brightness) : 0;
    }

    public bool IsBurnt(string fixtureId) => _lamps.TryGetValue(fixtureId, out var ids) && ids.Any(id => _sim.Status(id).Tripped);

    /// <summary>Tensión en un contacto o equipo (V).</summary>
    public double VoltageAt(string fixtureId)
    {
        if (_outlets.TryGetValue(fixtureId, out var parts) || _loads.TryGetValue(fixtureId, out parts))
            return parts.Max(p => _sim.Status(p.Id).Voltage);
        if (_lamps.TryGetValue(fixtureId, out var ids)) return ids.Max(id => _sim.Status(id).Voltage);
        if (_emergency.TryGetValue(fixtureId, out var s)) return _sim.Status(s).Voltage;
        return 0;
    }

    /// <summary>Potencia que consume ahora un contacto o equipo (W).</summary>
    public double PowerAt(string fixtureId)
    {
        double w = 0;
        if (_outlets.TryGetValue(fixtureId, out var parts) || _loads.TryGetValue(fixtureId, out parts))
            foreach (var (id, _) in parts)
            {
                var st = _sim.Status(id);
                var r = Schematic.Find(id)!.Params["R"];
                w += st.Voltage * st.Voltage / r;
            }
        return w;
    }

    public bool IsSimulated(string fixtureId) => !NotSimulated.Contains(fixtureId);

    /// <summary>Clic en un termomagnético: abrir o rearmar.</summary>
    public void ToggleBreaker(string circuitId)
    {
        if (_breakers.TryGetValue(circuitId, out var id)) { _sim.PointerDown(id); _sim.Run(0.02); }
    }

    public ElementStatus BreakerStatus(string circuitId) => _breakers.TryGetValue(circuitId, out var id) ? _sim.Status(id) : ElementStatus.Empty;

    /// <summary>Corriente que pasa por el interruptor del circuito (A, la mayor de sus polos).</summary>
    public double CircuitCurrent(string circuitId)
    {
        if (!_breakers.TryGetValue(circuitId, out var id)) return 0;
        var st = _sim.Status(id);
        return st.PoleCurrents.Length > 0 ? st.PoleCurrents.Max(Math.Abs) : Math.Abs(st.Current);
    }

    /// <summary>Tensión en el extremo del circuito (la peor lámpara, contacto o equipo con consumo).</summary>
    public double CircuitEndVoltage(string circuitId)
    {
        var members = _doc.Electrical.Fixtures.Where(f => f.CircuitId == circuitId).Select(f => f.FixtureId).ToList();
        var v = members.Select(VoltageAt).Where(x => x > 1).ToList();
        return v.Count == 0 ? 0 : v.Min();
    }

    public double CircuitNominal(string circuitId) => _circuitVolts.GetValueOrDefault(circuitId, 127);

    /// <summary>Diferencial (GFCI) de un contacto o de su circuito: estado y prueba con el botón TEST.</summary>
    public ElementStatus? GfciStatus(string fixtureId, string? circuitId)
    {
        if (_outletGfci.TryGetValue(fixtureId, out var id)) return _sim.Status(id);
        if (circuitId is not null && _circuitGfci.TryGetValue(circuitId, out id)) return _sim.Status(id);
        return null;
    }

    /// <summary>Pulsa TEST del GFCI (dispara) o lo rearma si ya disparó.</summary>
    public void PressGfci(string fixtureId, string? circuitId, bool test)
    {
        Guid id;
        if (!_outletGfci.TryGetValue(fixtureId, out id) && (circuitId is null || !_circuitGfci.TryGetValue(circuitId, out id))) return;
        if (test && _leaks.TryGetValue(id, out var leak))
        {
            _sim.SetParameter(leak, "R", 127 / 0.020);
            _sim.Run(0.05);
            _sim.SetParameter(leak, "R", OffResistance);
        }
        else
        {
            foreach (var g in new[] { _outletGfci.GetValueOrDefault(fixtureId), circuitId is null ? default : _circuitGfci.GetValueOrDefault(circuitId) })
                if (g != default && !_sim.Status(g).Closed) _sim.PointerDown(g);
        }
        _sim.Run(0.02);
    }

    /// <summary>Consumo total de la instalación (W).</summary>
    public double TotalPower()
    {
        double w = 0;
        foreach (var id in _lamps.Values.SelectMany(x => x))
        {
            var st = _sim.Status(id);
            var p = Schematic.Find(id)!;
            w += st.Voltage * st.Voltage * p.Params["P"] / (127.0 * 127.0);
        }
        foreach (var f in _outlets.Keys.Concat(_loads.Keys)) w += PowerAt(f);
        return w;
    }
}
