namespace Simulador.Hidraulica.Neumatica;

/// <summary>
/// Simulación de un circuito neumático por volúmenes concentrados.
/// <list type="bullet">
/// <item>Cada grupo de tomas unidas por mangueras es un volumen con su presión (aire isotermo, p·V = n·p₀).</item>
/// <item>El aire pasa por válvulas y reguladores según ISO 6358: q = C·p₁ en régimen crítico y
/// q = C·p₁·√(1 − ((p₂/p₁ − b)/(1 − b))²) en subcrítico.</item>
/// <item>Las cámaras de los cilindros cambian de volumen con el émbolo, que se mueve por la diferencia de fuerzas
/// (presiones, carga, muelle, rozamiento seco y viscoso) y se para en los topes.</item>
/// <item>Una toma sin manguera escapa a la atmósfera.</item>
/// </list>
/// </summary>
public sealed class PneumaticSimulation
{
    public const double Atmosphere = 101_325;
    /// <summary>Presión de referencia del aire normal (ANR) en que se expresan los caudales.</summary>
    public const double ReferencePressure = 1e5;
    private const double PortVolume = 2e-6;

    private readonly Dictionary<(string, string), int> _portNode = [];
    private readonly List<int> _tubeNode = [];
    private double[] _p = [];
    private double[] _fixed = [];   // presión absoluta impuesta o NaN
    private double[] _baseVolume = [];
    private double[] _volume = [];
    private readonly Dictionary<string, (double X, double V)> _cyl = [];
    private readonly Dictionary<string, bool> _actuated = [];
    private readonly Dictionary<string, bool> _compressorOn = [];
    private readonly HashSet<string> _pressed = [];
    private readonly Dictionary<string, bool> _coils = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(int A, int B, double C, double Bc, bool OneWay)> _restrictions = [];
    private int _atm;

    public PneumaticCircuit Circuit { get; }
    public double Time { get; private set; }
    /// <summary>Paso interno de integración (s).</summary>
    public double Substep { get; set; } = 2e-4;
    /// <summary>Caudal (m³/s normales) que ha entrado en el circuito desde las fuentes, acumulado: consumo de aire.</summary>
    public double AirConsumed { get; private set; }

    public PneumaticSimulation(PneumaticCircuit circuit)
    {
        Circuit = circuit;
        BuildNodes();
        Reset();
    }

    // ---------------------------------------------------------------- Topología

    private void BuildNodes()
    {
        var keys = Circuit.Components.SelectMany(c => c.Ports.Select(p => (c.Id, p.Name))).ToList();
        var index = keys.Select((k, i) => (k, i)).ToDictionary(t => t.k, t => t.i);
        var parent = Enumerable.Range(0, keys.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var connected = new HashSet<int>();
        foreach (var t in Circuit.Tubes)
        {
            if (!index.TryGetValue((t.FromComponent, t.FromPort), out var a) || !index.TryGetValue((t.ToComponent, t.ToPort), out var b)) continue;
            parent[Find(a)] = Find(b);
            connected.Add(a);
            connected.Add(b);
        }

        // Nodo 0: atmósfera. Cada grupo con alguna manguera es un nodo; una toma suelta escapa al aire.
        var groupNode = new Dictionary<int, int>();
        int count = 1;
        for (int i = 0; i < keys.Count; i++)
        {
            if (!connected.Contains(i)) { _portNode[keys[i]] = 0; continue; }
            int root = Find(i);
            if (!groupNode.TryGetValue(root, out var n)) groupNode[root] = n = count++;
            _portNode[keys[i]] = n;
        }
        _atm = 0;
        _p = new double[count];
        _fixed = Enumerable.Repeat(double.NaN, count).ToArray();
        _fixed[0] = Atmosphere;
        _baseVolume = new double[count];
        foreach (var (key, n) in _portNode) if (n != 0) _baseVolume[n] += PortVolume;
        foreach (var t in Circuit.Tubes)
        {
            int n = _portNode.GetValueOrDefault((t.FromComponent, t.FromPort));
            _tubeNode.Add(n);
            _baseVolume[n] += t.Volume;
        }
        foreach (var r in Circuit.Components.OfType<AirReceiver>()) _baseVolume[Node(r.Id, "1")] += r.Volume;
        foreach (var s in Circuit.Components.OfType<AirSupply>())
            if (Node(s.Id, "1") is var n and > 0) _fixed[n] = Atmosphere + s.Pressure;
        _volume = new double[count];
    }

    private int Node(string component, string port) => _portNode.GetValueOrDefault((component, port));

    /// <summary>Vuelve al estado inicial: presiones de partida, cilindros en su posición inicial y válvulas en reposo.</summary>
    public void Reset()
    {
        Time = 0;
        AirConsumed = 0;
        for (int i = 0; i < _p.Length; i++) _p[i] = double.IsNaN(_fixed[i]) ? Atmosphere : _fixed[i];
        foreach (var r in Circuit.Components.OfType<AirReceiver>())
            if (Node(r.Id, "1") is var n and > 0 && double.IsNaN(_fixed[n])) _p[n] = Math.Max(_p[n], Atmosphere + r.InitialPressure);
        _cyl.Clear();
        foreach (var c in Circuit.Components.OfType<Cylinder>()) _cyl[c.Id] = (Math.Clamp(c.InitialPosition, 0, 1) * c.Stroke, 0);
        _actuated.Clear();
        foreach (var v in Circuit.Components.OfType<DirectionalValve>()) _actuated[v.Id] = v.IsBistable && v.InitiallyActuated;
        _compressorOn.Clear();
        foreach (var k in Circuit.Components.OfType<Compressor>()) _compressorOn[k.Id] = true;
        UpdateVolumes(rescale: false);

        // Llenado inicial con los cilindros quietos, para no empezar con un transitorio de arranque.
        double t = 0;
        while (t < 1.0) { Advance(Substep, lockCylinders: true); t += Substep; }
        Time = 0;
        AirConsumed = 0;
    }

    // ---------------------------------------------------------------- Mandos

    /// <summary>Acciona o suelta el pulsador / la palanca de una válvula.</summary>
    public void SetPressed(string valveId, bool pressed)
    {
        var v = Circuit.Get<DirectionalValve>(valveId);
        if (v.Actuation == ValveActuation.Lever) { if (pressed) _actuated[v.Id] = !_actuated[v.Id]; return; }
        if (pressed) _pressed.Add(valveId); else _pressed.Remove(valveId);
    }

    public bool IsPressed(string valveId) => _pressed.Contains(valveId);

    /// <summary>Clave de una bobina para el PLC: su nombre, o "Válvula.14"/"Válvula.12" si no tiene.</summary>
    public static string CoilKey(DirectionalValve v, bool coil14) =>
        coil14 ? (string.IsNullOrWhiteSpace(v.Coil14) ? v.Id + ".14" : v.Coil14) : (string.IsNullOrWhiteSpace(v.Coil12) ? v.Id + ".12" : v.Coil12);

    public void SetCoil(string coil, bool energized) => _coils[coil] = energized;
    public bool IsCoilEnergized(string coil) => _coils.GetValueOrDefault(coil);

    /// <summary>Todas las bobinas del circuito.</summary>
    public IEnumerable<string> Coils => Circuit.Components.OfType<DirectionalValve>().SelectMany(v => v.Actuation switch
    {
        ValveActuation.Solenoid => [CoilKey(v, true)],
        ValveActuation.DoubleSolenoid => new[] { CoilKey(v, true), CoilKey(v, false) },
        _ => [],
    });

    // ---------------------------------------------------------------- Lecturas

    /// <summary>Presión manométrica (Pa) en una toma.</summary>
    public double PressureAt(string component, string port) => _p[Node(component, port)] - Atmosphere;
    /// <summary>Presión manométrica (Pa) dentro de una manguera.</summary>
    public double TubePressure(int tubeIndex) => _p[_tubeNode[tubeIndex]] - Atmosphere;
    public double Position(string cylinder) => _cyl[cylinder].X;
    public double Velocity(string cylinder) => _cyl[cylinder].V;
    public double PositionRatio(string cylinder) => _cyl[cylinder].X / Circuit.Get<Cylinder>(cylinder).Stroke;
    public bool IsActuated(string valveId) => _actuated.GetValueOrDefault(valveId);
    public bool IsCompressorRunning(string id) => _compressorOn.GetValueOrDefault(id);

    /// <summary>Fuerza neta de las presiones sobre el émbolo (N, positiva hacia fuera).</summary>
    public double PressureForce(string cylinder)
    {
        var c = Circuit.Get<Cylinder>(cylinder);
        double pa = PressureAt(c.Id, "A");
        double pb = c.Kind == CylinderKind.DoubleActing ? PressureAt(c.Id, "B") : 0;
        return pa * c.PistonArea - pb * c.AnnulusArea;
    }

    /// <summary>Estado de un detector magnético por su nombre ("1S1"…).</summary>
    public bool SensorActive(string name)
    {
        foreach (var c in Circuit.Components.OfType<Cylinder>())
        {
            double tol = Math.Max(0.002, 0.01 * c.Stroke);
            if (c.SensorRetracted == name && _cyl[c.Id].X <= tol) return true;
            if (c.SensorExtended == name && _cyl[c.Id].X >= c.Stroke - tol) return true;
        }
        return false;
    }

    public IEnumerable<string> Sensors => Circuit.Components.OfType<Cylinder>()
        .SelectMany(c => new[] { c.SensorRetracted, c.SensorExtended }).Where(s => !string.IsNullOrWhiteSpace(s));

    // ---------------------------------------------------------------- Integración

    public void Step(TimeSpan dt) => Step(dt.TotalSeconds);

    public void Step(double seconds)
    {
        int n = Math.Max(1, (int)Math.Ceiling(seconds / Substep - 1e-9));
        double h = seconds / n;
        for (int i = 0; i < n; i++) Advance(h, lockCylinders: false);
    }

    private void Advance(double h, bool lockCylinders)
    {
        UpdateValves();
        BuildRestrictions();

        // Compresores: caudal que baja linealmente con la presión, con presostato todo/nada.
        foreach (var k in Circuit.Components.OfType<Compressor>())
        {
            int n = Node(k.Id, "1");
            if (n == 0) continue;
            double pg = _p[n] - Atmosphere;
            if (pg >= k.CutOutPressure) _compressorOn[k.Id] = false;
            else if (pg <= k.CutInPressure) _compressorOn[k.Id] = true;
            if (!_compressorOn[k.Id] || pg >= k.MaxPressure || !double.IsNaN(_fixed[n])) continue;
            double q = k.FreeAirDelivery * (1 - Math.Max(pg, 0) / k.MaxPressure);
            _p[n] += q * h * ReferencePressure / _volume[n];
            AirConsumed += q * h;
        }

        // Paso de aire por cada restricción, limitado para que nunca se inviertan las presiones (estable con cualquier paso).
        foreach (var (a, b, c, bc, oneWay) in _restrictions)
        {
            int up = _p[a] >= _p[b] ? a : b, down = up == a ? b : a;
            if (oneWay && up != a) continue;
            double p1 = _p[up], p2 = _p[down];
            if (p1 - p2 < 1e-6) continue;
            double q = Iso6358(c, bc, p1, p2) * h; // m³ normales en este paso
            bool upFixed = !double.IsNaN(_fixed[up]), downFixed = !double.IsNaN(_fixed[down]);
            if (upFixed && downFixed) { if (up != _atm) AirConsumed += q; continue; }
            double vu = upFixed ? double.PositiveInfinity : _volume[up];
            double vd = downFixed ? double.PositiveInfinity : _volume[down];
            double veff = double.IsInfinity(vu) ? vd : double.IsInfinity(vd) ? vu : vu * vd / (vu + vd);
            double qMax = (p1 - p2) * veff / ReferencePressure;
            q = Math.Min(q, qMax);
            if (!upFixed) _p[up] -= q * ReferencePressure / vu;
            else if (up != _atm) AirConsumed += q;
            if (!downFixed) _p[down] += q * ReferencePressure / vd;
        }

        if (!lockCylinders) MoveCylinders(h);
        UpdateVolumes(rescale: true);
        Time += h;
    }

    /// <summary>Caudal normal (m³/s) por una restricción ISO 6358 de conductancia C y relación crítica b.</summary>
    public static double Iso6358(double c, double b, double p1, double p2)
    {
        double r = p2 / p1;
        if (r <= b) return c * p1;
        const double rLin = 0.995; // tramo lineal junto a Δp = 0 para que el caudal no tenga pendiente infinita
        double Sub(double x) { double y = (x - b) / (1 - b); return c * p1 * Math.Sqrt(Math.Max(0, 1 - y * y)); }
        return r < rLin ? Sub(r) : Sub(rLin) * (1 - r) / (1 - rLin);
    }

    private void UpdateValves()
    {
        foreach (var v in Circuit.Components.OfType<DirectionalValve>())
        {
            bool now = _actuated[v.Id];
            switch (v.Actuation)
            {
                case ValveActuation.PushButton: now = _pressed.Contains(v.Id); break;
                case ValveActuation.Lever: break;
                case ValveActuation.Solenoid: now = IsCoilEnergized(CoilKey(v, true)); break;
                case ValveActuation.DoubleSolenoid:
                {
                    bool a = IsCoilEnergized(CoilKey(v, true)), b = IsCoilEnergized(CoilKey(v, false));
                    if (a && !b) now = true; else if (b && !a) now = false;
                    break;
                }
                case ValveActuation.Roller:
                    if (Circuit.Find(v.RollerCylinder) is Cylinder c && _cyl.TryGetValue(c.Id, out var s))
                        now = Math.Abs(s.X - Math.Clamp(v.RollerPosition, 0, 1) * c.Stroke) <= Math.Max(0.003, 0.015 * c.Stroke);
                    else now = false;
                    break;
                case ValveActuation.Pilot: now = PressureAt(v.Id, "14") >= v.PilotPressure; break;
                case ValveActuation.DoublePilot:
                {
                    bool a = PressureAt(v.Id, "14") >= v.PilotPressure, b = PressureAt(v.Id, "12") >= v.PilotPressure;
                    if (a && !b) now = true; else if (b && !a) now = false;
                    break;
                }
            }
            _actuated[v.Id] = now;
        }
    }

    private void BuildRestrictions()
    {
        _restrictions.Clear();
        void Add(int a, int b, double c, double bc = 0.3, bool oneWay = false) { if (a != b && c > 0) _restrictions.Add((a, b, c, bc, oneWay)); }
        foreach (var comp in Circuit.Components)
        {
            int N(string port) => Node(comp.Id, port);
            switch (comp)
            {
                case DirectionalValve v:
                {
                    bool on = _actuated[v.Id];
                    if (v.Ways == ValveWays.ThreeTwo)
                    {
                        bool open = on ^ v.NormallyOpen;
                        if (open) Add(N("1"), N("2"), v.Conductance, v.CriticalRatio);
                        else Add(N("2"), _atm, v.Conductance, v.CriticalRatio);
                    }
                    else if (on) { Add(N("1"), N("4"), v.Conductance, v.CriticalRatio); Add(N("2"), _atm, v.Conductance, v.CriticalRatio); }
                    else { Add(N("1"), N("2"), v.Conductance, v.CriticalRatio); Add(N("4"), _atm, v.Conductance, v.CriticalRatio); }
                    break;
                }
                case FlowControl f:
                    Add(N("1"), N("2"), f.MaxConductance * Math.Clamp(f.Opening, 0, 1), 0.3);
                    if (f.OneWay) Add(N("2"), N("1"), Math.Max(f.MaxConductance, 1e-8), 0.3, oneWay: true);
                    break;
                case CheckValve k:
                    Add(N("1"), N("2"), k.Conductance, 0.3, oneWay: true);
                    break;
                case LogicValve l:
                {
                    int a = N("1a"), b = N("1b");
                    bool pickA = l.Kind == LogicKind.Or ? _p[a] >= _p[b] : _p[a] <= _p[b];
                    Add(pickA ? a : b, N("2"), l.Conductance, 0.3);
                    break;
                }
            }
        }
    }

    private void MoveCylinders(double h)
    {
        foreach (var c in Circuit.Components.OfType<Cylinder>())
        {
            var (x, v) = _cyl[c.Id];
            double f = PressureForce(c.Id) - c.Load;
            if (c.Kind == CylinderKind.SingleActing) f -= c.SpringPreload + c.SpringRate * x;
            f -= c.ViscousFriction * v;
            // Rozamiento seco: si está parado y no vence el rozamiento, sigue parado.
            if (Math.Abs(v) < 1e-4)
            {
                if (Math.Abs(f) <= c.Friction) { _cyl[c.Id] = (x, 0); continue; }
                f -= Math.Sign(f) * c.Friction;
            }
            else f -= Math.Sign(v) * c.Friction;
            v += f / c.Mass * h;
            x += v * h;
            if (x <= 0) { x = 0; if (v < 0) v = 0; }
            if (x >= c.Stroke) { x = c.Stroke; if (v > 0) v = 0; }
            _cyl[c.Id] = (x, v);
        }
    }

    private void UpdateVolumes(bool rescale)
    {
        var vol = (double[])_baseVolume.Clone();
        foreach (var c in Circuit.Components.OfType<Cylinder>())
        {
            double x = _cyl.TryGetValue(c.Id, out var s) ? s.X : 0;
            vol[Node(c.Id, "A")] += c.DeadVolume + c.PistonArea * x;
            if (c.Kind == CylinderKind.DoubleActing) vol[Node(c.Id, "B")] += c.DeadVolume + c.AnnulusArea * (c.Stroke - x);
        }
        for (int i = 0; i < vol.Length; i++)
        {
            vol[i] = Math.Max(vol[i], 1e-7);
            // Compresión o expansión isoterma de la cámara al moverse el émbolo.
            if (rescale && double.IsNaN(_fixed[i]) && _volume[i] > 0) _p[i] *= _volume[i] / vol[i];
        }
        _volume = vol;
    }
}
