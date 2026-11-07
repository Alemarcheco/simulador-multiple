namespace Simulador.Banco.Ui;

/// <summary>
/// Historial completo de las tendencias de un montaje, en tiempo simulado. Se llena desde el hilo de la simulación
/// (cada fotograma publicado) y la gráfica lo lee desde la interfaz, así que todo va bajo un cerrojo.
/// Si el historial crece mucho, se aclara a la mitad de muestras y se dobla el intervalo: nunca se pierde el principio.
/// </summary>
public sealed class TrendHistory
{
    private readonly object _gate = new();
    private readonly List<double> _times = [];
    private readonly List<double[]> _values = [];
    private string[] _names = [];
    private double _last = double.NegativeInfinity;

    public TrendHistory(double interval = 0.1, int maxSamples = 500_000)
    {
        Interval = interval;
        MaxSamples = maxSamples;
    }

    /// <summary>Segundos simulados entre muestras (se dobla cada vez que se aclara el historial).</summary>
    public double Interval { get; private set; }
    public int MaxSamples { get; }

    public IReadOnlyList<string> Names { get { lock (_gate) return _names; } }
    public int Count { get { lock (_gate) return _times.Count; } }

    /// <summary>Primer y último instante guardados, o null si aún no hay nada.</summary>
    public (double Start, double End)? Range
    {
        get { lock (_gate) return _times.Count == 0 ? null : (_times[0], _times[^1]); }
    }

    public void Clear(IReadOnlyList<string>? names = null)
    {
        lock (_gate)
        {
            _times.Clear();
            _values.Clear();
            if (names is not null) _names = [.. names];
            _last = double.NegativeInfinity;
        }
    }

    /// <summary>
    /// Ofrece una muestra; solo se guarda si ha pasado <see cref="Interval"/> desde la anterior.
    /// Si el tiempo vuelve atrás (reinicio de la simulación), el historial empieza de nuevo.
    /// </summary>
    public bool Offer(double t, Func<double[]> values)
    {
        lock (_gate)
        {
            if (t < _last) { _times.Clear(); _values.Clear(); _last = double.NegativeInfinity; }
            // Rejilla fija de Interval: con fotogramas cada 16 ms se guarda una muestra cada 0,1 s exactos de media, sin deriva.
            if (_times.Count > 0 && Math.Floor(t / Interval + 1e-9) <= Math.Floor(_last / Interval + 1e-9)) return false;
            _last = t;
            _times.Add(t);
            _values.Add(values());
            if (_times.Count > MaxSamples) Thin();
            return true;
        }
    }

    private void Thin()
    {
        int w = 0;
        for (int r = 0; r < _times.Count; r += 2, w++) { _times[w] = _times[r]; _values[w] = _values[r]; }
        _times.RemoveRange(w, _times.Count - w);
        _values.RemoveRange(w, _values.Count - w);
        Interval *= 2;
    }

    /// <summary>Valores de todas las series en el instante guardado más próximo a <paramref name="t"/>.</summary>
    public (double Time, double[] Values)? At(double t)
    {
        lock (_gate)
        {
            if (_times.Count == 0) return null;
            int i = LowerBound(t);
            if (i >= _times.Count) i = _times.Count - 1;
            else if (i > 0 && t - _times[i - 1] < _times[i] - t) i--;
            return (_times[i], _values[i]);
        }
    }

    /// <summary>
    /// Puntos de la serie <paramref name="series"/> entre <paramref name="from"/> y <paramref name="to"/>, reducidos a
    /// <paramref name="buckets"/> columnas guardando el mínimo y el máximo de cada una (no se pierden picos al alejar).
    /// Incluye un punto a cada lado del intervalo para que la línea llegue a los bordes.
    /// </summary>
    public List<(double T, double V)> Window(int series, double from, double to, int buckets)
    {
        var result = new List<(double, double)>();
        lock (_gate)
        {
            if (_times.Count == 0 || series < 0 || series >= _names.Length || to <= from) return result;
            int a = Math.Max(0, LowerBound(from) - 1);
            int b = Math.Min(_times.Count - 1, LowerBound(to));
            int n = b - a + 1;
            if (n <= 2 * buckets)
            {
                for (int i = a; i <= b; i++) result.Add((_times[i], _values[i][series]));
                return result;
            }
            double width = (to - from) / buckets;
            int k = a;
            result.Add((_times[a], _values[a][series]));
            k++;
            while (k < b)
            {
                int bucket = (int)Math.Floor((_times[k] - from) / width);
                int iMin = k, iMax = k;
                while (k < b && (int)Math.Floor((_times[k] - from) / width) == bucket)
                {
                    if (_values[k][series] < _values[iMin][series]) iMin = k;
                    if (_values[k][series] > _values[iMax][series]) iMax = k;
                    k++;
                }
                if (iMin == iMax) result.Add((_times[iMin], _values[iMin][series]));
                else if (iMin < iMax) { result.Add((_times[iMin], _values[iMin][series])); result.Add((_times[iMax], _values[iMax][series])); }
                else { result.Add((_times[iMax], _values[iMax][series])); result.Add((_times[iMin], _values[iMin][series])); }
            }
            result.Add((_times[b], _values[b][series]));
        }
        return result;
    }

    private int LowerBound(double t)
    {
        int lo = 0, hi = _times.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_times[mid] < t) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
