// Contrato: /mnt/project-files/contratos/bus-senales.md (responsable: hilo PLC y Arduino).
// Proponer cambios a ese hilo antes de modificar estos tipos.
namespace Simulador.Core.Signals;

public enum SignalKind { DI, DO, AI, AO }

public enum SignalUnit
{
    Bool, Volt, MilliAmp, Pascal, CubicMeterPerSecond, RadPerSecond, NewtonMeter, Meter, Percent, Kelvin,
}

public enum CommonType { None, PNP, NPN, Relay }

public enum PowerDomain
{
    Electrical3Ph, ElectricalDC, MechanicalRotational, MechanicalLinear, Hydraulic, Pneumatic, Thermal,
}

public enum Causality { ImposesEffort, ImposesFlow }

/// <summary>Orden determinista de ejecución dentro de un tick.</summary>
public enum SimPhase { Inputs = 0, Logic = 1, Physics = 2, Outputs = 3 }
