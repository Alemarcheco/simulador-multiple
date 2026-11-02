namespace Simulador.Core.Signals;

/// <summary>Señal entre módulos (entrada/salida digital o analógica). Unidades SI salvo mA.</summary>
public interface ISignalPort
{
    /// <summary>"Equipo.Puerto", p. ej. "PLC1.I0.0", "ARD1.D13".</summary>
    string Id { get; }
    SignalKind Kind { get; }
    SignalUnit Unit { get; }
    /// <summary>Nivel nominal (24 V, 230 V, 5 V, 3,3 V…).</summary>
    double NominalLevel { get; }
    bool IsAc { get; }
    CommonType Common { get; }
    /// <summary>Valor actual (voltios, mA, unidad SI; DO usa 0/1).</summary>
    double Value { get; set; }
    event EventHandler<double>? Changed;
}

/// <summary>Acoplamiento físico esfuerzo–flujo entre dominios.</summary>
public interface IPowerPort
{
    string Id { get; }
    PowerDomain Domain { get; }
    /// <summary>V, N·m, N, Pa, K.</summary>
    double Effort { get; set; }
    /// <summary>A, rad/s, m/s, m³/s, W.</summary>
    double Flow { get; set; }
    Causality Causality { get; }
}

/// <summary>Implementación sencilla de <see cref="ISignalPort"/> para módulos y pruebas.</summary>
public sealed class SignalPort(
    string id, SignalKind kind, SignalUnit unit, double nominalLevel = 0, bool isAc = false,
    CommonType common = CommonType.None) : ISignalPort
{
    private double _value;

    public string Id { get; } = id;
    public SignalKind Kind { get; } = kind;
    public SignalUnit Unit { get; } = unit;
    public double NominalLevel { get; } = nominalLevel;
    public bool IsAc { get; } = isAc;
    public CommonType Common { get; } = common;

    public double Value
    {
        get => _value;
        set
        {
            if (_value.Equals(value)) return;
            _value = value;
            Changed?.Invoke(this, value);
        }
    }

    public event EventHandler<double>? Changed;

    public override string ToString() => $"{Id} = {Value} {Unit}";
}
