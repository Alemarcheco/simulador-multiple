using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Runtime.Fbd;

/// <summary>
/// Traduce un FBD a la representación intermedia común. Cada bloque deja su resultado en una variable oculta
/// (<see cref="FbdNode.RuntimeName"/>) para poder colorear el diagrama en vivo.
/// </summary>
public static class FbdCompiler
{
    public static ProgramUnit Compile(FbdProgram program)
    {
        var vars = new List<VarDecl>();
        var body = new List<Stmt>();
        var instances = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int temp = 0;
        int net = 0;

        foreach (var n in program.Networks)
        {
            net++;
            if (n.Root is null) continue;
            int line = net;

            Expr Node(FbdNode node)
            {
                Expr e;
                switch (node)
                {
                    case FbdOperand o:
                        if (string.IsNullOrWhiteSpace(o.Text))
                            throw new PlcCompileException("Hay una entrada sin conectar.", line, 1);
                        e = LadderCompiler.Operand(o.Text);
                        if (o.Negated) e = new UnaryExpr("NOT", e);
                        node.RuntimeName = null;
                        return e;
                    case FbdGate g:
                    {
                        int? fixedCount = FbdGate.FixedInputs(g.Kind);
                        if (fixedCount is { } c && g.Inputs.Count != c)
                            throw new PlcCompileException($"El bloque {FbdGate.Symbol(g.Kind)} necesita {c} entrada(s).", line, 1);
                        if (g.Inputs.Count == 0)
                            throw new PlcCompileException($"El bloque {FbdGate.Symbol(g.Kind)} no tiene entradas.", line, 1);
                        var inputs = g.Inputs.Select(Node).ToList();
                        e = g.Kind switch
                        {
                            GateKind.And => Fold("AND", inputs),
                            GateKind.Or => Fold("OR", inputs),
                            GateKind.Xor => Fold("XOR", inputs),
                            GateKind.Not => new UnaryExpr("NOT", inputs[0]),
                            GateKind.Gt => new BinaryExpr(">", inputs[0], inputs[1]),
                            GateKind.Ge => new BinaryExpr(">=", inputs[0], inputs[1]),
                            GateKind.Eq => new BinaryExpr("=", inputs[0], inputs[1]),
                            GateKind.Ne => new BinaryExpr("<>", inputs[0], inputs[1]),
                            GateKind.Lt => new BinaryExpr("<", inputs[0], inputs[1]),
                            GateKind.Le => new BinaryExpr("<=", inputs[0], inputs[1]),
                            GateKind.Add => Fold("+", inputs),
                            GateKind.Sub => new BinaryExpr("-", inputs[0], inputs[1]),
                            GateKind.Mul => Fold("*", inputs),
                            GateKind.Div => new BinaryExpr("/", inputs[0], inputs[1]),
                            GateKind.Max => inputs.Count == 1 ? inputs[0] : new CallExpr("MAX", inputs),
                            GateKind.Min => inputs.Count == 1 ? inputs[0] : new CallExpr("MIN", inputs),
                            GateKind.Sel => new CallExpr("SEL", inputs),
                            _ => inputs[0],
                        };
                        if (g.Negated) e = new UnaryExpr("NOT", e);
                        break;
                    }
                    case FbdInstance fb:
                    {
                        if (!FunctionBlock.TryCreate(fb.Type, out var probe))
                            throw new PlcCompileException($"Bloque desconocido '{fb.Type}'.", line, 1);
                        if (instances.TryGetValue(fb.Instance, out var existing))
                        {
                            if (!string.Equals(existing, fb.Type, StringComparison.OrdinalIgnoreCase))
                                throw new PlcCompileException($"'{fb.Instance}' ya es un {existing}.", line, 1);
                            throw new PlcCompileException($"La instancia '{fb.Instance}' se usa dos veces; usa {fb.Instance}.{fb.Output} como operando.", line, 1);
                        }
                        instances[fb.Instance] = fb.Type;
                        vars.Add(new VarDecl(fb.Instance, fb.Type.ToUpperInvariant(), null, null));
                        var args = new List<(string, Expr)>();
                        foreach (var pin in FbdProgram.InputPins(fb.Type))
                            if (fb.Inputs.TryGetValue(pin, out var input)) args.Add((pin, Node(input)));
                        if (!probe.Has(fb.Output))
                            throw new PlcCompileException($"{fb.Type} no tiene la salida '{fb.Output}'.", line, 1);
                        body.Add(new FbCallStmt(fb.Instance, args) { Line = line });
                        e = new MemberExpr(fb.Instance, fb.Output);
                        node.RuntimeName = $"{fb.Instance}.{fb.Output}";
                        return e;
                    }
                    default:
                        throw new PlcCompileException("Elemento de FBD desconocido.", line, 1);
                }

                string name = $"__FBD{++temp}";
                bool boolean = node is FbdGate gate && (FbdGate.IsBoolean(gate.Kind) || gate.Negated);
                vars.Add(new VarDecl(name, boolean ? "BOOL" : "LREAL", null, null));
                body.Add(new AssignStmt(new NameExpr(name), e) { Line = line });
                node.RuntimeName = name;
                return new NameExpr(name);
            }

            var result = Node(n.Root);
            foreach (var o in n.Outputs)
            {
                if (string.IsNullOrWhiteSpace(o.Target)) continue;
                var target = LadderCompiler.Operand(o.Target);
                body.Add(o.Kind switch
                {
                    FbdAssignKind.Set => new IfStmt([(result, [new AssignStmt(target, new LiteralExpr(PlcValue.True))])], null) { Line = line },
                    FbdAssignKind.Reset => new IfStmt([(result, [new AssignStmt(target, new LiteralExpr(PlcValue.False))])], null) { Line = line },
                    _ => new AssignStmt(target, result) { Line = line },
                });
            }
        }
        return new ProgramUnit("FBD", vars, body);
    }

    private static Expr Fold(string op, List<Expr> inputs) => inputs.Aggregate((a, b) => new BinaryExpr(op, a, b));

    // ---- Archivos .fbd.json ----

    public const string Extension = ".fbd.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Save(FbdProgram program) => JsonSerializer.Serialize(program, Json);

    public static FbdProgram Load(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<FbdProgram>(json, Json) ?? throw new FormatException("El archivo está vacío.");
            if (p.Version > 1) throw new FormatException($"El archivo es de una versión más nueva ({p.Version}) del simulador.");
            return p;
        }
        catch (JsonException ex)
        {
            throw new FormatException($"El archivo no es un programa FBD válido: {ex.Message}", ex);
        }
    }
}
