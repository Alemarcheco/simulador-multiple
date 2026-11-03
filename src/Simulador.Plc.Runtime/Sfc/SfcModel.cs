using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Runtime.Sfc;

// Diagrama de funciones secuenciales (SFC, IEC 61131-3) / GRAFCET (IEC 60848): etapas, transiciones con su condición
// (receptividad) y acciones asociadas a cada etapa. Se escribe en la forma textual de la norma:
//
//   INITIAL_STEP Reposo: END_STEP
//   STEP Llenar: Valvula(N); END_STEP
//   TRANSITION FROM Reposo TO Llenar := Marcha; END_TRANSITION
//   ACTION Contar: Piezas := Piezas + 1; END_ACTION

/// <summary>Asociación de una acción a una etapa con su calificador (N, S, R, P, D, L).</summary>
public sealed record SfcActionAssoc(string Action, string Qualifier, TimeSpan? Time);

public sealed record SfcStep(string Name, bool Initial, IReadOnlyList<SfcActionAssoc> Actions)
{
    public int Line { get; init; }
}

public sealed record SfcTransition(string? Name, IReadOnlyList<string> From, IReadOnlyList<string> To, Expr Condition, string ConditionText)
{
    public int Line { get; init; }
}

/// <summary>Acción con código en texto estructurado.</summary>
public sealed record SfcAction(string Name, IReadOnlyList<Stmt> Body);

public sealed record SfcChart(
    string Name,
    IReadOnlyList<VarDecl> Vars,
    IReadOnlyList<SfcStep> Steps,
    IReadOnlyList<SfcTransition> Transitions,
    IReadOnlyList<SfcAction> Actions);
