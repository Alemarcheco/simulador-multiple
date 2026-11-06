namespace Simulador.Banco.Generic;

/// <summary>Claves de <see cref="Core.CoSim.ComponentState"/> que publican los equipos genéricos del banco.</summary>
public static class GenericKeys
{
    public const string Voltage = "voltage";        // V
    public const string Current = "current";        // A
    public const string Frequency = "frequency";    // Hz
    public const string Closed = "closed";          // bool
    public const string CoilOn = "coilOn";          // bool
    public const string Tripped = "tripped";        // bool
    public const string Speed = "speed";            // rad/s
    public const string Rpm = "rpm";                // rpm
    public const string Torque = "torque";          // N·m
    public const string LoadTorque = "loadTorque";  // N·m
    public const string Power = "power";            // W
    public const string Admission = "admission";    // 0–1
    public const string Pressure = "pressure";      // Pa (relativa)
    public const string Flow = "flow";              // m³/s
    public const string Output = "output";          // valor de salida (unidad del puerto)
    public const string Input = "input";            // valor de entrada (unidad del puerto)
    public const string Relief = "relief";          // bool, válvula de seguridad abierta
}
