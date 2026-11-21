using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Mesa;

/// <summary>Una magnitud que lee una sonda: nombre, valor en la unidad que se muestra y unidad.</summary>
public readonly record struct Reading(string Name, double Value, string Unit)
{
    public override string ToString() => $"{Name} {Format(Value)} {Unit}".Trim();

    public static string Format(double v) =>
        double.IsNaN(v) ? "—" :
        Math.Abs(v) < 0.0005 ? "0" :
        Math.Abs(v) >= 1000 ? v.ToString("0") :
        Math.Abs(v) >= 100 ? v.ToString("0.0") :
        Math.Abs(v) >= 10 ? v.ToString("0.00") : v.ToString("0.###");
}

/// <summary>
/// Lo que mide una sonda puesta en un borne: una señal, un puerto de potencia (tensión y corriente, par y
/// velocidad, presión y caudal) o una boca hidráulica. Siempre se lee desde el hilo de simulación.
/// </summary>
public sealed class Probe
{
    private readonly Func<Reading[]> _read;

    public Probe(string target, Func<Reading[]> read, bool digital)
    {
        Target = target;
        _read = read;
        IsDigital = digital;
    }

    /// <summary>"M1.Bornes", "PLC1.%QX0.0"…</summary>
    public string Target { get; }
    public bool IsDigital { get; }
    public Reading[] Read() => _read();

    public static Probe ForSignal(ISignalPort s)
    {
        bool digital = s.Kind is SignalKind.DI or SignalKind.DO;
        return new Probe(s.Id, () => [SignalReading(s)], digital);
    }

    public static Reading SignalReading(ISignalPort s) => s.Unit switch
    {
        SignalUnit.Bool => new("estado", s.Value, ""),
        SignalUnit.Volt => new("tensión", s.Value, "V"),
        SignalUnit.MilliAmp => new("corriente", s.Value, "mA"),
        SignalUnit.Pascal => new("presión", s.Value / 1e5, "bar"),
        SignalUnit.CubicMeterPerSecond => new("caudal", s.Value * 1000, "L/s"),
        SignalUnit.RadPerSecond => new("velocidad", s.Value * 60 / (2 * Math.PI), "rpm"),
        SignalUnit.NewtonMeter => new("par", s.Value, "N·m"),
        SignalUnit.Meter => new("nivel", s.Value, "m"),
        SignalUnit.Percent => new("valor", s.Value, "%"),
        SignalUnit.Kelvin => new("temperatura", s.Value - 273.15, "°C"),
        _ => new("valor", s.Value, ""),
    };

    public static Probe ForPower(IPowerPort p) => new(p.Id, () => PowerReadings(p), false);

    /// <summary>Lo primero es lo que se dibuja en el osciloscopio: la corriente, la velocidad o la presión.</summary>
    public static Reading[] PowerReadings(IPowerPort p) => p.Domain switch
    {
        PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC =>
            [new("corriente", p.Flow, "A"), new("tensión", p.Effort, "V"), new("potencia", (p.Domain == PowerDomain.Electrical3Ph ? Math.Sqrt(3) : 1) * p.Effort * p.Flow / 1000, "kVA")],
        PowerDomain.MechanicalRotational =>
            [new("velocidad", p.Flow * 60 / (2 * Math.PI), "rpm"), new("par", p.Effort, "N·m"), new("potencia", p.Effort * p.Flow / 1000, "kW")],
        PowerDomain.MechanicalLinear => [new("velocidad", p.Flow, "m/s"), new("fuerza", p.Effort, "N")],
        PowerDomain.Hydraulic or PowerDomain.Pneumatic => [new("presión", p.Effort / 1e5, "bar"), new("caudal", p.Flow * 1000, "L/s")],
        PowerDomain.Thermal => [new("temperatura", p.Effort - 273.15, "°C"), new("calor", p.Flow / 1000, "kW")],
        _ => [new("esfuerzo", p.Effort, ""), new("flujo", p.Flow, "")],
    };

    /// <summary>Boca hidráulica: se lee de la instantánea de la pieza (nivel, presión, caudal).</summary>
    public static Probe ForHydraulic(string target, ISimComponent part) => new(target, () =>
    {
        var s = part.Snapshot().Values;
        var list = new List<Reading>();
        if (s.TryGetValue("level", out var lv)) list.Add(new("nivel", Convert.ToDouble(lv), "m"));
        if (s.TryGetValue("flow", out var fl)) list.Add(new("caudal", Convert.ToDouble(fl) * 1000, "L/s"));
        if (s.TryGetValue("pressure", out var pr)) list.Add(new("presión", Convert.ToDouble(pr) / 1e5, "bar"));
        if (s.TryGetValue("head", out var hd)) list.Add(new("altura", Convert.ToDouble(hd), "m"));
        if (s.TryGetValue("velocity", out var ve)) list.Add(new("velocidad", Convert.ToDouble(ve), "m/s"));
        return list.Count > 0 ? [.. list] : [new("sin dato", double.NaN, "")];
    }, false);
}

public enum InstrumentKind { Multimeter, Oscilloscope, LogicAnalyzer }

/// <summary>Una muestra de todos los canales de un instrumento.</summary>
public readonly record struct InstrumentSample(double Time, double[] Values);

/// <summary>
/// Multímetro, osciloscopio o analizador lógico de la mesa. Es un componente más del banco (fase Outputs): lee sus
/// sondas en el hilo de simulación, en tiempo simulado, y guarda las muestras para que la interfaz las dibuje.
/// </summary>
public sealed class InstrumentComponent : ISimComponent
{
    private readonly object _gate = new();
    private readonly InstrumentSample[] _ring;
    private int _count, _head;
    private Reading[][] _last;
    private TimeSpan _time;

    public InstrumentComponent(string id, InstrumentKind kind, IReadOnlyList<Probe?> channels, int sampleEveryMs = 1, int capacity = 30_000)
    {
        Id = id;
        Kind = kind;
        Channels = channels;
        StepMultiple = Math.Max(1, sampleEveryMs);
        _ring = new InstrumentSample[capacity];
        _last = channels.Select(_ => Array.Empty<Reading>()).ToArray();
    }

    public string Id { get; }
    public string ModuleId => BancoModule.ModuleId;
    public InstrumentKind Kind { get; }
    /// <summary>Una sonda por canal; null = canal sin conectar.</summary>
    public IReadOnlyList<Probe?> Channels { get; }
    public IReadOnlyList<ISignalPort> Signals => [];
    public IReadOnlyList<IPowerPort> PowerPorts => [];
    public int StepMultiple { get; }
    public SimPhase Phase => SimPhase.Outputs;

    public void Initialize(SimContext context) => Sample(context.Clock.Now);

    public void DoStep(TimeSpan now, TimeSpan dt) => Sample(now + dt);

    private void Sample(TimeSpan t)
    {
        var readings = Channels.Select(c => c?.Read() ?? []).ToArray();
        var values = readings.Select(r => r.Length > 0 ? r[0].Value : double.NaN).ToArray();
        lock (_gate)
        {
            _time = t;
            _last = readings;
            _ring[_head] = new InstrumentSample(t.TotalSeconds, values);
            _head = (_head + 1) % _ring.Length;
            _count = Math.Min(_count + 1, _ring.Length);
        }
    }

    /// <summary>Últimas lecturas completas de cada canal (copia, segura entre hilos).</summary>
    public Reading[][] LastReadings()
    {
        lock (_gate) return _last.Select(r => r.ToArray()).ToArray();
    }

    /// <summary>Muestras desde <paramref name="fromTime"/> (s), de la más antigua a la más nueva.</summary>
    public List<InstrumentSample> Samples(double fromTime)
    {
        lock (_gate)
        {
            var list = new List<InstrumentSample>();
            for (int i = 0; i < _count; i++)
            {
                var s = _ring[(_head - _count + i + _ring.Length) % _ring.Length];
                if (s.Time >= fromTime) list.Add(s);
            }
            return list;
        }
    }

    public double Time { get { lock (_gate) return _time.TotalSeconds; } }

    public ComponentState Snapshot()
    {
        var v = new Dictionary<string, object>();
        var last = LastReadings();
        for (int i = 0; i < last.Length; i++)
            if (last[i].Length > 0) v[$"ch{i + 1}"] = last[i][0].Value;
        return new ComponentState(Id, _time, v);
    }

    public void Reset()
    {
        lock (_gate) { _count = 0; _head = 0; _time = TimeSpan.Zero; _last = Channels.Select(_ => Array.Empty<Reading>()).ToArray(); }
    }
}
