namespace Simulador.Core.Libraries.Model;

/// <summary>Sentencia <c>.model NOMBRE TIPO (parámetros)</c>.</summary>
public sealed record SpiceModel(
    string Name,
    // Tipo SPICE: D, NPN, PNP, NMOS, PMOS, NJF, PJF, SW, VDMOS…
    string Type,
    IReadOnlyDictionary<string, string> Parameters,
    string SourceText);

/// <summary>Bloque <c>.subckt NOMBRE pines… [params: …] … .ends</c>.</summary>
public sealed record SpiceSubcircuit(
    string Name,
    IReadOnlyList<string> Pins,
    IReadOnlyDictionary<string, string> Parameters,
    // Texto completo, desde .subckt hasta .ends, listo para incluir en la netlist de ngspice.
    string SourceText);
