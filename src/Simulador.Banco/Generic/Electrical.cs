using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Generic;

/// <summary>Red trifásica ideal: impone la tensión de línea (V eficaces) y recibe la corriente que piden las cargas.</summary>
public sealed class GridSource : GenericComponent
{
    public GridSource(string id, double lineVoltage = 400, double frequency = 50) : base(id)
    {
        LineVoltage = lineVoltage;
        Frequency = frequency;
        L = Power("L", PowerDomain.Electrical3Ph, Causality.ImposesEffort);
        L.Effort = lineVoltage;
    }

    public PowerPort L { get; }
    public double LineVoltage { get; set; }
    public double Frequency { get; }
    /// <summary>true = sin tensión (corte de suministro).</summary>
    public bool Outage { get; set; }
    public override SimPhase Phase => SimPhase.Inputs;

    protected override void Step(double dt) => L.Effort = Outage ? 0 : LineVoltage;

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Voltage] = L.Effort;
        v[GenericKeys.Current] = L.Flow;
        v[GenericKeys.Frequency] = Frequency;
    }

    public override void Reset() { base.Reset(); Outage = false; L.Effort = LineVoltage; L.Flow = 0; }
}

/// <summary>
/// Contactor tripolar con relé térmico (clase 10). La bobina (DI) cierra los polos tras un retardo mecánico, como
/// evento discreto exacto. El térmico abre si la corriente supera la de ajuste el tiempo suficiente.
/// </summary>
public sealed class Contactor : GenericComponent
{
    private bool _coil;
    private TimeSpan? _switchAt;
    private double _thermal;

    public Contactor(string id, double overloadSetting = 5, double closeDelayMs = 20) : base(id)
    {
        OverloadSetting = overloadSetting;
        CloseDelay = TimeSpan.FromMilliseconds(closeDelayMs);
        Coil = Signal("A1", SignalKind.DI, SignalUnit.Bool, 24);
        Auxiliary = Signal("13-14", SignalKind.DO, SignalUnit.Volt, 24);
        TripContact = Signal("97-98", SignalKind.DO, SignalUnit.Volt, 24);
        Line = Power("L", PowerDomain.Electrical3Ph, Causality.ImposesFlow);
        Load = Power("T", PowerDomain.Electrical3Ph, Causality.ImposesEffort);
    }

    public SignalPort Coil { get; }
    /// <summary>Contacto auxiliar NA alimentado a 24 V DC: 24 V con los polos cerrados (para una entrada del PLC).</summary>
    public SignalPort Auxiliary { get; }
    /// <summary>Contacto de señal del térmico alimentado a 24 V DC: 24 V si ha disparado.</summary>
    public SignalPort TripContact { get; }
    public PowerPort Line { get; }
    public PowerPort Load { get; }
    public double OverloadSetting { get; set; }
    public TimeSpan CloseDelay { get; }
    public bool Closed { get; private set; }
    public bool Tripped { get; private set; }
    public override SimPhase Phase => SimPhase.Logic;

    public override void Initialize(SimContext context)
    {
        base.Initialize(context);
        Coil.Changed += (_, v) =>
        {
            bool on = v > 0.5;
            if (on == _coil) return;
            _coil = on;
            _switchAt = context.Clock.Now + CloseDelay;
            context.Clock.RequestEventAt(_switchAt.Value);
        };
    }

    /// <summary>Rearme manual del térmico.</summary>
    public void ResetOverload() { Tripped = false; _thermal = 0; TripContact.Value = 0; }

    /// <summary>Estado térmico del relé (1,3 = disparo).</summary>
    public double Thermal => _thermal;

    protected override void Step(double dt)
    {
        var end = Time + TimeSpan.FromSeconds(dt);
        if (_switchAt is { } at && end >= at)
        {
            Closed = _coil;
            _switchAt = null;
        }
        if (Tripped) Closed = false;

        double i = Load.Flow;
        double ratio = OverloadSetting > 0 ? i / OverloadSetting : 0;
        _thermal += dt * (ratio * ratio - _thermal) / 240.0;
        if (_thermal > 1.3 && !Tripped) { Tripped = true; Closed = false; TripContact.Value = 24; }

        Load.Effort = Closed ? Line.Effort : 0;
        Line.Flow = Closed ? Load.Flow : 0;
        Auxiliary.Value = Closed ? 24 : 0;
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Closed] = Closed;
        v[GenericKeys.CoilOn] = _coil;
        v[GenericKeys.Tripped] = Tripped;
        v[GenericKeys.Current] = Load.Flow;
    }

    public override void Reset()
    {
        base.Reset();
        Closed = false; Tripped = false; _coil = false; _switchAt = null; _thermal = 0;
        Load.Effort = 0; Line.Flow = 0; Auxiliary.Value = 0; TripContact.Value = 0;
    }
}

/// <summary>
/// Motor asíncrono trifásico genérico: par por la fórmula de Kloss escalado con (V/Vn)², corriente con pico de arranque,
/// y la inercia total del eje (la suya + la que declara la carga). Impone la velocidad del eje y recibe el par resistente.
/// </summary>
public sealed class InductionMotorGeneric : GenericComponent
{
    private readonly double _sk, _ws, _a, _i0;

    public InductionMotorGeneric(string id, double powerKw = 4, double voltage = 400, double rpm = 2900,
        double ratedCurrent = 8, double startCurrentRatio = 6.5, double breakdownTorqueRatio = 2.8,
        double inertia = 0.01, double loadInertia = 0, double frequency = 50) : base(id)
    {
        RatedPower = powerKw * 1000;
        RatedVoltage = voltage;
        RatedCurrent = ratedCurrent;
        Inertia = inertia + loadInertia;
        int pp = Math.Max(1, (int)Math.Floor(60 * frequency / rpm));
        _ws = 2 * Math.PI * frequency / pp;
        double wn = rpm * 2 * Math.PI / 60;
        double sn = Math.Clamp((_ws - wn) / _ws, 0.005, 0.2);
        RatedTorque = RatedPower / wn;
        double k = breakdownTorqueRatio;
        _sk = sn * (k + Math.Sqrt(k * k - 1));
        BreakdownTorque = k * RatedTorque;
        StartCurrent = startCurrentRatio * ratedCurrent;
        _i0 = 0.35 * ratedCurrent;
        double irn = Math.Sqrt(ratedCurrent * ratedCurrent - _i0 * _i0);
        _a = sn * Math.Sqrt(Math.Max(StartCurrent * StartCurrent / (irn * irn) - 1, 1e-6));

        Terminals = Power("Bornes", PowerDomain.Electrical3Ph, Causality.ImposesFlow);
        Shaft = Power("Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow);
        SpeedSignal = Signal("Velocidad", SignalKind.AO, SignalUnit.RadPerSecond);
    }

    public PowerPort Terminals { get; }
    public PowerPort Shaft { get; }
    public SignalPort SpeedSignal { get; }
    public double RatedPower { get; }
    public double RatedVoltage { get; }
    public double RatedCurrent { get; }
    public double RatedTorque { get; }
    public double BreakdownTorque { get; }
    public double StartCurrent { get; }
    public double Inertia { get; }
    public double SyncSpeed => _ws;
    public double Torque { get; private set; }
    public double PeakCurrent { get; private set; }
    public override SimPhase Phase => SimPhase.Physics;

    protected override void Step(double dt)
    {
        double v = Terminals.Effort / RatedVoltage;
        double w = Shaft.Flow;
        double s = Math.Clamp((_ws - w) / _ws, 1e-6, 1.0);
        Torque = v > 0.05 ? 2 * BreakdownTorque / (s / _sk + _sk / s) * v * v : 0;
        double load = Math.Max(0, Shaft.Effort) + 0.005 * RatedTorque * w / _ws;
        double acc = (Torque - load) / Inertia;
        w = Math.Max(0, w + acc * dt);
        Shaft.Flow = w;
        SpeedSignal.Value = w;

        if (v > 0.05)
        {
            double ir = StartCurrent * s / Math.Sqrt(s * s + _a * _a);
            Terminals.Flow = v * Math.Sqrt(_i0 * _i0 + ir * ir);
        }
        else Terminals.Flow = 0;
        PeakCurrent = Math.Max(PeakCurrent, Terminals.Flow);
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Speed] = Shaft.Flow;
        v[GenericKeys.Rpm] = Shaft.Flow * 60 / (2 * Math.PI);
        v[GenericKeys.Torque] = Torque;
        v[GenericKeys.LoadTorque] = Shaft.Effort;
        v[GenericKeys.Current] = Terminals.Flow;
        v[GenericKeys.Voltage] = Terminals.Effort;
    }

    public override void Reset() { base.Reset(); Shaft.Flow = 0; Terminals.Flow = 0; Torque = 0; PeakCurrent = 0; SpeedSignal.Value = 0; }
}

/// <summary>
/// Generador síncrono genérico en isla con regulador de tensión ideal: la frecuencia sale de la velocidad del eje y
/// el par resistente de la potencia eléctrica que piden las cargas.
/// </summary>
public sealed class SynchronousGenerator : GenericComponent
{
    private readonly int _pp;
    private readonly double _wn;

    public SynchronousGenerator(string id, double ratedKva = 500, double voltage = 400, double rpm = 3000,
        double frequency = 50, double powerFactor = 0.9, double efficiency = 0.96) : base(id)
    {
        RatedKva = ratedKva;
        RatedVoltage = voltage;
        RatedFrequency = frequency;
        PowerFactor = powerFactor;
        Efficiency = efficiency;
        _pp = Math.Max(1, (int)Math.Round(60 * frequency / rpm));
        _wn = 2 * Math.PI * frequency / _pp;
        Shaft = Power("Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort);
        Terminals = Power("Bornes", PowerDomain.Electrical3Ph, Causality.ImposesEffort);
        SpeedSignal = Signal("Velocidad", SignalKind.AO, SignalUnit.RadPerSecond);
    }

    public PowerPort Shaft { get; }
    public PowerPort Terminals { get; }
    public SignalPort SpeedSignal { get; }
    public double RatedKva { get; }
    public double RatedVoltage { get; }
    public double RatedFrequency { get; }
    public double RatedSpeed => _wn;
    public double PowerFactor { get; }
    public double Efficiency { get; }
    public double Frequency => Shaft.Flow * _pp / (2 * Math.PI);
    public double ElectricalPower { get; private set; }
    public override SimPhase Phase => SimPhase.Physics;

    protected override void Step(double dt)
    {
        double w = Shaft.Flow;
        // Excitación: tensión proporcional a la velocidad hasta el 90 % de la nominal; por encima la mantiene el AVR.
        double v = RatedVoltage * Math.Clamp(w / (0.9 * _wn), 0, 1);
        Terminals.Effort = v;
        ElectricalPower = Math.Sqrt(3) * v * Terminals.Flow * PowerFactor;
        double mech = ElectricalPower / Efficiency;
        Shaft.Effort = w > 1 ? mech / w + 0.002 * RatedKva * 1000 / _wn * w / _wn : 0;
        SpeedSignal.Value = w;
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Speed] = Shaft.Flow;
        v[GenericKeys.Frequency] = Frequency;
        v[GenericKeys.Voltage] = Terminals.Effort;
        v[GenericKeys.Current] = Terminals.Flow;
        v[GenericKeys.Power] = ElectricalPower;
    }

    public override void Reset() { base.Reset(); Shaft.Effort = 0; Terminals.Effort = 0; ElectricalPower = 0; SpeedSignal.Value = 0; }
}

/// <summary>Carga eléctrica trifásica de impedancia constante (la potencia baja con el cuadrado de la tensión).</summary>
public sealed class ElectricalLoad : GenericComponent
{
    public ElectricalLoad(string id, double powerKw = 100, double voltage = 400, double powerFactor = 0.9) : base(id)
    {
        PowerKw = powerKw;
        RatedVoltage = voltage;
        PowerFactor = powerFactor;
        Terminals = Power("Bornes", PowerDomain.Electrical3Ph, Causality.ImposesFlow);
    }

    public PowerPort Terminals { get; }
    /// <summary>Potencia a tensión nominal (kW). Se puede cambiar en marcha.</summary>
    public double PowerKw { get; set; }
    public double RatedVoltage { get; }
    public double PowerFactor { get; }
    public override SimPhase Phase => SimPhase.Physics;

    protected override void Step(double dt)
    {
        double v = Terminals.Effort;
        Terminals.Flow = v > 1 ? PowerKw * 1000 * v / (RatedVoltage * RatedVoltage * Math.Sqrt(3) * PowerFactor) : 0;
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Voltage] = Terminals.Effort;
        v[GenericKeys.Current] = Terminals.Flow;
        v[GenericKeys.Power] = Math.Sqrt(3) * Terminals.Effort * Terminals.Flow * PowerFactor;
    }

    public override void Reset() { base.Reset(); Terminals.Flow = 0; }
}
