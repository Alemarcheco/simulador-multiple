using System.Globalization;
using System.Text;

namespace Simulador.Arduino.Runtime.Sketch;

/// <summary>Lo que el intérprete necesita del microcontrolador: pines, tiempo y puerto serie.</summary>
internal interface IMcuHost
{
    /// <summary>Tiempo simulado en ticks de 100 ns.</summary>
    long Now { get; }
    /// <summary>Consume tiempo de CPU; puede ceder el control al simulador.</summary>
    void Spend(long ticks);
    int IntBits { get; }
    IReadOnlyDictionary<string, long> Constants { get; }

    void PinMode(int pin, long mode, int line);
    void DigitalWrite(int pin, bool high, int line);
    bool DigitalRead(int pin, int line);
    int AnalogRead(int pin, int line);
    void AnalogWrite(int pin, int value, int line);
    void Tone(int pin, double hz, long? durationMs, int line);
    void NoTone(int pin, int line);
    void ServoWrite(ServoObj servo, int line);
    long PulseIn(int pin, bool level, long timeoutUs, int line);

    void SerialBegin(long baud);
    void SerialWrite(string text);
    int SerialAvailable();
    int SerialRead();
    int SerialPeek();
    Random Random { get; }
}

/// <summary>Intérprete de árbol del sketch. Cada sentencia consume tiempo de CPU simulado.</summary>
internal sealed class Interpreter
{
    /// <summary>Coste aproximado de una sentencia sencilla en un AVR a 16 MHz (0,5 µs).</summary>
    private const long StmtCost = 5;
    private const int MaxDepth = 200;

    private enum Flow { Normal, Break, Continue, Return }

    private sealed class Frame
    {
        public readonly List<Dictionary<string, Slot>> Scopes = [new(StringComparer.Ordinal)];
        public Val ReturnValue = Val.Void;
    }

    private readonly SketchProgram _program;
    private readonly IMcuHost _host;
    private readonly Dictionary<string, Slot> _globals = new(StringComparer.Ordinal);
    private readonly Stack<Frame> _frames = new();
    private readonly CType _int;
    private readonly CType _long = new("long", 32);
    private readonly CType _ulong = new("unsigned long", 32, true);

    public Interpreter(SketchProgram program, IMcuHost host)
    {
        _program = program;
        _host = host;
        _int = new CType("int", host.IntBits);
        foreach (var (name, value) in host.Constants)
            _globals[name] = new Slot(_int, Val.Int(value)) { IsConst = true };
    }

    /// <summary>Línea que se está ejecutando (para resaltarla en el editor).</summary>
    public volatile int CurrentLine;

    public IReadOnlyDictionary<string, Slot> Globals => _globals;

    public void InitGlobals()
    {
        _frames.Clear();
        _frames.Push(new Frame());
        foreach (var d in _program.Globals) Declare(d, _globals);
    }

    public void CallSetup() => CallUser(_program.Functions["setup"], [], 0);

    public void CallLoop() => CallUser(_program.Functions["loop"], [], 0);

    // ---- Variables ----

    private Slot? Find(string name)
    {
        if (_frames.Count > 0)
        {
            var scopes = _frames.Peek().Scopes;
            for (int i = scopes.Count - 1; i >= 0; i--)
                if (scopes[i].TryGetValue(name, out var s)) return s;
        }
        return _globals.GetValueOrDefault(name);
    }

    private Slot Lookup(string name, int line) =>
        Find(name) ?? throw new SketchRuntimeException($"'{name}' no está declarada.", line);

    private void Declare(VarDeclStmt d, Dictionary<string, Slot> scope)
    {
        foreach (var v in d.Vars)
        {
            if (d.IsStatic && d.Statics.TryGetValue(v.Name, out var existing))
            {
                scope[v.Name] = existing;
                continue;
            }
            var slot = new Slot(d.Type, Initial(d.Type, v, d.Line)) { IsConst = d.IsConst };
            if (d.IsStatic) d.Statics[v.Name] = slot;
            scope[v.Name] = slot;
        }
    }

    private Val Initial(CType type, Declarator v, int line)
    {
        if (v.Dims.Count > 0)
        {
            var dims = new List<int>();
            for (int i = 0; i < v.Dims.Count; i++)
            {
                if (v.Dims[i] is { } e) dims.Add((int)Eval(e).L);
                else if (i == 0 && v.Init is InitListExpr list) dims.Add(list.Items.Count);
                else if (i == 0 && v.Init is StrExpr s) dims.Add(s.Value.Length + 1);
                else throw new SketchRuntimeException($"Falta el tamaño del array '{v.Name}'.", line);
            }
            if (dims.Any(n => n <= 0 || n > 8192)) throw new SketchRuntimeException($"Tamaño de array no válido en '{v.Name}'.", line);
            var arr = NewArray(type, dims, 0);
            if (v.Init is not null) FillArray(arr, v.Init, line);
            return Val.Arr(arr);
        }
        if (type.ObjectClass == "Servo") return Val.Obj(new ServoObj());
        if (v.Init is InitListExpr { Items.Count: 1 } one) return Val.Coerce(Eval(one.Items[0]), type, line);
        if (v.Init is not null) return Val.Coerce(Eval(v.Init), type, line);
        return type.IsString ? Val.Str("") : type.IsFloat ? Val.Float(0) : Val.Int(0);
    }

    private static ArrayObj NewArray(CType type, List<int> dims, int level)
    {
        var items = new Val[dims[level]];
        for (int i = 0; i < items.Length; i++)
            items[i] = level + 1 < dims.Count ? Val.Arr(NewArray(type, dims, level + 1))
                : type.IsString ? Val.Str("") : type.IsFloat ? Val.Float(0) : Val.Int(0);
        return new ArrayObj(type, items);
    }

    private void FillArray(ArrayObj arr, Expr init, int line)
    {
        if (init is StrExpr s)
        {
            if (s.Value.Length >= arr.Items.Length) throw new SketchRuntimeException("El texto no cabe en el array (recuerda el '\\0' final).", line);
            for (int i = 0; i < s.Value.Length; i++) arr.Items[i] = Val.Int(s.Value[i]);
            return;
        }
        if (init is not InitListExpr list) throw new SketchRuntimeException("Un array se inicializa con { … }.", line);
        if (list.Items.Count > arr.Items.Length) throw new SketchRuntimeException("Hay más valores que posiciones en el array.", line);
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (arr.Items[i].K == VK.Arr) FillArray(arr.Items[i].A, list.Items[i], line);
            else arr.Items[i] = Val.Coerce(Eval(list.Items[i]), arr.Element, line);
        }
    }

    // ---- Sentencias ----

    private Flow Exec(Stmt s)
    {
        CurrentLine = s.Line;
        _host.Spend(StmtCost);
        switch (s)
        {
            case BlockStmt b:
                return ExecBlock(b.Body);
            case ExprStmt e:
                Eval(e.Expr);
                return Flow.Normal;
            case VarDeclStmt d:
                Declare(d, _frames.Peek().Scopes[^1]);
                return Flow.Normal;
            case IfStmt i:
                if (Eval(i.Cond).Truthy) return Exec(i.Then);
                return i.Else is null ? Flow.Normal : Exec(i.Else);
            case WhileStmt w:
                while (Eval(w.Cond).Truthy)
                {
                    var f = Exec(w.Body);
                    if (f == Flow.Break) break;
                    if (f == Flow.Return) return f;
                    _host.Spend(StmtCost);
                }
                return Flow.Normal;
            case DoWhileStmt dw:
                do
                {
                    var f = Exec(dw.Body);
                    if (f == Flow.Break) break;
                    if (f == Flow.Return) return f;
                } while (Eval(dw.Cond).Truthy);
                return Flow.Normal;
            case ForStmt fs:
            {
                var scopes = _frames.Peek().Scopes;
                scopes.Add(new Dictionary<string, Slot>(StringComparer.Ordinal));
                try
                {
                    if (fs.Init is not null) Exec(fs.Init);
                    while (fs.Cond is null || Eval(fs.Cond).Truthy)
                    {
                        var f = Exec(fs.Body);
                        if (f == Flow.Break) break;
                        if (f == Flow.Return) return f;
                        if (fs.Step is not null) Eval(fs.Step);
                        _host.Spend(StmtCost);
                    }
                }
                finally { scopes.RemoveAt(scopes.Count - 1); }
                return Flow.Normal;
            }
            case SwitchStmt sw:
                return ExecSwitch(sw);
            case BreakStmt:
                return Flow.Break;
            case ContinueStmt:
                return Flow.Continue;
            case ReturnStmt r:
                _frames.Peek().ReturnValue = r.Value is null ? Val.Void : Eval(r.Value);
                return Flow.Return;
            case EmptyStmt:
                return Flow.Normal;
        }
        throw new SketchRuntimeException("Sentencia desconocida.", s.Line);
    }

    private Flow ExecBlock(List<Stmt> body)
    {
        var scopes = _frames.Peek().Scopes;
        scopes.Add(new Dictionary<string, Slot>(StringComparer.Ordinal));
        try
        {
            foreach (var st in body)
            {
                var f = Exec(st);
                if (f != Flow.Normal) return f;
            }
            return Flow.Normal;
        }
        finally { scopes.RemoveAt(scopes.Count - 1); }
    }

    private Flow ExecSwitch(SwitchStmt sw)
    {
        long v = Eval(sw.Value).L;
        int start = -1;
        for (int i = 0; i < sw.Cases.Count && start < 0; i++)
            if (sw.Cases[i].Labels.Any(l => Eval(l).L == v)) start = i;
        if (start < 0) start = sw.Cases.FindIndex(c => c.IsDefault);
        if (start < 0) return Flow.Normal;
        var scopes = _frames.Peek().Scopes;
        scopes.Add(new Dictionary<string, Slot>(StringComparer.Ordinal));
        try
        {
            // Sin break se sigue con el siguiente case, como en C.
            for (int i = start; i < sw.Cases.Count; i++)
                foreach (var st in sw.Cases[i].Body)
                {
                    var f = Exec(st);
                    if (f == Flow.Break) return Flow.Normal;
                    if (f != Flow.Normal) return f;
                }
            return Flow.Normal;
        }
        finally { scopes.RemoveAt(scopes.Count - 1); }
    }

    // ---- Funciones de usuario ----

    private Val CallUser(FuncDef f, List<Expr> args, int line)
    {
        if (args.Count != f.Params.Count)
            throw new SketchRuntimeException($"{f.Name}() espera {f.Params.Count} argumento(s) y recibe {args.Count}.", line);
        if (_frames.Count > MaxDepth)
            throw new SketchRuntimeException($"Demasiadas llamadas anidadas a {f.Name}() (desbordamiento de pila).", line);
        var frame = new Frame();
        for (int i = 0; i < args.Count; i++)
        {
            var p = f.Params[i];
            if (p.IsRef || p.IsArray)
            {
                // Por referencia: se comparte la misma variable (o el mismo array).
                if (p.IsRef && args[i] is NameExpr n) frame.Scopes[0][p.Name] = Lookup(n.Name, line);
                else
                {
                    var a = Eval(args[i]);
                    if (p.IsArray && a.K != VK.Arr && a.K != VK.Str) throw new SketchRuntimeException($"{f.Name}(): el argumento '{p.Name}' debe ser un array.", line);
                    frame.Scopes[0][p.Name] = new Slot(p.Type, a);
                }
            }
            else frame.Scopes[0][p.Name] = new Slot(p.Type, Val.Coerce(Eval(args[i]), p.Type, line));
        }
        _frames.Push(frame);
        try
        {
            ExecBlock(f.Body.Body);
        }
        finally { _frames.Pop(); }
        var r = frame.ReturnValue;
        return f.Return.IsVoid ? Val.Void : Val.Coerce(r.K == VK.Void ? Val.Int(0) : r, f.Return, line);
    }

    // ---- Expresiones ----

    private Val Eval(Expr e)
    {
        switch (e)
        {
            case NumExpr n: return n.Value;
            case StrExpr s: return Val.Str(s.Value);
            case NameExpr n: return Lookup(n.Name, n.Line).Value;
            case UnaryExpr u:
            {
                var v = Eval(u.Operand);
                RequireNumber(v, u.Op, u.Line);
                return u.Op switch
                {
                    "!" => Val.Bool(!v.Truthy),
                    "~" => Val.Int(~v.L),
                    "-" => v.K == VK.Float ? Val.Float(-v.F) : Val.Int(-v.I),
                    _ => v,
                };
            }
            case IncExpr inc:
            {
                var old = Eval(inc.Target);
                RequireNumber(old, inc.Op, inc.Line);
                var nv = old.K == VK.Float ? Val.Float(old.F + (inc.Op == "++" ? 1 : -1)) : Val.Int(old.I + (inc.Op == "++" ? 1 : -1));
                var stored = Store(inc.Target, nv, inc.Line);
                return inc.Prefix ? stored : old;
            }
            case BinaryExpr b:
                return Binary(b);
            case AssignExpr a:
            {
                var v = Eval(a.Value);
                if (a.Op != "=") v = Arith(a.Op[..^1], Eval(a.Target), v, a.Line);
                return Store(a.Target, v, a.Line);
            }
            case CondExpr c:
                return Eval(c.Cond).Truthy ? Eval(c.Then) : Eval(c.Else);
            case IndexExpr ix:
            {
                var (arr, i, str) = Element(ix);
                if (arr is null) return Val.Int(str is not null && i < str.Length ? str[i] : 0);
                return arr.Items[i];
            }
            case CastExpr ce:
                return Val.Coerce(Eval(ce.Operand), ce.Type, ce.Line);
            case CallExpr call:
                return Call(call);
            case MethodExpr m:
                return Method(m);
            case InitListExpr il:
                throw new SketchRuntimeException("Una lista { … } solo puede usarse al declarar un array.", il.Line);
        }
        throw new SketchRuntimeException("Expresión desconocida.", e.Line);
    }

    private static void RequireNumber(Val v, string op, int line)
    {
        if (!v.IsNumber) throw new SketchRuntimeException($"El operador '{op}' necesita un número.", line);
    }

    /// <summary>Elemento de array (o null + índice si es un carácter de un String).</summary>
    private (ArrayObj? Array, int Index, string? Text) Element(IndexExpr ix)
    {
        var target = Eval(ix.Target);
        long i = Eval(ix.Index).L;
        if (target.K == VK.Str) return (null, i < 0 || i >= target.S.Length ? int.MaxValue : (int)i, target.S);
        if (target.K != VK.Arr) throw new SketchRuntimeException("Solo se pueden usar corchetes [ ] con arrays.", ix.Line);
        var arr = target.A;
        if (i < 0 || i >= arr.Items.Length)
            throw new SketchRuntimeException($"Índice {i} fuera del array (tiene {arr.Items.Length} posiciones: 0 a {arr.Items.Length - 1}). En la placa real esto corrompería la memoria.", ix.Line);
        return (arr, (int)i, null);
    }

    private Val Store(Expr target, Val v, int line)
    {
        switch (target)
        {
            case NameExpr n:
            {
                var slot = Lookup(n.Name, line);
                if (slot.IsConst) throw new SketchRuntimeException($"'{n.Name}' es constante y no se puede cambiar.", line);
                if (slot.Value.K == VK.Arr && v.K != VK.Arr) throw new SketchRuntimeException($"No se puede asignar a un array entero ('{n.Name}'); usa un índice.", line);
                var c = Val.Coerce(v, slot.Type, line);
                slot.Value = c;
                return c;
            }
            case IndexExpr ix:
            {
                var (arr, i, _) = Element(ix);
                if (arr is null)
                {
                    if (ix.Target is NameExpr sn && Lookup(sn.Name, line) is { Value.K: VK.Str } strSlot && i < strSlot.Value.S.Length)
                    {
                        var chars = strSlot.Value.S.ToCharArray();
                        chars[i] = (char)v.L;
                        strSlot.Value = Val.Str(new string(chars));
                    }
                    return v;
                }
                if (arr.Items[i].K == VK.Arr) throw new SketchRuntimeException("Falta un índice: es un array de varias dimensiones.", line);
                var c = Val.Coerce(v, arr.Element, line);
                arr.Items[i] = c;
                return c;
            }
        }
        throw new SketchRuntimeException("Solo se puede asignar a una variable o a un elemento de array.", line);
    }

    private Val Binary(BinaryExpr b)
    {
        switch (b.Op)
        {
            case "&&": return Val.Bool(Eval(b.Left).Truthy && Eval(b.Right).Truthy);
            case "||": return Val.Bool(Eval(b.Left).Truthy || Eval(b.Right).Truthy);
            case ",": Eval(b.Left); return Eval(b.Right);
        }
        return Arith(b.Op, Eval(b.Left), Eval(b.Right), b.Line);
    }

    private Val Arith(string op, Val l, Val r, int line)
    {
        if (l.K == VK.Str || r.K == VK.Str || l.K == VK.Arr || r.K == VK.Arr)
        {
            string ls = l.ToString(), rs = r.ToString();
            return op switch
            {
                "+" => Val.Str(ls + rs),
                "==" => Val.Bool(ls == rs),
                "!=" => Val.Bool(ls != rs),
                "<" => Val.Bool(string.CompareOrdinal(ls, rs) < 0),
                ">" => Val.Bool(string.CompareOrdinal(ls, rs) > 0),
                _ => throw new SketchRuntimeException($"El operador '{op}' no se puede usar con textos.", line),
            };
        }
        if (l.K == VK.Float || r.K == VK.Float)
        {
            double a = l.D, c = r.D;
            return op switch
            {
                "+" => Val.Float(a + c),
                "-" => Val.Float(a - c),
                "*" => Val.Float(a * c),
                "/" => Val.Float(a / c),
                "%" => throw new SketchRuntimeException("El operador % solo funciona con enteros; usa fmod().", line),
                "<" => Val.Bool(a < c),
                ">" => Val.Bool(a > c),
                "<=" => Val.Bool(a <= c),
                ">=" => Val.Bool(a >= c),
                "==" => Val.Bool(a == c),
                "!=" => Val.Bool(a != c),
                _ => throw new SketchRuntimeException($"El operador '{op}' necesita números enteros.", line),
            };
        }
        long x = l.I, y = r.I;
        return op switch
        {
            "+" => Val.Int(x + y),
            "-" => Val.Int(x - y),
            "*" => Val.Int(x * y),
            "/" => y == 0 ? throw new SketchRuntimeException("División por cero.", line) : Val.Int(x / y),
            "%" => y == 0 ? throw new SketchRuntimeException("División por cero (operador %).", line) : Val.Int(x % y),
            "&" => Val.Int(x & y),
            "|" => Val.Int(x | y),
            "^" => Val.Int(x ^ y),
            "<<" => Val.Int(x << (int)Math.Clamp(y, 0, 63)),
            ">>" => Val.Int(x >> (int)Math.Clamp(y, 0, 63)),
            "<" => Val.Bool(x < y),
            ">" => Val.Bool(x > y),
            "<=" => Val.Bool(x <= y),
            ">=" => Val.Bool(x >= y),
            "==" => Val.Bool(x == y),
            "!=" => Val.Bool(x != y),
            _ => throw new SketchRuntimeException($"Operador '{op}' desconocido.", line),
        };
    }

    // ---- Funciones de Arduino ----

    private int Pin(Expr e) => (int)Eval(e).L;

    private void Args(CallExpr c, int min, int max = -1)
    {
        if (max < 0) max = min;
        if (c.Args.Count < min || c.Args.Count > max)
            throw new SketchRuntimeException(min == max
                ? $"{c.Name}() necesita {min} argumento(s)."
                : $"{c.Name}() necesita entre {min} y {max} argumentos.", c.Line);
    }

    private Val Call(CallExpr c)
    {
        if (_program.Functions.TryGetValue(c.Name, out var f)) return CallUser(f, c.Args, c.Line);
        var a = c.Args;
        int line = c.Line;
        switch (c.Name)
        {
            case "pinMode": Args(c, 2); _host.PinMode(Pin(a[0]), Eval(a[1]).L, line); return Val.Void;
            case "digitalWrite": Args(c, 2); _host.DigitalWrite(Pin(a[0]), Eval(a[1]).Truthy, line); _host.Spend(40); return Val.Void;
            case "digitalRead": Args(c, 1); _host.Spend(40); return Val.Int(_host.DigitalRead(Pin(a[0]), line) ? 1 : 0);
            case "analogRead": Args(c, 1); _host.Spend(1120); return Val.Int(_host.AnalogRead(Pin(a[0]), line));
            case "analogWrite": Args(c, 2); _host.AnalogWrite(Pin(a[0]), (int)Math.Clamp(Eval(a[1]).L, 0, 255), line); _host.Spend(50); return Val.Void;
            case "delay":
            {
                Args(c, 1);
                long ms = Eval(a[0]).L;
                if (ms < 0) throw new SketchRuntimeException("delay() con un tiempo negativo.", line);
                _host.Spend(ms * 10_000);
                return Val.Void;
            }
            case "delayMicroseconds": Args(c, 1); _host.Spend(Math.Max(0, Eval(a[0]).L) * 10); return Val.Void;
            case "millis": Args(c, 0); return Val.Int(Val.Wrap(_host.Now / 10_000, 32, true));
            case "micros": Args(c, 0); return Val.Int(Val.Wrap(_host.Now / 10 / 4 * 4, 32, true)); // resolución de 4 µs, como en el Uno
            case "tone":
                Args(c, 2, 3);
                _host.Tone(Pin(a[0]), Eval(a[1]).D, a.Count > 2 ? Eval(a[2]).L : null, line);
                return Val.Void;
            case "noTone": Args(c, 1); _host.NoTone(Pin(a[0]), line); return Val.Void;
            case "pulseIn":
                Args(c, 2, 3);
                return Val.Int(_host.PulseIn(Pin(a[0]), Eval(a[1]).Truthy, a.Count > 2 ? Eval(a[2]).L : 1_000_000, line));

            case "map":
            {
                Args(c, 5);
                long x = Eval(a[0]).L, inMin = Eval(a[1]).L, inMax = Eval(a[2]).L, outMin = Eval(a[3]).L, outMax = Eval(a[4]).L;
                if (inMax == inMin) throw new SketchRuntimeException("map(): el rango de entrada no puede ser de tamaño cero.", line);
                return Val.Int((x - inMin) * (outMax - outMin) / (inMax - inMin) + outMin);
            }
            case "constrain":
            {
                Args(c, 3);
                var x = Eval(a[0]); var lo = Eval(a[1]); var hi = Eval(a[2]);
                return x.D < lo.D ? lo : x.D > hi.D ? hi : x;
            }
            case "min": { Args(c, 2); var x = Eval(a[0]); var y = Eval(a[1]); return x.D <= y.D ? x : y; }
            case "max": { Args(c, 2); var x = Eval(a[0]); var y = Eval(a[1]); return x.D >= y.D ? x : y; }
            case "abs": { Args(c, 1); var x = Eval(a[0]); return x.K == VK.Float ? Val.Float(Math.Abs(x.F)) : Val.Int(Math.Abs(x.I)); }
            case "sq": { Args(c, 1); var x = Eval(a[0]); return x.K == VK.Float ? Val.Float(x.F * x.F) : Val.Int(x.I * x.I); }
            case "sqrt": Args(c, 1); _host.Spend(300); return Val.Float(Math.Sqrt(Eval(a[0]).D));
            case "pow": Args(c, 2); _host.Spend(300); return Val.Float(Math.Pow(Eval(a[0]).D, Eval(a[1]).D));
            case "sin": Args(c, 1); _host.Spend(300); return Val.Float(Math.Sin(Eval(a[0]).D));
            case "cos": Args(c, 1); _host.Spend(300); return Val.Float(Math.Cos(Eval(a[0]).D));
            case "tan": Args(c, 1); _host.Spend(300); return Val.Float(Math.Tan(Eval(a[0]).D));
            case "atan2": Args(c, 2); _host.Spend(300); return Val.Float(Math.Atan2(Eval(a[0]).D, Eval(a[1]).D));
            case "log": Args(c, 1); _host.Spend(300); return Val.Float(Math.Log(Eval(a[0]).D));
            case "exp": Args(c, 1); _host.Spend(300); return Val.Float(Math.Exp(Eval(a[0]).D));
            case "fabs": Args(c, 1); return Val.Float(Math.Abs(Eval(a[0]).D));
            case "fmod": Args(c, 2); return Val.Float(Eval(a[0]).D % Eval(a[1]).D);
            case "round": Args(c, 1); return Val.Int((long)Math.Round(Eval(a[0]).D, MidpointRounding.AwayFromZero));
            case "floor": Args(c, 1); return Val.Float(Math.Floor(Eval(a[0]).D));
            case "ceil": Args(c, 1); return Val.Float(Math.Ceiling(Eval(a[0]).D));
            case "random":
            {
                Args(c, 1, 2);
                long lo = a.Count == 2 ? Eval(a[0]).L : 0, hi = Eval(a[^1]).L;
                return Val.Int(hi <= lo ? lo : lo + _host.Random.NextInt64(hi - lo));
            }
            case "randomSeed": Args(c, 1); return Val.Void;
            case "bitRead": Args(c, 2); return Val.Int((Eval(a[0]).L >> (int)Eval(a[1]).L) & 1);
            case "bit": Args(c, 1); return Val.Int(1L << (int)Eval(a[0]).L);
            case "bitSet": Args(c, 2); return Store(a[0], Val.Int(Eval(a[0]).L | (1L << (int)Eval(a[1]).L)), line);
            case "bitClear": Args(c, 2); return Store(a[0], Val.Int(Eval(a[0]).L & ~(1L << (int)Eval(a[1]).L)), line);
            case "bitWrite":
            {
                Args(c, 3);
                long x = Eval(a[0]).L, bitMask = 1L << (int)Eval(a[1]).L;
                return Store(a[0], Val.Int(Eval(a[2]).Truthy ? x | bitMask : x & ~bitMask), line);
            }
            case "highByte": Args(c, 1); return Val.Int((Eval(a[0]).L >> 8) & 0xFF);
            case "lowByte": Args(c, 1); return Val.Int(Eval(a[0]).L & 0xFF);
            case "isDigit": Args(c, 1); return Val.Bool(char.IsDigit((char)Eval(a[0]).L));
            case "isAlpha": Args(c, 1); return Val.Bool(char.IsLetter((char)Eval(a[0]).L));
            case "strlen": Args(c, 1); return Val.Int(Eval(a[0]) is var sv && sv.K == VK.Arr ? sv.A.AsText().Length : sv.S.Length);

            // Conversiones con sintaxis de función: int(x), float(x), String(x)…
            case "int": Args(c, 1); return Val.Coerce(Eval(a[0]), _int, line);
            case "long": Args(c, 1); return Val.Coerce(Eval(a[0]), _long, line);
            case "word" or "unsigned": Args(c, 1); return Val.Coerce(Eval(a[0]), new CType("word", 16, true), line);
            case "byte": Args(c, 1); return Val.Coerce(Eval(a[0]), new CType("byte", 8, true), line);
            case "char": Args(c, 1); return Val.Coerce(Eval(a[0]), new CType("char", 8), line);
            case "float" or "double": Args(c, 1); return Val.Coerce(Eval(a[0]), new CType("float", 32, IsFloat: true), line);
            case "bool" or "boolean": Args(c, 1); return Val.Bool(Eval(a[0]).Truthy);
            case "String":
            {
                Args(c, 0, 2);
                if (a.Count == 0) return Val.Str("");
                var v = Eval(a[0]);
                if (a.Count == 2 && v.IsNumber) return Val.Str(FormatNumber(v, Eval(a[1]).L));
                return Val.Str(v.ToString());
            }
        }
        throw new SketchRuntimeException($"La función {c.Name}() no existe (ni en el sketch ni en las de Arduino que simula el programa).", line);
    }

    /// <summary>Formato de print(x, DEC/HEX/OCT/BIN) o de decimales para reales.</summary>
    private static string FormatNumber(Val v, long format)
    {
        if (v.K == VK.Float) return v.F.ToString("F" + Math.Clamp(format, 0, 10), CultureInfo.InvariantCulture);
        long x = v.I;
        return format switch
        {
            16 => Convert.ToString(x, 16).ToUpperInvariant(),
            8 => Convert.ToString(x, 8),
            2 => Convert.ToString(x, 2),
            _ => x.ToString(CultureInfo.InvariantCulture),
        };
    }

    private Val Method(MethodExpr m)
    {
        int line = m.Line;
        var a = m.Args;
        if (m.Target is NameExpr { Name: "Serial" } && Find("Serial") is null)
        {
            switch (m.Method)
            {
                case "begin": _host.SerialBegin(a.Count > 0 ? Eval(a[0]).L : 9600); return Val.Void;
                case "print":
                case "println":
                {
                    var sb = new StringBuilder();
                    if (a.Count > 0)
                    {
                        var v = Eval(a[0]);
                        sb.Append(a.Count > 1 && v.IsNumber ? FormatNumber(v, Eval(a[1]).L) : v.ToString());
                    }
                    if (m.Method == "println") sb.Append("\r\n");
                    _host.SerialWrite(sb.ToString());
                    _host.Spend(sb.Length * 10);
                    return Val.Int(sb.Length);
                }
                case "write":
                {
                    var v = a.Count > 0 ? Eval(a[0]) : Val.Int(0);
                    string text = v.K == VK.Str ? v.S : ((char)(v.L & 0xFF)).ToString();
                    _host.SerialWrite(text);
                    return Val.Int(text.Length);
                }
                case "available": return Val.Int(_host.SerialAvailable());
                case "read": return Val.Int(_host.SerialRead());
                case "peek": return Val.Int(_host.SerialPeek());
                case "flush" or "end": return Val.Void;
                case "parseInt":
                {
                    var sb = new StringBuilder();
                    while (_host.SerialAvailable() > 0 && !char.IsDigit((char)_host.SerialPeek()) && _host.SerialPeek() != '-') _host.SerialRead();
                    if (_host.SerialAvailable() > 0 && _host.SerialPeek() == '-') sb.Append((char)_host.SerialRead());
                    while (_host.SerialAvailable() > 0 && char.IsDigit((char)_host.SerialPeek())) sb.Append((char)_host.SerialRead());
                    return Val.Int(long.TryParse(sb.ToString(), out var n) ? n : 0);
                }
                case "readString":
                case "readStringUntil":
                {
                    char stop = a.Count > 0 ? (char)Eval(a[0]).L : '\0';
                    var sb = new StringBuilder();
                    while (_host.SerialAvailable() > 0)
                    {
                        char ch = (char)_host.SerialRead();
                        if (m.Method == "readStringUntil" && ch == stop) break;
                        sb.Append(ch);
                    }
                    return Val.Str(sb.ToString());
                }
            }
            throw new SketchRuntimeException($"Serial.{m.Method}() no está soportado en el simulador.", line);
        }

        var target = Eval(m.Target);
        if (target.O is ServoObj servo)
        {
            switch (m.Method)
            {
                case "attach":
                    servo.Pin = (int)Eval(a[0]).L;
                    _host.ServoWrite(servo, line);
                    return Val.Int(1);
                case "write":
                {
                    long v = Eval(a[0]).L;
                    servo.Angle = v > 200 ? Math.Clamp((v - 544) * 180.0 / (2400 - 544), 0, 180) : Math.Clamp(v, 0, 180);
                    _host.ServoWrite(servo, line);
                    return Val.Void;
                }
                case "writeMicroseconds":
                    servo.Angle = Math.Clamp((Eval(a[0]).L - 544) * 180.0 / (2400 - 544), 0, 180);
                    _host.ServoWrite(servo, line);
                    return Val.Void;
                case "read": return Val.Int((long)Math.Round(servo.Angle));
                case "attached": return Val.Bool(servo.Pin >= 0);
                case "detach": servo.Pin = -1; return Val.Void;
            }
            throw new SketchRuntimeException($"Servo.{m.Method}() no existe.", line);
        }
        if (target.K == VK.Str)
        {
            string s = target.S;
            switch (m.Method)
            {
                case "length": return Val.Int(s.Length);
                case "toInt": return Val.Int(long.TryParse(new string(s.Trim().TakeWhile((ch, i) => char.IsDigit(ch) || (i == 0 && ch == '-')).ToArray()), out var n) ? n : 0);
                case "toFloat": return Val.Float(double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0);
                case "charAt": { long i = Eval(a[0]).L; return Val.Int(i >= 0 && i < s.Length ? s[(int)i] : 0); }
                case "indexOf": { var x = Eval(a[0]); return Val.Int(x.K == VK.Str ? s.IndexOf(x.S, StringComparison.Ordinal) : s.IndexOf((char)x.L)); }
                case "substring":
                {
                    int from = (int)Math.Clamp(Eval(a[0]).L, 0, s.Length);
                    int to = a.Count > 1 ? (int)Math.Clamp(Eval(a[1]).L, from, s.Length) : s.Length;
                    return Val.Str(s[from..to]);
                }
                case "equals": return Val.Bool(s == Eval(a[0]).ToString());
                case "startsWith": return Val.Bool(s.StartsWith(Eval(a[0]).ToString(), StringComparison.Ordinal));
                case "endsWith": return Val.Bool(s.EndsWith(Eval(a[0]).ToString(), StringComparison.Ordinal));
                case "toUpperCase" or "toLowerCase" or "trim" or "concat" or "replace":
                {
                    string r = m.Method switch
                    {
                        "toUpperCase" => s.ToUpperInvariant(),
                        "toLowerCase" => s.ToLowerInvariant(),
                        "trim" => s.Trim(),
                        "concat" => s + Eval(a[0]),
                        _ => s.Replace(Eval(a[0]).ToString(), Eval(a[1]).ToString(), StringComparison.Ordinal),
                    };
                    // En Arduino estos métodos modifican el propio String.
                    if (m.Target is NameExpr or IndexExpr) Store(m.Target, Val.Str(r), line);
                    return m.Method == "concat" ? Val.Bool(true) : Val.Void;
                }
            }
            throw new SketchRuntimeException($"String.{m.Method}() no está soportado en el simulador.", line);
        }
        throw new SketchRuntimeException($"'.{m.Method}()' solo se puede usar con Serial, String o Servo.", line);
    }
}
