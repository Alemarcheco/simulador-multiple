using Simulador.Hidraulica.Model;

namespace Simulador.Hidraulica.Solver;

/// <summary>
/// Resuelve el estado permanente de la red en un instante (método del gradiente de Todini–Pilati, Newton global):
/// incógnitas = alturas de los nudos libres y caudales de todos los enlaces. Los depósitos y los tanques con
/// agua fijan su altura; un tanque vacío se trata como nudo libre para que no entregue agua que no tiene.
/// </summary>
public sealed class NetworkSolver
{
    private readonly HydraulicNetwork _net;
    private readonly Dictionary<string, int> _nodeIndex;

    public int MaxIterations { get; set; } = 100;
    public double Tolerance { get; set; } = 1e-7;

    public NetworkSolver(HydraulicNetwork network)
    {
        _net = network;
        _nodeIndex = network.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(t => t.Id, t => t.i);
    }

    /// <param name="fixedHeads">Altura piezométrica (m) de cada nudo que la tiene impuesta, por índice de nudo; NaN = libre.</param>
    /// <param name="demands">Consumo (m³/s) de cada nudo libre, por índice de nudo.</param>
    /// <param name="initialFlows">Caudales de partida (los del paso anterior) o null.</param>
    public SolveResult Solve(double[] fixedHeads, double[] demands, double[]? initialFlows = null)
    {
        int nNodes = _net.Nodes.Count, nLinks = _net.Links.Count;
        var emitterK = EmitterCoefficients();
        var heads = new double[nNodes];
        var free = new List<int>();
        for (int i = 0; i < nNodes; i++)
            if (double.IsNaN(fixedHeads[i])) free.Add(i); else heads[i] = fixedHeads[i];

        var freePos = new int[nNodes];
        Array.Fill(freePos, -1);
        for (int k = 0; k < free.Count; k++) freePos[free[k]] = k;

        double headGuess = fixedHeads.Where(h => !double.IsNaN(h)).DefaultIfEmpty(0).Average();
        foreach (var i in free) heads[i] = Math.Max(headGuess, _net.Nodes[i].Elevation);

        var from = new int[nLinks];
        var to = new int[nLinks];
        var q = new double[nLinks];
        for (int l = 0; l < nLinks; l++)
        {
            var link = _net.Links[l];
            from[l] = _nodeIndex[link.From];
            to[l] = _nodeIndex[link.To];
            q[l] = initialFlows is { } f && l < f.Length && double.IsFinite(f[l]) ? f[l] : InitialGuess(link);
        }

        var h = new double[nLinks];
        var d = new double[nLinks];
        int m = free.Count;
        var a = new double[m, m];
        var rhs = new double[m];
        bool converged = false;
        int iter = 0;
        double residual = double.PositiveInfinity;

        for (; iter < MaxIterations && !converged; iter++)
        {
            for (int l = 0; l < nLinks; l++)
            {
                var link = _net.Links[l];
                h[l] = LinkLaws.HeadLoss(link, q[l], _net.Fluid);
                double dq = 1e-8 + 1e-4 * Math.Abs(q[l]);
                double g = (LinkLaws.HeadLoss(link, q[l] + dq, _net.Fluid) - LinkLaws.HeadLoss(link, q[l] - dq, _net.Fluid)) / (2 * dq);
                d[l] = Math.Max(g, 1e-3);
            }

            // Sistema reducido: (A21 D⁻¹ A12) ΔH = C − A21 D⁻¹ E
            Array.Clear(a);
            Array.Clear(rhs);
            for (int k = 0; k < m; k++)
            {
                rhs[k] = -demands[free[k]];
                // Descarga libre (aspersor, orificio): q = Ke·√h, linealizada junto a h = 0.
                if (emitterK[free[k]] > 0)
                {
                    var (qe, ge) = Emitter(emitterK[free[k]], heads[free[k]] - _net.Nodes[free[k]].Elevation);
                    rhs[k] -= qe;
                    a[k, k] += ge;
                }
            }
            for (int l = 0; l < nLinks; l++)
            {
                // E = h(Q) − H_entrada + H_salida (debe ser 0)
                double e = h[l] - heads[from[l]] + heads[to[l]];
                double inv = 1 / d[l];
                int pf = freePos[from[l]], pt = freePos[to[l]];
                // Continuidad: C_n = Σ entradas − Σ salidas − consumo
                // y −A21 D⁻¹ E, con A21[n,l] = −1 si n es la entrada del enlace y +1 si es la salida.
                if (pf >= 0) rhs[pf] += -q[l] + inv * e;
                if (pt >= 0) rhs[pt] += q[l] - inv * e;
                if (pf >= 0) a[pf, pf] += inv;
                if (pt >= 0) a[pt, pt] += inv;
                if (pf >= 0 && pt >= 0) { a[pf, pt] -= inv; a[pt, pf] -= inv; }
            }
            for (int k = 0; k < m; k++) if (a[k, k] == 0) a[k, k] = 1e-12;

            var dH = m > 0 ? LinearSolver.Solve(a, rhs) : [];
            for (int k = 0; k < m; k++) heads[free[k]] += dH[k];

            double sumDq = 0, sumQ = 0;
            for (int l = 0; l < nLinks; l++)
            {
                double e = h[l] - heads[from[l]] + heads[to[l]];
                double step = -e / d[l];
                q[l] += step;
                sumDq += Math.Abs(step);
                sumQ += Math.Abs(q[l]);
            }
            residual = sumDq / Math.Max(sumQ, 1e-9);
            converged = residual < Tolerance || sumDq < 1e-12;
        }

        var emitted = new double[nNodes];
        for (int i = 0; i < nNodes; i++)
            if (emitterK[i] > 0 && double.IsNaN(fixedHeads[i]))
                emitted[i] = Emitter(emitterK[i], heads[i] - _net.Nodes[i].Elevation).Q;
        return new SolveResult(heads, q, iter, converged, residual, emitted);
    }

    /// <summary>Coeficiente de la ley del orificio Ke (m³/s por √m) de cada nudo.</summary>
    private double[] EmitterCoefficients()
    {
        double h1 = 1e5 / (_net.Fluid.Density * LinkLaws.Gravity); // 1 bar en metros de columna
        return _net.Nodes.Select(n => n is Junction { DischargeAt1Bar: > 0 } j ? j.DischargeAt1Bar / Math.Sqrt(h1) : 0).ToArray();
    }

    private static (double Q, double Gradient) Emitter(double ke, double h)
    {
        const double hLin = 0.05;
        if (h <= 0) return (0, ke * 1e-6);
        if (h < hLin) return (ke * h / Math.Sqrt(hLin), ke / Math.Sqrt(hLin));
        double r = Math.Sqrt(h);
        return (ke * r, ke / (2 * r));
    }

    private static double InitialGuess(HydraulicLink link) => link switch
    {
        Pump b => b.RelativeSpeed > 0.01 ? b.RatedFlow * b.RelativeSpeed : 0,
        Pipe p => 0.3 * Math.PI * p.Diameter * p.Diameter / 4,
        Valve v => v.Opening > 1e-3 ? 0.3 * Math.PI * v.Diameter * v.Diameter / 4 : 0,
        _ => 0,
    };
}

/// <param name="Emitted">Caudal que sale por las descargas libres de cada nudo (m³/s).</param>
public sealed record SolveResult(double[] Heads, double[] Flows, int Iterations, bool Converged, double Residual, double[] Emitted);

/// <summary>Eliminación gaussiana con pivoteo parcial (redes de hasta unos cientos de nudos).</summary>
public static class LinearSolver
{
    public static double[] Solve(double[,] matrix, double[] b)
    {
        int n = b.Length;
        var a = (double[,])matrix.Clone();
        var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (p != c)
            {
                for (int k = 0; k < n; k++) (a[c, k], a[p, k]) = (a[p, k], a[c, k]);
                (x[c], x[p]) = (x[p], x[c]);
            }
            double piv = a[c, c];
            if (Math.Abs(piv) < 1e-300) continue;
            for (int r = c + 1; r < n; r++)
            {
                double f = a[r, c] / piv;
                if (f == 0) continue;
                for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
                x[r] -= f * x[c];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k];
            x[r] = Math.Abs(a[r, r]) < 1e-300 ? 0 : s / a[r, r];
        }
        return x;
    }
}
