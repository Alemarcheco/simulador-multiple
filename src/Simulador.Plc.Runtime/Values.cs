using System.Globalization;

namespace Simulador.Plc.Runtime;

/// <summary>Tipos elementales soportados. Los enteros (SINT…LINT, WORD…) se guardan como 64 bits.</summary>
public enum PlcType { Bool, Int, Real, Time }

/// <summary>Valor de una variable del PLC.</summary>
public readonly struct PlcValue : IEquatable<PlcValue>
{
    private readonly long _i;
    private readonly double _r;

    private PlcValue(PlcType type, long i, double r)
    {
        Type = type;
        _i = i;
        _r = r;
    }

    public PlcType Type { get; }

    public static PlcValue FromBool(bool b) => new(PlcType.Bool, b ? 1 : 0, 0);
    public static PlcValue FromInt(long i) => new(PlcType.Int, i, 0);
    public static PlcValue FromReal(double r) => new(PlcType.Real, 0, r);
    public static PlcValue FromTime(TimeSpan t) => new(PlcType.Time, t.Ticks, 0);

    public static PlcValue True { get; } = FromBool(true);
    public static PlcValue False { get; } = FromBool(false);

    public static PlcValue Default(PlcType type) => type switch
    {
        PlcType.Bool => False,
        PlcType.Int => FromInt(0),
        PlcType.Real => FromReal(0),
        PlcType.Time => FromTime(TimeSpan.Zero),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public bool AsBool => Type switch
    {
        PlcType.Real => _r != 0,
        _ => _i != 0,
    };

    public long AsInt => Type switch
    {
        PlcType.Real => (long)Math.Round(_r, MidpointRounding.ToEven),
        PlcType.Time => _i / TimeSpan.TicksPerMillisecond,
        _ => _i,
    };

    public double AsReal => Type switch
    {
        PlcType.Real => _r,
        PlcType.Time => (double)_i / TimeSpan.TicksPerMillisecond,
        _ => _i,
    };

    public TimeSpan AsTime => Type switch
    {
        PlcType.Time => new TimeSpan(_i),
        PlcType.Real => TimeSpan.FromMilliseconds(_r),
        _ => TimeSpan.FromMilliseconds(_i),
    };

    /// <summary>Convierte al tipo indicado (conversión implícita al asignar).</summary>
    public PlcValue ConvertTo(PlcType type) => type switch
    {
        PlcType.Bool => FromBool(AsBool),
        PlcType.Int => FromInt(AsInt),
        PlcType.Real => FromReal(AsReal),
        PlcType.Time => FromTime(AsTime),
        _ => this,
    };

    public bool Equals(PlcValue other) => Type == other.Type && _i == other._i && _r.Equals(other._r);
    public override bool Equals(object? obj) => obj is PlcValue v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Type, _i, _r);
    public static bool operator ==(PlcValue a, PlcValue b) => a.Equals(b);
    public static bool operator !=(PlcValue a, PlcValue b) => !a.Equals(b);

    public override string ToString() => Type switch
    {
        PlcType.Bool => AsBool ? "TRUE" : "FALSE",
        PlcType.Int => _i.ToString(CultureInfo.InvariantCulture),
        PlcType.Real => _r.ToString("0.###", CultureInfo.InvariantCulture),
        PlcType.Time => FormatTime(AsTime),
        _ => "?",
    };

    private static string FormatTime(TimeSpan t)
    {
        if (t.TotalMilliseconds % 1000 == 0) return $"T#{(long)t.TotalSeconds}s";
        return $"T#{(long)t.TotalMilliseconds}ms";
    }
}
