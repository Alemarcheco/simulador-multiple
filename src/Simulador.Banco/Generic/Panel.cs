using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Generic;

/// <summary>
/// Pulsador o selector de panel: un contacto alimentado a 24 V DC que da 24 V cuando está cerrado. Puede ser
/// normalmente abierto (NA, marcha) o normalmente cerrado (NC, paro); el pulsador vuelve solo al soltarlo.
/// </summary>
public sealed class PanelSwitch : GenericComponent
{
    public PanelSwitch(string id, bool normallyClosed = false, bool latching = false) : base(id)
    {
        NormallyClosed = normallyClosed;
        Latching = latching;
        Contact = Signal("13-14", SignalKind.DO, SignalUnit.Volt, 24);
        Contact.Value = normallyClosed ? 24 : 0;
    }

    public SignalPort Contact { get; }
    public bool NormallyClosed { get; }
    /// <summary>true = selector (se queda donde lo dejas); false = pulsador (vuelve al soltar).</summary>
    public bool Latching { get; }
    /// <summary>Accionado por el usuario (pulsado o selector en 1).</summary>
    public bool Actuated { get; set; }
    public override SimPhase Phase => SimPhase.Inputs;

    protected override void Step(double dt) => Contact.Value = Actuated != NormallyClosed ? 24 : 0;

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Closed] = Actuated != NormallyClosed;
        v[PanelKeys.Actuated] = Actuated;
    }

    public override void Reset() { base.Reset(); Actuated = false; Contact.Value = NormallyClosed ? 24 : 0; }
}

/// <summary>Piloto luminoso de 24 V: se enciende con la entrada por encima de la mitad de su tensión (o a 1 lógico).</summary>
public sealed class PilotLamp : GenericComponent
{
    public PilotLamp(string id, double nominal = 24) : base(id)
    {
        Nominal = nominal;
        Input = Signal("X1", SignalKind.DI, SignalUnit.Volt, nominal);
    }

    public SignalPort Input { get; }
    public double Nominal { get; }
    public bool Lit { get; private set; }
    public override SimPhase Phase => SimPhase.Outputs;

    /// <summary>Una salida a 1 lógico (relé, transistor) también lo enciende.</summary>
    protected override void Step(double dt)
    {
        double x = Input.Value;
        Lit = x > 0.5 && (x <= 1.0001 || x >= Nominal / 2);
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[PanelKeys.Lit] = Lit;
        v[GenericKeys.Input] = Input.Value;
    }

    public override void Reset() { base.Reset(); Lit = false; }
}

/// <summary>Potenciómetro o generador de consigna: una salida analógica que el usuario mueve durante la simulación.</summary>
public sealed class SetpointSource : GenericComponent
{
    public SetpointSource(string id, SignalUnit unit, double min, double max, double initial) : base(id)
    {
        Min = min; Max = max; Initial = Math.Clamp(initial, Math.Min(min, max), Math.Max(min, max));
        Value = Initial;
        Output = Signal("Salida", SignalKind.AO, unit);
        Output.Value = Initial;
    }

    public SignalPort Output { get; }
    public double Min { get; }
    public double Max { get; }
    public double Initial { get; }
    public double Value { get; set; }
    public override SimPhase Phase => SimPhase.Inputs;

    protected override void Step(double dt) => Output.Value = Value;

    protected override void Fill(Dictionary<string, object> v) => v[GenericKeys.Output] = Output.Value;

    public override void Reset() { base.Reset(); Value = Initial; Output.Value = Initial; }
}

/// <summary>
/// Embarrado (juego de barras): reparte la tensión de una entrada entre varias salidas y suma las corrientes que
/// piden. Es lo que permite colgar varios motores o cargas de una misma red o generador.
/// </summary>
public sealed class Busbar : GenericComponent
{
    public const int Outputs = 4;

    public Busbar(string id) : base(id)
    {
        Supply = Power("Entrada", PowerDomain.Electrical3Ph, Causality.ImposesFlow);
        Feeders = Enumerable.Range(1, Outputs).Select(i => Power($"S{i}", PowerDomain.Electrical3Ph, Causality.ImposesEffort)).ToArray();
    }

    public PowerPort Supply { get; }
    public IReadOnlyList<PowerPort> Feeders { get; }
    public override SimPhase Phase => SimPhase.Physics;

    protected override void Step(double dt)
    {
        foreach (var f in Feeders) f.Effort = Supply.Effort;
        Supply.Flow = Feeders.Sum(f => f.Flow);
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Voltage] = Supply.Effort;
        v[GenericKeys.Current] = Supply.Flow;
    }

    public override void Reset() { base.Reset(); Supply.Flow = 0; foreach (var f in Feeders) f.Effort = 0; }
}

/// <summary>Claves de los equipos de panel.</summary>
public static class PanelKeys
{
    public const string Actuated = "actuated"; // bool
    public const string Lit = "lit";           // bool
}
