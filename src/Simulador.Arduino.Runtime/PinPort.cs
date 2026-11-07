using Simulador.Core.Signals;

namespace Simulador.Arduino.Runtime;

public enum PinMode { Input, Output, InputPullup }

/// <summary>
/// Pin de la placa visto desde fuera: si es salida, <see cref="Value"/> es la tensión que da el microcontrolador; si es
/// entrada, lo que se escriba en <see cref="Value"/> es la tensión que le llega desde el circuito.
/// </summary>
public sealed class PinPort : ISignalPort
{
    private double _lastValue;

    internal PinPort(string id, int number, string name, double vcc, bool pwm, bool analog, bool analogOnly)
    {
        Id = id;
        Number = number;
        Name = name;
        NominalLevel = vcc;
        IsPwmCapable = pwm;
        IsAnalog = analog;
        IsAnalogOnly = analogOnly;
    }

    public string Id { get; }
    public int Number { get; }
    /// <summary>"D13", "A0"…</summary>
    public string Name { get; }
    public bool IsPwmCapable { get; }
    public bool IsAnalog { get; }
    public bool IsAnalogOnly { get; }

    public PinMode Mode { get; internal set; }
    /// <summary>true si el sketch ha llamado a pinMode() para este pin.</summary>
    public bool Configured { get; internal set; }
    /// <summary>Tensión que pone el microcontrolador cuando el pin es salida (media si hay PWM).</summary>
    public double OutputVolts { get; internal set; }
    /// <summary>Ciclo de trabajo PWM (0-1) tras analogWrite, o null.</summary>
    public double? Duty { get; internal set; }
    /// <summary>Frecuencia de tone(), o null.</summary>
    public double? ToneHz { get; internal set; }
    /// <summary>Frecuencia del PWM (MicroPython: PWM.freq()), o null si el pin no da PWM.</summary>
    public double? PwmHz { get; internal set; }
    /// <summary>Ángulo del servo conectado (0-180°), o null.</summary>
    public double? ServoAngle { get; internal set; }
    /// <summary>Tensión que llega desde fuera, o null si el pin no está conectado a nada.</summary>
    public double? ExternalVolts { get; private set; }

    public SignalKind Kind => Mode == PinMode.Output ? SignalKind.DO : IsAnalog ? SignalKind.AI : SignalKind.DI;
    public SignalUnit Unit => SignalUnit.Volt;
    public double NominalLevel { get; }
    public bool IsAc => false;
    public CommonType Common => CommonType.None;

    public bool IsOutput => Mode == PinMode.Output;

    /// <summary>Nivel lógico de una salida.</summary>
    public bool OutputHigh => OutputVolts > NominalLevel / 2;

    public double Value
    {
        get => Mode == PinMode.Output ? OutputVolts : ExternalVolts ?? (Mode == PinMode.InputPullup ? NominalLevel : 0);
        set
        {
            ExternalVolts = value;
            Notify();
        }
    }

    /// <summary>Deja el pin sin conectar (al aire).</summary>
    public void Disconnect()
    {
        ExternalVolts = null;
        Notify();
    }

    public event EventHandler<double>? Changed;

    internal void Notify()
    {
        double v = Value;
        if (v.Equals(_lastValue)) return;
        _lastValue = v;
        Changed?.Invoke(this, v);
    }

    internal void ResetHardware()
    {
        Mode = PinMode.Input;
        Configured = false;
        OutputVolts = 0;
        Duty = null;
        ToneHz = null;
        PwmHz = null;
        ServoAngle = null;
        Notify();
    }

    public override string ToString() => $"{Id} = {Value:0.##} V ({Mode})";
}
