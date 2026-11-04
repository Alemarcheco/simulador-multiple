namespace Simulador.Arduino.Runtime.Sketch;

/// <summary>
/// Analizador sintáctico del subconjunto de C/C++ que usan los sketches de Arduino: variables y arrays, funciones,
/// if/else, while, do-while, for, switch, enum, operadores de C, conversiones y los objetos Serial, String y Servo.
/// </summary>
internal sealed class Parser
{
    private static readonly HashSet<string> Qualifiers = ["const", "static", "volatile", "unsigned", "signed", "long", "short", "inline", "constexpr"];

    private static readonly HashSet<string> BaseTypes =
    [
        "void", "bool", "boolean", "char", "byte", "int", "float", "double", "word", "size_t", "String",
        "uint8_t", "int8_t", "uint16_t", "int16_t", "uint32_t", "int32_t", "uint64_t", "int64_t", "Servo",
    ];

    private readonly List<Token> _t;
    private readonly int _intBits;
    private readonly Dictionary<string, CType> _aliases = [];
    private int _p;

    private Parser(List<Token> tokens, int intBits)
    {
        _t = tokens;
        _intBits = intBits;
    }

    public static SketchProgram Parse(string source, int intBits = 16)
    {
        var parser = new Parser(Lexer.Tokenize(source), intBits);
        return parser.ParseProgram();
    }

    // ---- Utilidades ----

    private Token Peek(int k = 0) => _t[Math.Min(_p + k, _t.Count - 1)];
    private Token Next() => _t[Math.Min(_p++, _t.Count - 1)];
    private bool IsOp(string op, int k = 0) => Peek(k).Kind == Tk.Op && Peek(k).Text == op;
    private bool IsWord(string w, int k = 0) => Peek(k).Kind == Tk.Ident && Peek(k).Text == w;

    private bool Accept(string op)
    {
        if (!IsOp(op)) return false;
        _p++;
        return true;
    }

    private Token Expect(string op)
    {
        if (IsOp(op)) return Next();
        var t = Peek();
        string hint = op == ";" ? " ¿Falta un punto y coma al final de la línea anterior?" : "";
        throw new SketchCompileException($"Se esperaba '{op}' y hay {t}.{hint}", op == ";" && _p > 0 ? _t[_p - 1].Line : t.Line);
    }

    private string ExpectIdent()
    {
        var t = Peek();
        if (t.Kind != Tk.Ident) throw new SketchCompileException($"Se esperaba un nombre y hay {t}.", t.Line);
        _p++;
        return t.Text;
    }

    private bool IsTypeStart(int k = 0)
    {
        var t = Peek(k);
        return t.Kind == Tk.Ident && (BaseTypes.Contains(t.Text) || Qualifiers.Contains(t.Text) || _aliases.ContainsKey(t.Text) || t.Text == "enum");
    }

    // ---- Tipos ----

    private (CType Type, bool IsStatic, bool IsConst) ParseType()
    {
        bool isStatic = false, isConst = false, unsigned = false, signed = false;
        int longs = 0, shorts = 0;
        string? baseName = null;
        int line = Peek().Line;
        while (Peek().Kind == Tk.Ident)
        {
            string w = Peek().Text;
            if (w is "const" or "constexpr") isConst = true;
            else if (w == "static") isStatic = true;
            else if (w is "volatile" or "inline") { }
            else if (w == "unsigned") unsigned = true;
            else if (w == "signed") signed = true;
            else if (w == "long") longs++;
            else if (w == "short") shorts++;
            else if (baseName is null && (BaseTypes.Contains(w) || _aliases.ContainsKey(w))) baseName = w;
            else break;
            _p++;
        }
        _ = signed;
        if (baseName is not null && _aliases.TryGetValue(baseName, out var alias)) return (alias, isStatic, isConst);
        baseName ??= longs > 0 || shorts > 0 || unsigned || signed ? "int" : throw new SketchCompileException("Se esperaba un tipo (int, float…).", line);

        CType t = baseName switch
        {
            "void" => new CType("void", 0, IsVoid: true),
            "bool" or "boolean" => new CType("bool", 8, true, IsBool: true),
            "char" => new CType(unsigned ? "unsigned char" : "char", 8, unsigned),
            "byte" or "uint8_t" => new CType(baseName, 8, true),
            "int8_t" => new CType(baseName, 8),
            "uint16_t" or "word" => new CType(baseName, 16, true),
            "int16_t" => new CType(baseName, 16),
            "uint32_t" => new CType(baseName, 32, true),
            "int32_t" => new CType(baseName, 32),
            "uint64_t" => new CType(baseName, 64, true),
            "int64_t" => new CType(baseName, 64),
            "size_t" => new CType(baseName, _intBits, true),
            "float" => new CType("float", 32, IsFloat: true),
            "double" => new CType("double", _intBits == 16 ? 32 : 64, IsFloat: true), // en AVR double = float
            "String" => new CType("String", 0, IsString: true),
            "Servo" => new CType("Servo", 0, ObjectClass: "Servo"),
            _ => IntType(longs, shorts, unsigned),
        };
        return (t, isStatic, isConst);
    }

    private CType IntType(int longs, int shorts, bool unsigned)
    {
        int bits = longs >= 2 ? 64 : longs == 1 ? 32 : shorts > 0 ? 16 : _intBits;
        string name = (unsigned ? "unsigned " : "") + (longs >= 2 ? "long long" : longs == 1 ? "long" : shorts > 0 ? "short" : "int");
        return new CType(name, bits, unsigned);
    }

    // ---- Programa ----

    private SketchProgram ParseProgram()
    {
        var globals = new List<VarDeclStmt>();
        var funcs = new Dictionary<string, FuncDef>(StringComparer.Ordinal);
        while (Peek().Kind != Tk.Eof)
        {
            if (Accept(";")) continue;
            if (IsWord("enum"))
            {
                globals.Add(ParseEnum());
                continue;
            }
            if (IsWord("struct") || IsWord("class") || IsWord("typedef") || IsWord("template"))
                throw new SketchCompileException($"'{Peek().Text}' todavía no está soportado en el simulador.", Peek().Line);
            if (!IsTypeStart())
            {
                var t = Peek();
                string msg = t.Kind == Tk.Ident
                    ? $"'{t.Text}' no es un tipo conocido. Fuera de las funciones solo puede haber declaraciones."
                    : $"No se esperaba {t} aquí.";
                throw new SketchCompileException(msg, t.Line);
            }
            int line = Peek().Line;
            var (type, isStatic, isConst) = ParseType();
            if (Peek().Kind == Tk.Ident && IsOp("(", 1))
            {
                string name = Next().Text;
                var ps = ParseParams();
                if (Accept(";")) continue; // prototipo
                if (!IsOp("{")) throw new SketchCompileException($"Se esperaba '{{' para el cuerpo de {name}().", Peek().Line);
                var body = ParseBlock();
                if (funcs.ContainsKey(name)) throw new SketchCompileException($"La función {name}() está definida dos veces.", line);
                funcs[name] = new FuncDef(type, name, ps, body, line);
                continue;
            }
            globals.Add(ParseDeclarators(type, isStatic, isConst, line));
        }
        if (!funcs.ContainsKey("setup")) throw new SketchCompileException("Falta la función void setup() { … }.", 1);
        if (!funcs.ContainsKey("loop")) throw new SketchCompileException("Falta la función void loop() { … }.", 1);
        return new SketchProgram(globals, funcs);
    }

    private VarDeclStmt ParseEnum()
    {
        int line = Next().Line;
        if (IsWord("class")) _p++;
        string? name = Peek().Kind == Tk.Ident ? Next().Text : null;
        var intType = new CType("int", _intBits);
        if (name is not null) _aliases[name] = intType;
        Expect("{");
        var decls = new List<Declarator>();
        long value = 0;
        while (!IsOp("}"))
        {
            string item = ExpectIdent();
            if (Accept("="))
            {
                var e = ParseTernary();
                value = e is NumExpr n ? n.Value.L : e is UnaryExpr { Op: "-", Operand: NumExpr m } ? -m.Value.L
                    : throw new SketchCompileException("El valor de un enum debe ser un número.", line);
            }
            decls.Add(new Declarator(item, [], new NumExpr(Val.Int(value), line), null));
            value++;
            if (!Accept(",")) break;
        }
        Expect("}");
        Expect(";");
        return new VarDeclStmt(intType, decls, false, true, line);
    }

    private List<Param> ParseParams()
    {
        Expect("(");
        var ps = new List<Param>();
        if (IsWord("void") && IsOp(")", 1)) _p++;
        while (!IsOp(")"))
        {
            var (type, _, _) = ParseType();
            bool isRef = Accept("&");
            bool isPtr = Accept("*");
            string name = ExpectIdent();
            bool isArray = isPtr;
            while (Accept("["))
            {
                while (!IsOp("]")) Next();
                Expect("]");
                isArray = true;
            }
            ps.Add(new Param(type, name, isArray, isRef));
            if (!Accept(",")) break;
        }
        Expect(")");
        return ps;
    }

    private VarDeclStmt ParseDeclarators(CType type, bool isStatic, bool isConst, int line)
    {
        var vars = new List<Declarator>();
        do
        {
            if (Accept("*")) throw new SketchCompileException("Los punteros todavía no están soportados; usa arrays.", Peek().Line);
            string name = ExpectIdent();
            var dims = new List<Expr?>();
            while (Accept("["))
            {
                dims.Add(IsOp("]") ? null : ParseExpr());
                Expect("]");
            }
            Expr? init = null;
            List<Expr>? ctor = null;
            if (Accept("=")) init = IsOp("{") ? ParseInitList() : ParseAssign();
            else if (IsOp("{")) init = ParseInitList();
            else if (IsOp("("))
            {
                _p++;
                ctor = [];
                while (!IsOp(")"))
                {
                    ctor.Add(ParseAssign());
                    if (!Accept(",")) break;
                }
                Expect(")");
                if (!type.IsObject && ctor.Count == 1) { init = ctor[0]; ctor = null; }
            }
            if (type.IsVoid) throw new SketchCompileException($"Una variable no puede ser void ('{name}').", line);
            vars.Add(new Declarator(name, dims, init, ctor));
        } while (Accept(","));
        Expect(";");
        return new VarDeclStmt(type, vars, isStatic, isConst, line);
    }

    private InitListExpr ParseInitList()
    {
        int line = Expect("{").Line;
        var items = new List<Expr>();
        while (!IsOp("}"))
        {
            items.Add(IsOp("{") ? ParseInitList() : ParseAssign());
            if (!Accept(",")) break;
        }
        Expect("}");
        return new InitListExpr(items, line);
    }

    // ---- Sentencias ----

    private BlockStmt ParseBlock()
    {
        int line = Expect("{").Line;
        var body = new List<Stmt>();
        while (!IsOp("}"))
        {
            if (Peek().Kind == Tk.Eof) throw new SketchCompileException("Falta una llave '}' de cierre.", line);
            body.Add(ParseStmt());
        }
        Expect("}");
        return new BlockStmt(body, line);
    }

    private Stmt ParseStmt()
    {
        var t = Peek();
        int line = t.Line;
        if (IsOp("{")) return ParseBlock();
        if (Accept(";")) return new EmptyStmt(line);
        if (t.Kind == Tk.Ident)
        {
            switch (t.Text)
            {
                case "if":
                {
                    _p++;
                    Expect("(");
                    var c = ParseExpr();
                    Expect(")");
                    var then = ParseStmt();
                    Stmt? els = null;
                    if (IsWord("else")) { _p++; els = ParseStmt(); }
                    return new IfStmt(c, then, els, line);
                }
                case "while":
                {
                    _p++;
                    Expect("(");
                    var c = ParseExpr();
                    Expect(")");
                    return new WhileStmt(c, ParseStmt(), line);
                }
                case "do":
                {
                    _p++;
                    var body = ParseStmt();
                    if (!IsWord("while")) throw new SketchCompileException("Falta 'while (…);' después de 'do { … }'.", Peek().Line);
                    _p++;
                    Expect("(");
                    var c = ParseExpr();
                    Expect(")");
                    Expect(";");
                    return new DoWhileStmt(body, c, line);
                }
                case "for":
                {
                    _p++;
                    Expect("(");
                    Stmt? init = null;
                    if (!Accept(";"))
                    {
                        if (IsTypeStart())
                        {
                            var (type, st, cn) = ParseType();
                            init = ParseDeclarators(type, st, cn, line);
                        }
                        else
                        {
                            init = new ExprStmt(ParseExpr(), line);
                            Expect(";");
                        }
                    }
                    Expr? cond = IsOp(";") ? null : ParseExpr();
                    Expect(";");
                    Expr? step = IsOp(")") ? null : ParseExpr();
                    Expect(")");
                    return new ForStmt(init, cond, step, ParseStmt(), line);
                }
                case "switch":
                    return ParseSwitch();
                case "break":
                    _p++;
                    Expect(";");
                    return new BreakStmt(line);
                case "continue":
                    _p++;
                    Expect(";");
                    return new ContinueStmt(line);
                case "return":
                {
                    _p++;
                    Expr? v = IsOp(";") ? null : ParseExpr();
                    Expect(";");
                    return new ReturnStmt(v, line);
                }
                case "else":
                    throw new SketchCompileException("'else' sin 'if' delante (¿sobra un punto y coma después del if?).", line);
                case "enum":
                    return ParseEnum();
            }
            // int(x) o String(x) al principio de una sentencia es una conversión, no una declaración.
            if (IsTypeStart() && !IsOp("(", 1))
            {
                var (type, st, cn) = ParseType();
                return ParseDeclarators(type, st, cn, line);
            }
        }
        var e = ParseExpr();
        Expect(";");
        return new ExprStmt(e, line);
    }

    private Stmt ParseSwitch()
    {
        int line = Next().Line;
        Expect("(");
        var v = ParseExpr();
        Expect(")");
        Expect("{");
        var cases = new List<SwitchCase>();
        while (!Accept("}"))
        {
            var labels = new List<Expr>();
            bool isDefault = false;
            while (IsWord("case") || IsWord("default"))
            {
                if (Next().Text == "default") isDefault = true;
                else labels.Add(ParseTernary());
                Expect(":");
            }
            if (labels.Count == 0 && !isDefault) throw new SketchCompileException("Dentro de switch se esperaba 'case' o 'default'.", Peek().Line);
            var body = new List<Stmt>();
            while (!IsWord("case") && !IsWord("default") && !IsOp("}"))
            {
                if (Peek().Kind == Tk.Eof) throw new SketchCompileException("Falta la llave '}' del switch.", line);
                body.Add(ParseStmt());
            }
            cases.Add(new SwitchCase(labels, isDefault, body));
        }
        return new SwitchStmt(v, cases, line);
    }

    // ---- Expresiones ----

    private Expr ParseExpr()
    {
        var e = ParseAssign();
        while (IsOp(","))
        {
            int line = Next().Line;
            var right = ParseAssign();
            e = new BinaryExpr(",", e, right, line);
        }
        return e;
    }

    private static readonly HashSet<string> AssignOps = ["=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>="];

    private Expr ParseAssign()
    {
        var left = ParseTernary();
        if (Peek().Kind == Tk.Op && AssignOps.Contains(Peek().Text))
        {
            var op = Next();
            if (left is not (NameExpr or IndexExpr))
                throw new SketchCompileException($"A la izquierda de '{op.Text}' tiene que haber una variable.", op.Line);
            return new AssignExpr(op.Text, left, ParseAssign(), op.Line);
        }
        return left;
    }

    private Expr ParseTernary()
    {
        var c = ParseBinary(0);
        if (!IsOp("?")) return c;
        int line = Next().Line;
        var a = ParseAssign();
        Expect(":");
        var b = ParseAssign();
        return new CondExpr(c, a, b, line);
    }

    private static readonly string[][] Levels =
    [
        ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>"], ["+", "-"], ["*", "/", "%"],
    ];

    private Expr ParseBinary(int level)
    {
        if (level == Levels.Length) return ParseUnary();
        var left = ParseBinary(level + 1);
        while (Peek().Kind == Tk.Op && Levels[level].Contains(Peek().Text))
        {
            var op = Next();
            var right = ParseBinary(level + 1);
            left = new BinaryExpr(op.Text, left, right, op.Line);
        }
        return left;
    }

    private Expr ParseUnary()
    {
        var t = Peek();
        if (t.Kind == Tk.Op)
        {
            switch (t.Text)
            {
                case "!" or "~" or "-" or "+":
                    _p++;
                    return new UnaryExpr(t.Text, ParseUnary(), t.Line);
                case "++" or "--":
                    _p++;
                    return new IncExpr(t.Text, true, ParseUnary(), t.Line);
                case "&":
                    throw new SketchCompileException("Los punteros (&variable) todavía no están soportados.", t.Line);
                case "(" when IsTypeStart(1):
                {
                    int save = _p;
                    _p++;
                    var (type, _, _) = ParseType();
                    if (Accept(")")) return new CastExpr(type, ParseUnary(), t.Line);
                    _p = save;
                    break;
                }
            }
        }
        return ParsePostfix(ParsePrimary());
    }

    private Expr ParsePostfix(Expr e)
    {
        while (true)
        {
            var t = Peek();
            if (Accept("["))
            {
                var index = ParseExpr();
                Expect("]");
                e = new IndexExpr(e, index, t.Line);
            }
            else if (Accept("."))
            {
                string m = ExpectIdent();
                var args = IsOp("(") ? ParseArgs() : null;
                if (args is null) throw new SketchCompileException($"Se esperaba '(' después de .{m}.", t.Line);
                e = new MethodExpr(e, m, args, t.Line);
            }
            else if (IsOp("++") || IsOp("--"))
            {
                _p++;
                e = new IncExpr(t.Text, false, e, t.Line);
            }
            else return e;
        }
    }

    private List<Expr> ParseArgs()
    {
        Expect("(");
        var args = new List<Expr>();
        while (!IsOp(")"))
        {
            args.Add(ParseAssign());
            if (!Accept(",")) break;
        }
        Expect(")");
        return args;
    }

    private Expr ParsePrimary()
    {
        var t = Next();
        switch (t.Kind)
        {
            case Tk.Int:
            case Tk.Char:
                return new NumExpr(Val.Int(t.Int), t.Line);
            case Tk.Float:
                return new NumExpr(Val.Float(t.Float), t.Line);
            case Tk.String:
                return new StrExpr(t.Text, t.Line);
            case Tk.Ident:
                if (t.Text is "true" or "HIGH") return new NumExpr(Val.Int(1), t.Line);
                if (t.Text is "false" or "LOW") return new NumExpr(Val.Int(0), t.Line);
                if (IsOp("(")) return new CallExpr(t.Text, ParseArgs(), t.Line);
                return new NameExpr(t.Text, t.Line);
            case Tk.Op when t.Text == "(":
            {
                var e = ParseExpr();
                Expect(")");
                return e;
            }
        }
        throw new SketchCompileException(t.Kind == Tk.Eof ? "El programa termina de forma inesperada (¿falta una llave '}'?)." : $"No se esperaba {t} aquí.", t.Line);
    }
}
