using System.Globalization;
using System.Text.RegularExpressions;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Runtime.Il;

/// <summary>
/// Traduce lista de instrucciones (IL, AWL/STL) de IEC 61131-3 a la representación intermedia común.
/// </summary>
/// <remarks>
/// Instrucciones: LD/LDN, ST/STN, S, R, AND/ANDN, OR/ORN, XOR/XORN, NOT, ADD, SUB, MUL, DIV, MOD, GT, GE, EQ, NE, LE, LT,
/// paréntesis «AND(» … «)», JMP/JMPC/JMPCN, CAL/CALC/CALCN y RET/RETC/RETCN. El acumulador (resultado actual) se lleva
/// como una expresión y se guarda en una variable oculta cuando hace falta para respetar el orden de ejecución.
/// Las etiquetas empiezan un tramo nuevo: tras una etiqueta la primera instrucción debe cargar el acumulador (LD).
/// </remarks>
public sealed partial class IlCompiler
{
    private readonly PlcProfile? _profile;
    private readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VarDecl> _vars = [];
    private int _temps;

    private IlCompiler(PlcProfile? profile) => _profile = profile;

    public static ProgramUnit Compile(string source, PlcProfile? profile = null) => new IlCompiler(profile).Run(source);

    private sealed record Line(int Number, string? Label, string Op, string Operand);

    private sealed class Segment
    {
        public string? Label;
        public List<Stmt> Body { get; } = [];
        /// <summary>null = sigue en el tramo siguiente.</summary>
        public Stmt? Exit;
    }

    [GeneratedRegex(@"\(\*.*?\*\)", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"^\s*(?:([A-Za-z_]\w*)\s*:(?!=))?\s*(.*)$")]
    private static partial Regex LabelLine();

    private ProgramUnit Run(string source)
    {
        // Los comentarios de bloque se sustituyen por espacios conservando los saltos de línea.
        source = BlockComment().Replace(source, m => new string(m.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()));
        var rawLines = source.Replace("\r", "").Split('\n');

        // Declaraciones: desde el principio hasta el último END_VAR (se analizan con el analizador de ST).
        int bodyStart = 0;
        for (int i = 0; i < rawLines.Length; i++)
            if (Regex.IsMatch(rawLines[i], @"\bEND_VAR\b", RegexOptions.IgnoreCase)) bodyStart = i + 1;
        if (bodyStart > 0)
        {
            var declText = Regex.Replace(string.Join('\n', rawLines.Take(bodyStart)), @"^\s*PROGRAM\s+\w+", "", RegexOptions.IgnoreCase);
            var decl = Parser.ParseProgram(declText);
            foreach (var v in decl.Vars)
            {
                _vars.Add(v);
                _types[v.Name] = v.TypeName;
            }
        }

        var lines = new List<Line>();
        for (int i = bodyStart; i < rawLines.Length; i++)
        {
            var text = rawLines[i];
            int c = text.IndexOf("//", StringComparison.Ordinal);
            if (c >= 0) text = text[..c];
            text = text.Trim();
            if (text.Length == 0) continue;
            if (Regex.IsMatch(text, @"^(PROGRAM\b.*|END_PROGRAM)$", RegexOptions.IgnoreCase)) continue;
            var m = LabelLine().Match(text);
            string? label = m.Groups[1].Success ? m.Groups[1].Value : null;
            string rest = m.Groups[2].Value.Trim();
            if (rest.Length == 0)
            {
                lines.Add(new Line(i + 1, label, "", ""));
                continue;
            }
            int sp = rest.IndexOfAny([' ', '\t', '(']);
            string op = sp < 0 ? rest : rest[..sp];
            string operand = sp < 0 ? "" : rest[sp..].Trim();
            if (sp >= 0 && rest[sp] == '(' && !op.StartsWith("CAL", StringComparison.OrdinalIgnoreCase))
            {
                op += "(";
                operand = rest[(sp + 1)..].Trim();
            }
            lines.Add(new Line(i + 1, label, op.ToUpperInvariant(), operand));
        }

        var segments = new List<Segment> { new() };
        Expr? cr = null;
        var stack = new Stack<(string Op, Expr? Saved)>();
        bool hasJumps = false;

        Segment Cur() => segments[^1];

        void Emit(Stmt s, int line) => Cur().Body.Add(s with { Line = line });

        Expr Need(Line l) => cr ?? throw Error(l, "El acumulador está vacío: empieza con LD.");

        // Guarda el acumulador en una variable oculta (salvo constantes), para que un ST posterior no
        // cambie su valor.
        void Materialize(Line l)
        {
            if (cr is null or LiteralExpr || cr is NameExpr n0 && n0.Name.StartsWith("__CR", StringComparison.Ordinal)) return;
            string name = $"__CR{++_temps}";
            string type = TypeOf(cr);
            _vars.Add(new VarDecl(name, type, null, null) { Line = l.Number });
            _types[name] = type;
            Emit(new AssignStmt(new NameExpr(name), cr), l.Number);
            cr = new NameExpr(name);
        }

        void NewSegment(string? label, Line l)
        {
            if (cr is not null && stack.Count > 0) throw Error(l, "Falta cerrar un paréntesis antes de la etiqueta.");
            segments.Add(new Segment { Label = label });
            cr = null;
        }

        foreach (var l in lines)
        {
            if (l.Label is not null)
            {
                if (segments.Any(s => string.Equals(s.Label, l.Label, StringComparison.OrdinalIgnoreCase)))
                    throw Error(l, $"La etiqueta '{l.Label}' está repetida.");
                NewSegment(l.Label, l);
            }
            if (l.Op.Length == 0) continue;

            string op = Aliases.GetValueOrDefault(l.Op, l.Op);
            // En AWL de Siemens la primera U/O/UN/ON de una red carga el acumulador.
            if (cr is null && SiemensFirstLoad.TryGetValue(l.Op, out var load)) op = load;
            switch (op)
            {
                case "LD": cr = Operand(l); break;
                case "LDN": cr = Not(Operand(l)); break;
                case "ST":
                    Materialize(l);
                    Emit(new AssignStmt(Target(l), Need(l)), l.Number);
                    break;
                case "STN":
                    Materialize(l);
                    Emit(new AssignStmt(Target(l), Not(Need(l))), l.Number);
                    break;
                case "S":
                case "R":
                    Materialize(l);
                    Emit(new IfStmt([(Need(l), [new AssignStmt(Target(l), new LiteralExpr(PlcValue.FromBool(op == "S")))])], null), l.Number);
                    break;
                case "NOT": cr = Not(Need(l)); break;
                case ")":
                    if (stack.Count == 0) throw Error(l, "')' sin '(' correspondiente.");
                    var (pop, saved) = stack.Pop();
                    cr = Combine(pop, saved ?? throw Error(l, "Paréntesis vacío."), Need(l), l);
                    break;
                case "JMP":
                case "JMPC":
                case "JMPCN":
                {
                    hasJumps = true;
                    Expr? cond = null;
                    if (op != "JMP")
                    {
                        Materialize(l);
                        cond = op == "JMPC" ? Need(l) : Not(Need(l));
                    }
                    Cur().Exit = new JumpStmt(l.Operand.Trim(), cond) { Line = l.Number };
                    NewSegment(null, l);
                    break;
                }
                case "RET":
                    Emit(new ReturnStmt(), l.Number);
                    break;
                case "RETC":
                case "RETCN":
                    Emit(new IfStmt([(op == "RETC" ? Need(l) : Not(Need(l)), [new ReturnStmt()])], null), l.Number);
                    break;
                case "CAL":
                case "CALC":
                case "CALCN":
                {
                    var call = Call(l);
                    if (op == "CAL") Emit(call, l.Number);
                    else Emit(new IfStmt([(op == "CALC" ? Need(l) : Not(Need(l)), [call])], null), l.Number);
                    break;
                }
                default:
                {
                    bool paren = op.EndsWith('(');
                    string bare = paren ? op[..^1] : op;
                    if (!Binary.ContainsKey(bare)) throw Error(l, $"Instrucción desconocida '{l.Op}'.");
                    if (paren)
                    {
                        stack.Push((bare, Need(l)));
                        cr = l.Operand.Length == 0 ? null : Operand(l);
                    }
                    else cr = Combine(bare, Need(l), Operand(l), l);
                    break;
                }
            }
        }
        if (stack.Count > 0) throw new PlcCompileException("Falta cerrar un paréntesis.", lines.LastOrDefault()?.Number ?? 1, 1);

        if (!hasJumps) return new ProgramUnit("IL", _vars, segments.SelectMany(s => s.Body).ToList());
        return new ProgramUnit("IL", _vars, StateMachine(segments));
    }

    /// <summary>Salto (solo existe durante la compilación; se convierte en la máquina de estados).</summary>
    private sealed record JumpStmt(string Target, Expr? Condition) : Stmt;

    /// <summary>
    /// Con saltos, cada tramo es un estado: __PC := 0; WHILE __PC >= 0 DO CASE __PC OF 0: …; __PC := siguiente … END_CASE
    /// END_WHILE. El perro guardián corta los bucles infinitos.
    /// </summary>
    private List<Stmt> StateMachine(List<Segment> segments)
    {
        _vars.Add(new VarDecl("__PC", "INT", null, null));
        var pc = new NameExpr("__PC");
        Expr Lit(long v) => new LiteralExpr(PlcValue.FromInt(v));
        int IndexOf(JumpStmt j)
        {
            int i = segments.FindIndex(s => string.Equals(s.Label, j.Target, StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new PlcCompileException($"La etiqueta '{j.Target}' no existe.", j.Line, 1);
            return i;
        }

        var cases = new List<(IReadOnlyList<(long, long)>, IReadOnlyList<Stmt>)>();
        for (int i = 0; i < segments.Count; i++)
        {
            var body = new List<Stmt>(segments[i].Body);
            long next = i + 1 < segments.Count ? i + 1 : -1;
            switch (segments[i].Exit)
            {
                case JumpStmt { Condition: null } j:
                    body.Add(new AssignStmt(pc, Lit(IndexOf(j))));
                    break;
                case JumpStmt j:
                    body.Add(new IfStmt([(j.Condition, [new AssignStmt(pc, Lit(IndexOf(j)))])], [new AssignStmt(pc, Lit(next))]));
                    break;
                default:
                    body.Add(new AssignStmt(pc, Lit(next)));
                    break;
            }
            cases.Add(([(i, i)], body));
        }
        return
        [
            new AssignStmt(pc, Lit(0)),
            new WhileStmt(new BinaryExpr(">=", pc, Lit(0)), [new CaseStmt(pc, cases, [new AssignStmt(pc, Lit(-1))])]),
        ];
    }

    /// <summary>Nombres de otras marcas: AWL de Siemens (U, O, =…), STL en inglés (A, AN) y Mitsubishi/Delta (LDI, ANI, OUT…).</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["U"] = "AND", ["UN"] = "ANDN", ["A"] = "AND", ["AN"] = "ANDN", ["O"] = "OR", ["ON"] = "ORN", ["X"] = "XOR", ["XN"] = "XORN",
        ["U("] = "AND(", ["UN("] = "ANDN(", ["A("] = "AND(", ["AN("] = "ANDN(", ["O("] = "OR(", ["ON("] = "ORN(",
        ["="] = "ST", ["LDI"] = "LDN", ["ANI"] = "ANDN", ["ORI"] = "ORN", ["OUT"] = "ST", ["SET"] = "S", ["RST"] = "R",
    };

    private static readonly Dictionary<string, string> SiemensFirstLoad = new(StringComparer.OrdinalIgnoreCase)
    {
        ["U"] = "LD", ["A"] = "LD", ["O"] = "LD", ["UN"] = "LDN", ["AN"] = "LDN", ["ON"] = "LDN",
    };

    private static readonly Dictionary<string, string> Binary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AND"] = "AND", ["ANDN"] = "AND!", ["&"] = "AND", ["&N"] = "AND!",
        ["OR"] = "OR", ["ORN"] = "OR!", ["XOR"] = "XOR", ["XORN"] = "XOR!",
        ["ADD"] = "+", ["SUB"] = "-", ["MUL"] = "*", ["DIV"] = "/", ["MOD"] = "MOD",
        ["GT"] = ">", ["GE"] = ">=", ["EQ"] = "=", ["NE"] = "<>", ["LE"] = "<=", ["LT"] = "<",
    };

    private Expr Combine(string op, Expr left, Expr right, Line l)
    {
        string b = Binary[op];
        if (b.EndsWith('!')) return new BinaryExpr(b[..^1], left, Not(right));
        return new BinaryExpr(b, left, right);
    }

    private static Expr Not(Expr e) => new UnaryExpr("NOT", e);

    private Expr Operand(Line l)
    {
        if (l.Operand.Length == 0) throw Error(l, $"{l.Op} necesita un operando.");
        string text = l.Operand;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && text.Contains('.') && !char.IsLetter(text[0]))
        {
            // "1.5" es un real, salvo que la marca lo use como dirección (Omron 0.00).
            if (_profile is null || !_profile.TryParse(text, out _)) return new LiteralExpr(PlcValue.FromReal(r));
        }
        try
        {
            return LadderCompiler.Operand(text);
        }
        catch (PlcCompileException ex)
        {
            throw Error(l, ex.Message);
        }
    }

    private Expr Target(Line l)
    {
        var e = Operand(l);
        if (e is LiteralExpr) throw Error(l, "No se puede guardar en una constante.");
        return e;
    }

    private Stmt Call(Line l)
    {
        var m = Regex.Match(l.Operand, @"^([A-Za-z_]\w*)\s*(?:\((.*)\))?\s*$");
        if (!m.Success) throw Error(l, "Formato de CAL: CAL Instancia(Entrada := valor, …).");
        var args = new List<(string, Expr)>();
        if (m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0)
        {
            foreach (var part in m.Groups[2].Value.Split(','))
            {
                var kv = part.Split(":=", 2);
                if (kv.Length != 2) throw Error(l, $"Parámetro no válido '{part.Trim()}' (usa Nombre := valor).");
                args.Add((kv[0].Trim(), Operand(l with { Operand = kv[1].Trim() })));
            }
        }
        return new FbCallStmt(m.Groups[1].Value, args) { Line = l.Number };
    }

    /// <summary>Tipo aproximado de una expresión para declarar la variable oculta del acumulador.</summary>
    private string TypeOf(Expr e) => e switch
    {
        LiteralExpr { Value.Type: var t } => t switch { PlcType.Bool => "BOOL", PlcType.Real => "LREAL", PlcType.Time => "TIME", _ => "DINT" },
        NameExpr n when _types.TryGetValue(n.Name, out var t) => t,
        NameExpr n when _profile is not null && _profile.TryParse(n.Name, out var a) => a.Size == IoSize.Bit ? "BOOL" : "DINT",
        NameExpr n when IecAddressSize(n.Name) is { } size => size,
        NameExpr => "BOOL",
        MemberExpr m when _types.TryGetValue(m.Instance, out var fbType) && FunctionBlock.TryCreate(fbType, out var fb) && fb.Has(m.Member) =>
            fb.Types[m.Member] switch { PlcType.Bool => "BOOL", PlcType.Real => "LREAL", PlcType.Time => "TIME", _ => "DINT" },
        UnaryExpr u => TypeOf(u.Operand),
        BinaryExpr { Op: "=" or "<>" or "<" or ">" or "<=" or ">=" } => "BOOL",
        BinaryExpr { Op: "AND" or "OR" or "XOR" } b => TypeOf(b.Left),
        BinaryExpr b => Wider(TypeOf(b.Left), TypeOf(b.Right)),
        _ => "DINT",
    };

    private static string? IecAddressSize(string text) =>
        IecAddress.TryParse(text, out var a) ? (a.Size == IoSize.Bit ? "BOOL" : "DINT") : null;

    private static string Wider(string a, string b)
    {
        if (a.Contains("REAL") || b.Contains("REAL")) return "LREAL";
        if (a == "TIME" || b == "TIME") return "TIME";
        return "DINT";
    }

    private static PlcCompileException Error(Line l, string message) => new(message, l.Number, 1);
}
