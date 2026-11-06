using Simulador.Hidraulica.Model;

namespace Simulador.Hidraulica.Solver;

/// <summary>
/// Simulación dinámica (periodo extendido): en cada paso resuelve caudales y presiones de la red y luego integra
/// los niveles de los tanques y el recorrido de las válvulas. El tiempo solo avanza con <see cref="Step"/>,
/// así que funciona igual dentro del banco de pruebas combinado (reloj común) que en la pestaña.
/// </summary>
public sealed class HydraulicSimulation
{
    private readonly NetworkSolver _solver;
    private readonly int[] _from, _to;
    private double[]? _lastFlows;

    public HydraulicNetwork Network { get; }
    public TimeSpan Time { get; private set; }
    /// <summary>Subpaso interno máximo (s). Los pasos más largos se trocean.</summary>
    public double MaxSubstep { get; set; } = 1.0;

    public double[] Heads { get; private set; }
    public double[] Flows { get; private set; }
    /// <summary>Caudal que rebosa de cada tanque (m³/s), por índice de nudo.</summary>
    public double[] Overflow { get; private set; }
    /// <summary>Caudal que sale por las descargas libres (aspersores, orificios) de cada nudo (m³/s).</summary>
    public double[] Emitted { get; private set; } = [];
    public bool LastSolveConverged { get; private set; } = true;
    public IReadOnlyList<string> Warnings => _warnings;
    private readonly List<string> _warnings = [];

    public HydraulicSimulation(HydraulicNetwork network)
    {
        var report = network.Validate();
        if (!report.IsValid)
            throw new InvalidOperationException("La red no se puede simular:\n" + string.Join("\n", report.Errors));
        Network = network;
        _solver = new NetworkSolver(network);
        var index = network.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(t => t.Id, t => t.i);
        _from = network.Links.Select(l => index[l.From]).ToArray();
        _to = network.Links.Select(l => index[l.To]).ToArray();
        Heads = new double[network.Nodes.Count];
        Flows = new double[network.Links.Count];
        Overflow = new double[network.Nodes.Count];
        Emitted = new double[network.Nodes.Count];
        Reset();
    }

    /// <summary>Vuelve al estado inicial del diseño y calcula el instante cero.</summary>
    public void Reset()
    {
        Time = TimeSpan.Zero;
        _lastFlows = null;
        _warnings.Clear();
        foreach (var t in Network.Nodes.OfType<Tank>()) t.Level = Math.Clamp(t.InitialLevel, 0, t.MaxLevel);
        foreach (var p in Network.Links.OfType<Pump>()) p.RelativeSpeed = p.InitiallyOn ? 1 : 0;
        foreach (var v in Network.Links.OfType<Valve>())
            v.Opening = v.OpeningSetpoint = v.Kind == ValveKind.Check ? 1 : Math.Clamp(v.InitialOpening, 0, 1);
        SolveHydraulics();
        if (ApplyPressureSwitches()) SolveHydraulics();
    }

    // ---- Mando desde la interfaz, el PLC o el motor ----

    public void SetPumpRunning(string pumpId, bool on) => Get<Pump>(pumpId).RelativeSpeed = on ? 1 : 0;

    /// <summary>Velocidad del eje en rad/s (la impone el motor del módulo eléctrico).</summary>
    public void SetPumpShaftSpeed(string pumpId, double radPerSecond)
    {
        var p = Get<Pump>(pumpId);
        p.RelativeSpeed = p.RatedSpeed > 0 ? Math.Max(radPerSecond, 0) / p.RatedSpeed : 0;
    }

    /// <summary>Apertura pedida (0–1). La válvula se mueve hacia ella según su tiempo de recorrido.</summary>
    public void SetValveOpening(string valveId, double opening)
    {
        var v = Get<Valve>(valveId);
        if (v.Kind == ValveKind.Check) return;
        v.OpeningSetpoint = Math.Clamp(opening, 0, 1);
        if (v.StrokeTime <= 0) v.Opening = v.OpeningSetpoint;
    }

    public void SetDemand(string junctionId, double demand) => Get<Junction>(junctionId).Demand = demand;

    // ---- Avance en el tiempo ----

    public void Step(TimeSpan dt)
    {
        double remaining = dt.TotalSeconds;
        while (remaining > 1e-12)
        {
            double h = Math.Min(remaining, Math.Min(MaxSubstep, LevelLimitedStep()));
            Integrate(h);
            remaining -= h;
            Time += TimeSpan.FromSeconds(h);
        }
    }

    /// <summary>Recalcula caudales y presiones sin avanzar el tiempo (tras cambiar un mando o el diseño).</summary>
    public void Refresh() => SolveHydraulics();

    /// <summary>Paso que no cambia ningún nivel más del 1 % de la altura del tanque.</summary>
    private double LevelLimitedStep()
    {
        double h = double.PositiveInfinity;
        for (int i = 0; i < Network.Nodes.Count; i++)
            if (Network.Nodes[i] is Tank t)
            {
                double rate = Math.Abs(NetInflow(i) - Overflow[i]) / t.Area;
                if (rate > 1e-12) h = Math.Min(h, 0.01 * t.MaxLevel / rate);
                if (t.Closed && rate > 1e-12)
                {
                    // Con aire comprimido la altura cambia más deprisa que el nivel: limita el salto a 0,25 m.
                    double dl = 1e-4;
                    double dHdL = 1 + (t.GasPressureAt(Math.Min(t.Level + dl, t.MaxLevel * 0.999)) - t.GasPressureAt(t.Level)) / dl / RhoG;
                    h = Math.Min(h, 0.25 / (rate * Math.Max(dHdL, 1)));
                }
            }
        return Math.Max(h, 1e-3);
    }

    private void Integrate(double h)
    {
        foreach (var v in Network.Links.OfType<Valve>())
            if (v.StrokeTime > 0 && v.Opening != v.OpeningSetpoint)
            {
                double maxMove = h / v.StrokeTime;
                v.Opening += Math.Clamp(v.OpeningSetpoint - v.Opening, -maxMove, maxMove);
            }

        for (int i = 0; i < Network.Nodes.Count; i++)
            if (Network.Nodes[i] is Tank t)
                t.Level = Math.Clamp(t.Level + (NetInflow(i) - Overflow[i]) * h / t.Area, 0, t.Closed ? t.MaxLevel * 0.999 : t.MaxLevel);

        SolveHydraulics();
        if (ApplyPressureSwitches()) SolveHydraulics();
    }

    /// <summary>Arranca o para las bombas con presostato según la presión medida. true si alguna cambió.</summary>
    private bool ApplyPressureSwitches()
    {
        bool changed = false;
        foreach (var p in Network.Links.OfType<Pump>())
        {
            if (!p.HasPressureSwitch || Network.FindNode(p.PressureSwitchNode) is null) continue;
            double pressure = PressureAt(p.PressureSwitchNode);
            bool on = p.RelativeSpeed > 0.01;
            if (on && pressure >= p.SwitchOffPressure) { p.RelativeSpeed = 0; changed = true; }
            else if (!on && pressure <= p.SwitchOnPressure) { p.RelativeSpeed = 1; changed = true; }
        }
        return changed;
    }

    /// <summary>Caudal neto que entra al nudo por sus enlaces (m³/s).</summary>
    public double NetInflow(int nodeIndex)
    {
        double s = 0;
        for (int l = 0; l < Flows.Length; l++)
        {
            if (_to[l] == nodeIndex) s += Flows[l];
            if (_from[l] == nodeIndex) s -= Flows[l];
        }
        return s;
    }

    private void SolveHydraulics()
    {
        int n = Network.Nodes.Count;
        var fixedHeads = new double[n];
        var demands = new double[n];
        var emptyTanks = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var node = Network.Nodes[i];
            fixedHeads[i] = node switch
            {
                Reservoir r => r.Elevation + r.Level,
                Tank t => t.Elevation + t.Level + t.GasPressureAt(t.Level) / RhoG,
                _ => double.NaN,
            };
            if (node is Junction j) demands[i] = j.Demand;
            if (node is Tank { Level: <= 1e-6 }) emptyTanks.Add(i);
        }

        var result = _solver.Solve(fixedHeads, demands, _lastFlows);
        Apply(result);

        // Un tanque vacío no puede entregar agua: si le sale caudal, pasa a ser un nudo sin consumo.
        bool resolve = false;
        foreach (var i in emptyTanks)
            if (NetInflow(i) < -1e-12) { fixedHeads[i] = double.NaN; resolve = true; }
        if (resolve)
        {
            result = _solver.Solve(fixedHeads, demands, _lastFlows);
            Apply(result);
        }

        for (int i = 0; i < n; i++)
            Overflow[i] = Network.Nodes[i] is Tank { Closed: false } t && t.Level >= t.MaxLevel - 1e-9 ? Math.Max(NetInflow(i), 0) : 0;

        LastSolveConverged = result.Converged;
        if (!result.Converged)
            AddWarning($"El cálculo no convergió en t = {Time.TotalSeconds:0.###} s (residuo {result.Residual:E1}).");
        for (int i = 0; i < n; i++)
            if (Network.Nodes[i] is Junction && PressureAt(i) < -50_000)
                AddWarning($"{Network.Nodes[i].Id}: presión muy negativa, riesgo de cavitación o aspiración imposible.");
    }

    private void Apply(SolveResult r)
    {
        Emitted = r.Emitted;
        Heads = r.Heads;
        Flows = r.Flows;
        _lastFlows = r.Flows;
    }

    private void AddWarning(string w)
    {
        if (!_warnings.Contains(w) && _warnings.Count < 50) _warnings.Add(w);
    }

    // ---- Resultados ----

    private double RhoG => Network.Fluid.Density * LinkLaws.Gravity;

    /// <summary>Presión manométrica (Pa). En un tanque, la del fondo.</summary>
    public double PressureAt(int nodeIndex)
    {
        var node = Network.Nodes[nodeIndex];
        return RhoG * (Heads[nodeIndex] - node.Elevation);
    }

    public double PressureAt(string nodeId) => PressureAt(NodeIndex(nodeId));
    /// <summary>Caudal que sale por la descarga libre del nudo (m³/s).</summary>
    public double EmittedAt(string nodeId) => Emitted[NodeIndex(nodeId)];
    public double HeadAt(string nodeId) => Heads[NodeIndex(nodeId)];
    public double FlowIn(string linkId) => Flows[LinkIndex(linkId)];

    public double VelocityIn(string linkId)
    {
        var link = Network.Links[LinkIndex(linkId)];
        double d = link switch { Pipe p => p.Diameter, Valve v => v.Diameter, _ => 0 };
        return d > 0 ? FlowIn(linkId) / (Math.PI * d * d / 4) : 0;
    }

    /// <summary>Altura que da la bomba ahora (m).</summary>
    public double PumpHead(string pumpId)
    {
        int l = LinkIndex(pumpId);
        return Heads[_to[l]] - Heads[_from[l]];
    }

    /// <summary>Potencia en el eje (W) = ρ·g·Q·H / η.</summary>
    public double PumpShaftPower(string pumpId)
    {
        var p = Get<Pump>(pumpId);
        double q = Math.Max(FlowIn(pumpId), 0);
        return p.RelativeSpeed < 0.01 ? 0 : RhoG * q * Math.Max(PumpHead(pumpId), 0) / Math.Max(p.Efficiency, 0.05);
    }

    /// <summary>Par resistente en el eje (N·m) que devuelve la bomba al motor.</summary>
    public double PumpTorque(string pumpId)
    {
        var p = Get<Pump>(pumpId);
        double w = p.RelativeSpeed * p.RatedSpeed;
        return w > 1e-3 ? PumpShaftPower(pumpId) / w : 0;
    }

    public double SensorValue(Sensor s) => s.Kind switch
    {
        SensorKind.Pressure => PressureAt(s.Target),
        SensorKind.Level => Get<Tank>(s.Target).Level,
        _ => FlowIn(s.Target),
    };

    public int NodeIndex(string id) => Network.Nodes.FindIndex(n => n.Id == id) is var i and >= 0
        ? i : throw new KeyNotFoundException($"No existe el nudo '{id}'.");

    public int LinkIndex(string id) => Network.Links.FindIndex(l => l.Id == id) is var i and >= 0
        ? i : throw new KeyNotFoundException($"No existe el enlace '{id}'.");

    private T Get<T>(string id) where T : class =>
        (Network.Nodes.FirstOrDefault(n => n.Id == id) as T)
        ?? (Network.Links.FirstOrDefault(l => l.Id == id) as T)
        ?? throw new KeyNotFoundException($"No existe '{id}' de tipo {typeof(T).Name}.");
}
