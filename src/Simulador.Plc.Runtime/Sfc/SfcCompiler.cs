using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Runtime.Sfc;

/// <summary>
/// Traduce un SFC/GRAFCET a la representación intermedia común. En cada ciclo:
/// 1) se evalúan todas las transiciones franqueables (todas sus etapas de origen activas y condición verdadera) con el
/// estado del ciclo anterior; 2) se desactivan sus etapas de origen y se activan las de destino (franqueo simultáneo,
/// regla 4 de GRAFCET: si una etapa se activa y desactiva a la vez, queda activa); 3) se ejecutan las acciones.
/// </summary>
public static class SfcCompiler
{
    public static ProgramUnit Compile(SfcChart chart)
    {
        var vars = new List<VarDecl>(chart.Vars);
        var body = new List<Stmt>();
        var steps = new Dictionary<string, SfcStep>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in chart.Steps)
        {
            if (!steps.TryAdd(s.Name, s)) throw new PlcCompileException($"La etapa '{s.Name}' está repetida.", s.Line, 1);
            vars.Add(new VarDecl(s.Name, SfcStepBlock.TypeName, null, null) { Line = s.Line });
        }
        if (steps.Count == 0) throw new PlcCompileException("El diagrama no tiene etapas.", 1, 1);
        if (!chart.Steps.Any(s => s.Initial)) throw new PlcCompileException("Falta la etapa inicial (INITIAL_STEP).", chart.Steps[0].Line, 1);
        var actions = chart.Actions.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        Expr X(string step) => new MemberExpr(step, "X");
        Expr T(string step) => new MemberExpr(step, "T");
        LiteralExpr Bool(bool b) => new(PlcValue.FromBool(b));

        // Arranque: activa las etapas iniciales en el primer ciclo.
        vars.Add(new VarDecl("__SFC_INIT", "BOOL", null, new LiteralExpr(PlcValue.True)));
        body.Add(new IfStmt([(new NameExpr("__SFC_INIT"),
            [.. chart.Steps.Where(s => s.Initial).Select(s => (Stmt)new AssignStmt(X(s.Name), Bool(true))), new AssignStmt(new NameExpr("__SFC_INIT"), Bool(false))])], null));

        // 1) Transiciones franqueables.
        int k = 0;
        var fire = new List<(string Var, SfcTransition Tr)>();
        foreach (var tr in chart.Transitions)
        {
            foreach (var s in tr.From.Concat(tr.To))
                if (!steps.ContainsKey(s)) throw new PlcCompileException($"La transición usa la etapa '{s}', que no existe.", tr.Line, 1);
            string v = $"__TR{++k}";
            vars.Add(new VarDecl(v, "BOOL", null, null));
            Expr enabled = tr.From.Select(X).Aggregate((a, b) => new BinaryExpr("AND", a, b));
            body.Add(new AssignStmt(new NameExpr(v), new BinaryExpr("AND", enabled, tr.Condition)) { Line = tr.Line });
            fire.Add((v, tr));
        }

        // 2) Franqueo: primero se desactiva y después se activa.
        foreach (var (v, tr) in fire)
            body.Add(new IfStmt([(new NameExpr(v), tr.From.Select(s => (Stmt)new AssignStmt(X(s), Bool(false))).ToList())], null) { Line = tr.Line });
        foreach (var (v, tr) in fire)
            body.Add(new IfStmt([(new NameExpr(v), tr.To.Select(s => (Stmt)new AssignStmt(X(s), Bool(true))).ToList())], null) { Line = tr.Line });

        // Actualiza el tiempo de cada etapa.
        foreach (var s in chart.Steps) body.Add(new FbCallStmt(s.Name, []) { Line = s.Line });

        // 3) Acciones. Las N sobre una variable booleana se combinan: la variable vale TRUE si alguna etapa activa la pide.
        var nTargets = new Dictionary<string, List<Expr>>(StringComparer.OrdinalIgnoreCase);
        int pulses = 0;
        foreach (var s in chart.Steps)
        {
            foreach (var a in s.Actions)
            {
                Expr active = a.Qualifier switch
                {
                    "D" => new BinaryExpr("AND", X(s.Name), new BinaryExpr(">=", T(s.Name), new LiteralExpr(PlcValue.FromTime(a.Time!.Value)))),
                    "L" => new BinaryExpr("AND", X(s.Name), new BinaryExpr("<", T(s.Name), new LiteralExpr(PlcValue.FromTime(a.Time!.Value)))),
                    _ => X(s.Name),
                };
                if (a.Qualifier == "P")
                {
                    string trig = $"__SFC_P{++pulses}";
                    vars.Add(new VarDecl(trig, "R_TRIG", null, null));
                    body.Add(new FbCallStmt(trig, [("CLK", X(s.Name))]) { Line = s.Line });
                    active = new MemberExpr(trig, "Q");
                }

                if (actions.TryGetValue(a.Action, out var code))
                {
                    if (a.Qualifier is "S" or "R")
                        throw new PlcCompileException($"La acción con código '{a.Action}' no admite {a.Qualifier}; usa N, P, D o L.", s.Line, 1);
                    body.Add(new IfStmt([(active, code.Body)], null) { Line = s.Line });
                    continue;
                }

                var target = Target(a.Action);
                switch (a.Qualifier)
                {
                    case "S":
                    case "R":
                        body.Add(new IfStmt([(active, [new AssignStmt(target, Bool(a.Qualifier == "S"))])], null) { Line = s.Line });
                        break;
                    case "P":
                        body.Add(new IfStmt([(active, [new AssignStmt(target, Bool(true))])], null) { Line = s.Line });
                        break;
                    default:
                        if (!nTargets.TryGetValue(a.Action, out var list)) nTargets[a.Action] = list = [];
                        list.Add(active);
                        break;
                }
            }
        }
        foreach (var (name, conds) in nTargets)
            body.Add(new AssignStmt(Target(name), conds.Aggregate((a, b) => new BinaryExpr("OR", a, b))));

        return new ProgramUnit(chart.Name, vars, body);
    }

    private static Expr Target(string name) => Ladder.LadderCompiler.Operand(name);
}
