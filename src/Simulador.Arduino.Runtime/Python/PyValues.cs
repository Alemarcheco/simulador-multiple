using System.Globalization;
using System.Text;

namespace Simulador.Arduino.Runtime.Python;

/// <summary>Excepción de Python (NameError, TypeError…) mientras corre el programa.</summary>
internal sealed class PyError(string type, string message) : Exception(message)
{
    public string Type { get; } = type;
    public int Line { get; set; }
    public object? Value { get; set; }
}

internal sealed class PyList(List<object?> items)
{
    public List<object?> Items { get; } = items;
}

internal sealed class PyTuple(object?[] items)
{
    public object?[] Items { get; } = items;
}

internal sealed class PyDict
{
    public Dictionary<object, object?> Items { get; } = new(PyEquality.Instance);
}

internal sealed class PyRange(long start, long stop, long step)
{
    public long Start { get; } = start;
    public long Stop { get; } = stop;
    public long Step { get; } = step;

    public long Count => Step > 0 ? Math.Max(0, (Stop - Start + Step - 1) / Step) : Math.Max(0, (Start - Stop - Step - 1) / -Step);

    public long this[long i] => Start + i * Step;
}

/// <summary>Argumentos de una llamada: posicionales y con nombre.</summary>
internal sealed record PyArgs(IReadOnlyList<object?> Pos, IReadOnlyDictionary<string, object?> Kw)
{
    public static readonly IReadOnlyDictionary<string, object?> NoKw = new Dictionary<string, object?>();

    public object? Get(int index, string name, object? fallback = null) =>
        index < Pos.Count ? Pos[index] : Kw.TryGetValue(name, out var v) ? v : fallback;

    public bool Has(int index, string name) => index < Pos.Count || Kw.ContainsKey(name);
}

internal sealed class PyBuiltin(string name, Func<PyArgs, object?> fn)
{
    public string Name { get; } = name;
    public Func<PyArgs, object?> Fn { get; } = fn;
}

internal sealed class PyFunction(string name, IReadOnlyList<PParam> pars, IReadOnlyList<PStmt>? body, PExpr? lambdaBody, PyEnv closure, object?[] defaults)
{
    public string Name { get; } = name;
    public IReadOnlyList<PParam> Params { get; } = pars;
    public IReadOnlyList<PStmt>? Body { get; } = body;
    public PExpr? LambdaBody { get; } = lambdaBody;
    public PyEnv Closure { get; } = closure;
    public object?[] Defaults { get; } = defaults;
}

internal sealed class PyBound(object self, object func)
{
    public object Self { get; } = self;
    public object Func { get; } = func;
}

internal sealed class PyClass(string name, Dictionary<string, object?> attrs)
{
    public string Name { get; } = name;
    public Dictionary<string, object?> Attrs { get; } = attrs;
}

internal sealed class PyInstance(PyClass cls)
{
    public PyClass Class { get; } = cls;
    public Dictionary<string, object?> Attrs { get; } = [];
}

/// <summary>Tipo de excepción (ValueError…) y sus instancias.</summary>
internal sealed class PyExceptionType(string name)
{
    public string Name { get; } = name;
}

internal sealed class PyExceptionObj(string type, string message)
{
    public string Type { get; } = type;
    public string Message { get; } = message;
}

internal sealed class PyModuleObj(string name)
{
    public string Name { get; } = name;
    public Dictionary<string, object?> Attrs { get; } = [];
}

/// <summary>Objeto implementado en C# (Pin, PWM, ADC…): expone atributos y métodos por nombre.</summary>
internal abstract class PyNative
{
    public abstract string TypeName { get; }
    public abstract object? GetAttr(string name);
    /// <summary>true si el atributo existe aunque su valor sea None.</summary>
    public virtual bool HasAttr(string name) => false;
    public virtual object? Call(PyArgs args) => throw new PyError("TypeError", $"'{TypeName}' no se puede llamar como función");
    public virtual string Repr() => $"<{TypeName}>";
}

/// <summary>Variables de un ámbito (módulo o llamada a función) y el ámbito que lo encierra.</summary>
internal sealed class PyEnv(PyEnv? parent)
{
    public PyEnv? Parent { get; } = parent;
    public Dictionary<string, object?> Vars { get; } = new(StringComparer.Ordinal);
    public HashSet<string>? Globals { get; set; }

    public PyEnv Module
    {
        get
        {
            var e = this;
            while (e.Parent is not null) e = e.Parent;
            return e;
        }
    }
}

/// <summary>Igualdad de Python para las claves de dict (1 == 1.0 == True).</summary>
internal sealed class PyEquality : IEqualityComparer<object>
{
    public static readonly PyEquality Instance = new();

    public new bool Equals(object? x, object? y) => PyOps.Eq(x, y);

    public int GetHashCode(object o) => o switch
    {
        bool b => b ? 1 : 0,
        long l => l.GetHashCode(),
        double d when d == Math.Floor(d) && Math.Abs(d) < 9e18 => ((long)d).GetHashCode(),
        PyTuple t => t.Items.Aggregate(17, (h, x) => h * 31 + (x is null ? 0 : GetHashCode(x))),
        _ => o.GetHashCode(),
    };
}

/// <summary>Operaciones de Python sobre valores: verdad, igualdad, aritmética, texto.</summary>
internal static class PyOps
{
    public static string TypeName(object? v) => v switch
    {
        null => "NoneType",
        bool => "bool",
        long => "int",
        double => "float",
        string => "str",
        PyList => "list",
        PyTuple => "tuple",
        PyDict => "dict",
        PyRange => "range",
        PyFunction or PyBuiltin or PyBound => "function",
        PyClass => "type",
        PyInstance i => i.Class.Name,
        PyModuleObj => "module",
        PyExceptionObj e => e.Type,
        PyExceptionType => "type",
        PyNative n => n.TypeName,
        _ => v.GetType().Name,
    };

    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        double d => d != 0,
        string s => s.Length > 0,
        PyList l => l.Items.Count > 0,
        PyTuple t => t.Items.Length > 0,
        PyDict d => d.Items.Count > 0,
        PyRange r => r.Count > 0,
        _ => true,
    };

    public static bool IsNumber(object? v) => v is long or double or bool;

    public static long ToLong(object? v) => v switch
    {
        long l => l,
        bool b => b ? 1 : 0,
        _ => throw new PyError("TypeError", $"se esperaba un entero y es {TypeName(v)}"),
    };

    public static double ToDouble(object? v) => v switch
    {
        long l => l,
        double d => d,
        bool b => b ? 1 : 0,
        _ => throw new PyError("TypeError", $"se esperaba un número y es {TypeName(v)}"),
    };

    public static bool Eq(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (IsNumber(a) && IsNumber(b))
            return a is double || b is double ? ToDouble(a) == ToDouble(b) : ToLong(a) == ToLong(b);
        return (a, b) switch
        {
            (string x, string y) => x == y,
            (PyList x, PyList y) => x.Items.Count == y.Items.Count && x.Items.Zip(y.Items).All(p => Eq(p.First, p.Second)),
            (PyTuple x, PyTuple y) => x.Items.Length == y.Items.Length && x.Items.Zip(y.Items).All(p => Eq(p.First, p.Second)),
            (PyDict x, PyDict y) => x.Items.Count == y.Items.Count && x.Items.All(kv => y.Items.TryGetValue(kv.Key, out var v) && Eq(kv.Value, v)),
            _ => ReferenceEquals(a, b) || a.Equals(b),
        };
    }

    public static int Compare(object? a, object? b)
    {
        if (IsNumber(a) && IsNumber(b))
            return a is double || b is double ? ToDouble(a).CompareTo(ToDouble(b)) : ToLong(a).CompareTo(ToLong(b));
        if (a is string x && b is string y) return string.CompareOrdinal(x, y);
        if (a is PyList la && b is PyList lb) return CompareSeq(la.Items, lb.Items);
        if (a is PyTuple ta && b is PyTuple tb) return CompareSeq(ta.Items, tb.Items);
        throw new PyError("TypeError", $"no se puede comparar {TypeName(a)} con {TypeName(b)}");
    }

    private static int CompareSeq(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            int c = Compare(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Count.CompareTo(b.Count);
    }

    public static string Str(object? v) => v switch
    {
        string s => s,
        PyExceptionObj e => e.Message,
        _ => Repr(v),
    };

    public static string Repr(object? v) => v switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => FloatRepr(d),
        string s => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n") + "'",
        PyList l => "[" + string.Join(", ", l.Items.Select(Repr)) + "]",
        PyTuple t => t.Items.Length == 1 ? "(" + Repr(t.Items[0]) + ",)" : "(" + string.Join(", ", t.Items.Select(Repr)) + ")",
        PyDict d => "{" + string.Join(", ", d.Items.Select(kv => Repr(kv.Key) + ": " + Repr(kv.Value))) + "}",
        PyRange r => r.Step == 1 ? $"range({r.Start}, {r.Stop})" : $"range({r.Start}, {r.Stop}, {r.Step})",
        PyFunction f => $"<function {f.Name}>",
        PyBuiltin b => $"<built-in function {b.Name}>",
        PyBound => "<bound method>",
        PyClass c => $"<class '{c.Name}'>",
        PyInstance i => $"<{i.Class.Name} object>",
        PyModuleObj m => $"<module '{m.Name}'>",
        PyExceptionObj e => $"{e.Type}('{e.Message}')",
        PyExceptionType t => $"<class '{t.Name}'>",
        PyNative n => n.Repr(),
        _ => v.ToString() ?? "",
    };

    public static string FloatRepr(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e16) return d.ToString("0.0", CultureInfo.InvariantCulture);
        string r = d.ToString("R", CultureInfo.InvariantCulture);
        if (r.Contains('E'))
        {
            // 1E-05 → 1e-05
            var parts = r.Split('E');
            int exp = int.Parse(parts[1], CultureInfo.InvariantCulture);
            r = parts[0] + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
        }
        return r;
    }

    /// <summary>Aplica una especificación de formato de Python (".2f", "05d", ">8", "x"…).</summary>
    public static string Format(object? v, string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return Str(v);
        int i = 0;
        char fill = ' ', align = '\0', sign = '-';
        if (spec.Length >= 2 && spec[1] is '<' or '>' or '^' or '=') { fill = spec[0]; align = spec[1]; i = 2; }
        else if (spec[0] is '<' or '>' or '^' or '=') { align = spec[0]; i = 1; }
        if (i < spec.Length && spec[i] is '+' or '-' or ' ') sign = spec[i++];
        if (i < spec.Length && spec[i] == '0') { fill = '0'; if (align == '\0') align = '='; i++; }
        int width = 0;
        while (i < spec.Length && char.IsDigit(spec[i])) width = width * 10 + (spec[i++] - '0');
        bool comma = i < spec.Length && spec[i] == ',';
        if (comma) i++;
        int? precision = null;
        if (i < spec.Length && spec[i] == '.')
        {
            i++;
            int p = 0;
            while (i < spec.Length && char.IsDigit(spec[i])) p = p * 10 + (spec[i++] - '0');
            precision = p;
        }
        char type = i < spec.Length ? spec[i] : '\0';
        string body;
        bool numeric = IsNumber(v) && v is not bool || (v is bool && type != '\0');
        switch (type)
        {
            case 'f' or 'F':
                body = ToDouble(v).ToString((comma ? "#,##0." : "0.") + new string('0', precision ?? 6), CultureInfo.InvariantCulture);
                if (precision == 0) body = ToDouble(v).ToString(comma ? "#,##0" : "0", CultureInfo.InvariantCulture);
                break;
            case 'e' or 'E':
                body = ToDouble(v).ToString("0." + new string('0', precision ?? 6) + "e+00", CultureInfo.InvariantCulture);
                break;
            case '%':
                body = (ToDouble(v) * 100).ToString("0." + new string('0', precision ?? 6), CultureInfo.InvariantCulture) + "%";
                if (precision == 0) body = (ToDouble(v) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                break;
            case 'd':
                body = comma ? ToLong(v).ToString("#,##0", CultureInfo.InvariantCulture) : ToLong(v).ToString(CultureInfo.InvariantCulture);
                break;
            case 'x': body = ToLong(v).ToString("x", CultureInfo.InvariantCulture); break;
            case 'X': body = ToLong(v).ToString("X", CultureInfo.InvariantCulture); break;
            case 'b': body = Convert.ToString(ToLong(v), 2); break;
            case 'o': body = Convert.ToString(ToLong(v), 8); break;
            case 'c': body = ((char)ToLong(v)).ToString(); break;
            case 'g' or 'G':
                body = ToDouble(v).ToString("G" + (precision ?? 6), CultureInfo.InvariantCulture);
                break;
            default:
                body = v is double d && precision is { } pr ? d.ToString("0." + new string('#', Math.Max(pr - 1, 0)), CultureInfo.InvariantCulture)
                    : v is string s && precision is { } sp ? s[..Math.Min(sp, s.Length)] : Str(v);
                break;
        }
        if (numeric && sign == '+' && !body.StartsWith('-')) body = "+" + body;
        if (numeric && sign == ' ' && !body.StartsWith('-')) body = " " + body;
        if (body.Length >= width) return body;
        if (align == '\0') align = numeric ? '>' : '<';
        int pad = width - body.Length;
        return align switch
        {
            '<' => body + new string(fill, pad),
            '^' => new string(fill, pad / 2) + body + new string(fill, pad - pad / 2),
            '=' when body.Length > 0 && body[0] is '-' or '+' or ' ' => body[0] + new string(fill, pad) + body[1..],
            _ => new string(fill, pad) + body,
        };
    }

    /// <summary>"texto {} y {nombre:.2f}".format(...)</summary>
    public static string FormatMethod(string fmt, PyArgs args)
    {
        var sb = new StringBuilder();
        int auto = 0;
        for (int i = 0; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c == '{' && i + 1 < fmt.Length && fmt[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (c != '{') { sb.Append(c); continue; }
            int end = fmt.IndexOf('}', i);
            if (end < 0) throw new PyError("ValueError", "falta '}' en el formato");
            string field = fmt[(i + 1)..end];
            string? spec = null;
            int colon = field.IndexOf(':');
            if (colon >= 0) { spec = field[(colon + 1)..]; field = field[..colon]; }
            object? value = field.Length == 0 ? args.Pos.ElementAtOrDefault(auto++)
                : int.TryParse(field, out int n) ? args.Pos.ElementAtOrDefault(n)
                : args.Kw.TryGetValue(field, out var kv) ? kv : throw new PyError("KeyError", field);
            sb.Append(Format(value, spec));
            i = end;
        }
        return sb.ToString();
    }

    /// <summary>"%d grados" % valor</summary>
    public static string Percent(string fmt, object? arg)
    {
        var values = arg is PyTuple t ? t.Items : [arg];
        var sb = new StringBuilder();
        int k = 0;
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%') { sb.Append(fmt[i]); continue; }
            int j = i + 1;
            if (j < fmt.Length && fmt[j] == '%') { sb.Append('%'); i = j; continue; }
            while (j < fmt.Length && !char.IsLetter(fmt[j])) j++;
            if (j >= fmt.Length) throw new PyError("ValueError", "formato % incompleto");
            string spec = fmt[(i + 1)..j];
            char type = fmt[j];
            if (k >= values.Length) throw new PyError("TypeError", "faltan valores para el formato %");
            object? v = values[k++];
            sb.Append(type switch
            {
                's' => Format(Str(v), spec),
                'r' => Format(Repr(v), spec),
                'i' or 'u' => Format(v is double dd ? (long)dd : v, spec + "d"),
                _ => Format(type == 'd' && v is double d2 ? (long)d2 : v, spec + type),
            });
            i = j;
        }
        return sb.ToString();
    }
}
