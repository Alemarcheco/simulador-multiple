using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Generic;

public enum TurbineKind { Steam, Gas, Hydro }

/// <summary>
/// Turbina genérica (vapor, gas o hidráulica): par proporcional a la admisión y decreciente con la velocidad,
/// actuador de admisión con retardo, limitador de velocidad (102–106 %) y disparo por sobrevelocidad al 110 %. Lleva la inercia del eje e impone la velocidad.
/// </summary>
public sealed class Turbine : GenericComponent
{
    private double _admission;

    public Turbine(string id, TurbineKind kind = TurbineKind.Steam, double powerKw = 500, double rpm = 3000,
        double inertia = 8, double loadInertia = 0, double actuatorTime = 0.8) : base(id)
    {
        Kind = kind;
        RatedPower = powerKw * 1000;
        RatedSpeed = rpm * 2 * Math.PI / 60;
        RatedTorque = RatedPower / RatedSpeed;
        Inertia = inertia + loadInertia;
        ActuatorTime = actuatorTime;
        AdmissionCommand = Signal("Admision", SignalKind.AI, SignalUnit.Percent);
        Shaft = Power("Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow);
        SpeedSignal = Signal("Velocidad", SignalKind.AO, SignalUnit.RadPerSecond);
    }

    public TurbineKind Kind { get; }
    public SignalPort AdmissionCommand { get; }
    public PowerPort Shaft { get; }
    public SignalPort SpeedSignal { get; }
    public double RatedPower { get; }
    public double RatedSpeed { get; }
    public double RatedTorque { get; }
    public double Inertia { get; }
    public double ActuatorTime { get; }
    public double Admission => _admission;
    public double Torque { get; private set; }
    public bool Tripped { get; private set; }
    public override SimPhase Phase => SimPhase.Physics;

    public void ResetTrip() => Tripped = false;

    protected override void Step(double dt)
    {
        double w = Shaft.Flow;
        // Limitador de velocidad propio de la turbina: recorta la admisión entre el 102 % y el 106 % de la nominal.
        double limiter = Math.Clamp((1.06 * RatedSpeed - w) / (0.04 * RatedSpeed), 0, 1);
        double target = Tripped ? 0 : Math.Clamp(AdmissionCommand.Value / 100, 0, 1);
        _admission += (target - _admission) * Math.Min(1, dt / ActuatorTime);
        // El limitador actúa directamente sobre la válvula (corte rápido), sin el retardo del posicionador.
        _admission = Math.Min(_admission, limiter);
        Torque = Math.Max(0, RatedTorque * _admission * (2 - w / RatedSpeed));
        double load = Math.Max(0, Shaft.Effort) + 0.01 * RatedTorque * w / RatedSpeed;
        w = Math.Max(0, w + (Torque - load) / Inertia * dt);
        Shaft.Flow = w;
        SpeedSignal.Value = w;
        if (w > 1.1 * RatedSpeed) Tripped = true;
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Speed] = Shaft.Flow;
        v[GenericKeys.Rpm] = Shaft.Flow * 60 / (2 * Math.PI);
        v[GenericKeys.Admission] = _admission;
        v[GenericKeys.Torque] = Torque;
        v[GenericKeys.Power] = Torque * Shaft.Flow;
        v[GenericKeys.Tripped] = Tripped;
    }

    public override void Reset() { base.Reset(); _admission = 0; Shaft.Flow = 0; Torque = 0; Tripped = false; SpeedSignal.Value = 0; }
}

/// <summary>
/// Compresor genérico (centrífugo o de tornillo): caudal de aire libre proporcional a la velocidad y menor con la
/// presión; par a partir del trabajo isotérmico. Devuelve el par al eje y entrega caudal al depósito.
/// </summary>
public sealed class Compressor : GenericComponent
{
    public const double Atmosphere = 101_325;

    public Compressor(string id, double ratedFlow = 0.5, double maxPressure = 9e5, double rpm = 3000, double efficiency = 0.7) : base(id)
    {
        RatedFlow = ratedFlow;
        MaxPressure = maxPressure;
        RatedSpeed = rpm * 2 * Math.PI / 60;
        Efficiency = efficiency;
        Shaft = Power("Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort);
        Outlet = Power("Salida", PowerDomain.Pneumatic, Causality.ImposesFlow);
    }

    public PowerPort Shaft { get; }
    public PowerPort Outlet { get; }
    /// <summary>Caudal de aire libre a velocidad nominal y presión cero (m³/s).</summary>
    public double RatedFlow { get; }
    /// <summary>Presión relativa a la que el caudal cae a cero (Pa).</summary>
    public double MaxPressure { get; }
    public double RatedSpeed { get; }
    public double Efficiency { get; }
    public double ShaftPower { get; private set; }
    public override SimPhase Phase => SimPhase.Physics;

    protected override void Step(double dt)
    {
        double w = Shaft.Flow, p = Math.Max(0, Outlet.Effort);
        double q = RatedFlow * (w / RatedSpeed) * Math.Clamp(1 - p / MaxPressure, 0, 1);
        Outlet.Flow = q;
        ShaftPower = Atmosphere * q * Math.Log((p + Atmosphere) / Atmosphere) / Efficiency;
        double drag = 0.02 * (Atmosphere * RatedFlow * Math.Log(1 + MaxPressure / Atmosphere)) / RatedSpeed * Math.Pow(w / RatedSpeed, 2);
        Shaft.Effort = (w > 1 ? ShaftPower / w : 0) + drag;
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Speed] = Shaft.Flow;
        v[GenericKeys.Flow] = Outlet.Flow;
        v[GenericKeys.Pressure] = Outlet.Effort;
        v[GenericKeys.Power] = ShaftPower;
    }

    public override void Reset() { base.Reset(); Outlet.Flow = 0; Shaft.Effort = 0; ShaftPower = 0; }
}

/// <summary>Depósito de aire comprimido isotérmico con consumo ajustable, válvula de seguridad y transmisor de presión.</summary>
public sealed class AirReceiver : GenericComponent
{
    private double _p;

    public AirReceiver(string id, double volume = 5, double reliefPressure = 9.5e5, double initialPressure = 0) : base(id)
    {
        Volume = volume;
        ReliefPressure = reliefPressure;
        InitialPressure = initialPressure;
        _p = initialPressure;
        Inlet = Power("Entrada", PowerDomain.Pneumatic, Causality.ImposesEffort);
        PressureSignal = Signal("Presion", SignalKind.AO, SignalUnit.Pascal);
        Inlet.Effort = _p;
    }

    public PowerPort Inlet { get; }
    public SignalPort PressureSignal { get; }
    public double Volume { get; }
    public double ReliefPressure { get; }
    public double InitialPressure { get; }
    /// <summary>Consumo de aire libre a 7 bar (m³/s). Se puede cambiar en marcha.</summary>
    public double Demand { get; set; } = 0.15;
    public double Pressure => _p;
    public bool ReliefOpen { get; private set; }
    public override SimPhase Phase => SimPhase.Physics;

    protected override void Step(double dt)
    {
        double outflow = Demand * Math.Sqrt(Math.Max(0, _p) / 7e5);
        ReliefOpen = _p > ReliefPressure || (ReliefOpen && _p > 0.95 * ReliefPressure);
        if (ReliefOpen) outflow += 2 * Math.Max(Inlet.Flow, 0.05);
        _p = Math.Max(0, _p + Compressor.Atmosphere / Volume * (Inlet.Flow - outflow) * dt);
        Inlet.Effort = _p;
        PressureSignal.Value = _p;
    }

    protected override void Fill(Dictionary<string, object> v)
    {
        v[GenericKeys.Pressure] = _p;
        v[GenericKeys.Flow] = Inlet.Flow;
        v[GenericKeys.Relief] = ReliefOpen;
    }

    public override void Reset() { base.Reset(); _p = InitialPressure; Inlet.Effort = _p; PressureSignal.Value = _p; ReliefOpen = false; }
}
