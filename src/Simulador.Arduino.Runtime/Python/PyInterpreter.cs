using Simulador.Arduino.Runtime.Sketch;

namespace Simulador.Arduino.Runtime.Python;

/// <summary>
/// Intérprete de árbol de MicroPython. El programa corre una vez de arriba abajo (como main.py en la placa); cada
/// sentencia consume tiempo de CPU simulado y entre sentencia y sentencia se atienden las interrupciones de los pines
/// y los temporizadores de <c>machine</c>.
/// </summary>
internal sealed partial class PyInterpreter
{
    /// <summary>Coste aproximado de una sentencia en MicroPython sobre un ESP32 o un RP2040 (≈3 µs).</summary>
    private const long StmtCost = 30;
    private const long CallCost = 20;
    private const int MaxDepth = 60;

    private enum Flow { Normal, Break, Continue, Return }

    private readonly PyModuleAst _ast;
    private readonly PyEnv _globals = new(null);
    private readonly Dictionary<string, object?> _builtins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<PyModuleObj>> _moduleFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PyModuleObj> _modules = new(StringComparer.Ordinal);
    private object? _returnValue;
    private int _depth;
    private PyError? _handling; // excepción que se está atendiendo (para "raise" sin argumentos)

    public PyInterpreter(PyModuleAst ast, IMcuHost host, ArduinoBoard board, WifiWorld? wifi = null)
    {
        _ast = ast;
        Host = host;
        Board = board;
        Wifi = wifi;
        InitBuiltins();
        InitModules();
        InitNetworkModules();
    }

    internal IMcuHost Host { get; }
    internal ArduinoBoard Board { get; }

    /// <summary>Línea que se está ejecutando.</summary>
    public int CurrentLine { get; private set; }

    /// <summary>Ejecuta el programa entero. Los errores de Python salen como <see cref="SketchRuntimeException"/>.</summary>
    public void Run()
    {
        try
        {
            ExecBlock(_ast.Body, _globals);
        }
        catch (PyError e) when (e.Type == "SystemExit")
        {
        }
        catch (PyError e)
        {
            int line = e.Line > 0 ? e.Line : CurrentLine;
            throw new SketchRuntimeException($"{e.Type}: {e.Message}", line);
        }
    }

    /// <summary>Variables globales del programa para el inspector (sin módulos, funciones ni clases).</summary>
    public IReadOnlyList<SketchVariable> Variables()
    {
        var list = new List<SketchVariable>();
        foreach (var (name, value) in _globals.Vars.ToList())
        {
            if (value is PyModuleObj or PyFunction or PyBuiltin or PyClass or PyExceptionType || name.StartsWith("__", StringComparison.Ordinal)) continue;
            if (value is PyNative n && n.TypeName == "type") continue;
            string text = PyOps.Repr(value);
            if (text.Length > 80) text = text[..77] + "…";
            list.Add(new SketchVariable(name, PyOps.TypeName(value), text));
        }
        return list;
    }

    // ---- Tiempo ----

    /// <summary>Consume tiempo de CPU y atiende interrupciones pendientes.</summary>
    internal void Spend(long ticks)
    {
        Host.Spend(ticks);
        PollEvents();
    }

    /// <summary>Espera (time.sleep): si hay interrupciones o temporizadores, se atienden cada milisegundo.</summary>
    internal void Sleep(long ticks)
    {
        if (ticks <= 0) { PollEvents(); return; }
        if (!HasEvents)
        {
            Host.Spend(ticks);
            return;
        }
        long end = Host.Now + ticks;
        while (Host.Now < end)
        {
            Host.Spend(Math.Min(10_000, end - Host.Now));
            PollEvents();
        }
    }

    // ---- Sentencias ----

    private Flow ExecBlock(IReadOnlyList<PStmt> body, PyEnv env)
    {
        foreach (var s in body)
        {
            var f = Exec(s, env);
            if (f != Flow.Normal) return f;
        }
        return Flow.Normal;
    }

    private Flow Exec(PStmt s, PyEnv env)
    {
        CurrentLine = s.Line;
        Spend(StmtCost);
        try
        {
            return ExecInner(s, env);
        }
        catch (PyError e) when (e.Line == 0)
        {
            e.Line = s.Line;
            throw;
        }
    }

    private Flow ExecInner(PStmt s, PyEnv env)
    {
        switch (s)
        {
            case PExprStmt e:
                Eval(e.Expr, env);
                return Flow.Normal;
            case PAssign a:
            {
                var v = Eval(a.Value, env);
                foreach (var t in a.Targets) Assign(t, v, env);
                return Flow.Normal;
            }
            case PAugAssign a:
            {
                var cur = Eval(a.Target, env);
                var rhs = Eval(a.Value, env);
                if (a.Op == "+" && cur is PyList l)
                {
                    l.Items.AddRange(Iterate(rhs));
                    return Flow.Normal;
                }
                Assign(a.Target, Binary(a.Op, cur, rhs), env);
                return Flow.Normal;
            }
            case PIf i:
                return PyOps.Truthy(Eval(i.Cond, env)) ? ExecBlock(i.Body, env) : ExecBlock(i.Else, env);
            case PWhile w:
            {
                while (true)
                {
                    CurrentLine = w.Line;
                    if (!PyOps.Truthy(Eval(w.Cond, env))) return ExecBlock(w.Else, env);
                    var f = ExecBlock(w.Body, env);
                    if (f == Flow.Break) return Flow.Normal;
                    if (f == Flow.Return) return f;
                    Spend(StmtCost);
                }
            }
            case PFor fo:
            {
                foreach (var item in Iterate(Eval(fo.Iter, env)))
                {
                    CurrentLine = fo.Line;
                    Assign(fo.Target, item, env);
                    var f = ExecBlock(fo.Body, env);
                    if (f == Flow.Break) return Flow.Normal;
                    if (f == Flow.Return) return f;
                    Spend(StmtCost);
                }
                return ExecBlock(fo.Else, env);
            }
            case PDef d:
                SetName(d.Name, MakeFunction(d.Name, d.Params, d.Body, null, env), env);
                return Flow.Normal;
            case PReturn r:
                _returnValue = r.Value is null ? null : Eval(r.Value, env);
                return Flow.Return;
            case PBreak: return Flow.Break;
            case PContinue: return Flow.Continue;
            case PPass: return Flow.Normal;
            case PGlobal g:
                env.Globals ??= new HashSet<string>(StringComparer.Ordinal);
                foreach (var n in g.Names) env.Globals.Add(n);
                return Flow.Normal;
            case PImport im:
            {
                var m = Import(im.Module);
                if (im.Alias is not null) SetName(im.Alias, m, env);
                else SetName(im.Module.Split('.')[0], im.Module.Contains('.') ? Import(im.Module.Split('.')[0]) : m, env);
                return Flow.Normal;
            }
            case PFromImport fi:
            {
                var m = Import(fi.Module);
                foreach (var (name, alias) in fi.Names)
                {
                    if (name == "*")
                    {
                        foreach (var (k, v) in m.Attrs) if (!k.StartsWith('_')) SetName(k, v, env);
                        continue;
                    }
                    if (!m.Attrs.TryGetValue(name, out var v2))
                        throw new PyError("ImportError", $"no se puede importar '{name}' de '{fi.Module}'");
                    SetName(alias ?? name, v2, env);
                }
                return Flow.Normal;
            }
            case PTry t: return ExecTry(t, env);
            case PRaise r:
            {
                if (r.Exc is null)
                {
                    if (_handling is null) throw new PyError("RuntimeError", "no hay ninguna excepción que relanzar");
                    throw _handling;
                }
                throw MakeError(Eval(r.Exc, env));
            }
            case PDel d:
                foreach (var t in d.Targets) Delete(t, env);
                return Flow.Normal;
            case PClass c:
            {
                var body = new PyEnv(env);
                ExecBlock(c.Body, body);
                var attrs = new Dictionary<string, object?>(body.Vars, StringComparer.Ordinal);
                var bases = new List<object?>();
                foreach (var b in c.Bases)
                {
                    if (Lookup(b, env) is PyClass bc)
                    {
                        bases.Add(bc);
                        foreach (var (k, v) in bc.Attrs) if (k != "__bases__") attrs.TryAdd(k, v);
                    }
                }
                attrs["__bases__"] = new PyTuple([.. bases]);
                bool isException = c.Bases.Any(b => Lookup(b, env) is PyExceptionType || IsUserException(Lookup(b, env)));
                if (isException) attrs["__exception__"] = true;
                if (c.Bases.Select(b => Lookup(b, env)).OfType<PyExceptionType>().FirstOrDefault() is { } exBase) attrs["__exbase__"] = exBase.Name;
                SetName(c.Name, new PyClass(c.Name, attrs), env);
                return Flow.Normal;
            }
        }
        throw new PyError("SyntaxError", "sentencia no soportada");
    }

    private static bool IsUserException(object? v) => v is PyClass c && c.Attrs.ContainsKey("__exception__");

    private Flow ExecTry(PTry t, PyEnv env)
    {
        Flow flow;
        try
        {
            try
            {
                flow = ExecBlock(t.Body, env);
            }
            catch (PyError e) when (Matches(t.Handlers, e, env) is not null)
            {
                var h = Matches(t.Handlers, e, env)!;
                if (h.Name is not null) SetName(h.Name, e.Value ?? new PyExceptionObj(e.Type, e.Message), env);
                var previous = _handling;
                _handling = e;
                try
                {
                    return ExecBlock(h.Body, env);
                }
                finally
                {
                    _handling = previous;
                }
            }
            if (flow == Flow.Normal) flow = ExecBlock(t.Else, env);
            return flow;
        }
        finally
        {
            if (t.Finally.Count > 0)
            {
                var saved = _returnValue;
                var f = ExecBlock(t.Finally, env);
                if (f == Flow.Normal) _returnValue = saved;
            }
        }
    }

    private static readonly Dictionary<string, string> ParentType = new(StringComparer.Ordinal)
    {
        ["ZeroDivisionError"] = "ArithmeticError",
        ["OverflowError"] = "ArithmeticError",
        ["IndexError"] = "LookupError",
        ["KeyError"] = "LookupError",
        ["ArithmeticError"] = "Exception",
        ["LookupError"] = "Exception",
        ["OSError"] = "Exception",
        ["RecursionError"] = "RuntimeError",
        ["NotImplementedError"] = "RuntimeError",
        ["ImportError"] = "Exception",
    };

    private PHandler? Matches(IReadOnlyList<PHandler> handlers, PyError e, PyEnv env)
    {
        foreach (var h in handlers)
        {
            if (h.Types.Count == 0) return h;
            foreach (var type in h.Types)
            {
                if (type is "Exception" or "BaseException") return h;
                string builtinType = e.Value is PyInstance { Class.Attrs: var ca } && ca.TryGetValue("__exbase__", out var xb) && xb is string xs ? xs : e.Type;
                for (string? t = builtinType; t is not null; t = ParentType.GetValueOrDefault(t))
                    if (t == type) return h;
                // Excepciones propias: class MiError(ValueError), y sus subclases
                if (e.Value is PyInstance inst && Lookup(type, env) is PyClass hc && IsSubclass(inst.Class, hc)) return h;
            }
        }
        return null;
    }

    private PyError MakeError(object? v) => v switch
    {
        PyExceptionObj o => new PyError(o.Type, o.Message) { Value = o },
        PyExceptionType t => new PyError(t.Name, "") { Value = new PyExceptionObj(t.Name, "") },
        PyInstance i when IsUserException(i.Class) => new PyError(i.Class.Name, i.Attrs.TryGetValue("args", out var a) && a is PyTuple { Items.Length: > 0 } tu ? PyOps.Str(tu.Items[0]) : "") { Value = i },
        PyClass c when IsUserException(c) => MakeError(Instantiate(c, new PyArgs([], PyArgs.NoKw))),
        _ => new PyError("TypeError", "solo se pueden lanzar excepciones (raise ValueError(\"...\"))"),
    };

    // ---- Nombres ----

    private object? Lookup(string name, PyEnv env)
    {
        if (env.Globals?.Contains(name) == true) env = env.Module;
        for (var e = env; e is not null; e = e.Parent)
            if (e.Vars.TryGetValue(name, out var v)) return v;
        if (_builtins.TryGetValue(name, out var b)) return b;
        throw new PyError("NameError", $"el nombre '{name}' no está definido" + NameHint(name));
    }

    private static string NameHint(string name) => name switch
    {
        "true" => " (en Python se escribe True)",
        "false" => " (en Python se escribe False)",
        "null" or "NULL" => " (en Python se escribe None)",
        "Pin" or "PWM" or "ADC" or "Timer" => $" (falta 'from machine import {name}')",
        "sleep" or "sleep_ms" => " (falta 'from time import " + name + "' o escribir time." + name + ")",
        "machine" or "time" or "utime" or "math" or "random" => $" (falta 'import {name}')",
        _ => "",
    };

    private static void SetName(string name, object? value, PyEnv env)
    {
        if (env.Globals?.Contains(name) == true) env = env.Module;
        env.Vars[name] = value;
    }

    private void Assign(PExpr target, object? value, PyEnv env)
    {
        switch (target)
        {
            case PName n:
                SetName(n.Id, value, env);
                return;
            case PTupleExpr or PListExpr:
            {
                var targets = target is PTupleExpr te ? te.Items : ((PListExpr)target).Items;
                var items = Iterate(value).ToList();
                if (items.Count != targets.Count)
                    throw new PyError("ValueError", $"se esperaban {targets.Count} valores para desempaquetar y hay {items.Count}");
                for (int i = 0; i < targets.Count; i++) Assign(targets[i], items[i], env);
                return;
            }
            case PIndex ix:
            {
                var obj = Eval(ix.Target, env);
                var key = Eval(ix.Index, env);
                switch (obj)
                {
                    case PyList l:
                        l.Items[NormIndex(key, l.Items.Count)] = value;
                        return;
                    case PyDict d:
                        d.Items[Hashable(key)] = value;
                        return;
                    case PyNative n when n.GetAttr("__setitem__") is { } set:
                        Call(set, new PyArgs([key, value], PyArgs.NoKw));
                        return;
                }
                throw new PyError("TypeError", $"'{PyOps.TypeName(obj)}' no admite asignar elementos");
            }
            case PAttr at:
            {
                var obj = Eval(at.Target, env);
                switch (obj)
                {
                    case PyInstance i: i.Attrs[at.Name] = value; return;
                    case PyClass c: c.Attrs[at.Name] = value; return;
                    case PyModuleObj m: m.Attrs[at.Name] = value; return;
                }
                throw new PyError("AttributeError", $"no se puede cambiar el atributo '{at.Name}' de '{PyOps.TypeName(obj)}'");
            }
        }
        throw new PyError("SyntaxError", "no se puede asignar a esta expresión");
    }

    private void Delete(PExpr target, PyEnv env)
    {
        switch (target)
        {
            case PName n:
                if (!env.Vars.Remove(n.Id)) throw new PyError("NameError", $"el nombre '{n.Id}' no está definido");
                return;
            case PIndex ix:
            {
                var obj = Eval(ix.Target, env);
                var key = Eval(ix.Index, env);
                if (obj is PyList l) { l.Items.RemoveAt(NormIndex(key, l.Items.Count)); return; }
                if (obj is PyDict d)
                {
                    if (!d.Items.Remove(Hashable(key))) throw new PyError("KeyError", PyOps.Repr(key));
                    return;
                }
                throw new PyError("TypeError", $"'{PyOps.TypeName(obj)}' no admite borrar elementos");
            }
        }
        throw new PyError("SyntaxError", "no se puede borrar esta expresión");
    }

    private PyModuleObj Import(string name)
    {
        if (_modules.TryGetValue(name, out var m)) return m;
        if (!_moduleFactories.TryGetValue(name, out var factory))
            throw new PyError("ImportError", $"no existe el módulo '{name}'. Disponibles en el simulador: {string.Join(", ", _moduleFactories.Keys.Order())}");
        m = factory();
        _modules[name] = m;
        return m;
    }

    // ---- Expresiones ----

    internal object? Eval(PExpr e, PyEnv env)
    {
        switch (e)
        {
            case PConst c: return c.Value;
            case PName n: return Lookup(n.Id, env);
            case PFString f:
            {
                var sb = new System.Text.StringBuilder();
                foreach (var part in f.Parts)
                {
                    if (part is string s) sb.Append(s);
                    else if (part is PFormatPart fp)
                    {
                        var v = Eval(fp.Expr, env);
                        string? spec = fp.Spec;
                        if (spec is not null && spec.StartsWith("!r", StringComparison.Ordinal)) { v = PyOps.Repr(v); spec = spec.Length > 2 && spec[2] == ':' ? spec[3..] : null; }
                        sb.Append(PyOps.Format(v, spec));
                    }
                }
                return sb.ToString();
            }
            case PBin b: return Binary(b.Op, Eval(b.Left, env), Eval(b.Right, env));
            case PUnary u:
            {
                var v = Eval(u.Operand, env);
                return u.Op switch
                {
                    "not" => !PyOps.Truthy(v),
                    "-" => v is double d ? (object)(-d) : -PyOps.ToLong(v),
                    "+" => v is double d2 ? (object)d2 : PyOps.ToLong(v),
                    "~" => ~PyOps.ToLong(v),
                    _ => throw new PyError("SyntaxError", $"operador {u.Op}"),
                };
            }
            case PBool bo:
            {
                var l = Eval(bo.Left, env);
                if (bo.Op == "and") return PyOps.Truthy(l) ? Eval(bo.Right, env) : l;
                return PyOps.Truthy(l) ? l : Eval(bo.Right, env);
            }
            case PCompare c:
            {
                var left = Eval(c.First, env);
                foreach (var (op, rexpr) in c.Rest)
                {
                    var right = Eval(rexpr, env);
                    if (!Compare(op, left, right)) return false;
                    left = right;
                }
                return true;
            }
            case PIfExp i: return PyOps.Truthy(Eval(i.Cond, env)) ? Eval(i.Then, env) : Eval(i.Else, env);
            case PCall call: return EvalCall(call, env);
            case PAttr a: return GetAttr(Eval(a.Target, env), a.Name);
            case PIndex ix: return Index(Eval(ix.Target, env), ix.Index, env);
            case PListExpr l: return new PyList(l.Items.Select(x => Eval(x, env)).ToList());
            case PTupleExpr t: return new PyTuple(t.Items.Select(x => Eval(x, env)).ToArray());
            case PDictExpr d:
            {
                var dict = new PyDict();
                foreach (var (k, v) in d.Items) dict.Items[Hashable(Eval(k, env))] = Eval(v, env);
                return dict;
            }
            case PLambda la: return MakeFunction("<lambda>", la.Params, null, la.Body, env);
            case PListComp lc:
            {
                var scope = new PyEnv(env);
                var result = new List<object?>();
                foreach (var item in Iterate(Eval(lc.Iter, env)))
                {
                    Assign(lc.Target, item, scope);
                    if (lc.Cond is null || PyOps.Truthy(Eval(lc.Cond, scope))) result.Add(Eval(lc.Element, scope));
                    Spend(StmtCost / 3);
                }
                return new PyList(result);
            }
            case PSlice: throw new PyError("SyntaxError", "una porción (a:b) solo vale entre corchetes");
        }
        throw new PyError("SyntaxError", "expresión no soportada");
    }

    private PyFunction MakeFunction(string name, IReadOnlyList<PParam> pars, IReadOnlyList<PStmt>? body, PExpr? lambdaBody, PyEnv env)
    {
        var defaults = pars.Select(p => p.Default is null ? null : Eval(p.Default, env)).ToArray();
        return new PyFunction(name, pars, body, lambdaBody, env, defaults);
    }

    private object? EvalCall(PCall call, PyEnv env)
    {
        var f = Eval(call.Func, env);
        var pos = new List<object?>(call.Args.Count);
        foreach (var a in call.Args) pos.Add(Eval(a, env));
        IReadOnlyDictionary<string, object?> kw = PyArgs.NoKw;
        if (call.Kwargs.Count > 0)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in call.Kwargs) d[name] = Eval(value, env);
            kw = d;
        }
        int line = CurrentLine;
        var result = Call(f, new PyArgs(pos, kw));
        CurrentLine = line;
        return result;
    }

    /// <summary>Llama a cualquier cosa invocable de Python.</summary>
    internal object? Call(object? f, PyArgs args)
    {
        switch (f)
        {
            case PyBuiltin b: return b.Fn(args);
            case PyFunction fn: return CallFunction(fn, args, null);
            case PyBound bound:
                return bound.Func is PyFunction bf ? CallFunction(bf, args, bound.Self) : Call(bound.Func, args);
            case PyClass c: return Instantiate(c, args);
            case PyExceptionType t:
                return new PyExceptionObj(t.Name, args.Pos.Count > 0 ? PyOps.Str(args.Pos[0]) : "");
            case PyNative n: return n.Call(args);
        }
        throw new PyError("TypeError", $"'{PyOps.TypeName(f)}' no se puede llamar como función");
    }

    private object? CallFunction(PyFunction fn, PyArgs args, object? self)
    {
        if (++_depth > MaxDepth)
        {
            _depth = 0;
            throw new PyError("RecursionError", $"demasiadas llamadas anidadas (más de {MaxDepth}): ¿una función que se llama a sí misma sin fin?");
        }
        try
        {
            Spend(CallCost);
            var env = new PyEnv(fn.Closure);
            var pos = self is null ? args.Pos : [self, .. args.Pos];
            if (pos.Count > fn.Params.Count)
                throw new PyError("TypeError", $"{fn.Name}() admite {fn.Params.Count} argumentos y recibe {pos.Count}");
            for (int i = 0; i < fn.Params.Count; i++)
            {
                var p = fn.Params[i];
                if (i < pos.Count) env.Vars[p.Name] = pos[i];
                else if (args.Kw.TryGetValue(p.Name, out var kv)) env.Vars[p.Name] = kv;
                else if (p.Default is not null) env.Vars[p.Name] = fn.Defaults[i];
                else throw new PyError("TypeError", $"a {fn.Name}() le falta el argumento '{p.Name}'");
            }
            foreach (var k in args.Kw.Keys)
                if (!fn.Params.Any(p => p.Name == k)) throw new PyError("TypeError", $"{fn.Name}() no tiene ningún parámetro llamado '{k}'");
            if (fn.LambdaBody is not null) return Eval(fn.LambdaBody, env);
            _returnValue = null;
            var flow = ExecBlock(fn.Body!, env);
            var r = flow == Flow.Return ? _returnValue : null;
            _returnValue = null;
            return r;
        }
        finally
        {
            if (_depth > 0) _depth--;
        }
    }

    private object? Instantiate(PyClass c, PyArgs args)
    {
        var inst = new PyInstance(c);
        if (c.Attrs.TryGetValue("__init__", out var init) && init is PyFunction f) CallFunction(f, args, inst);
        else if (IsUserException(c)) inst.Attrs["args"] = new PyTuple([.. args.Pos]);
        return inst;
    }

    private bool Compare(string op, object? a, object? b) => op switch
    {
        "==" => PyOps.Eq(a, b),
        "!=" => !PyOps.Eq(a, b),
        "<" => PyOps.Compare(a, b) < 0,
        ">" => PyOps.Compare(a, b) > 0,
        "<=" => PyOps.Compare(a, b) <= 0,
        ">=" => PyOps.Compare(a, b) >= 0,
        "in" => Contains(b, a),
        "not in" => !Contains(b, a),
        "is" => a is null || b is null ? a is null && b is null : a is bool || a is long ? PyOps.Eq(a, b) : ReferenceEquals(a, b),
        "is not" => !Compare("is", a, b),
        _ => throw new PyError("SyntaxError", $"comparación {op}"),
    };

    private bool Contains(object? container, object? item) => container switch
    {
        string s when item is string sub => s.Contains(sub, StringComparison.Ordinal),
        PyDict d => d.Items.ContainsKey(Hashable(item)),
        PyRange r => item is long or bool && PyOps.ToLong(item) is var v && (r.Step > 0 ? v >= r.Start && v < r.Stop : v <= r.Start && v > r.Stop) && (v - r.Start) % r.Step == 0,
        _ => Iterate(container).Any(x => PyOps.Eq(x, item)),
    };

    internal static object Hashable(object? key) => key switch
    {
        null => NoneKey,
        PyList or PyDict => throw new PyError("TypeError", $"un '{PyOps.TypeName(key)}' no puede ser clave de un diccionario"),
        _ => key,
    };

    private static readonly object NoneKey = new();

    internal static object? Unkey(object k) => ReferenceEquals(k, NoneKey) ? null : k;

    internal static object? Binary(string op, object? a, object? b)
    {
        if (PyOps.IsNumber(a) && PyOps.IsNumber(b))
        {
            bool fl = a is double || b is double;
            switch (op)
            {
                case "+": return fl ? (object)(PyOps.ToDouble(a) + PyOps.ToDouble(b)) : unchecked(PyOps.ToLong(a) + PyOps.ToLong(b));
                case "-": return fl ? (object)(PyOps.ToDouble(a) - PyOps.ToDouble(b)) : unchecked(PyOps.ToLong(a) - PyOps.ToLong(b));
                case "*": return fl ? (object)(PyOps.ToDouble(a) * PyOps.ToDouble(b)) : unchecked(PyOps.ToLong(a) * PyOps.ToLong(b));
                case "/":
                {
                    double d = PyOps.ToDouble(b);
                    if (d == 0) throw new PyError("ZeroDivisionError", "división entre cero");
                    return PyOps.ToDouble(a) / d;
                }
                case "//":
                    if (fl)
                    {
                        double d = PyOps.ToDouble(b);
                        if (d == 0) throw new PyError("ZeroDivisionError", "división entre cero");
                        return Math.Floor(PyOps.ToDouble(a) / d);
                    }
                    else
                    {
                        long x = PyOps.ToLong(a), y = PyOps.ToLong(b);
                        if (y == 0) throw new PyError("ZeroDivisionError", "división entera entre cero");
                        long q = x / y;
                        if ((x % y != 0) && ((x < 0) != (y < 0))) q--;
                        return q;
                    }
                case "%":
                    if (fl)
                    {
                        double x = PyOps.ToDouble(a), y = PyOps.ToDouble(b);
                        if (y == 0) throw new PyError("ZeroDivisionError", "módulo entre cero");
                        double r = x % y;
                        return r != 0 && (r < 0) != (y < 0) ? r + y : r;
                    }
                    else
                    {
                        long x = PyOps.ToLong(a), y = PyOps.ToLong(b);
                        if (y == 0) throw new PyError("ZeroDivisionError", "módulo entre cero");
                        long r = x % y;
                        return r != 0 && (r < 0) != (y < 0) ? r + y : r;
                    }
                case "**":
                    if (!fl && PyOps.ToLong(b) >= 0)
                    {
                        long bas = PyOps.ToLong(a), exp = PyOps.ToLong(b), res = 1;
                        unchecked { while (exp-- > 0) res *= bas; }
                        return res;
                    }
                    return Math.Pow(PyOps.ToDouble(a), PyOps.ToDouble(b));
                case "&" when !fl: return PyOps.ToLong(a) & PyOps.ToLong(b);
                case "|" when !fl: return PyOps.ToLong(a) | PyOps.ToLong(b);
                case "^" when !fl: return PyOps.ToLong(a) ^ PyOps.ToLong(b);
                case "<<" when !fl: return PyOps.ToLong(a) << (int)PyOps.ToLong(b);
                case ">>" when !fl: return PyOps.ToLong(a) >> (int)PyOps.ToLong(b);
            }
        }
        switch (op, a, b)
        {
            case ("+", string x, string y): return x + y;
            case ("+", PyList x, PyList y): return new PyList([.. x.Items, .. y.Items]);
            case ("+", PyTuple x, PyTuple y): return new PyTuple([.. x.Items, .. y.Items]);
            case ("*", string x, long or bool): return string.Concat(Enumerable.Repeat(x, (int)Math.Max(0, PyOps.ToLong(b))));
            case ("*", long or bool, string y): return string.Concat(Enumerable.Repeat(y, (int)Math.Max(0, PyOps.ToLong(a))));
            case ("*", PyList x, long or bool): return new PyList(Enumerable.Repeat(x.Items, (int)Math.Max(0, PyOps.ToLong(b))).SelectMany(i => i).ToList());
            case ("*", long or bool, PyList y): return new PyList(Enumerable.Repeat(y.Items, (int)Math.Max(0, PyOps.ToLong(a))).SelectMany(i => i).ToList());
            case ("%", string x, _): return PyOps.Percent(x, b);
            case ("|", PyDict x, PyDict y):
            {
                var d = new PyDict();
                foreach (var kv in x.Items) d.Items[kv.Key] = kv.Value;
                foreach (var kv in y.Items) d.Items[kv.Key] = kv.Value;
                return d;
            }
        }
        string hint = op == "+" && (a is string || b is string) ? " (para unir texto y números usa str(numero) o una f-string)" : "";
        throw new PyError("TypeError", $"no se puede hacer {PyOps.TypeName(a)} {op} {PyOps.TypeName(b)}{hint}");
    }

    // ---- Índices e iteración ----

    internal static int NormIndex(object? key, int count)
    {
        long i = key is double ? throw new PyError("TypeError", "los índices tienen que ser enteros, no float (usa int(...) o //)") : PyOps.ToLong(key);
        if (i < 0) i += count;
        if (i < 0 || i >= count) throw new PyError("IndexError", $"índice {PyOps.ToLong(key)} fuera de rango (hay {count} elementos)");
        return (int)i;
    }

    private object? Index(object? obj, PExpr indexExpr, PyEnv env)
    {
        if (indexExpr is PSlice sl)
        {
            long? lo = sl.Lower is null ? null : PyOps.ToLong(Eval(sl.Lower, env));
            long? hi = sl.Upper is null ? null : PyOps.ToLong(Eval(sl.Upper, env));
            long step = sl.Step is null ? 1 : PyOps.ToLong(Eval(sl.Step, env));
            if (step == 0) throw new PyError("ValueError", "el paso de la porción no puede ser cero");
            return obj switch
            {
                string s => string.Concat(SliceIdx(s.Length, lo, hi, step).Select(i => s[i])),
                PyList l => new PyList(SliceIdx(l.Items.Count, lo, hi, step).Select(i => l.Items[i]).ToList()),
                PyTuple t => new PyTuple(SliceIdx(t.Items.Length, lo, hi, step).Select(i => t.Items[i]).ToArray()),
                _ => throw new PyError("TypeError", $"'{PyOps.TypeName(obj)}' no admite porciones [a:b]"),
            };
        }
        var key = Eval(indexExpr, env);
        return obj switch
        {
            string s => s[NormIndex(key, s.Length)].ToString(),
            PyList l => l.Items[NormIndex(key, l.Items.Count)],
            PyTuple t => t.Items[NormIndex(key, t.Items.Length)],
            PyRange r => r[NormIndex(key, (int)r.Count)],
            PyDict d => d.Items.TryGetValue(Hashable(key), out var v) ? v : throw new PyError("KeyError", PyOps.Repr(key)),
            PyNative n when n.GetAttr("__getitem__") is { } get => Call(get, new PyArgs([key], PyArgs.NoKw)),
            _ => throw new PyError("TypeError", $"'{PyOps.TypeName(obj)}' no admite índices []"),
        };
    }

    private static IEnumerable<int> SliceIdx(int count, long? lo, long? hi, long step)
    {
        long Clamp(long v, long min, long max) => Math.Max(min, Math.Min(max, v));
        if (step > 0)
        {
            long start = lo is { } a ? Clamp(a < 0 ? a + count : a, 0, count) : 0;
            long stop = hi is { } b ? Clamp(b < 0 ? b + count : b, 0, count) : count;
            for (long i = start; i < stop; i += step) yield return (int)i;
        }
        else
        {
            long start = lo is { } a ? Clamp(a < 0 ? a + count : a, -1, count - 1) : count - 1;
            long stop = hi is { } b ? Clamp(b < 0 ? b + count : b, -1, count - 1) : -1;
            for (long i = start; i > stop; i += step) yield return (int)i;
        }
    }

    internal IEnumerable<object?> Iterate(object? v)
    {
        switch (v)
        {
            case PyList l:
                // Copia: modificar la lista mientras se recorre no rompe el bucle.
                return l.Items.ToArray();
            case PyTuple t: return t.Items;
            case string s: return s.Select(c => (object?)c.ToString());
            case PyDict d: return d.Items.Keys.Select(Unkey).ToArray();
            case PyRange r: return RangeItems(r);
            case PyNative n when n.GetAttr("__iter__") is { } it: return Iterate(Call(it, new PyArgs([], PyArgs.NoKw)));
        }
        throw new PyError("TypeError", $"'{PyOps.TypeName(v)}' no se puede recorrer con for");
    }

    private static IEnumerable<object?> RangeItems(PyRange r)
    {
        for (long i = 0; i < r.Count; i++) yield return r[i];
    }
}
