using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Generic;

/// <summary>
/// Transmisor o convertidor de señal lineal: lee una magnitud en su unidad y la entrega escalada, p. ej. un nivel de
/// 0–3 m como 0–10 V para la analógica del PLC, o 0–10 V del PLC como 0–100 % de admisión.
/// </summary>
public sealed class Transmitter : GenericComponent
{
    public Transmitter(string id, SignalUnit inputUnit, double inMin, double inMax, SignalUnit outputUnit, double outMin, double outMax) : base(id)
    {
        InMin = inMin; InMax = inMax; OutMin = outMin; OutMax = outMax;
        Input = Signal("Entrada", SignalKind.AI, inputUnit);
        Output = Signal("Salida", SignalKind.AO, outputUnit);
    }

    public SignalPort Input { get; }
    public SignalPort Output { get; }
    public double InMin { get; }
    public double InMax { get; }
    public double OutMin { get; }
    public double OutMax { get; }
    public override SimPhase Phase => SimPhase.Inputs;

    protected override void Step(double dt)
    {
        double x = Math.Clamp((Input.Value - InMin) / (InMax - InMin), 0, 1);
        Output.Value = OutMin + x * (OutMax - OutMin);
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Input] = Input.Value;
        v[GenericKeys.Output] = Output.Value;
    }

    public override void Reset() { base.Reset(); Output.Value = OutMin; }
}

/// <summary>Regulador PI genérico (p. ej. regulador de velocidad de una turbina). Salida 0–100 %.</summary>
public sealed class PiController : GenericComponent
{
    private double _integral;

    public PiController(string id, SignalUnit pvUnit, double setpoint, double kp, double ti, bool reverse = true) : base(id)
    {
        Setpoint = setpoint; Kp = kp; Ti = ti; Reverse = reverse;
        ProcessValue = Signal("Medida", SignalKind.AI, pvUnit);
        Output = Signal("Salida", SignalKind.AO, SignalUnit.Percent);
    }

    public SignalPort ProcessValue { get; }
    public SignalPort Output { get; }
    public double Setpoint { get; set; }
    /// <summary>Ganancia en % de salida por unidad de error relativo (error / consigna).</summary>
    public double Kp { get; set; }
    public double Ti { get; set; }
    /// <summary>true = la salida sube cuando la medida baja (lo habitual en un regulador de velocidad).</summary>
    public bool Reverse { get; }
    public bool Enabled { get; set; } = true;
    public override int StepMultiple => 10;
    public override SimPhase Phase => SimPhase.Logic;

    protected override void Step(double dt)
    {
        if (!Enabled) { Output.Value = 0; _integral = 0; return; }
        double e = (Setpoint - ProcessValue.Value) / Math.Max(Math.Abs(Setpoint), 1e-9);
        if (!Reverse) e = -e;
        double p = Kp * e;
        double candidate = _integral + Kp * e * dt / Ti;
        double u = p + candidate;
        if (u is >= 0 and <= 100) _integral = candidate;   // antisaturación
        Output.Value = Math.Clamp(p + _integral, 0, 100);
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Input] = ProcessValue.Value;
        v[GenericKeys.Output] = Output.Value;
    }

    public override void Reset() { base.Reset(); _integral = 0; Output.Value = 0; }
}
