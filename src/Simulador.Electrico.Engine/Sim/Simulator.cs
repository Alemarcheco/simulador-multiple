using System.Numerics;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;

namespace Simulador.Electrico.Engine.Sim;

public enum EventSeverity { Info, Warning, Fault }

public sealed record SimEvent(double Time, EventSeverity Severity, string Text);

/// <summary>
/// Simula un esquema de mando y potencia. Cada paso:
/// 1) fija el estado de contactos y protecciones, 2) resuelve la red por análisis nodal con fasores
/// (AC) y en continua (DC) por superposición, 3) actualiza bobinas, temporizadores, protecciones,
/// lámparas y la mecánica de los motores.
/// Los elementos con la misma designación (KM1, S1, F2…) forman un aparato y comparten estado.
/// </summary>
public sealed class Simulator
{
    public const double DefaultStep = 0.001;

    private const double ContactConductance = 1000;   // 1 mΩ
    private const double BreakerConductance = 500;
    private const double FuseConductance = 333;
    private const double ThermalConductance = 200;
    private const double Gmin = 1e-9;
    private const double GroundBond = 1;               // unión neutro/0 V a tierra en el origen

    private readonly ElementCatalog _catalog;
    private readonly List<Elem> _elems = [];
    private readonly Dictionary<Guid, Elem> _byId = [];
    private readonly Dictionary<string, int> _netOfTerminal = [];
    private readonly List<Device> _devices = [];
    private readonly List<SimEvent> _events = [];
    private int _netCount;
    private Complex[] _vac = [];
    private double[] _vdc = [];
    private bool _dirty = true;
    private double _sinceSolve;
    private double _acFrequency = 50;
    private bool _hasAc, _hasDc;

    public Schematic Schematic { get; }
    public double Time { get; private set; }
    public IReadOnlyList<SimEvent> Events => _events;
    public int NetCount => _netCount;

    public Simulator(Schematic schematic, ElementCatalog? catalog = null)
    {
        Schematic = schematic;
        _catalog = catalog ?? ElementCatalog.Default;
        Build();
    }

    // ------------------------------------------------------------------ construcción

    private void Build()
    {
        foreach (var p in Schematic.Elements)
        {
            if (!_catalog.TryGet(p.Type, out var type))
            {
                Log(EventSeverity.Warning, $"Tipo de elemento desconocido «{p.Type}» ({p.Tag}); se ignora.");
                continue;
            }
            var e = new Elem(p, type);
            _elems.Add(e);
            _byId[p.Id] = e;
        }

        // Redes: unión de bornes conectados por conductores.
        var uf = new Dictionary<string, string>();
        string FindRoot(string k)
        {
            if (!uf.TryGetValue(k, out var parent)) { uf[k] = k; return k; }
            if (parent == k) return k;
            var root = FindRoot(parent);
            uf[k] = root;
            return root;
        }
        foreach (var e in _elems)
            foreach (var t in e.Type.Terminals)
                FindRoot(Key(e.Placed.Id, t.Id));
        foreach (var w in Schematic.Wires)
        {
            if (!_byId.ContainsKey(w.A.Element) || !_byId.ContainsKey(w.B.Element)) continue;
            var a = FindRoot(w.A.Key);
            var b = FindRoot(w.B.Key);
            if (a != b) uf[a] = b;
        }
        var netIds = new Dictionary<string, int>();
        foreach (var e in _elems)
        {
            e.Nets = new int[e.Type.Terminals.Count];
            for (var i = 0; i < e.Type.Terminals.Count; i++)
            {
                var key = Key(e.Placed.Id, e.Type.Terminals[i].Id);
                var root = FindRoot(key);
                if (!netIds.TryGetValue(root, out var n)) { n = netIds.Count; netIds[root] = n; }
                e.Nets[i] = n;
                _netOfTerminal[key] = n;
            }
        }
        _netCount = netIds.Count;

        // Nudo interno de la estrella de los motores de 3 bornes.
        foreach (var e in _elems.Where(e => e.Type.Behavior == Behavior.Motor3))
            e.InternalNet = _netCount++;

        // Motores.
        foreach (var e in _elems.Where(e => e.Type.Behavior is Behavior.Motor3 or Behavior.Motor6))
        {
            e.Motor = new InductionMotor(e.P("Pn"), e.P("Uw"), e.P("rpm"), e.P("cos"), e.P("eta"), e.P("kI"),
                e.P("f"), e.P("J"), e.P("carga"), e.P("tipoCarga") >= 0.5);
        }

        // Aparatos por designación.
        foreach (var group in _elems.GroupBy(e => NormalizeTag(e)))
        {
            var dev = new Device(group.Key, [.. group]);
            foreach (var e in dev.Elements) e.Device = dev;
            var drivers = dev.Elements.Where(e => e.Type.IsCoil || e.Type.IsManual || IsProtectionDriver(e.Type.Behavior)).ToList();
            var kinds = drivers.Select(d => DriverKindOf(d.Type)).Distinct().ToList();
            if (kinds.Count > 1)
                Log(EventSeverity.Warning, $"{dev.Tag}: mezcla aparatos de distinto tipo con la misma designación ({string.Join(", ", drivers.Select(d => d.Type.Name).Distinct())}).");
            dev.Driver = drivers.FirstOrDefault();
            dev.Kind = dev.Driver is null ? DriverKind.None : DriverKindOf(dev.Driver.Type);
            if (dev.Elements.Count(e => e.Type.IsCoil) > 1)
                Log(EventSeverity.Warning, $"{dev.Tag}: hay más de una bobina con la misma designación.");
            if (dev.Kind == DriverKind.None && dev.Elements.Any(e => e.Type.IsFollowerContact))
                Log(EventSeverity.Warning, $"{dev.Tag}: sus contactos no tienen bobina ni aparato que los accione; quedan en reposo.");
            if (dev.Kind is DriverKind.Breaker or DriverKind.Rcd) dev.Closed = true;
            _devices.Add(dev);
        }

        _hasAc = _elems.Any(e => e.Type.Behavior is Behavior.SourceAc1 or Behavior.Source3Ph);
        _hasDc = _elems.Any(e => e.Type.Behavior == Behavior.SourceDc);
        var firstAc = _elems.FirstOrDefault(e => e.Type.Behavior is Behavior.SourceAc1 or Behavior.Source3Ph);
        if (firstAc is not null) _acFrequency = firstAc.P("f");
        foreach (var m in _elems.Where(e => e.Motor is not null)) m.Motor!.Frequency = _acFrequency;

        if (!_elems.Any(e => e.Type.IsSource))
            Log(EventSeverity.Warning, "El esquema no tiene ninguna alimentación.");
        _vac = new Complex[_netCount];
        _vdc = new double[_netCount];
    }

    private static string Key(Guid id, string terminal) => $"{id:N}:{terminal}";

    private static string NormalizeTag(Elem e) =>
        string.IsNullOrWhiteSpace(e.Placed.Tag) ? $"#{e.Placed.Id:N}" : e.Placed.Tag.Trim().ToUpperInvariant();

    private static bool IsProtectionDriver(Behavior b) =>
        b is Behavior.Breaker or Behavior.MotorBreaker or Behavior.ThermalRelay or Behavior.Rcd or Behavior.Fuse;

    private static DriverKind DriverKindOf(ElementType t) => t.Behavior switch
    {
        Behavior.CoilContactor => DriverKind.Contactor,
        Behavior.CoilRelay => DriverKind.Relay,
        Behavior.CoilTimerOn => DriverKind.TimerOn,
        Behavior.CoilTimerOff => DriverKind.TimerOff,
        Behavior.CoilFlasher => DriverKind.Flasher,
        Behavior.CoilImpulse => DriverKind.Impulse,
        Behavior.Breaker or Behavior.MotorBreaker => DriverKind.Breaker,
        Behavior.Rcd => DriverKind.Rcd,
        Behavior.ThermalRelay => DriverKind.Thermal,
        Behavior.Fuse => DriverKind.Fuse,
        _ when t.IsManual => DriverKind.Manual,
        _ => DriverKind.None,
    };

    // ------------------------------------------------------------------ interacción del usuario

    /// <summary>Clic (o pulsación mantenida) sobre un elemento. <paramref name="alt"/> = Mayús (botón de prueba del diferencial).</summary>
    public void PointerDown(Guid id, bool alt = false)
    {
        if (!_byId.TryGetValue(id, out var e)) return;
        var dev = e.Device;
        switch (e.Type.Behavior)
        {
            case Behavior.PushNO or Behavior.PushNC:
                dev.Position = 1;
                break;
            case Behavior.Switch or Behavior.EmergencyStop or Behavior.LimitNO or Behavior.LimitNC
                or Behavior.SensorNO or Behavior.SensorNC or Behavior.TwoWay or Behavior.Intermediate:
                dev.Position = dev.Position == 0 ? 1 : 0;
                if (e.Type.Behavior == Behavior.EmergencyStop)
                    Log(EventSeverity.Info, dev.Position == 1 ? $"{dev.Tag}: seta de emergencia pulsada." : $"{dev.Tag}: seta rearmada.");
                break;
            case Behavior.Selector3:
                dev.Position = (dev.Position + 1) % 3;
                break;
            case Behavior.Rcd when alt:
                dev.TestPressed = true;
                break;
            case Behavior.Breaker or Behavior.MotorBreaker or Behavior.Rcd:
                if (dev.Closed) { dev.Closed = false; Log(EventSeverity.Info, $"{dev.Tag}: abierto a mano."); }
                else
                {
                    dev.Closed = true;
                    dev.Tripped = false;
                    Log(EventSeverity.Info, $"{dev.Tag}: rearmado y cerrado.");
                }
                break;
            case Behavior.ThermalRelay:
                if (dev.Tripped)
                {
                    if (dev.Thermal < 1.0) { dev.Tripped = false; Log(EventSeverity.Info, $"{dev.Tag}: relé térmico rearmado."); }
                    else Log(EventSeverity.Warning, $"{dev.Tag}: aún está caliente ({dev.Thermal / 1.44:P0}); espera a que enfríe para rearmar.");
                }
                break;
            case Behavior.Fuse:
                if (dev.Tripped) { dev.Tripped = false; foreach (var x in dev.Elements) x.FuseDamage = new double[x.Type.PoleCount]; Log(EventSeverity.Info, $"{dev.Tag}: fusible repuesto."); }
                break;
            case Behavior.Lamp or Behavior.Pilot:
                if (e.Burnt) { e.Burnt = false; Log(EventSeverity.Info, $"{dev.Tag}: lámpara sustituida."); }
                break;
            default:
                return;
        }
        _dirty = true;
    }

    public void PointerUp(Guid id)
    {
        if (!_byId.TryGetValue(id, out var e)) return;
        if (e.Type.IsMomentary) { e.Device.Position = 0; _dirty = true; }
        if (e.Type.Behavior == Behavior.Rcd && e.Device.TestPressed) { e.Device.TestPressed = false; _dirty = true; }
    }

    /// <summary>Ajusta la carga de un motor durante la simulación (% del par asignado).</summary>
    public void SetMotorLoad(Guid id, double percent)
    {
        if (_byId.TryGetValue(id, out var e) && e.Motor is not null) e.Motor.LoadFraction = percent / 100;
    }

    /// <summary>Vuelve al estado inicial: todo en reposo, protecciones cerradas, motores parados.</summary>
    public void Reset()
    {
        Time = 0;
        _events.Clear();
        foreach (var d in _devices)
        {
            d.Position = 0; d.CoilOn = false; d.Actuated = false; d.Pending = null; d.Elapsed = 0;
            d.Tripped = false; d.Thermal = 0; d.TestPressed = false; d.LastCoilOn = false;
            d.Closed = d.Kind is DriverKind.Breaker or DriverKind.Rcd;
        }
        foreach (var e in _elems)
        {
            e.Burnt = false; e.OverTime = 0; e.Motor?.Reset();
            e.FuseDamage = new double[e.Type.PoleCount];
            e.OvervoltageWarned = false;
        }
        _dirty = true;
    }

    // ------------------------------------------------------------------ bucle

    /// <summary>Avanza <paramref name="seconds"/> en pasos de 1 ms.</summary>
    public void Run(double seconds, double step = DefaultStep)
    {
        var n = (int)Math.Round(seconds / step);
        for (var i = 0; i < n; i++) Step(step);
    }

    public void Step(double dt = DefaultStep)
    {
        UpdateContacts();
        _sinceSolve += dt;
        if (_dirty || (_sinceSolve >= 0.005 && _elems.Any(e => e.Motor is not null && e.MotorMoving)))
        {
            Solve();
            _dirty = false;
            _sinceSolve = 0;
        }
        UpdateCoils(dt);
        UpdateProtections(dt);
        UpdateLoads(dt);
        Time += dt;
    }

    private void UpdateContacts()
    {
        foreach (var e in _elems)
        {
            var d = e.Device;
            var b = e.Type.Behavior;
            bool act = d.Actuated;
            e.Paths = b switch
            {
                Behavior.PushNO or Behavior.Switch or Behavior.LimitNO or Behavior.SensorNO => [d.Position != 0],
                Behavior.PushNC or Behavior.EmergencyStop or Behavior.LimitNC or Behavior.SensorNC => [d.Position == 0],
                Behavior.TwoWay => [d.Position == 0, d.Position == 1],               // C-1, C-2
                Behavior.Intermediate => [d.Position == 0, d.Position == 1],         // directo, cruzado
                Behavior.Selector3 => [d.Position == 1, d.Position == 2],            // C-1, C-2
                Behavior.ContactNO or Behavior.TimedContactNO => [act],
                Behavior.ContactNC or Behavior.TimedContactNC => [!act],
                Behavior.MainPoles => [act],
                Behavior.Breaker or Behavior.MotorBreaker or Behavior.Rcd => [d.Closed],
                Behavior.Fuse => [!d.Tripped],
                _ => [],
            };
        }
    }

    // ------------------------------------------------------------------ red eléctrica

    private sealed class Mna(int n)
    {
        public readonly Complex[,] G = new Complex[n, n];
        public readonly Complex[] I = new Complex[n];

        public void Y(int a, int b, Complex y)
        {
            if (a >= 0) G[a, a] += y;
            if (b >= 0) G[b, b] += y;
            if (a >= 0 && b >= 0) { G[a, b] -= y; G[b, a] -= y; }
        }

        public void Src(int a, int b, Complex i)
        {
            if (a >= 0) I[a] += i;
            if (b >= 0) I[b] -= i;
        }
    }

    private void Solve()
    {
        Array.Clear(_vac);
        Array.Clear(_vdc);
        if (_hasAc) _vac = SolveOne(ac: true);
        if (_hasDc)
        {
            var v = SolveOne(ac: false);
            for (var i = 0; i < _netCount; i++) _vdc[i] = v[i].Real;
        }
        foreach (var e in _elems.Where(e => e.Motor is not null))
        {
            var (a, b, c) = WindingNodes(e);
            Complex Vw(int p, int q) => V(p) - V(q);
            double Dw(int p, int q) => VD(p) - VD(q);
            e.Motor!.SetWindingVoltages(Vw(a[0], a[1]), Vw(b[0], b[1]), Vw(c[0], c[1]),
                new[] { Dw(a[0], a[1]), Dw(b[0], b[1]), Dw(c[0], c[1]) }.Max(Math.Abs));
            e.SlipAtSolve = e.Motor.Slip;
        }
    }

    private Complex V(int net) => net < 0 ? Complex.Zero : _vac[net];
    private double VD(int net) => net < 0 ? 0 : _vdc[net];

    private Complex[] SolveOne(bool ac)
    {
        var m = new Mna(_netCount);
        for (var i = 0; i < _netCount; i++) m.G[i, i] += Gmin;
        foreach (var e in _elems) Stamp(e, m, ac);
        return LinearSolver.Solve(m.G, m.I);
    }

    private (int[] a, int[] b, int[] c) WindingNodes(Elem e)
    {
        if (e.Type.Behavior == Behavior.Motor6)
            return ([e.Nets[0], e.Nets[3]], [e.Nets[1], e.Nets[4]], [e.Nets[2], e.Nets[5]]);
        var delta = e.P("conexion") >= 0.5;
        return delta
            ? ([e.Nets[0], e.Nets[1]], [e.Nets[1], e.Nets[2]], [e.Nets[2], e.Nets[0]])
            : ([e.Nets[0], e.InternalNet], [e.Nets[1], e.InternalNet], [e.Nets[2], e.InternalNet]);
    }

    private double CoilReactanceScale => _acFrequency / 50;

    /// <summary>Admitancia de una bobina de mando (contactor, relé, electroválvula) en AC o DC.</summary>
    private Complex CoilAdmittance(Elem e, bool ac)
    {
        var vn = e.P("V");
        var s = Math.Max(e.P("S"), 0.01);
        var acCoil = e.P("ac") >= 0.5;
        var zmag = vn * vn / s;
        if (!acCoil) return 1 / zmag; // bobina de continua: resistiva
        var r = zmag * 0.3;
        var x = zmag * Math.Sqrt(1 - 0.09);
        return ac ? 1 / new Complex(r, x * CoilReactanceScale) : 1 / r;
    }

    private void Stamp(Elem e, Mna m, bool ac)
    {
        var n = e.Nets;
        switch (e.Type.Behavior)
        {
            case Behavior.Source3Ph:
            {
                var z = Math.Max(e.P("Zsc"), 1e-4);
                var vph = e.P("V") / Math.Sqrt(3);
                for (var k = 0; k < 3; k++)
                {
                    m.Y(n[k], n[3], 1 / z);
                    if (ac) m.Src(n[k], n[3], Complex.FromPolarCoordinates(vph / z, -2 * Math.PI * k / 3));
                }
                m.Y(n[3], -1, GroundBond);
                m.Y(n[4], n[3], 1000); // PE unido al neutro en el origen (TN-S)
                break;
            }
            case Behavior.SourceAc1:
            {
                var z = Math.Max(e.P("Zsc"), 1e-4);
                m.Y(n[0], n[1], 1 / z);
                if (ac) m.Src(n[0], n[1], e.P("V") / z);
                m.Y(n[1], -1, GroundBond);
                break;
            }
            case Behavior.SourceDc:
            {
                var z = Math.Max(e.P("Zsc"), 1e-4);
                m.Y(n[0], n[1], 1 / z);
                if (!ac) m.Src(n[0], n[1], e.P("V") / z);
                m.Y(n[1], -1, GroundBond);
                break;
            }
            case Behavior.PushNO or Behavior.PushNC or Behavior.EmergencyStop or Behavior.Switch or Behavior.LimitNO
                or Behavior.LimitNC or Behavior.SensorNO or Behavior.SensorNC or Behavior.ContactNO or Behavior.ContactNC
                or Behavior.TimedContactNO or Behavior.TimedContactNC:
                if (e.Paths[0]) m.Y(n[0], n[1], ContactConductance);
                break;
            case Behavior.TwoWay: // 1, 2, C
            case Behavior.Selector3: // C, 1, 2
            {
                var (c, o1, o2) = e.Type.Behavior == Behavior.TwoWay ? (n[2], n[0], n[1]) : (n[0], n[1], n[2]);
                if (e.Paths[0]) m.Y(c, o1, ContactConductance);
                if (e.Paths[1]) m.Y(c, o2, ContactConductance);
                break;
            }
            case Behavior.Intermediate: // 1, 2 arriba; 3, 4 abajo
                if (e.Paths[0]) { m.Y(n[0], n[2], ContactConductance); m.Y(n[1], n[3], ContactConductance); }
                else { m.Y(n[0], n[3], ContactConductance); m.Y(n[1], n[2], ContactConductance); }
                break;
            case Behavior.MainPoles or Behavior.Breaker or Behavior.MotorBreaker or Behavior.Rcd or Behavior.Fuse or Behavior.ThermalRelay:
            {
                var g = e.Type.Behavior switch
                {
                    Behavior.Fuse => FuseConductance,
                    Behavior.ThermalRelay => ThermalConductance,
                    Behavior.MainPoles => ContactConductance,
                    _ => BreakerConductance,
                };
                var closed = e.Type.Behavior == Behavior.ThermalRelay || e.Paths[0];
                if (e.Type.Behavior == Behavior.Fuse)
                {
                    for (var k = 0; k < e.Type.PoleCount; k++)
                        if (e.FuseDamage[k] < 1) m.Y(n[2 * k], n[2 * k + 1], g);
                }
                else if (closed)
                {
                    for (var k = 0; k < e.Type.PoleCount; k++) m.Y(n[2 * k], n[2 * k + 1], g);
                }
                if (e.Type.Behavior == Behavior.Rcd && closed && e.Device.TestPressed)
                {
                    // Botón de prueba: resistencia desde la salida del primer polo a la entrada del último (fuera del toroidal).
                    var r = 230 / (1.5 * e.P("Idn") / 1000);
                    m.Y(n[1], n[2 * (e.Type.PoleCount - 1)], 1 / r);
                }
                break;
            }
            case Behavior.CoilContactor or Behavior.CoilRelay or Behavior.CoilTimerOn or Behavior.CoilTimerOff
                or Behavior.CoilFlasher or Behavior.CoilImpulse or Behavior.Solenoid:
                m.Y(n[0], n[1], CoilAdmittance(e, ac));
                break;
            case Behavior.Lamp or Behavior.Pilot or Behavior.Bell:
                if (!e.Burnt) m.Y(n[0], n[1], e.P("P") / (e.P("V") * e.P("V")));
                break;
            case Behavior.Heater:
                m.Y(n[0], n[1], 1 / Math.Max(e.P("R"), 1e-6));
                break;
            case Behavior.Voltmeter:
                m.Y(n[0], n[1], 1e-7);
                break;
            case Behavior.Ammeter:
                m.Y(n[0], n[1], ContactConductance);
                break;
            case Behavior.Motor3 or Behavior.Motor6:
            {
                var (a, b, c) = WindingNodes(e);
                var w = new[] { a, b, c };
                if (!ac)
                {
                    foreach (var wk in w) m.Y(wk[0], wk[1], 1 / e.Motor!.WindingResistance);
                    break;
                }
                var y = e.Motor!.WindingAdmittance();
                for (var k = 0; k < 3; k++)
                    for (var j = 0; j < 3; j++)
                    {
                        var (pk, qk) = (w[k][0], w[k][1]);
                        var (pj, qj) = (w[j][0], w[j][1]);
                        Add(m, pk, pj, y[k, j]); Add(m, pk, qj, -y[k, j]);
                        Add(m, qk, pj, -y[k, j]); Add(m, qk, qj, y[k, j]);
                    }
                break;
            }
        }
    }

    private static void Add(Mna m, int r, int c, Complex v)
    {
        if (r >= 0 && c >= 0) m.G[r, c] += v;
    }

    // ------------------------------------------------------------------ medidas

    private (Complex ac, double dc) Across(Elem e, int i, int j) => (V(e.Nets[i]) - V(e.Nets[j]), VD(e.Nets[i]) - VD(e.Nets[j]));

    private static double Rms(Complex ac, double dc) => Math.Sqrt(ac.Magnitude * ac.Magnitude + dc * dc);

    /// <summary>Tensión eficaz entre dos bornes del elemento (AC y DC superpuestas).</summary>
    public double VoltageAcross(Elem e, int i, int j)
    {
        var (a, d) = Across(e, i, j);
        return Rms(a, d);
    }

    /// <summary>Corriente de cada polo (bornes 2k-1 → 2k) de un aparato de paso, como fasor AC y valor DC.</summary>
    private (Complex ac, double dc)[] PoleCurrents(Elem e)
    {
        var poles = e.Type.PoleCount;
        var res = new (Complex, double)[poles];
        double g = e.Type.Behavior switch
        {
            Behavior.Fuse => FuseConductance,
            Behavior.ThermalRelay => ThermalConductance,
            Behavior.MainPoles => ContactConductance,
            _ => BreakerConductance,
        };
        for (var k = 0; k < poles; k++)
        {
            var conducts = e.Type.Behavior switch
            {
                Behavior.Fuse => e.FuseDamage[k] < 1,
                Behavior.ThermalRelay => true,
                _ => e.Paths.Length > 0 && e.Paths[0],
            };
            if (!conducts) continue;
            var (a, d) = Across(e, 2 * k, 2 * k + 1);
            res[k] = (a * g, d * g);
        }
        return res;
    }

    private double TwoTerminalCurrent(Elem e)
    {
        var (a, d) = Across(e, 0, 1);
        switch (e.Type.Behavior)
        {
            case Behavior.Lamp or Behavior.Pilot or Behavior.Bell:
                if (e.Burnt) return 0;
                var g = e.P("P") / (e.P("V") * e.P("V"));
                return Rms(a * g, d * g);
            case Behavior.Heater:
                return Rms(a, d) / Math.Max(e.P("R"), 1e-6);
            case Behavior.Ammeter:
                return Rms(a * ContactConductance, d * ContactConductance);
            case Behavior.CoilContactor or Behavior.CoilRelay or Behavior.CoilTimerOn or Behavior.CoilTimerOff
                or Behavior.CoilFlasher or Behavior.CoilImpulse or Behavior.Solenoid:
                return Rms(a * CoilAdmittance(e, true), d * CoilAdmittance(e, false).Real);
            default:
                if (e.Paths.Length > 0 && e.Paths[0]) return Rms(a * ContactConductance, d * ContactConductance);
                return 0;
        }
    }

    // ------------------------------------------------------------------ actualización de aparatos

    private void UpdateCoils(double dt)
    {
        foreach (var d in _devices)
        {
            if (d.Kind is DriverKind.None or DriverKind.Manual or DriverKind.Breaker or DriverKind.Rcd or DriverKind.Thermal or DriverKind.Fuse)
            {
                var act = d.Kind switch
                {
                    DriverKind.Manual => d.Position != 0,
                    DriverKind.Breaker or DriverKind.Rcd => d.Closed,
                    DriverKind.Thermal or DriverKind.Fuse => d.Tripped,
                    _ => false,
                };
                SetActuated(d, act);
                continue;
            }

            var coil = d.Driver!;
            var v = VoltageAcross(coil, 0, 1);
            var vn = coil.P("V");
            d.CoilVoltage = v;
            var on = d.CoilOn ? v >= 0.5 * vn : v >= 0.85 * vn;
            if (v > 1.15 * vn && !coil.OvervoltageWarned)
            {
                coil.OvervoltageWarned = true;
                Log(EventSeverity.Warning, $"{d.Tag}: la bobina recibe {v:0} V y es de {vn:0} V. Se quemaría.");
            }
            else if (d.CoilOn != on && on == false && v > 0.2 * vn && v < 0.5 * vn)
            {
                Log(EventSeverity.Warning, $"{d.Tag}: tensión de bobina insuficiente ({v:0} V de {vn:0} V); el aparato cae.");
            }
            d.CoilOn = on;

            switch (d.Kind)
            {
                case DriverKind.Contactor or DriverKind.Relay:
                {
                    var (closeDelay, openDelay) = d.Kind == DriverKind.Contactor ? (0.020, 0.012) : (0.010, 0.008);
                    if (on != d.Actuated)
                    {
                        if (d.Pending is null || d.Pending.Value.target != on) d.Pending = (on, Time + (on ? closeDelay : openDelay));
                        if (Time >= d.Pending.Value.at) { SetActuated(d, on); d.Pending = null; }
                    }
                    else d.Pending = null;
                    break;
                }
                case DriverKind.TimerOn:
                    if (on) { d.Elapsed += dt; SetActuated(d, d.Elapsed >= coil.P("t")); }
                    else { d.Elapsed = 0; SetActuated(d, false); }
                    break;
                case DriverKind.TimerOff:
                    if (on) { d.Elapsed = 0; SetActuated(d, true); }
                    else if (d.Actuated) { d.Elapsed += dt; if (d.Elapsed >= coil.P("t")) SetActuated(d, false); }
                    break;
                case DriverKind.Flasher:
                    if (on)
                    {
                        var period = coil.P("ton") + coil.P("toff");
                        d.Elapsed = (d.Elapsed + dt) % period;
                        SetActuated(d, d.Elapsed < coil.P("ton"));
                    }
                    else { d.Elapsed = 0; SetActuated(d, false); }
                    break;
                case DriverKind.Impulse:
                    if (on && !d.LastCoilOn) SetActuated(d, !d.Actuated);
                    break;
            }
            d.LastCoilOn = on;
        }
    }

    private void SetActuated(Device d, bool value)
    {
        if (d.Actuated == value) return;
        d.Actuated = value;
        _dirty = true;
        if (d.Kind is DriverKind.Contactor or DriverKind.Relay or DriverKind.TimerOn or DriverKind.TimerOff or DriverKind.Impulse)
            Log(EventSeverity.Info, value ? $"{d.Tag}: actúa." : $"{d.Tag}: vuelve a reposo.");
    }

    private static double ThermalTau(double cls) => cls switch { < 0.5 => 213, < 1.5 => 496, _ => 780 };

    private void UpdateProtections(double dt)
    {
        foreach (var e in _elems)
        {
            var b = e.Type.Behavior;
            if (b is not (Behavior.Fuse or Behavior.Breaker or Behavior.MotorBreaker or Behavior.ThermalRelay or Behavior.Rcd or Behavior.MainPoles))
            {
                if (e.Type.Terminals.Count == 2) e.Current = TwoTerminalCurrent(e);
                continue;
            }
            var poles = PoleCurrents(e);
            e.PoleCurrent = poles.Select(p => Rms(p.ac, p.dc)).ToArray();
            e.Current = e.PoleCurrent.Length == 0 ? 0 : e.PoleCurrent.Max();
            var d = e.Device;

            switch (b)
            {
                case Behavior.Fuse:
                {
                    var inom = e.P("In");
                    var aM = e.P("tipo") >= 0.5;
                    for (var k = 0; k < poles.Length; k++)
                    {
                        if (e.FuseDamage[k] >= 1) continue;
                        var ratio = e.PoleCurrent[k] / inom;
                        var (threshold, knee) = aM ? (4.0, 4.0) : (1.25, 2.0);
                        if (ratio > threshold) e.FuseDamage[k] += dt / (100 * Math.Pow(knee / ratio, 5));
                        else e.FuseDamage[k] *= Math.Exp(-dt / 30);
                        if (e.FuseDamage[k] >= 1)
                        {
                            d.Tripped = true;
                            _dirty = true;
                            Log(EventSeverity.Fault, $"{d.Tag}: se funde el fusible del polo {k + 1} ({e.PoleCurrent[k]:0.#} A en un fusible de {inom:0.#} A).");
                        }
                    }
                    break;
                }
                case Behavior.Breaker or Behavior.MotorBreaker or Behavior.ThermalRelay:
                {
                    var isBreaker = b == Behavior.Breaker;
                    var inom = isBreaker ? e.P("In") : e.P("Ir");
                    var ratio = e.Current / inom;
                    var tau = isBreaker ? 66 : b == Behavior.MotorBreaker ? 213 : ThermalTau(e.P("clase"));
                    var trip = isBreaker ? 1.69 : 1.44;
                    d.Thermal += (ratio * ratio - d.Thermal) * (1 - Math.Exp(-dt / tau));
                    if (b == Behavior.ThermalRelay)
                    {
                        if (!d.Tripped && d.Thermal >= trip)
                        {
                            d.Tripped = true;
                            _dirty = true;
                            Log(EventSeverity.Fault, $"{d.Tag}: dispara el relé térmico por sobrecarga ({e.Current:0.#} A con ajuste {inom:0.#} A).");
                        }
                        break;
                    }
                    if (!d.Closed) break;
                    var magnetic = isBreaker ? e.P("curva") switch { < 0.5 => 5.0, < 1.5 => 10.0, _ => 20.0 } : e.P("Im");
                    if (ratio >= magnetic)
                    {
                        Trip(d, $"{d.Tag}: disparo magnético por cortocircuito ({FormatAmps(e.Current)}, umbral {magnetic * inom:0} A).");
                    }
                    else if (d.Thermal >= trip)
                    {
                        Trip(d, $"{d.Tag}: disparo térmico por sobrecarga ({e.Current:0.#} A con calibre {inom:0.#} A).");
                    }
                    break;
                }
                case Behavior.Rcd:
                {
                    if (!d.Closed) { e.Residual = 0; break; }
                    Complex sumAc = 0; double sumDc = 0;
                    foreach (var p in poles) { sumAc += p.ac; sumDc += p.dc; }
                    e.Residual = Rms(sumAc, sumDc);
                    var idn = e.P("Idn") / 1000;
                    if (e.Residual >= idn)
                        Trip(d, $"{d.Tag}: dispara el diferencial por fuga de {e.Residual * 1000:0} mA (sensibilidad {idn * 1000:0} mA).");
                    else if (e.Current > e.P("In") * 1.45)
                        Log(EventSeverity.Warning, $"{d.Tag}: el diferencial soporta {e.Current:0} A y es de {e.P("In"):0} A; necesita un magnetotérmico aguas arriba.");
                    break;
                }
            }
        }
    }

    private static string FormatAmps(double a) => a >= 1000 ? $"{a / 1000:0.0} kA" : $"{a:0} A";

    private void Trip(Device d, string message)
    {
        d.Closed = false;
        d.Tripped = true;
        _dirty = true;
        Log(EventSeverity.Fault, message);
    }

    private void UpdateLoads(double dt)
    {
        foreach (var e in _elems)
        {
            switch (e.Type.Behavior)
            {
                case Behavior.Lamp or Behavior.Pilot or Behavior.Bell or Behavior.Solenoid:
                {
                    var v = VoltageAcross(e, 0, 1);
                    var vn = e.P("V");
                    e.Voltage = v;
                    e.Brightness = e.Burnt ? 0 : Math.Clamp(v / vn, 0, 1.3);
                    if (e.Type.Behavior == Behavior.Solenoid)
                    {
                        var on = e.SolenoidOpen ? v >= 0.5 * vn : v >= 0.85 * vn;
                        if (on != e.SolenoidOpen) Log(EventSeverity.Info, $"{e.Device.Tag}: {(on ? "abre" : "cierra")}.");
                        e.SolenoidOpen = on;
                        break;
                    }
                    if (!e.Burnt && v > 1.25 * vn)
                    {
                        e.OverTime += dt;
                        if (e.OverTime > 1.5)
                        {
                            e.Burnt = true;
                            _dirty = true;
                            Log(EventSeverity.Fault, $"{e.Device.Tag}: se funde por sobretensión ({v:0} V en una lámpara de {vn:0} V). Clic para cambiarla.");
                        }
                    }
                    else e.OverTime = 0;
                    break;
                }
                case Behavior.Voltmeter or Behavior.Heater or Behavior.Ammeter or Behavior.CoilContactor or Behavior.CoilRelay
                    or Behavior.CoilTimerOn or Behavior.CoilTimerOff or Behavior.CoilFlasher or Behavior.CoilImpulse:
                    e.Voltage = VoltageAcross(e, 0, 1);
                    break;
                case Behavior.Motor3 or Behavior.Motor6:
                {
                    var m = e.Motor!;
                    m.Step(dt);
                    e.MotorMoving = Math.Abs(m.Torque) > 1e-6 || Math.Abs(m.Omega) > 1e-3;
                    if (Math.Abs(m.Slip - e.SlipAtSolve) > 0.002) _dirty = true;
                    var (a, bb, c) = WindingNodes(e);
                    var y = m.WindingAdmittance();
                    var w = new[] { a, bb, c };
                    var vw = w.Select(x => V(x[0]) - V(x[1])).ToArray();
                    e.WindingCurrent = new double[3];
                    for (var k = 0; k < 3; k++)
                    {
                        Complex i = 0;
                        for (var j = 0; j < 3; j++) i += y[k, j] * vw[j];
                        var idc = (VD(w[k][0]) - VD(w[k][1])) / m.WindingResistance;
                        e.WindingCurrent[k] = Rms(i, idc);
                    }
                    e.Current = e.WindingCurrent.Max();
                    var running = Math.Abs(m.Omega) > 0.05 * m.SyncOmega;
                    if (running != e.WasRunning)
                    {
                        Log(EventSeverity.Info, running
                            ? $"{e.Device.Tag}: el motor arranca ({(m.Omega > 0 ? "giro a derechas" : "giro a izquierdas")})."
                            : $"{e.Device.Tag}: el motor se para.");
                        e.WasRunning = running;
                    }
                    var unbalance = m.PositiveSequenceVoltage > 1 ? m.NegativeSequenceVoltage / m.PositiveSequenceVoltage : 0;
                    var lostPhase = unbalance > 0.4 && m.PositiveSequenceVoltage > 0.2 * m.WindingVoltage;
                    if (lostPhase && !e.PhaseLossWarned)
                    {
                        e.PhaseLossWarned = true;
                        Log(EventSeverity.Warning, $"{e.Device.Tag}: le falta una fase. El motor no arranca (o se calienta si ya giraba).");
                    }
                    else if (!lostPhase) e.PhaseLossWarned = false;
                    break;
                }
            }
        }
    }

    private void Log(EventSeverity s, string text)
    {
        _events.Add(new SimEvent(Time, s, text));
        if (_events.Count > 2000) _events.RemoveRange(0, 500);
    }

    // ------------------------------------------------------------------ consultas para la vista y las pruebas

    public ElementStatus Status(Guid id)
    {
        if (!_byId.TryGetValue(id, out var e)) return ElementStatus.Empty;
        var d = e.Device;
        return new ElementStatus
        {
            Paths = e.Paths,
            Position = d.Position,
            CoilOn = d.CoilOn,
            Actuated = d.Actuated,
            Closed = d.Closed,
            Tripped = d.Tripped || e.Burnt,
            Thermal = d.Thermal,
            Voltage = e.Voltage,
            Current = e.Current,
            PoleCurrents = e.PoleCurrent,
            Brightness = e.Brightness,
            Rpm = e.Motor?.Rpm ?? 0,
            Torque = e.Motor?.Torque ?? 0,
            Residual = e.Residual,
            SolenoidOpen = e.SolenoidOpen,
            TimerElapsed = d.Elapsed,
            FuseBlown = e.FuseDamage.Select(x => x >= 1).ToArray(),
        };
    }

    public ElementStatus Status(string tag) =>
        _elems.FirstOrDefault(e => string.Equals(e.Placed.Tag, tag, StringComparison.OrdinalIgnoreCase) && (e.Type.IsCoil || e.Motor is not null || e.Type.PoleCount > 0 || e.Type.Behavior is Behavior.Lamp or Behavior.Pilot))
            is { } x ? Status(x.Placed.Id) : ElementStatus.Empty;

    /// <summary>Tensión eficaz de un borne respecto a tierra (neutro / 0 V de las fuentes).</summary>
    public double TerminalVoltage(Guid element, string terminal) =>
        _netOfTerminal.TryGetValue(Key(element, terminal), out var n) ? Rms(_vac[n], _vdc[n]) : 0;

    public int NetOf(Guid element, string terminal) => _netOfTerminal.TryGetValue(Key(element, terminal), out var n) ? n : -1;
}

/// <summary>Estado visible de un elemento durante la simulación.</summary>
public sealed class ElementStatus
{
    public static readonly ElementStatus Empty = new();
    /// <summary>Estado de cada camino de contacto (cerrado = true).</summary>
    public bool[] Paths { get; init; } = [];
    public int Position { get; init; }
    public bool CoilOn { get; init; }
    public bool Actuated { get; init; }
    public bool Closed { get; init; }
    public bool Tripped { get; init; }
    /// <summary>Calentamiento relativo (1 = régimen asignado en continuo).</summary>
    public double Thermal { get; init; }
    public double Voltage { get; init; }
    public double Current { get; init; }
    public double[] PoleCurrents { get; init; } = [];
    public double Brightness { get; init; }
    public double Rpm { get; init; }
    public double Torque { get; init; }
    public double Residual { get; init; }
    public bool SolenoidOpen { get; init; }
    public double TimerElapsed { get; init; }
    public bool[] FuseBlown { get; init; } = [];
}

internal enum DriverKind { None, Manual, Contactor, Relay, TimerOn, TimerOff, Flasher, Impulse, Breaker, Rcd, Thermal, Fuse }

internal sealed class Device(string tag, List<Elem> elements)
{
    public string Tag { get; } = tag.StartsWith('#') ? "(sin designación)" : tag;
    public List<Elem> Elements { get; } = elements;
    public Elem? Driver { get; set; }
    public DriverKind Kind { get; set; }
    public int Position { get; set; }
    public bool CoilOn { get; set; }
    public bool LastCoilOn { get; set; }
    public double CoilVoltage { get; set; }
    public bool Actuated { get; set; }
    public (bool target, double at)? Pending { get; set; }
    public double Elapsed { get; set; }
    public bool Closed { get; set; }
    public bool Tripped { get; set; }
    public double Thermal { get; set; }
    public bool TestPressed { get; set; }
}

public sealed class Elem
{
    internal Elem(PlacedElement placed, ElementType type)
    {
        Placed = placed;
        Type = type;
        FuseDamage = new double[type.PoleCount];
    }

    public PlacedElement Placed { get; }
    public ElementType Type { get; }
    internal Device Device { get; set; } = null!;
    internal int[] Nets { get; set; } = [];
    internal int InternalNet { get; set; } = -1;
    internal bool[] Paths { get; set; } = [];
    internal InductionMotor? Motor { get; set; }
    internal bool MotorMoving { get; set; }
    internal double SlipAtSolve { get; set; } = 1;
    internal bool WasRunning { get; set; }
    internal bool PhaseLossWarned { get; set; }
    internal double[] FuseDamage { get; set; }
    internal double[] PoleCurrent { get; set; } = [];
    internal double[] WindingCurrent { get; set; } = [];
    internal double Current { get; set; }
    internal double Voltage { get; set; }
    internal double Residual { get; set; }
    internal double Brightness { get; set; }
    internal bool Burnt { get; set; }
    internal double OverTime { get; set; }
    internal bool OvervoltageWarned { get; set; }
    internal bool SolenoidOpen { get; set; }

    internal double P(string key) => Placed.Param(Type, key);
}
