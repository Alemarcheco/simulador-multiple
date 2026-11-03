using System.Numerics;

namespace Simulador.Electrico.Engine.Sim;

/// <summary>Resolución de sistemas densos por eliminación gaussiana con pivoteo parcial (redes de hasta unos cientos de nudos).</summary>
internal static class LinearSolver
{
    public static Complex[] Solve(Complex[,] a, Complex[] b)
    {
        var n = b.Length;
        var m = (Complex[,])a.Clone();
        var x = (Complex[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            var best = m[col, col].Magnitude;
            for (var r = col + 1; r < n; r++)
            {
                var v = m[r, col].Magnitude;
                if (v > best) { best = v; pivot = r; }
            }
            if (best < 1e-300) continue; // nudo aislado: queda a 0 V
            if (pivot != col)
            {
                for (var c = col; c < n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                (x[col], x[pivot]) = (x[pivot], x[col]);
            }
            var inv = 1 / m[col, col];
            for (var r = col + 1; r < n; r++)
            {
                var f = m[r, col] * inv;
                if (f == Complex.Zero) continue;
                for (var c = col; c < n; c++) m[r, c] -= f * m[col, c];
                x[r] -= f * x[col];
            }
        }
        for (var r = n - 1; r >= 0; r--)
        {
            var sum = x[r];
            for (var c = r + 1; c < n; c++) sum -= m[r, c] * x[c];
            x[r] = m[r, r].Magnitude < 1e-300 ? Complex.Zero : sum / m[r, r];
        }
        return x;
    }
}
