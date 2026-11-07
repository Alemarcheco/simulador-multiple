using Simulador.Hidraulica.Model;

namespace Simulador.Hidraulica.Solver;

/// <summary>Resultado de un análisis de golpe de ariete.</summary>
public sealed class WaterHammerResult
{
    public required double WaveSpeed { get; init; }          // m/s
    public required double Length { get; init; }             // m
    public required double InitialVelocity { get; init; }    // m/s
    public required double ClosingTime { get; init; }        // s
    /// <summary>Tiempo crítico 2L/a (s): un cierre más rápido da la sobrepresión completa de Joukowsky.</summary>
    public double CriticalTime => 2 * Length / WaveSpeed;
    /// <summary>Sobrepresión de Joukowsky ρ·a·ΔV (Pa).</summary>
    public required double JoukowskyRise { get; init; }
    public required double SteadyPressure { get; init; }     // Pa en la válvula antes del cierre
    public required double MaxPressure { get; init; }        // Pa
    public required double MinPressure { get; init; }        // Pa
    public required double[] Times { get; init; }
    public required double[] ValvePressure { get; init; }    // Pa
    public required double[] MidPressure { get; init; }      // Pa en la mitad de la conducción
    public bool FastClosure => ClosingTime <= CriticalTime;
    /// <summary>La presión baja de la de vapor: puede romperse la columna de agua y el cálculo deja de ser fiable.</summary>
    public bool ColumnSeparationRisk => MinPressure < -0.95e5;
    public required IReadOnlyList<string> PipeIds { get; init; }
}

/// <summary>
/// Golpe de ariete por el método de las características en una conducción simple: depósito (o tanque) →
/// tuberías en serie → válvula que se cierra linealmente. Las tuberías se sustituyen por una equivalente con la misma
/// inercia, el mismo tiempo de viaje de la onda y la misma pérdida de carga que en el régimen permanente.
/// </summary>
public static class WaterHammer
{
    public static WaterHammerResult Analyze(HydraulicNetwork net, HydraulicSimulation sim, string valveId, double closingTime,
        int reaches = 24)
    {
        var valve = net.FindLink(valveId) as Valve ?? throw new ArgumentException($"'{valveId}' no es una válvula.");
        double q0 = sim.FlowIn(valveId);
        if (q0 <= 1e-7)
            throw new InvalidOperationException($"Por {valveId} no circula agua en sentido de entrada a salida; ábrela o pon la simulación en marcha.");

        // Recorre aguas arriba desde la válvula hasta el depósito o tanque, por un único camino de tuberías.
        var pipes = new List<Pipe>();
        string node = valve.From;
        var visited = new HashSet<string> { node };
        while (net.FindNode(node) is Junction)
        {
            var links = net.Links.Where(l => l.Id != (pipes.Count == 0 ? valve.Id : pipes[^1].Id) && (l.From == node || l.To == node)).ToList();
            var others = net.Links.Count(l => l.From == node || l.To == node);
            if (links.Count != 1 || others != 2 || links[0] is not Pipe p || ((Junction)net.FindNode(node)!).Demand > 0)
                throw new InvalidOperationException(
                    "El golpe de ariete se analiza en conducciones simples: un depósito o tanque, tuberías en serie sin ramales, bombas ni consumos, y la válvula al final.");
            pipes.Add(p);
            node = p.From == node ? p.To : p.From;
            if (!visited.Add(node)) throw new InvalidOperationException("La conducción forma un circuito cerrado.");
        }
        if (pipes.Count == 0) throw new InvalidOperationException($"No hay ninguna tubería aguas arriba de {valveId}.");
        var source = net.FindNode(node)!;

        double rho = net.Fluid.Density, g = LinkLaws.Gravity;
        double length = pipes.Sum(p => p.Length);
        double travel = pipes.Sum(p => p.Length / p.WaveSpeed(net.Fluid));
        double a = length / travel;
        double area = length / pipes.Sum(p => p.Length / (Math.PI * p.Diameter * p.Diameter / 4));

        double hSource = sim.HeadAt(source.Id);
        double hUp = sim.HeadAt(valve.From);
        double hDown = sim.HeadAt(valve.To);
        double zValve = net.FindNode(valve.From)!.Elevation;
        double hf = Math.Max(hSource - hUp, 0);
        double dh0 = Math.Max(hUp - hDown, 1e-6);

        int n = Math.Max(reaches, 4);
        double dt = length / n / a;
        double b = a / (g * area);
        double r = hf / (q0 * q0) / n;   // pérdida por tramo, igual a la del permanente

        var h = new double[n + 1];
        var q = new double[n + 1];
        for (int i = 0; i <= n; i++) { q[i] = q0; h[i] = hSource - r * q0 * q0 * i; }
        var hp = new double[n + 1];
        var qp = new double[n + 1];

        double duration = Math.Max(closingTime, 0) + 12 * 2 * length / a;
        int steps = (int)Math.Min(Math.Ceiling(duration / dt), 400_000);
        int stride = Math.Max(1, steps / 4000);
        var times = new List<double>();
        var pv = new List<double>();
        var pm = new List<double>();
        double zMid = (net.FindNode(valve.From)!.Elevation + source.Elevation) / 2;
        double maxP = double.MinValue, minP = double.MaxValue;
        void Record(double t)
        {
            double pValve = rho * g * (h[n] - zValve);
            maxP = Math.Max(maxP, pValve);
            minP = Math.Min(minP, pValve);
            times.Add(t);
            pv.Add(pValve);
            pm.Add(rho * g * (h[n / 2] - zMid));
        }
        Record(0);

        for (int k = 1; k <= steps; k++)
        {
            double t = k * dt;
            for (int i = 1; i < n; i++)
            {
                double cp = h[i - 1] + b * q[i - 1] - r * q[i - 1] * Math.Abs(q[i - 1]);
                double cm = h[i + 1] - b * q[i + 1] + r * q[i + 1] * Math.Abs(q[i + 1]);
                hp[i] = (cp + cm) / 2;
                qp[i] = (cp - cm) / (2 * b);
            }
            // Aguas arriba: depósito de nivel constante.
            double cm0 = h[1] - b * q[1] + r * q[1] * Math.Abs(q[1]);
            hp[0] = hSource;
            qp[0] = (hp[0] - cm0) / b;
            // Aguas abajo: válvula con apertura relativa τ y ley del orificio.
            double cpn = h[n - 1] + b * q[n - 1] - r * q[n - 1] * Math.Abs(q[n - 1]);
            double tau = closingTime <= 0 ? 0 : Math.Max(0, 1 - t / closingTime);
            if (tau <= 0) { qp[n] = 0; hp[n] = cpn; }
            else
            {
                double cv = (tau * q0) * (tau * q0) / (2 * dh0);
                double avail = cpn - hDown;
                qp[n] = avail <= 0 ? 0 : -b * cv + Math.Sqrt(b * b * cv * cv + 2 * cv * avail);
                hp[n] = cpn - b * qp[n];
            }
            (h, hp) = (hp, h);
            (q, qp) = (qp, q);
            if (k % stride == 0) Record(t);
            else
            {
                double pValve = rho * g * (h[n] - zValve);
                maxP = Math.Max(maxP, pValve);
                minP = Math.Min(minP, pValve);
            }
        }

        double v0 = q0 / area;
        return new WaterHammerResult
        {
            WaveSpeed = a,
            Length = length,
            InitialVelocity = v0,
            ClosingTime = Math.Max(closingTime, 0),
            JoukowskyRise = rho * a * v0,
            SteadyPressure = rho * g * (hUp - zValve),
            MaxPressure = maxP,
            MinPressure = minP,
            Times = [.. times],
            ValvePressure = [.. pv],
            MidPressure = [.. pm],
            PipeIds = pipes.Select(p => p.Id).ToList(),
        };
    }
}
