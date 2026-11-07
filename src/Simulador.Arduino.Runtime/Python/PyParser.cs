using Simulador.Arduino.Runtime.Sketch;

namespace Simulador.Arduino.Runtime.Python;

/// <summary>Analizador sintáctico de un subconjunto amplio de MicroPython (sin generadores ni async).</summary>
internal sealed class PyParser
{
    private readonly List<PyToken> _t;
    private int _p;

    private PyParser(List<PyToken> tokens) => _t = tokens;

    public static PyModuleAst Parse(string source)
    {
        var parser = new PyParser(PyLexer.Tokenize(source));
        var body = new List<PStmt>();
        while (parser.Peek.Kind != PT.Eof)
        {
            if (parser.Peek.Kind == PT.Newline) { parser._p++; continue; }
            if (parser.Peek.Kind == PT.Indent) throw new SketchCompileException("Sangría inesperada (IndentationError).", parser.Peek.Line);
            body.AddRange(parser.Statement());
        }
        return new PyModuleAst(body);
    }

    private PyToken Peek => _t[_p];
    private PyToken PeekAt(int k) => _t[Math.Min(_p + k, _t.Count - 1)];
    private PyToken Next() => _t[_p++];

    private bool IsOp(string op) => Peek.Kind == PT.Op && Peek.Text == op;
    private bool IsName(string n) => Peek.Kind == PT.Name && Peek.Text == n;

    private bool AcceptOp(string op)
    {
        if (!IsOp(op)) return false;
        _p++;
        return true;
    }

    private bool AcceptName(string n)
    {
        if (!IsName(n)) return false;
        _p++;
        return true;
    }

    private void ExpectOp(string op)
    {
        if (!AcceptOp(op)) throw Error($"Se esperaba '{op}' y hay {Peek}.");
    }

    private string ExpectName()
    {
        if (Peek.Kind != PT.Name || Keywords.Contains(Peek.Text)) throw Error($"Se esperaba un nombre y hay {Peek}.");
        return Next().Text;
    }

    private SketchCompileException Error(string message) => new($"{message} (SyntaxError)", Peek.Line);

    private static readonly HashSet<string> Keywords =
    [
        "False", "None", "True", "and", "as", "assert", "break", "class", "continue", "def", "del", "elif", "else", "except",
        "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
        "return", "try", "while", "with", "yield",
    ];

    // ---- Sentencias ----

    private IReadOnlyList<PStmt> Block()
    {
        ExpectOp(":");
        if (Peek.Kind != PT.Newline) return SimpleLine(); // "if x: y = 1" en la misma línea
        _p++;
        while (Peek.Kind == PT.Newline) _p++;
        if (Peek.Kind != PT.Indent) throw Error("Falta la sangría del bloque (IndentationError).");
        _p++;
        var body = new List<PStmt>();
        while (Peek.Kind != PT.Dedent && Peek.Kind != PT.Eof)
        {
            if (Peek.Kind == PT.Newline) { _p++; continue; }
            body.AddRange(Statement());
        }
        if (Peek.Kind == PT.Dedent) _p++;
        return body;
    }

    private IReadOnlyList<PStmt> Statement()
    {
        var tok = Peek;
        if (tok.Kind == PT.Name)
        {
            switch (tok.Text)
            {
                case "if": _p++; return [If(tok.Line)];
                case "while":
                {
                    _p++;
                    var cond = Expression();
                    var body = Block();
                    var els = AcceptElse();
                    return [new PWhile(cond, body, els, tok.Line)];
                }
                case "for":
                {
                    _p++;
                    var target = TargetList();
                    if (!AcceptName("in")) throw Error("Se esperaba 'in' en el for.");
                    var iter = ExpressionList();
                    var body = Block();
                    var els = AcceptElse();
                    return [new PFor(target, iter, body, els, tok.Line)];
                }
                case "def":
                {
                    _p++;
                    string name = ExpectName();
                    var pars = Params(")");
                    if (AcceptOp("->")) Expression();
                    return [new PDef(name, pars, Block(), tok.Line)];
                }
                case "class":
                {
                    _p++;
                    string name = ExpectName();
                    var bases = new List<string>();
                    if (AcceptOp("("))
                    {
                        while (!IsOp(")"))
                        {
                            var t = Next();
                            if (t.Kind == PT.Name) bases.Add(t.Text);
                        }
                        _p++;
                    }
                    return [new PClass(name, bases, Block(), tok.Line)];
                }
                case "try": _p++; return [Try(tok.Line)];
                case "with": throw Error("'with' no está disponible en este simulador.");
                case "@": break;
            }
        }
        if (IsOp("@")) throw Error("Los decoradores no están disponibles en este simulador.");
        return SimpleLine();
    }

    private IReadOnlyList<PStmt> AcceptElse()
    {
        if (!IsName("else")) return [];
        _p++;
        return Block();
    }

    private PIf If(int line)
    {
        var cond = Expression();
        var body = Block();
        if (IsName("elif"))
        {
            var t = Next();
            return new PIf(cond, body, [If(t.Line)], line);
        }
        return new PIf(cond, body, AcceptElse(), line);
    }

    private PTry Try(int line)
    {
        var body = Block();
        var handlers = new List<PHandler>();
        while (IsName("except"))
        {
            _p++;
            var types = new List<string>();
            string? alias = null;
            if (!IsOp(":"))
            {
                if (AcceptOp("("))
                {
                    do types.Add(DottedName()); while (AcceptOp(","));
                    ExpectOp(")");
                }
                else types.Add(DottedName());
                if (AcceptName("as")) alias = ExpectName();
            }
            handlers.Add(new PHandler(types, alias, Block()));
        }
        var els = AcceptElse();
        IReadOnlyList<PStmt> fin = [];
        if (AcceptName("finally")) fin = Block();
        if (handlers.Count == 0 && fin.Count == 0) throw Error("try necesita al menos un except o un finally.");
        return new PTry(body, handlers, els, fin, line);
    }

    private string DottedName()
    {
        string n = ExpectName();
        while (AcceptOp(".")) n += "." + ExpectName();
        return n;
    }

    private PImport ImportItem(int line)
    {
        string mod = DottedName();
        return new PImport(mod, AcceptName("as") ? ExpectName() : null, line);
    }

    /// <summary>Una o varias sentencias simples en una línea, separadas por ';'.</summary>
    private IReadOnlyList<PStmt> SimpleLine()
    {
        var list = new List<PStmt> { Simple() };
        while (list[^1] is PImport im && AcceptOp(",")) list.Add(ImportItem(im.Line)); // import network, time
        while (AcceptOp(";"))
        {
            if (Peek.Kind is PT.Newline or PT.Eof) break;
            list.Add(Simple());
            while (list[^1] is PImport im2 && AcceptOp(",")) list.Add(ImportItem(im2.Line));
        }
        if (Peek.Kind == PT.Newline) _p++;
        else if (Peek.Kind is not (PT.Eof or PT.Dedent)) throw Error($"Sobra {Peek} al final de la línea.");
        return list;
    }

    private PStmt Simple()
    {
        var tok = Peek;
        int line = tok.Line;
        if (tok.Kind == PT.Name)
        {
            switch (tok.Text)
            {
                case "pass": _p++; return new PPass(line);
                case "break": _p++; return new PBreak(line);
                case "continue": _p++; return new PContinue(line);
                case "return":
                    _p++;
                    return new PReturn(Peek.Kind is PT.Newline or PT.Eof || IsOp(";") ? null : ExpressionList(), line);
                case "global":
                case "nonlocal":
                {
                    _p++;
                    var names = new List<string> { ExpectName() };
                    while (AcceptOp(",")) names.Add(ExpectName());
                    return new PGlobal(names, line);
                }
                case "import":
                {
                    _p++;
                    return ImportItem(line);
                }
                case "from":
                {
                    _p++;
                    string mod = DottedName();
                    if (!AcceptName("import")) throw Error("Se esperaba 'import'.");
                    var names = new List<(string, string?)>();
                    if (AcceptOp("*")) names.Add(("*", null));
                    else
                    {
                        bool paren = AcceptOp("(");
                        do
                        {
                            if (paren && IsOp(")")) break;
                            string n = ExpectName();
                            names.Add((n, AcceptName("as") ? ExpectName() : null));
                        }
                        while (AcceptOp(","));
                        if (paren) ExpectOp(")");
                    }
                    return new PFromImport(mod, names, line);
                }
                case "raise":
                    _p++;
                    return new PRaise(Peek.Kind is PT.Newline or PT.Eof ? null : Expression(), line);
                case "del":
                {
                    _p++;
                    var targets = new List<PExpr> { Postfix() };
                    while (AcceptOp(",")) targets.Add(Postfix());
                    return new PDel(targets, line);
                }
                case "assert":
                {
                    _p++;
                    var cond = Expression();
                    PExpr? msg = AcceptOp(",") ? Expression() : null;
                    // assert x, "msg"  →  if not x: raise AssertionError(msg)
                    var call = new PCall(new PName("AssertionError", line), msg is null ? [] : [msg], [], line);
                    return new PIf(new PUnary("not", cond, line), [new PRaise(call, line)], [], line);
                }
            }
        }

        var first = ExpressionList();
        if (Peek.Kind == PT.Op && Peek.Text.Length >= 2 && Peek.Text.EndsWith('=') && Peek.Text is not ("==" or "!=" or "<=" or ">="))
        {
            string op = Next().Text[..^1];
            CheckTarget(first);
            return new PAugAssign(first, op, ExpressionList(), line);
        }
        if (IsOp(":"))
        {
            // Anotación de tipo: x: int = 3
            _p++;
            Expression();
            if (!AcceptOp("=")) return new PPass(line);
            CheckTarget(first);
            return new PAssign([first], ExpressionList(), line);
        }
        if (IsOp("="))
        {
            var targets = new List<PExpr> { first };
            PExpr value = first;
            while (AcceptOp("="))
            {
                value = ExpressionList();
                targets.Add(value);
            }
            targets.RemoveAt(targets.Count - 1);
            foreach (var t in targets) CheckTarget(t);
            return new PAssign(targets, value, line);
        }
        return new PExprStmt(first, line);
    }

    private void CheckTarget(PExpr e)
    {
        switch (e)
        {
            case PName or PAttr or PIndex: return;
            case PTupleExpr t: foreach (var x in t.Items) CheckTarget(x); return;
            case PListExpr l: foreach (var x in l.Items) CheckTarget(x); return;
            default: throw new SketchCompileException("No se puede asignar a esa expresión (SyntaxError).", e.Line);
        }
    }

    private PExpr TargetList()
    {
        int line = Peek.Line;
        var first = Postfix();
        if (!IsOp(",")) return first;
        var items = new List<PExpr> { first };
        while (AcceptOp(",") && !IsName("in")) items.Add(Postfix());
        return new PTupleExpr(items, line);
    }

    private List<PParam> Params(string close)
    {
        if (close == ")") ExpectOp("(");
        var list = new List<PParam>();
        while (!IsOp(close))
        {
            if (AcceptOp("*")) throw Error("Los parámetros *args no están disponibles en este simulador.");
            string n = ExpectName();
            if (close == ")" && AcceptOp(":")) Expression(); // anotación
            PExpr? def = AcceptOp("=") ? Expression() : null;
            list.Add(new PParam(n, def));
            if (!AcceptOp(",")) break;
        }
        if (close == ")") ExpectOp(")");
        return list;
    }

    // ---- Expresiones ----

    /// <summary>Expresión o tupla sin paréntesis (a, b = 1, 2).</summary>
    private PExpr ExpressionList()
    {
        int line = Peek.Line;
        var first = Expression();
        if (!IsOp(",")) return first;
        var items = new List<PExpr> { first };
        while (AcceptOp(","))
        {
            if (Peek.Kind is PT.Newline or PT.Eof || IsOp("=") || IsOp(")") || IsOp(":")) break;
            items.Add(Expression());
        }
        return new PTupleExpr(items, line);
    }

    private PExpr Expression()
    {
        if (IsName("lambda"))
        {
            int line = Next().Line;
            var pars = Params(":");
            ExpectOp(":");
            return new PLambda(pars, Expression(), line);
        }
        var e = Or();
        if (IsName("if") )
        {
            int line = Next().Line;
            var cond = Or();
            if (!AcceptName("else")) throw Error("Falta 'else' en la expresión condicional.");
            return new PIfExp(cond, e, Expression(), line);
        }
        return e;
    }

    private PExpr Or()
    {
        var e = And();
        while (IsName("or")) { int l = Next().Line; e = new PBool("or", e, And(), l); }
        return e;
    }

    private PExpr And()
    {
        var e = Not();
        while (IsName("and")) { int l = Next().Line; e = new PBool("and", e, Not(), l); }
        return e;
    }

    private PExpr Not()
    {
        if (IsName("not")) { int l = Next().Line; return new PUnary("not", Not(), l); }
        return Comparison();
    }

    private PExpr Comparison()
    {
        int line = Peek.Line;
        var first = BitOr();
        var rest = new List<(string, PExpr)>();
        while (true)
        {
            string? op = null;
            if (Peek.Kind == PT.Op && Peek.Text is "<" or ">" or "==" or "!=" or "<=" or ">=") op = Next().Text;
            else if (IsName("in")) { _p++; op = "in"; }
            else if (IsName("not") && PeekAt(1).Kind == PT.Name && PeekAt(1).Text == "in") { _p += 2; op = "not in"; }
            else if (IsName("is")) { _p++; op = AcceptName("not") ? "is not" : "is"; }
            if (op is null) break;
            rest.Add((op, BitOr()));
        }
        return rest.Count == 0 ? first : new PCompare(first, rest, line);
    }

    private PExpr BitOr() => LeftAssoc(BitXor, "|");
    private PExpr BitXor() => LeftAssoc(BitAnd, "^");
    private PExpr BitAnd() => LeftAssoc(Shift, "&");
    private PExpr Shift() => LeftAssoc(Arith, "<<", ">>");
    private PExpr Arith() => LeftAssoc(Term, "+", "-");
    private PExpr Term() => LeftAssoc(Factor, "*", "/", "//", "%", "@");

    private PExpr LeftAssoc(Func<PExpr> next, params string[] ops)
    {
        var e = next();
        while (Peek.Kind == PT.Op && ops.Contains(Peek.Text))
        {
            var t = Next();
            e = new PBin(t.Text, e, next(), t.Line);
        }
        return e;
    }

    private PExpr Factor()
    {
        if (Peek.Kind == PT.Op && Peek.Text is "-" or "+" or "~")
        {
            var t = Next();
            return new PUnary(t.Text, Factor(), t.Line);
        }
        return Power();
    }

    private PExpr Power()
    {
        var e = Postfix();
        if (IsOp("**"))
        {
            var t = Next();
            return new PBin("**", e, Factor(), t.Line);
        }
        return e;
    }

    private PExpr Postfix()
    {
        var e = Atom();
        while (true)
        {
            if (IsOp("("))
            {
                int line = Next().Line;
                var args = new List<PExpr>();
                var kw = new List<(string, PExpr)>();
                while (!IsOp(")"))
                {
                    if (Peek.Kind == PT.Name && PeekAt(1).Kind == PT.Op && PeekAt(1).Text == "=")
                    {
                        string n = Next().Text;
                        _p++;
                        kw.Add((n, Expression()));
                    }
                    else
                    {
                        if (kw.Count > 0) throw Error("Un argumento sin nombre no puede ir detrás de uno con nombre.");
                        var a = Expression();
                        if (IsName("for")) a = Comprehension(a, a.Line, ")");
                        args.Add(a);
                    }
                    if (!AcceptOp(",")) break;
                }
                ExpectOp(")");
                e = new PCall(e, args, kw, line);
            }
            else if (IsOp("["))
            {
                int line = Next().Line;
                var idx = SliceOrIndex();
                ExpectOp("]");
                e = new PIndex(e, idx, line);
            }
            else if (IsOp("."))
            {
                int line = Next().Line;
                e = new PAttr(e, ExpectName(), line);
            }
            else return e;
        }
    }

    private PExpr SliceOrIndex()
    {
        int line = Peek.Line;
        PExpr? lower = IsOp(":") ? null : Expression();
        if (!IsOp(":"))
        {
            if (!IsOp(",")) return lower!;
            var items = new List<PExpr> { lower! };
            while (AcceptOp(",") && !IsOp("]")) items.Add(Expression());
            return new PTupleExpr(items, line);
        }
        _p++;
        PExpr? upper = IsOp(":") || IsOp("]") ? null : Expression();
        PExpr? step = null;
        if (AcceptOp(":") && !IsOp("]")) step = Expression();
        return new PSlice(lower, upper, step, line);
    }

    private PExpr Comprehension(PExpr element, int line, string close)
    {
        _p++; // for
        var target = TargetList();
        if (!AcceptName("in")) throw Error("Se esperaba 'in'.");
        var iter = Or();
        PExpr? cond = AcceptName("if") ? Or() : null;
        if (IsName("for")) throw Error("Las comprensiones con varios for no están disponibles en este simulador.");
        return new PListComp(element, target, iter, cond, line);
    }

    private PExpr Atom()
    {
        var t = Peek;
        switch (t.Kind)
        {
            case PT.Int:
            case PT.Float:
                _p++;
                return new PConst(t.Value, t.Line);
            case PT.Str:
            case PT.FStr:
            {
                // Cadenas seguidas se concatenan ("a" "b").
                var parts = new List<object>();
                bool any = false;
                while (Peek.Kind is PT.Str or PT.FStr)
                {
                    var s = Next();
                    if (s.Kind == PT.FStr) { any = true; parts.AddRange(FString(s)); }
                    else parts.Add(s.Text);
                }
                return any ? new PFString(parts, t.Line) : new PConst(string.Concat(parts.Cast<string>()), t.Line);
            }
            case PT.Name:
                _p++;
                return t.Text switch
                {
                    "True" => new PConst(true, t.Line),
                    "False" => new PConst(false, t.Line),
                    "None" => new PConst(null, t.Line),
                    _ when Keywords.Contains(t.Text) => throw new SketchCompileException($"'{t.Text}' no puede ir aquí (SyntaxError).", t.Line),
                    _ => new PName(t.Text, t.Line),
                };
            case PT.Op when t.Text == "(":
            {
                _p++;
                if (AcceptOp(")")) return new PTupleExpr([], t.Line);
                var first = Expression();
                if (IsName("for")) { var c = Comprehension(first, t.Line, ")"); ExpectOp(")"); return c; }
                if (AcceptOp(")")) return first;
                var items = new List<PExpr> { first };
                while (AcceptOp(",") && !IsOp(")")) items.Add(Expression());
                ExpectOp(")");
                return new PTupleExpr(items, t.Line);
            }
            case PT.Op when t.Text == "[":
            {
                _p++;
                var items = new List<PExpr>();
                if (AcceptOp("]")) return new PListExpr(items, t.Line);
                var first = Expression();
                if (IsName("for")) { var c = Comprehension(first, t.Line, "]"); ExpectOp("]"); return c; }
                items.Add(first);
                while (AcceptOp(",") && !IsOp("]")) items.Add(Expression());
                ExpectOp("]");
                return new PListExpr(items, t.Line);
            }
            case PT.Op when t.Text == "{":
            {
                _p++;
                var items = new List<(PExpr, PExpr)>();
                while (!IsOp("}"))
                {
                    var k = Expression();
                    ExpectOp(":");
                    items.Add((k, Expression()));
                    if (!AcceptOp(",")) break;
                }
                ExpectOp("}");
                return new PDictExpr(items, t.Line);
            }
        }
        throw Error($"No se esperaba {t}.");
    }

    /// <summary>Trocea una f-string: texto literal y expresiones entre llaves con formato opcional ({x:.2f}).</summary>
    private static List<object> FString(PyToken tok)
    {
        var parts = new List<object>();
        string s = tok.Text;
        var lit = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{' && i + 1 < s.Length && s[i + 1] == '{') { lit.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < s.Length && s[i + 1] == '}') { lit.Append('}'); i++; continue; }
            if (c != '{') { lit.Append(c); continue; }
            int depth = 1, j = i + 1, colon = -1;
            for (; j < s.Length && depth > 0; j++)
            {
                if (s[j] is '{' or '(' or '[') depth++;
                else if (s[j] is '}' or ')' or ']') depth--;
                else if (s[j] == ':' && depth == 1 && colon < 0) colon = j;
                if (depth == 0) break;
            }
            if (j >= s.Length) throw new SketchCompileException("Falta cerrar '}' en la f-string.", tok.Line);
            string exprText = s[(i + 1)..(colon >= 0 ? colon : j)];
            string? spec = colon >= 0 ? s[(colon + 1)..j] : null;
            if (exprText.EndsWith('=')) { lit.Append(exprText); exprText = exprText[..^1]; }
            if (lit.Length > 0) { parts.Add(lit.ToString()); lit.Clear(); }
            var sub = new PyParser(PyLexer.Tokenize(exprText.Trim()));
            var expr = sub.Expression();
            expr = Relocate(expr, tok.Line);
            parts.Add(new PFormatPart(expr, spec));
            i = j;
        }
        if (lit.Length > 0) parts.Add(lit.ToString());
        return parts;
    }

    /// <summary>Las expresiones dentro de una f-string se numeran desde 1: se les pone la línea real.</summary>
    private static PExpr Relocate(PExpr e, int line) => e with { Line = line };
}
