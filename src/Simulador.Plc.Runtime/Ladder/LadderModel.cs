namespace Simulador.Plc.Runtime.Ladder;

// Diagrama de contactos (Ladder, LD). Cada segmento (rung) tiene una condición formada por contactos en serie y en
// paralelo, y una o varias salidas en paralelo al final. Se traduce a la representación intermedia común.

/// <summary>Elemento de la parte de condiciones de un segmento.</summary>
public abstract record LadderNode;

public enum ContactKind
{
    /// <summary>Contacto normalmente abierto  —| |—</summary>
    NormallyOpen,
    /// <summary>Contacto normalmente cerrado  —|/|—</summary>
    NormallyClosed,
    /// <summary>Flanco ascendente  —|P|—</summary>
    RisingEdge,
    /// <summary>Flanco descendente  —|N|—</summary>
    FallingEdge,
}

/// <summary>Contacto sobre un operando: variable, dirección de la marca (I0.0, X0…) o salida de bloque (T1.Q).</summary>
public sealed record Contact(ContactKind Kind, string Operand) : LadderNode;

/// <summary>Contacto de comparación: —[ C1.CV &gt;= 5 ]—</summary>
public sealed record CompareContact(string Left, string Op, string Right) : LadderNode;

/// <summary>Elementos en serie (AND).</summary>
public sealed record Series(IReadOnlyList<LadderNode> Items) : LadderNode
{
    public Series(params LadderNode[] items) : this((IReadOnlyList<LadderNode>)items) { }
}

/// <summary>Ramas en paralelo (OR).</summary>
public sealed record ParallelBranch(IReadOnlyList<LadderNode> Branches) : LadderNode
{
    public ParallelBranch(params LadderNode[] branches) : this((IReadOnlyList<LadderNode>)branches) { }
}

/// <summary>Salida de un segmento.</summary>
public abstract record LadderOutput;

public enum CoilKind
{
    /// <summary>Bobina  —( )—</summary>
    Normal,
    /// <summary>Bobina negada  —(/)—</summary>
    Negated,
    /// <summary>Activar (enclavar)  —(S)—</summary>
    Set,
    /// <summary>Desactivar  —(R)—</summary>
    Reset,
}

public sealed record Coil(CoilKind Kind, string Operand) : LadderOutput;

public enum TimerKind { TON, TOF, TP }

/// <summary>Caja de temporizador alimentada por la condición del segmento. Sus salidas se leen como Instancia.Q / .ET.</summary>
public sealed record TimerBox(TimerKind Kind, string Instance, TimeSpan Preset) : LadderOutput;

public enum CounterKind { CTU, CTD }

/// <summary>Caja de contador. <see cref="ResetOperand"/> es la entrada R (CTU) o LD (CTD).</summary>
public sealed record CounterBox(CounterKind Kind, string Instance, long Preset, string? ResetOperand) : LadderOutput;

/// <summary>Caja MOVE: copia un valor cuando la condición es verdadera.</summary>
public sealed record MoveBox(string Source, string Destination) : LadderOutput;

public sealed record Rung(LadderNode? Condition, IReadOnlyList<LadderOutput> Outputs, string? Comment = null)
{
    public Rung(LadderNode? condition, params LadderOutput[] outputs) : this(condition, (IReadOnlyList<LadderOutput>)outputs) { }
}

/// <summary>Programa Ladder: tabla de variables (opcional) y segmentos.</summary>
public sealed record LadderProgram(IReadOnlyList<St.VarDecl> Variables, IReadOnlyList<Rung> Rungs)
{
    public LadderProgram(params Rung[] rungs) : this([], rungs) { }
}
