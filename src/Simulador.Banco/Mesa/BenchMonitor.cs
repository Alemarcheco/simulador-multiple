using Simulador.Core.CoSim;

namespace Simulador.Banco.Mesa;

/// <summary>Un aviso aparecido durante la simulación, con el instante en que empezó.</summary>
public sealed record TimedIssue(double Time, BenchIssue Issue);

/// <summary>
/// Vigila el montaje mientras simula: cada pieza declara sus condiciones (térmico disparado, motor bloqueado, tanque
/// rebosando…) y aquí se avisa cuando una se cumple el tiempo suficiente. Guarda también el historial.
/// </summary>
public sealed class BenchMonitor(BuiltBench bench)
{
    private readonly Dictionary<string, double> _since = [];
    private readonly HashSet<string> _reported = [];
    private readonly List<TimedIssue> _log = [];
    private readonly List<TimedIssue> _active = [];

    /// <summary>Avisos vigentes ahora mismo.</summary>
    public IReadOnlyList<TimedIssue> Active => _active;
    /// <summary>Todo lo que ha pasado desde que empezó la simulación, en orden.</summary>
    public IReadOnlyList<TimedIssue> Log => _log;

    public event EventHandler? Changed;

    public void Clear()
    {
        _since.Clear(); _reported.Clear(); _log.Clear(); _active.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(SimFrame frame)
    {
        double t = frame.Time.TotalSeconds;
        var nowActive = new List<TimedIssue>();
        bool changed = false;
        foreach (var bp in bench.Parts.Values)
        {
            if (bp.Watches is null) continue;
            var reader = bench.Reader(frame, bp.Part.Id);
            if (!reader.Has) continue;
            foreach (var w in bp.Watches(reader))
            {
                string key = bp.Part.Id + ":" + w.Key;
                if (!w.Active)
                {
                    _since.Remove(key);
                    if (_reported.Remove(key)) changed = true;
                    continue;
                }
                if (!_since.TryGetValue(key, out double start)) _since[key] = start = t;
                if (t - start + 1e-9 < w.Hold) continue;
                var issue = new TimedIssue(start, new BenchIssue(w.Level, bp.Part.Id, w.Message, w.Hint));
                nowActive.Add(issue);
                if (_reported.Add(key)) { _log.Add(issue); changed = true; }
            }
        }
        if (changed || nowActive.Count != _active.Count)
        {
            _active.Clear();
            _active.AddRange(nowActive.OrderBy(i => i.Issue.Level).ThenBy(i => i.Time));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Los textos pueden cambiar (corrientes, velocidades) aunque sean los mismos avisos.
            _active.Clear();
            _active.AddRange(nowActive.OrderBy(i => i.Issue.Level).ThenBy(i => i.Time));
        }
    }

    /// <summary>Peor estado que tiene ahora una pieza por sus avisos (para pintarla de rojo o ámbar).</summary>
    public IssueLevel? WorstFor(string partId)
    {
        IssueLevel? worst = null;
        foreach (var a in _active)
            if (a.Issue.PartId == partId && (worst is null || a.Issue.Level < worst)) worst = a.Issue.Level;
        return worst;
    }
}
