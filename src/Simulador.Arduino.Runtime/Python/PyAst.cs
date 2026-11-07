namespace Simulador.Arduino.Runtime.Python;

// ---- Expresiones ----

internal abstract record PExpr(int Line);
internal sealed record PConst(object? Value, int Line) : PExpr(Line);
internal sealed record PName(string Id, int Line) : PExpr(Line);
internal sealed record PFString(IReadOnlyList<object> Parts, int Line) : PExpr(Line); // string o (PExpr, formato)
internal sealed record PFormatPart(PExpr Expr, string? Spec);
internal sealed record PBin(string Op, PExpr Left, PExpr Right, int Line) : PExpr(Line);
internal sealed record PUnary(string Op, PExpr Operand, int Line) : PExpr(Line);
internal sealed record PBool(string Op, PExpr Left, PExpr Right, int Line) : PExpr(Line);
internal sealed record PCompare(PExpr First, IReadOnlyList<(string Op, PExpr Right)> Rest, int Line) : PExpr(Line);
internal sealed record PIfExp(PExpr Cond, PExpr Then, PExpr Else, int Line) : PExpr(Line);
internal sealed record PCall(PExpr Func, IReadOnlyList<PExpr> Args, IReadOnlyList<(string Name, PExpr Value)> Kwargs, int Line) : PExpr(Line);
internal sealed record PAttr(PExpr Target, string Name, int Line) : PExpr(Line);
internal sealed record PIndex(PExpr Target, PExpr Index, int Line) : PExpr(Line);
internal sealed record PSlice(PExpr? Lower, PExpr? Upper, PExpr? Step, int Line) : PExpr(Line);
internal sealed record PListExpr(IReadOnlyList<PExpr> Items, int Line) : PExpr(Line);
internal sealed record PTupleExpr(IReadOnlyList<PExpr> Items, int Line) : PExpr(Line);
internal sealed record PDictExpr(IReadOnlyList<(PExpr Key, PExpr Value)> Items, int Line) : PExpr(Line);
internal sealed record PLambda(IReadOnlyList<PParam> Params, PExpr Body, int Line) : PExpr(Line);
internal sealed record PListComp(PExpr Element, PExpr Target, PExpr Iter, PExpr? Cond, int Line) : PExpr(Line);

internal sealed record PParam(string Name, PExpr? Default);

// ---- Sentencias ----

internal abstract record PStmt(int Line);
internal sealed record PExprStmt(PExpr Expr, int Line) : PStmt(Line);
internal sealed record PAssign(IReadOnlyList<PExpr> Targets, PExpr Value, int Line) : PStmt(Line);
internal sealed record PAugAssign(PExpr Target, string Op, PExpr Value, int Line) : PStmt(Line);
internal sealed record PIf(PExpr Cond, IReadOnlyList<PStmt> Body, IReadOnlyList<PStmt> Else, int Line) : PStmt(Line);
internal sealed record PWhile(PExpr Cond, IReadOnlyList<PStmt> Body, IReadOnlyList<PStmt> Else, int Line) : PStmt(Line);
internal sealed record PFor(PExpr Target, PExpr Iter, IReadOnlyList<PStmt> Body, IReadOnlyList<PStmt> Else, int Line) : PStmt(Line);
internal sealed record PDef(string Name, IReadOnlyList<PParam> Params, IReadOnlyList<PStmt> Body, int Line) : PStmt(Line);
internal sealed record PReturn(PExpr? Value, int Line) : PStmt(Line);
internal sealed record PBreak(int Line) : PStmt(Line);
internal sealed record PContinue(int Line) : PStmt(Line);
internal sealed record PPass(int Line) : PStmt(Line);
internal sealed record PGlobal(IReadOnlyList<string> Names, int Line) : PStmt(Line);
internal sealed record PImport(string Module, string? Alias, int Line) : PStmt(Line);
internal sealed record PFromImport(string Module, IReadOnlyList<(string Name, string? Alias)> Names, int Line) : PStmt(Line); // "*" = todo
internal sealed record PTry(IReadOnlyList<PStmt> Body, IReadOnlyList<PHandler> Handlers, IReadOnlyList<PStmt> Else, IReadOnlyList<PStmt> Finally, int Line) : PStmt(Line);
internal sealed record PHandler(IReadOnlyList<string> Types, string? Name, IReadOnlyList<PStmt> Body);
internal sealed record PRaise(PExpr? Exc, int Line) : PStmt(Line);
internal sealed record PDel(IReadOnlyList<PExpr> Targets, int Line) : PStmt(Line);
internal sealed record PClass(string Name, IReadOnlyList<string> Bases, IReadOnlyList<PStmt> Body, int Line) : PStmt(Line);

internal sealed record PyModuleAst(IReadOnlyList<PStmt> Body);
