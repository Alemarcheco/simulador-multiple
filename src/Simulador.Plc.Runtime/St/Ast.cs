namespace Simulador.Plc.Runtime.St;

// Representación intermedia común: todos los lenguajes (ST, Ladder y, más adelante, FBD/IL/SFC) se traducen a este árbol.

public abstract record Expr;

public sealed record LiteralExpr(PlcValue Value) : Expr;

/// <summary>Nombre de variable, o dirección de la marca (X0, I0.0…) si no hay variable con ese nombre.</summary>
public sealed record NameExpr(string Name) : Expr;

/// <summary>Miembro de un bloque de función: T1.Q, C1.CV.</summary>
public sealed record MemberExpr(string Instance, string Member) : Expr;

/// <summary>Dirección directa: %IX0.0, o una dirección de la marca ya resuelta.</summary>
public sealed record AddressExpr(IoAddress Address, string Text) : Expr;

public sealed record UnaryExpr(string Op, Expr Operand) : Expr;

public sealed record BinaryExpr(string Op, Expr Left, Expr Right) : Expr;

/// <summary>Llamada a función estándar: ABS(x), MAX(a, b).</summary>
public sealed record CallExpr(string Function, IReadOnlyList<Expr> Args) : Expr;

public abstract record Stmt
{
    /// <summary>Línea del texto original (0 si se generó desde otro lenguaje).</summary>
    public int Line { get; init; }
}

public sealed record AssignStmt(Expr Target, Expr Value) : Stmt;

/// <summary>Llamada a una instancia de bloque de función: T1(IN := x, PT := T#5s).</summary>
public sealed record FbCallStmt(string Instance, IReadOnlyList<(string Param, Expr Value)> Inputs) : Stmt;

public sealed record IfStmt(IReadOnlyList<(Expr Cond, IReadOnlyList<Stmt> Body)> Branches, IReadOnlyList<Stmt>? Else) : Stmt;

public sealed record CaseStmt(Expr Selector, IReadOnlyList<(IReadOnlyList<(long From, long To)> Labels, IReadOnlyList<Stmt> Body)> Cases, IReadOnlyList<Stmt>? Else) : Stmt;

public sealed record ForStmt(string Var, Expr From, Expr To, Expr? By, IReadOnlyList<Stmt> Body) : Stmt;

public sealed record WhileStmt(Expr Cond, IReadOnlyList<Stmt> Body) : Stmt;

public sealed record RepeatStmt(IReadOnlyList<Stmt> Body, Expr Until) : Stmt;

public sealed record ExitStmt : Stmt;

public sealed record ReturnStmt : Stmt;

/// <summary>Declaración de variable. <see cref="TypeName"/> es un tipo elemental o un bloque de función (TON, CTU…).</summary>
public sealed record VarDecl(string Name, string TypeName, string? At, Expr? Initial, bool Retain = false)
{
    public int Line { get; init; }
}

/// <summary>Programa compilado: variables y sentencias que se ejecutan en cada ciclo.</summary>
public sealed record ProgramUnit(string Name, IReadOnlyList<VarDecl> Vars, IReadOnlyList<Stmt> Body);
