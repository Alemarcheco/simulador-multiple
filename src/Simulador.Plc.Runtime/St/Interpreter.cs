using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Plc.Runtime.St;

/// <summary>Error en tiempo de ejecución: el PLC pasa a STOP (como un fallo de CPU).</summary>
public sealed class PlcRuntimeException(string message, int line = 0)
    : Exception(line > 0 ? $"Línea {line}: {message}" : message)
{
    public int Line { get; } = line;
}

/// <summary>Variable del programa: elemental (con o sin dirección AT) o instancia de bloque de función.</summary>
public sealed class Variable
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public PlcType Type { get; init; }
    public IoAddress? Address { get; init; }
    public FunctionBlock? Block { get; init; }
    public PlcValue Value { get; set; }
    public bool Retain { get; init; }
}

/// <summary>
/// Intérprete de la representación intermedia. Ejecuta el cuerpo del programa una vez por ciclo de scan.
/// </summary>
public sealed class Interpreter
{
    private readonly ProgramUnit _program;
    private readonly PlcProfile _profile;
    private readonly ProcessImage _image;
    private readonly Dictionary<string, Variable> _vars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IoAddress> _addressCache = new(StringComparer.OrdinalIgnoreCase);
    private long _budget;
    private TimeSpan _now;

    /// <summary>Máximo de operaciones por ciclo antes de disparar el perro guardián (bucles infinitos).</summary>
    public long WatchdogOperations { get; set; } = 2_000_000;

    public Interpreter(ProgramUnit program, PlcProfile profile, ProcessImage image)
    {
        _program = program;
        _profile = profile;
        _image = image;
        foreach (var d in program.Vars) Declare(d);
        Validate(program.Body);
    }

    public IReadOnlyCollection<Variable> Variables => _vars.Values;

    public Variable? Find(string name) => _vars.GetValueOrDefault(name);

    private static readonly Dictionary<string, PlcType> Elementary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOOL"] = PlcType.Bool,
        ["SINT"] = PlcType.Int, ["INT"] = PlcType.Int, ["DINT"] = PlcType.Int, ["LINT"] = PlcType.Int,
        ["USINT"] = PlcType.Int, ["UINT"] = PlcType.Int, ["UDINT"] = PlcType.Int, ["ULINT"] = PlcType.Int,
        ["BYTE"] = PlcType.Int, ["WORD"] = PlcType.Int, ["DWORD"] = PlcType.Int, ["LWORD"] = PlcType.Int,
        ["REAL"] = PlcType.Real, ["LREAL"] = PlcType.Real,
        ["TIME"] = PlcType.Time,
    };

    private void Declare(VarDecl d)
    {
        if (_vars.ContainsKey(d.Name)) throw new PlcCompileException($"La variable '{d.Name}' está declarada dos veces.", d.Line, 1);
        if (Elementary.TryGetValue(d.TypeName, out var type))
        {
            IoAddress? addr = null;
            if (d.At is not null)
            {
                if (!_profile.TryParse(d.At, out var a))
                    throw new PlcCompileException($"Dirección '{d.At}' no válida para {_profile.DisplayName}.", d.Line, 1);
                addr = a;
            }
            var v = new Variable { Name = d.Name, TypeName = d.TypeName, Type = type, Address = addr, Retain = d.Retain };
            v.Value = d.Initial is null ? PlcValue.Default(type) : EvalConst(d.Initial, d.Line).ConvertTo(type);
            _vars[d.Name] = v;
            return;
        }
        if (FunctionBlock.TryCreate(d.TypeName, out var fb))
        {
            if (d.At is not null) throw new PlcCompileException("Un bloque de función no puede tener dirección AT.", d.Line, 1);
            _vars[d.Name] = new Variable { Name = d.Name, TypeName = d.TypeName, Block = fb };
            return;
        }
        throw new PlcCompileException($"Tipo desconocido '{d.TypeName}'.", d.Line, 1);
    }

    private PlcValue EvalConst(Expr e, int line)
    {
        try
        {
            _budget = WatchdogOperations;
            return Eval(e);
        }
        catch (PlcRuntimeException ex)
        {
            throw new PlcCompileException(ex.Message, line, 1);
        }
    }

    /// <summary>Comprueba nombres y bloques en compilación para avisar antes de pasar a RUN.</summary>
    private void Validate(IEnumerable<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            try
            {
                switch (s)
                {
                    case AssignStmt a: CheckExpr(a.Target); CheckExpr(a.Value); break;
                    case FbCallStmt c:
                        var fb = Block(c.Instance);
                        foreach (var (p, v) in c.Inputs)
                        {
                            if (!fb.Inputs.Contains(p)) throw new PlcRuntimeException($"'{p}' no es una entrada de {c.Instance} ({_vars[c.Instance].TypeName}).");
                            CheckExpr(v);
                        }
                        break;
                    case IfStmt i:
                        foreach (var (cond, body) in i.Branches) { CheckExpr(cond); Validate(body); }
                        if (i.Else is not null) Validate(i.Else);
                        break;
                    case CaseStmt cs:
                        CheckExpr(cs.Selector);
                        foreach (var (_, body) in cs.Cases) Validate(body);
                        if (cs.Else is not null) Validate(cs.Else);
                        break;
                    case ForStmt f:
                        CheckExpr(new NameExpr(f.Var)); CheckExpr(f.From); CheckExpr(f.To);
                        if (f.By is not null) CheckExpr(f.By);
                        Validate(f.Body);
                        break;
                    case WhileStmt w: CheckExpr(w.Cond); Validate(w.Body); break;
                    case RepeatStmt r: Validate(r.Body); CheckExpr(r.Until); break;
                }
            }
            catch (PlcRuntimeException ex)
            {
                throw new PlcCompileException(ex.Message, s.Line, 1);
            }
        }
    }

    private void CheckExpr(Expr e)
    {
        switch (e)
        {
            case NameExpr n:
                if (!_vars.TryGetValue(n.Name, out var v)) Resolve(n.Name);
                else if (v.Block is not null) throw new PlcRuntimeException($"'{n.Name}' es un bloque de función: usa {n.Name}.Q u otra salida.");
                break;
            case MemberExpr m:
                var fb = Block(m.Instance);
                if (!fb.Has(m.Member)) throw new PlcRuntimeException($"{m.Instance} ({_vars[m.Instance].TypeName}) no tiene '{m.Member}'.");
                break;
            case UnaryExpr u: CheckExpr(u.Operand); break;
            case BinaryExpr b: CheckExpr(b.Left); CheckExpr(b.Right); break;
            case CallExpr c:
                if (!Functions.Contains(c.Function)) throw new PlcRuntimeException($"Función desconocida '{c.Function}'.");
                foreach (var a in c.Args) CheckExpr(a);
                break;
        }
    }

    /// <summary>Ejecuta un ciclo. Las entradas ya deben estar copiadas en la imagen de proceso.</summary>
    public void RunCycle(TimeSpan now)
    {
        _now = now;
        _budget = WatchdogOperations;
        try
        {
            Exec(_program.Body);
        }
        catch (ReturnSignal)
        {
        }
    }

    /// <summary>Vuelve a los valores iniciales (arranque en frío). Las variables RETAIN se conservan.</summary>
    public void ColdStart()
    {
        foreach (var d in _program.Vars)
        {
            var v = _vars[d.Name];
            if (v.Block is not null)
            {
                FunctionBlock.TryCreate(d.TypeName, out var fresh);
                foreach (var (k, val) in fresh.Members.ToList()) v.Block[k] = val;
            }
            else if (!v.Retain)
            {
                v.Value = d.Initial is null ? PlcValue.Default(v.Type) : EvalConst(d.Initial, d.Line).ConvertTo(v.Type);
            }
        }
    }

    private sealed class ExitSignal : Exception;
    private sealed class ReturnSignal : Exception;

    private void Tick(int line)
    {
        if (--_budget < 0)
            throw new PlcRuntimeException("Perro guardián: el ciclo no termina (¿bucle infinito?).", line);
    }

    private void Exec(IReadOnlyList<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            Tick(s.Line);
            try
            {
                ExecOne(s);
            }
            catch (PlcRuntimeException ex) when (ex.Line == 0 && s.Line > 0)
            {
                throw new PlcRuntimeException(ex.Message, s.Line);
            }
        }
    }

    private void ExecOne(Stmt s)
    {
        switch (s)
        {
            case AssignStmt a:
                Store(a.Target, Eval(a.Value));
                break;
            case FbCallStmt c:
                var fb = Block(c.Instance);
                foreach (var (p, v) in c.Inputs) fb[p] = Eval(v);
                fb.Execute(_now);
                break;
            case IfStmt i:
                foreach (var (cond, body) in i.Branches)
                {
                    if (Eval(cond).AsBool)
                    {
                        Exec(body);
                        return;
                    }
                }
                if (i.Else is not null) Exec(i.Else);
                break;
            case CaseStmt cs:
                long sel = Eval(cs.Selector).AsInt;
                foreach (var (labels, body) in cs.Cases)
                {
                    if (labels.Any(l => sel >= l.From && sel <= l.To))
                    {
                        Exec(body);
                        return;
                    }
                }
                if (cs.Else is not null) Exec(cs.Else);
                break;
            case ForStmt f:
                var target = new NameExpr(f.Var);
                long from = Eval(f.From).AsInt, to = Eval(f.To).AsInt, by = f.By is null ? 1 : Eval(f.By).AsInt;
                if (by == 0) throw new PlcRuntimeException("El paso BY de un FOR no puede ser 0.");
                try
                {
                    for (long k = from; by > 0 ? k <= to : k >= to; k += by)
                    {
                        Store(target, PlcValue.FromInt(k));
                        Exec(f.Body);
                    }
                }
                catch (ExitSignal) { }
                break;
            case WhileStmt w:
                try
                {
                    while (Eval(w.Cond).AsBool)
                    {
                        Tick(w.Line);
                        Exec(w.Body);
                    }
                }
                catch (ExitSignal) { }
                break;
            case RepeatStmt r:
                try
                {
                    do
                    {
                        Tick(r.Line);
                        Exec(r.Body);
                    } while (!Eval(r.Until).AsBool);
                }
                catch (ExitSignal) { }
                break;
            case ExitStmt:
                throw new ExitSignal();
            case ReturnStmt:
                throw new ReturnSignal();
        }
    }

    private FunctionBlock Block(string name)
    {
        if (!_vars.TryGetValue(name, out var v)) throw new PlcRuntimeException($"'{name}' no está declarado.");
        return v.Block ?? throw new PlcRuntimeException($"'{name}' no es un bloque de función.");
    }

    private IoAddress Resolve(string text)
    {
        if (_addressCache.TryGetValue(text, out var a)) return a;
        if (!_profile.TryParse(text, out a))
            throw new PlcRuntimeException($"'{text}' no es una variable declarada ni una dirección válida de {_profile.DisplayName}.");
        _addressCache[text] = a;
        return a;
    }

    /// <summary>Lee una variable, miembro de bloque o dirección por su texto (para tablas de observación y Ladder).</summary>
    public PlcValue Read(string operand)
    {
        int dot = operand.IndexOf('.');
        if (dot > 0 && _vars.TryGetValue(operand[..dot], out var v) && v.Block is not null)
            return v.Block[operand[(dot + 1)..]];
        return Eval(new NameExpr(operand));
    }

    private PlcValue Eval(Expr e)
    {
        switch (e)
        {
            case LiteralExpr l:
                return l.Value;
            case NameExpr n:
                if (_vars.TryGetValue(n.Name, out var v))
                {
                    if (v.Block is not null) throw new PlcRuntimeException($"'{n.Name}' es un bloque de función.");
                    return v.Address is { } va ? _image.Read(va).ConvertTo(v.Type) : v.Value;
                }
                return _image.Read(Resolve(n.Name));
            case AddressExpr ad:
                return _image.Read(ad.Address);
            case MemberExpr m:
                return Block(m.Instance)[m.Member];
            case UnaryExpr u:
                var o = Eval(u.Operand);
                return u.Op switch
                {
                    "NOT" => o.Type == PlcType.Bool ? PlcValue.FromBool(!o.AsBool) : PlcValue.FromInt(~o.AsInt),
                    "-" => o.Type switch
                    {
                        PlcType.Real => PlcValue.FromReal(-o.AsReal),
                        PlcType.Time => PlcValue.FromTime(-o.AsTime),
                        _ => PlcValue.FromInt(-o.AsInt),
                    },
                    _ => throw new PlcRuntimeException($"Operador '{u.Op}' desconocido."),
                };
            case BinaryExpr b:
                return Binary(b.Op, Eval(b.Left), Eval(b.Right));
            case CallExpr c:
                return Call(c.Function, c.Args.Select(Eval).ToList());
        }
        throw new PlcRuntimeException("Expresión no soportada.");
    }

    private static PlcValue Binary(string op, PlcValue a, PlcValue b)
    {
        bool anyReal = a.Type == PlcType.Real || b.Type == PlcType.Real;
        bool anyTime = a.Type == PlcType.Time || b.Type == PlcType.Time;
        switch (op)
        {
            case "AND":
            case "OR":
            case "XOR":
                if (a.Type == PlcType.Bool && b.Type == PlcType.Bool)
                    return PlcValue.FromBool(op switch { "AND" => a.AsBool && b.AsBool, "OR" => a.AsBool || b.AsBool, _ => a.AsBool ^ b.AsBool });
                return PlcValue.FromInt(op switch { "AND" => a.AsInt & b.AsInt, "OR" => a.AsInt | b.AsInt, _ => a.AsInt ^ b.AsInt });
            case "=": return PlcValue.FromBool(Compare(a, b) == 0);
            case "<>": return PlcValue.FromBool(Compare(a, b) != 0);
            case "<": return PlcValue.FromBool(Compare(a, b) < 0);
            case ">": return PlcValue.FromBool(Compare(a, b) > 0);
            case "<=": return PlcValue.FromBool(Compare(a, b) <= 0);
            case ">=": return PlcValue.FromBool(Compare(a, b) >= 0);
        }

        if (anyTime && op is "+" or "-")
            return PlcValue.FromTime(op == "+" ? a.AsTime + b.AsTime : a.AsTime - b.AsTime);
        if (anyTime && op is "*" or "/" && a.Type == PlcType.Time && b.Type != PlcType.Time)
            return PlcValue.FromTime(op == "*" ? a.AsTime * b.AsReal : a.AsTime / b.AsReal);

        if (anyReal || op == "**")
        {
            double x = a.AsReal, y = b.AsReal;
            return PlcValue.FromReal(op switch
            {
                "+" => x + y,
                "-" => x - y,
                "*" => x * y,
                "/" => y == 0 ? throw new PlcRuntimeException("División por cero.") : x / y,
                "MOD" => x % y,
                "**" => Math.Pow(x, y),
                _ => throw new PlcRuntimeException($"Operador '{op}' desconocido."),
            });
        }

        long i = a.AsInt, j = b.AsInt;
        return PlcValue.FromInt(op switch
        {
            "+" => i + j,
            "-" => i - j,
            "*" => i * j,
            "/" => j == 0 ? throw new PlcRuntimeException("División por cero.") : i / j,
            "MOD" => j == 0 ? throw new PlcRuntimeException("División por cero.") : i % j,
            _ => throw new PlcRuntimeException($"Operador '{op}' desconocido."),
        });
    }

    private static int Compare(PlcValue a, PlcValue b)
    {
        if (a.Type == PlcType.Time || b.Type == PlcType.Time) return a.AsTime.CompareTo(b.AsTime);
        if (a.Type == PlcType.Real || b.Type == PlcType.Real) return a.AsReal.CompareTo(b.AsReal);
        return a.AsInt.CompareTo(b.AsInt);
    }

    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "SQRT", "MIN", "MAX", "LIMIT", "SEL", "MOVE", "SIN", "COS", "TAN", "EXP", "LN", "TRUNC",
        "BOOL_TO_INT", "INT_TO_BOOL", "INT_TO_REAL", "REAL_TO_INT", "DINT_TO_REAL", "REAL_TO_DINT", "TIME_TO_INT",
        "INT_TO_TIME", "TIME_TO_DINT", "DINT_TO_TIME", "INT_TO_DINT", "DINT_TO_INT",
    };

    private static PlcValue Call(string f, List<PlcValue> a)
    {
        void Arity(int n)
        {
            if (a.Count != n) throw new PlcRuntimeException($"{f} necesita {n} argumento(s).");
        }

        switch (f)
        {
            case "ABS":
                Arity(1);
                return a[0].Type == PlcType.Real ? PlcValue.FromReal(Math.Abs(a[0].AsReal)) : PlcValue.FromInt(Math.Abs(a[0].AsInt));
            case "SQRT": Arity(1); return PlcValue.FromReal(Math.Sqrt(a[0].AsReal));
            case "SIN": Arity(1); return PlcValue.FromReal(Math.Sin(a[0].AsReal));
            case "COS": Arity(1); return PlcValue.FromReal(Math.Cos(a[0].AsReal));
            case "TAN": Arity(1); return PlcValue.FromReal(Math.Tan(a[0].AsReal));
            case "EXP": Arity(1); return PlcValue.FromReal(Math.Exp(a[0].AsReal));
            case "LN": Arity(1); return PlcValue.FromReal(Math.Log(a[0].AsReal));
            case "TRUNC": Arity(1); return PlcValue.FromInt((long)Math.Truncate(a[0].AsReal));
            case "MOVE": Arity(1); return a[0];
            case "MIN":
            case "MAX":
                if (a.Count < 2) throw new PlcRuntimeException($"{f} necesita al menos 2 argumentos.");
                var best = a[0];
                foreach (var x in a.Skip(1))
                    if (f == "MIN" ? Compare(x, best) < 0 : Compare(x, best) > 0) best = x;
                return best;
            case "LIMIT":
                Arity(3);
                if (Compare(a[1], a[0]) < 0) return a[0];
                if (Compare(a[1], a[2]) > 0) return a[2];
                return a[1];
            case "SEL":
                Arity(3);
                return a[0].AsBool ? a[2] : a[1];
        }

        Arity(1);
        string to = f[(f.LastIndexOf("_TO_", StringComparison.Ordinal) + 4)..];
        return to switch
        {
            "BOOL" => a[0].ConvertTo(PlcType.Bool),
            "REAL" => a[0].ConvertTo(PlcType.Real),
            "TIME" => a[0].ConvertTo(PlcType.Time),
            _ => a[0].ConvertTo(PlcType.Int),
        };
    }

    private void Store(Expr target, PlcValue value)
    {
        switch (target)
        {
            case NameExpr n:
                if (_vars.TryGetValue(n.Name, out var v))
                {
                    if (v.Block is not null) throw new PlcRuntimeException($"No se puede asignar a la instancia '{n.Name}'.");
                    var converted = value.ConvertTo(v.Type);
                    if (v.Address is { } va) _image.Write(va, converted);
                    else v.Value = converted;
                    return;
                }
                var addr = Resolve(n.Name);
                if (addr.Area == IoArea.I)
                    throw new PlcRuntimeException($"No se puede escribir en la entrada '{n.Name}'.");
                _image.Write(addr, value);
                return;
            case AddressExpr ad:
                _image.Write(ad.Address, value);
                return;
            case MemberExpr m:
                var fb = Block(m.Instance);
                if (!fb.Inputs.Contains(m.Member)) throw new PlcRuntimeException($"'{m.Member}' es una salida de {m.Instance}; solo se pueden asignar entradas.");
                fb[m.Member] = value;
                return;
        }
        throw new PlcRuntimeException("Destino de asignación no válido.");
    }
}
