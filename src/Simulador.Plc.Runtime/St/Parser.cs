using System.Globalization;

namespace Simulador.Plc.Runtime.St;

/// <summary>
/// Analizador sintáctico de texto estructurado. Acepta un PROGRAM completo o solo bloques VAR y sentencias.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _t;
    private int _p;

    private Parser(string source) => _t = Lexer.Tokenize(source);

    public static ProgramUnit ParseProgram(string source) => new Parser(source).Program();

    private Token Cur => _t[_p];
    private Token Next(int k = 1) => _t[Math.Min(_p + k, _t.Count - 1)];

    private Token Take() => _t[_p++];

    private bool Accept(string s)
    {
        if (!Cur.Is(s)) return false;
        _p++;
        return true;
    }

    private Token Expect(string s)
    {
        if (!Cur.Is(s)) throw Error($"Se esperaba '{s}' y se encontró {Cur}.");
        return Take();
    }

    private string ExpectIdent()
    {
        if (Cur.Kind != TokenKind.Ident || IsKeyword(Cur.Text)) throw Error($"Se esperaba un nombre y se encontró {Cur}.");
        return Take().Text;
    }

    private PlcCompileException Error(string msg) => new(msg, Cur.Line, Cur.Column);

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROGRAM", "END_PROGRAM", "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_GLOBAL", "END_VAR", "RETAIN",
        "CONSTANT", "AT", "IF", "THEN", "ELSIF", "ELSE", "END_IF", "CASE", "OF", "END_CASE", "FOR", "TO", "BY", "DO",
        "END_FOR", "WHILE", "END_WHILE", "REPEAT", "UNTIL", "END_REPEAT", "EXIT", "RETURN", "AND", "OR", "XOR", "NOT",
        "MOD", "TRUE", "FALSE", "INITIAL_STEP", "STEP", "END_STEP", "TRANSITION", "FROM", "END_TRANSITION",
        "ACTION", "END_ACTION",
    };

    private static bool IsKeyword(string s) => Keywords.Contains(s);

    /// <summary>Analiza un diagrama SFC/GRAFCET escrito en la forma textual de IEC 61131-3.</summary>
    public static Sfc.SfcChart ParseSfc(string source) => new Parser(source).Chart();

    private Sfc.SfcChart Chart()
    {
        string name = "SFC";
        bool hasHeader = Accept("PROGRAM");
        if (hasHeader) name = ExpectIdent();
        var vars = new List<VarDecl>();
        while (Cur.Is("VAR") || Cur.Is("VAR_INPUT") || Cur.Is("VAR_OUTPUT") || Cur.Is("VAR_IN_OUT") || Cur.Is("VAR_GLOBAL"))
            vars.AddRange(VarBlock());

        var steps = new List<Sfc.SfcStep>();
        var transitions = new List<Sfc.SfcTransition>();
        var actions = new List<Sfc.SfcAction>();
        while (Cur.Kind != TokenKind.End && !Cur.Is("END_PROGRAM"))
        {
            int line = Cur.Line;
            if (Cur.Is("INITIAL_STEP") || Cur.Is("STEP"))
            {
                bool initial = Take().Is("INITIAL_STEP");
                string stepName = ExpectIdent();
                Expect(":");
                var assocs = new List<Sfc.SfcActionAssoc>();
                while (!Accept("END_STEP"))
                {
                    if (Cur.Kind == TokenKind.End) throw Error($"Falta END_STEP en la etapa '{stepName}'.");
                    // La acción puede ser una acción con código, una variable o una dirección (%QX0.0).
                    string action = Cur.Kind == TokenKind.Address ? Take().Text : ExpectIdent();
                    string qualifier = "N";
                    TimeSpan? time = null;
                    if (Accept("("))
                    {
                        qualifier = ExpectIdent().ToUpperInvariant();
                        if (Accept(","))
                        {
                            var t = Cur;
                            if (t.Kind != TokenKind.Time) throw Error("Se esperaba un tiempo (T#…) en la acción.");
                            Take();
                            time = Lexer.ParseTime(t.Text, t);
                        }
                        Expect(")");
                    }
                    if (qualifier is not ("N" or "S" or "R" or "P" or "D" or "L"))
                        throw Error($"Calificador '{qualifier}' no válido (usa N, S, R, P, D o L).");
                    if (qualifier is "D" or "L" && time is null)
                        throw Error($"El calificador {qualifier} necesita un tiempo: {action}({qualifier}, T#5s).");
                    Expect(";");
                    assocs.Add(new Sfc.SfcActionAssoc(action, qualifier, time));
                }
                steps.Add(new Sfc.SfcStep(stepName, initial, assocs) { Line = line });
            }
            else if (Accept("TRANSITION"))
            {
                string? trName = Cur.Kind == TokenKind.Ident && !IsKeyword(Cur.Text) ? Take().Text : null;
                Expect("FROM");
                var from = StepList();
                Expect("TO");
                var to = StepList();
                Expect(":=");
                int startTok = _p;
                var cond = Expression();
                string text = string.Join(" ", _t.Skip(startTok).Take(_p - startTok).Select(x => x.Kind == TokenKind.Time ? "T#" + x.Text : x.Text));
                Expect(";");
                Expect("END_TRANSITION");
                transitions.Add(new Sfc.SfcTransition(trName, from, to, cond, text) { Line = line });
            }
            else if (Accept("ACTION"))
            {
                string actionName = ExpectIdent();
                Expect(":");
                var body = Statements("END_ACTION");
                Expect("END_ACTION");
                actions.Add(new Sfc.SfcAction(actionName, body));
            }
            else throw Error($"Se esperaba INITIAL_STEP, STEP, TRANSITION o ACTION y se encontró {Cur}.");
        }
        if (hasHeader) Expect("END_PROGRAM");
        if (Cur.Kind != TokenKind.End) throw Error($"Texto inesperado {Cur} al final del programa.");
        return new Sfc.SfcChart(name, vars, steps, transitions, actions);
    }

    private List<string> StepList()
    {
        var list = new List<string>();
        if (Accept("("))
        {
            do list.Add(ExpectIdent()); while (Accept(","));
            Expect(")");
        }
        else list.Add(ExpectIdent());
        return list;
    }

    private ProgramUnit Program()
    {
        string name = "Main";
        bool hasHeader = Accept("PROGRAM");
        if (hasHeader) name = ExpectIdent();

        var vars = new List<VarDecl>();
        while (Cur.Is("VAR") || Cur.Is("VAR_INPUT") || Cur.Is("VAR_OUTPUT") || Cur.Is("VAR_IN_OUT") || Cur.Is("VAR_GLOBAL"))
            vars.AddRange(VarBlock());

        var body = Statements("END_PROGRAM");
        if (hasHeader) Expect("END_PROGRAM");
        if (Cur.Kind != TokenKind.End) throw Error($"Texto inesperado {Cur} al final del programa.");
        return new ProgramUnit(name, vars, body);
    }

    private List<VarDecl> VarBlock()
    {
        Take();
        bool retain = Accept("RETAIN");
        Accept("CONSTANT");
        var list = new List<VarDecl>();
        while (!Accept("END_VAR"))
        {
            if (Cur.Kind == TokenKind.End) throw Error("Falta END_VAR.");
            int line = Cur.Line;
            var names = new List<string> { ExpectIdent() };
            while (Accept(",")) names.Add(ExpectIdent());

            string? at = null;
            if (Accept("AT"))
            {
                // La dirección puede ser IEC (%I0.0) o de la marca (X0, I0.0, 100.00): se juntan los tokens hasta ':'.
                var parts = new List<string>();
                while (!Cur.Is(":") && Cur.Kind != TokenKind.End) parts.Add(Take().Text);
                at = string.Concat(parts);
            }
            Expect(":");
            string type = ExpectIdent().ToUpperInvariant();
            Expr? init = null;
            if (Accept(":=")) init = Expression();
            Expect(";");
            list.AddRange(names.Select(n => new VarDecl(n, type, at, init, retain) { Line = line }));
        }
        return list;
    }

    private List<Stmt> Statements(params string[] terminators)
    {
        var list = new List<Stmt>();
        while (Cur.Kind != TokenKind.End && !terminators.Any(Cur.Is))
        {
            if (Accept(";")) continue;
            list.Add(Statement());
        }
        return list;
    }

    private Stmt Statement()
    {
        int line = Cur.Line;
        Stmt s;
        if (Accept("IF")) s = If();
        else if (Accept("CASE")) s = Case();
        else if (Accept("FOR")) s = For();
        else if (Accept("WHILE"))
        {
            var cond = Expression();
            Expect("DO");
            var body = Statements("END_WHILE");
            Expect("END_WHILE");
            s = new WhileStmt(cond, body);
        }
        else if (Accept("REPEAT"))
        {
            var body = Statements("UNTIL");
            Expect("UNTIL");
            var cond = Expression();
            Expect("END_REPEAT");
            s = new RepeatStmt(body, cond);
        }
        else if (Accept("EXIT")) s = new ExitStmt();
        else if (Accept("RETURN")) s = new ReturnStmt();
        else if (Cur.Kind == TokenKind.Ident && !IsKeyword(Cur.Text) && Next().Is("("))
        {
            string inst = Take().Text;
            Expect("(");
            var inputs = new List<(string, Expr)>();
            if (!Cur.Is(")"))
            {
                do
                {
                    string param = ExpectIdent();
                    if (Accept("=>"))
                    {
                        // Salidas en la llamada (Q => x) no se admiten aún: se leen como T1.Q.
                        throw Error("Las asignaciones de salida '=>' aún no se admiten; usa instancia.Salida en una expresión.");
                    }
                    Expect(":=");
                    inputs.Add((param, Expression()));
                } while (Accept(","));
            }
            Expect(")");
            s = new FbCallStmt(inst, inputs);
        }
        else
        {
            var target = Primary();
            if (target is not (NameExpr or MemberExpr or AddressExpr))
                throw Error("Solo se puede asignar a una variable, a una dirección o a una entrada de un bloque.");
            Expect(":=");
            s = new AssignStmt(target, Expression());
        }
        Expect(";");
        return s with { Line = line };
    }

    private Stmt If()
    {
        var branches = new List<(Expr, IReadOnlyList<Stmt>)>();
        var cond = Expression();
        Expect("THEN");
        branches.Add((cond, Statements("ELSIF", "ELSE", "END_IF")));
        List<Stmt>? els = null;
        while (true)
        {
            if (Accept("ELSIF"))
            {
                var c = Expression();
                Expect("THEN");
                branches.Add((c, Statements("ELSIF", "ELSE", "END_IF")));
            }
            else if (Accept("ELSE")) els = Statements("END_IF");
            else break;
        }
        Expect("END_IF");
        return new IfStmt(branches, els);
    }

    private Stmt Case()
    {
        var sel = Expression();
        Expect("OF");
        var cases = new List<(IReadOnlyList<(long, long)>, IReadOnlyList<Stmt>)>();
        List<Stmt>? els = null;
        while (!Cur.Is("END_CASE"))
        {
            if (Accept("ELSE"))
            {
                els = Statements("END_CASE");
                break;
            }
            var labels = new List<(long, long)>();
            do
            {
                long from = CaseLabel();
                long to = from;
                if (Accept(".")) { Expect("."); to = CaseLabel(); }
                labels.Add((from, to));
            } while (Accept(","));
            Expect(":");
            var body = new List<Stmt>();
            // El cuerpo termina donde empieza la siguiente etiqueta (número seguido de ':' ',' o '..').
            while (!Cur.Is("END_CASE") && !Cur.Is("ELSE") && !AtCaseLabel())
            {
                if (Accept(";")) continue;
                body.Add(Statement());
            }
            cases.Add((labels, body));
        }
        Expect("END_CASE");
        return new CaseStmt(sel, cases, els);
    }

    private bool AtCaseLabel()
    {
        int k = 0;
        if (Cur.Is("-")) k = 1;
        return Next(k).Kind == TokenKind.Int && (Next(k + 1).Is(":") || Next(k + 1).Is(",") || Next(k + 1).Is("."));
    }

    private long CaseLabel()
    {
        bool neg = Accept("-");
        if (Cur.Kind != TokenKind.Int) throw Error($"Se esperaba un número entero como etiqueta de CASE y se encontró {Cur}.");
        long v = long.Parse(Take().Text, CultureInfo.InvariantCulture);
        return neg ? -v : v;
    }

    private Stmt For()
    {
        string v = ExpectIdent();
        Expect(":=");
        var from = Expression();
        Expect("TO");
        var to = Expression();
        Expr? by = null;
        if (Accept("BY")) by = Expression();
        Expect("DO");
        var body = Statements("END_FOR");
        Expect("END_FOR");
        return new ForStmt(v, from, to, by, body);
    }

    // Precedencia: OR < XOR < AND < (= <>) < (< > <= >=) < (+ -) < (* / MOD) < ** < unarios
    private Expr Expression() => Or();

    private Expr Or()
    {
        var e = Xor();
        while (Accept("OR")) e = new BinaryExpr("OR", e, Xor());
        return e;
    }

    private Expr Xor()
    {
        var e = And();
        while (Accept("XOR")) e = new BinaryExpr("XOR", e, And());
        return e;
    }

    private Expr And()
    {
        var e = Equality();
        while (Accept("AND") || Accept("&")) e = new BinaryExpr("AND", e, Equality());
        return e;
    }

    private Expr Equality()
    {
        var e = Relational();
        while (Cur.Is("=") || Cur.Is("<>")) e = new BinaryExpr(Take().Text, e, Relational());
        return e;
    }

    private Expr Relational()
    {
        var e = Additive();
        while (Cur.Is("<") || Cur.Is(">") || Cur.Is("<=") || Cur.Is(">=")) e = new BinaryExpr(Take().Text, e, Additive());
        return e;
    }

    private Expr Additive()
    {
        var e = Multiplicative();
        while (Cur.Is("+") || Cur.Is("-")) e = new BinaryExpr(Take().Text, e, Multiplicative());
        return e;
    }

    private Expr Multiplicative()
    {
        var e = Power();
        while (Cur.Is("*") || Cur.Is("/") || Cur.Is("MOD")) e = new BinaryExpr(Take().Text.ToUpperInvariant(), e, Power());
        return e;
    }

    private Expr Power()
    {
        var e = Unary();
        if (Accept("**")) e = new BinaryExpr("**", e, Power());
        return e;
    }

    private Expr Unary()
    {
        if (Accept("NOT")) return new UnaryExpr("NOT", Unary());
        if (Accept("-")) return new UnaryExpr("-", Unary());
        if (Accept("+")) return Unary();
        return Primary();
    }

    private Expr Primary()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case TokenKind.Int:
                Take();
                return new LiteralExpr(PlcValue.FromInt(long.Parse(t.Text, CultureInfo.InvariantCulture)));
            case TokenKind.Real:
                Take();
                return new LiteralExpr(PlcValue.FromReal(double.Parse(t.Text, CultureInfo.InvariantCulture)));
            case TokenKind.Time:
                Take();
                return new LiteralExpr(PlcValue.FromTime(Lexer.ParseTime(t.Text, t)));
            case TokenKind.Address:
                Take();
                return new NameExpr(t.Text);
            case TokenKind.Symbol when t.Text == "(":
                Take();
                var inner = Expression();
                Expect(")");
                return inner;
            case TokenKind.Ident:
                if (t.Is("TRUE")) { Take(); return new LiteralExpr(PlcValue.True); }
                if (t.Is("FALSE")) { Take(); return new LiteralExpr(PlcValue.False); }
                if (IsKeyword(t.Text)) break;
                Take();
                if (Cur.Is("("))
                {
                    Take();
                    var args = new List<Expr>();
                    if (!Cur.Is(")"))
                        do args.Add(Expression()); while (Accept(","));
                    Expect(")");
                    return new CallExpr(t.Text.ToUpperInvariant(), args);
                }
                if (Cur.Is(".") && Next().Kind == TokenKind.Ident)
                {
                    Take();
                    return new MemberExpr(t.Text, Take().Text);
                }
                // Dirección de la marca escrita sin %: I0.0, Q0.1, M10.3 (Siemens).
                if (Cur.Is(".") && Next().Kind == TokenKind.Int)
                {
                    Take();
                    return new NameExpr($"{t.Text}.{Take().Text}");
                }
                return new NameExpr(t.Text);
        }
        throw Error($"Expresión no válida en {t}.");
    }
}
