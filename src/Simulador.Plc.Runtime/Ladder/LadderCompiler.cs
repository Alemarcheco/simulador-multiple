using System.Globalization;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Runtime.Ladder;

/// <summary>
/// Traduce un programa Ladder a la representación intermedia común. Las instancias de temporizadores y contadores que no
/// estén declaradas se declaran solas, y los contactos de flanco usan instancias ocultas R_TRIG/F_TRIG.
/// </summary>
public static class LadderCompiler
{
    public static ProgramUnit Compile(LadderProgram program)
    {
        var vars = program.Variables.ToList();
        var declared = new HashSet<string>(vars.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        var body = new List<Stmt>();
        int edge = 0;

        void Ensure(string name, string type)
        {
            if (declared.Add(name)) vars.Add(new VarDecl(name, type, null, null));
        }

        int rungNo = 0;
        foreach (var rung in program.Rungs)
        {
            rungNo++;
            var pre = new List<Stmt>();
            Expr cond = rung.Condition is null ? new LiteralExpr(PlcValue.True) : Condition(rung.Condition);

            Expr Condition(LadderNode n) => n switch
            {
                Contact { Kind: ContactKind.NormallyOpen } c => Operand(c.Operand),
                Contact { Kind: ContactKind.NormallyClosed } c => new UnaryExpr("NOT", Operand(c.Operand)),
                Contact c => Edge(c),
                CompareContact cc => new BinaryExpr(cc.Op, Operand(cc.Left), Operand(cc.Right)),
                Series s when s.Items.Count == 0 => new LiteralExpr(PlcValue.True),
                Series s => s.Items.Select(Condition).Aggregate((a, b) => new BinaryExpr("AND", a, b)),
                ParallelBranch p when p.Branches.Count == 0 => new LiteralExpr(PlcValue.False),
                ParallelBranch p => p.Branches.Select(Condition).Aggregate((a, b) => new BinaryExpr("OR", a, b)),
                _ => throw new PlcCompileException("Elemento de Ladder desconocido.", rungNo, 1),
            };

            Expr Edge(Contact c)
            {
                string inst = $"__EDGE{++edge}";
                Ensure(inst, c.Kind == ContactKind.RisingEdge ? "R_TRIG" : "F_TRIG");
                pre.Add(new FbCallStmt(inst, [("CLK", Operand(c.Operand))]) { Line = rungNo });
                return new MemberExpr(inst, "Q");
            }

            var stmts = new List<Stmt>();
            // Con varias salidas, la condición se evalúa una sola vez (como el flujo de corriente real del segmento).
            if (rung.Outputs.Count > 1 && cond is not LiteralExpr)
            {
                string flow = $"__RUNG{rungNo}";
                Ensure(flow, "BOOL");
                stmts.Add(new AssignStmt(new NameExpr(flow), cond));
                cond = new NameExpr(flow);
            }
            foreach (var o in rung.Outputs)
            {
                switch (o)
                {
                    case Coil { Kind: CoilKind.Normal } c:
                        stmts.Add(new AssignStmt(Operand(c.Operand), cond));
                        break;
                    case Coil { Kind: CoilKind.Negated } c:
                        stmts.Add(new AssignStmt(Operand(c.Operand), new UnaryExpr("NOT", cond)));
                        break;
                    case Coil c:
                        stmts.Add(new IfStmt([(cond, [new AssignStmt(Operand(c.Operand), new LiteralExpr(PlcValue.FromBool(c.Kind == CoilKind.Set)))])], null));
                        break;
                    case TimerBox t:
                        Ensure(t.Instance, t.Kind.ToString());
                        stmts.Add(new FbCallStmt(t.Instance, [("IN", cond), ("PT", new LiteralExpr(PlcValue.FromTime(t.Preset)))]));
                        break;
                    case CounterBox k:
                        Ensure(k.Instance, k.Kind.ToString());
                        var inputs = new List<(string, Expr)>
                        {
                            (k.Kind == CounterKind.CTU ? "CU" : "CD", cond),
                            (k.Kind == CounterKind.CTU ? "R" : "LD", k.ResetOperand is null ? new LiteralExpr(PlcValue.False) : Operand(k.ResetOperand)),
                            ("PV", new LiteralExpr(PlcValue.FromInt(k.Preset))),
                        };
                        stmts.Add(new FbCallStmt(k.Instance, inputs));
                        break;
                    case MoveBox m:
                        stmts.Add(new IfStmt([(cond, [new AssignStmt(Operand(m.Destination), Operand(m.Source))])], null));
                        break;
                }
            }

            body.AddRange(pre);
            body.AddRange(stmts.Select(s => s with { Line = rungNo }));
        }

        return new ProgramUnit("Ladder", vars, body);
    }

    /// <summary>Operando como texto: número, literal de tiempo, TRUE/FALSE, Instancia.Salida, variable o dirección.</summary>
    internal static Expr Operand(string text)
    {
        text = text.Trim();
        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new LiteralExpr(PlcValue.True);
        if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new LiteralExpr(PlcValue.False);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return new LiteralExpr(PlcValue.FromInt(i));
        if (text.StartsWith("T#", StringComparison.OrdinalIgnoreCase))
            return new LiteralExpr(PlcValue.FromTime(Lexer.ParseTime(text[2..], default)));
        // Números reales (2.5, -0.75, 1e3). "0.00" con dos decimales es una dirección de Omron, no un real.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
            && !System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\.\d\d$"))
            return new LiteralExpr(PlcValue.FromReal(r));

        // "T1.Q" es salida de bloque; "I0.0", "0.00" o "%IX0.0" son direcciones (las resuelve el intérprete).
        int dot = text.IndexOf('.');
        if (dot > 0 && !text.StartsWith('%') && char.IsLetter(text[dot + 1]))
            return new MemberExpr(text[..dot], text[(dot + 1)..]);
        return new NameExpr(text);
    }
}
