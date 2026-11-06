namespace Simulador.Arduino.Runtime.Sketch;

/// <summary>Tipo de C/C++ con el tamaño real del microcontrolador (en AVR, <c>int</c> es de 16 bits).</summary>
internal sealed record CType(string Name, int Bits, bool Unsigned = false, bool IsFloat = false, bool IsBool = false, bool IsString = false,
    bool IsVoid = false, string? ObjectClass = null)
{
    public bool IsObject => ObjectClass is not null;
    public override string ToString() => Name;
}

internal abstract record Expr(int Line);
internal sealed record NumExpr(Val Value, int Line) : Expr(Line);
internal sealed record StrExpr(string Value, int Line) : Expr(Line);
internal sealed record NameExpr(string Name, int Line) : Expr(Line);
internal sealed record UnaryExpr(string Op, Expr Operand, int Line) : Expr(Line);
/// <summary>++/-- antes (Prefix) o después de la variable.</summary>
internal sealed record IncExpr(string Op, bool Prefix, Expr Target, int Line) : Expr(Line);
internal sealed record BinaryExpr(string Op, Expr Left, Expr Right, int Line) : Expr(Line);
internal sealed record AssignExpr(string Op, Expr Target, Expr Value, int Line) : Expr(Line);
internal sealed record CondExpr(Expr Cond, Expr Then, Expr Else, int Line) : Expr(Line);
internal sealed record CallExpr(string Name, List<Expr> Args, int Line) : Expr(Line);
internal sealed record MethodExpr(Expr Target, string Method, List<Expr> Args, int Line) : Expr(Line);
internal sealed record IndexExpr(Expr Target, Expr Index, int Line) : Expr(Line);
internal sealed record CastExpr(CType Type, Expr Operand, int Line) : Expr(Line);
internal sealed record InitListExpr(List<Expr> Items, int Line) : Expr(Line);

internal abstract record Stmt(int Line);
internal sealed record BlockStmt(List<Stmt> Body, int Line) : Stmt(Line);
internal sealed record ExprStmt(Expr Expr, int Line) : Stmt(Line);

internal sealed record Declarator(string Name, List<Expr?> Dims, Expr? Init, List<Expr>? CtorArgs);

internal sealed record VarDeclStmt(CType Type, List<Declarator> Vars, bool IsStatic, bool IsConst, int Line) : Stmt(Line)
{
    /// <summary>Variables <c>static</c> locales: se crean una sola vez.</summary>
    public Dictionary<string, Slot> Statics { get; } = [];
}

internal sealed record IfStmt(Expr Cond, Stmt Then, Stmt? Else, int Line) : Stmt(Line);
internal sealed record WhileStmt(Expr Cond, Stmt Body, int Line) : Stmt(Line);
internal sealed record DoWhileStmt(Stmt Body, Expr Cond, int Line) : Stmt(Line);
internal sealed record ForStmt(Stmt? Init, Expr? Cond, Expr? Step, Stmt Body, int Line) : Stmt(Line);
internal sealed record SwitchCase(List<Expr> Labels, bool IsDefault, List<Stmt> Body);
internal sealed record SwitchStmt(Expr Value, List<SwitchCase> Cases, int Line) : Stmt(Line);
internal sealed record BreakStmt(int Line) : Stmt(Line);
internal sealed record ContinueStmt(int Line) : Stmt(Line);
internal sealed record ReturnStmt(Expr? Value, int Line) : Stmt(Line);
internal sealed record EmptyStmt(int Line) : Stmt(Line);

internal sealed record Param(CType Type, string Name, bool IsArray, bool IsRef);
internal sealed record FuncDef(CType Return, string Name, List<Param> Params, BlockStmt Body, int Line);

internal sealed record SketchProgram(List<VarDeclStmt> Globals, Dictionary<string, FuncDef> Functions);
