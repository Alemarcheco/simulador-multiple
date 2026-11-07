using System.Globalization;
using System.Text;

namespace Simulador.Arduino.Runtime.Python;

/// <summary>Funciones integradas de Python (print, len, range…) y métodos de str, list y dict.</summary>
internal sealed partial class PyInterpreter
{
    private static readonly string[] ExceptionNames =
    [
        "Exception", "BaseException", "ValueError", "TypeError", "KeyError", "IndexError", "ZeroDivisionError", "NameError",
        "AttributeError", "RuntimeError", "OSError", "ImportError", "StopIteration", "NotImplementedError", "AssertionError",
        "ArithmeticError", "LookupError", "OverflowError", "RecursionError", "KeyboardInterrupt",
    ];

    private void Def(string name, Func<PyArgs, object?> fn) => _builtins[name] = new PyBuiltin(name, fn);

    private void InitBuiltins()
    {
        foreach (var n in ExceptionNames) _builtins[n] = new PyExceptionType(n);

        Def("print", a =>
        {
            string sep = a.Kw.TryGetValue("sep", out var s) && s is string ss ? ss : " ";
            string end = a.Kw.TryGetValue("end", out var e) && e is string es ? es : "\n";
            Host.SerialWrite(string.Join(sep, a.Pos.Select(PyOps.Str)) + end);
            Spend(50 + 10 * a.Pos.Count);
            return null;
        });
        Def("len", a => Arg(a, 0, "len") switch
        {
            string s => (long)s.Length,
            PyList l => (long)l.Items.Count,
            PyTuple t => (long)t.Items.Length,
            PyDict d => (long)d.Items.Count,
            PyRange r => r.Count,
            var v => throw new PyError("TypeError", $"'{PyOps.TypeName(v)}' no tiene longitud"),
        });
        Def("int", a =>
        {
            if (a.Pos.Count == 0) return 0L;
            var v = a.Pos[0];
            switch (v)
            {
                case long l: return l;
                case bool b: return b ? 1L : 0L;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new PyError("ValueError", "no se puede convertir a int: " + PyOps.FloatRepr(d));
                    return (long)Math.Truncate(d);
                case string s:
                {
                    int bas = a.Pos.Count > 1 ? (int)PyOps.ToLong(a.Pos[1]) : 10;
                    string t = s.Trim().Replace("_", "");
                    try
                    {
                        if (bas == 10) return long.Parse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                        bool neg = t.StartsWith('-');
                        if (neg || t.StartsWith('+')) t = t[1..];
                        if (bas == 16 && t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
                        if (bas == 2 && t.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) t = t[2..];
                        long r = Convert.ToInt64(t, bas);
                        return neg ? -r : r;
                    }
                    catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                    {
                        throw new PyError("ValueError", $"el texto '{s}' no es un número entero válido");
                    }
                }
            }
            throw new PyError("TypeError", $"int() no acepta '{PyOps.TypeName(v)}'");
        });
        Def("float", a =>
        {
            if (a.Pos.Count == 0) return 0.0;
            var v = a.Pos[0];
            if (v is string s)
            {
                string t = s.Trim().ToLowerInvariant();
                if (t is "inf" or "+inf") return double.PositiveInfinity;
                if (t == "-inf") return double.NegativeInfinity;
                if (t == "nan") return double.NaN;
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                throw new PyError("ValueError", $"el texto '{s}' no es un número válido");
            }
            return PyOps.ToDouble(v);
        });
        Def("str", a => a.Pos.Count == 0 ? "" : PyOps.Str(a.Pos[0]));
        Def("repr", a => PyOps.Repr(Arg(a, 0, "repr")));
        Def("bool", a => a.Pos.Count > 0 && PyOps.Truthy(a.Pos[0]));
        Def("abs", a => Arg(a, 0, "abs") switch
        {
            double d => (object)Math.Abs(d),
            var v => Math.Abs(PyOps.ToLong(v)),
        });
        Def("min", a => MinMax(a, -1));
        Def("max", a => MinMax(a, 1));
        Def("sum", a =>
        {
            object? total = a.Pos.Count > 1 ? a.Pos[1] : 0L;
            foreach (var x in Iterate(Arg(a, 0, "sum"))) total = Binary("+", total, x);
            return total;
        });
        Def("round", a =>
        {
            double d = PyOps.ToDouble(Arg(a, 0, "round"));
            if (a.Pos.Count < 2 || a.Pos[1] is null)
                return Arg(a, 0, "round") is long l ? l : (long)Math.Round(d, MidpointRounding.ToEven);
            int digits = (int)PyOps.ToLong(a.Pos[1]);
            if (a.Pos[0] is long li) return li;
            if (digits < 0) return Math.Round(d / Math.Pow(10, -digits)) * Math.Pow(10, -digits);
            if (Math.Abs(d) < 1e12 && digits <= 10)
            {
                // Como Python: se redondea el valor binario exacto (2.675 es en realidad 2.67499999…).
                decimal exact = decimal.Parse(d.ToString("F" + (digits + 15), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                return (double)Math.Round(exact, digits, MidpointRounding.ToEven);
            }
            return Math.Round(d, Math.Min(digits, 15), MidpointRounding.ToEven);
        });
        Def("range", a =>
        {
            long start = 0, stop, step = 1;
            if (a.Pos.Count == 1) stop = PyOps.ToLong(a.Pos[0]);
            else if (a.Pos.Count >= 2)
            {
                start = PyOps.ToLong(a.Pos[0]);
                stop = PyOps.ToLong(a.Pos[1]);
                if (a.Pos.Count > 2) step = PyOps.ToLong(a.Pos[2]);
            }
            else throw new PyError("TypeError", "range() necesita al menos un argumento");
            if (step == 0) throw new PyError("ValueError", "el paso de range() no puede ser cero");
            return new PyRange(start, stop, step);
        });
        Def("list", a => new PyList(a.Pos.Count == 0 ? [] : Iterate(a.Pos[0]).ToList()));
        Def("tuple", a => new PyTuple(a.Pos.Count == 0 ? [] : Iterate(a.Pos[0]).ToArray()));
        Def("dict", a =>
        {
            var d = new PyDict();
            if (a.Pos.Count > 0)
            {
                if (a.Pos[0] is PyDict src) foreach (var kv in src.Items) d.Items[kv.Key] = kv.Value;
                else
                    foreach (var pair in Iterate(a.Pos[0]))
                    {
                        var kv = Iterate(pair).ToList();
                        if (kv.Count != 2) throw new PyError("ValueError", "dict() necesita parejas (clave, valor)");
                        d.Items[Hashable(kv[0])] = kv[1];
                    }
            }
            foreach (var (k, v) in a.Kw) d.Items[k] = v;
            return d;
        });
        Def("enumerate", a =>
        {
            long start = a.Has(1, "start") ? PyOps.ToLong(a.Get(1, "start")) : 0;
            return new PyList(Iterate(Arg(a, 0, "enumerate")).Select((x, i) => (object?)new PyTuple([start + i, x])).ToList());
        });
        Def("zip", a =>
        {
            var lists = a.Pos.Select(x => Iterate(x).ToList()).ToList();
            int n = lists.Count == 0 ? 0 : lists.Min(l => l.Count);
            return new PyList(Enumerable.Range(0, n).Select(i => (object?)new PyTuple(lists.Select(l => l[i]).ToArray())).ToList());
        });
        Def("sorted", a =>
        {
            var items = Iterate(Arg(a, 0, "sorted")).ToList();
            SortList(items, a.Kw.GetValueOrDefault("key"), a.Kw.TryGetValue("reverse", out var r) && PyOps.Truthy(r));
            return new PyList(items);
        });
        Def("reversed", a =>
        {
            var items = Iterate(Arg(a, 0, "reversed")).ToList();
            items.Reverse();
            return new PyList(items);
        });
        Def("map", a =>
        {
            var f = Arg(a, 0, "map");
            var lists = a.Pos.Skip(1).Select(x => Iterate(x).ToList()).ToList();
            int n = lists.Count == 0 ? 0 : lists.Min(l => l.Count);
            return new PyList(Enumerable.Range(0, n).Select(i => Call(f, new PyArgs(lists.Select(l => l[i]).ToList(), PyArgs.NoKw))).ToList());
        });
        Def("filter", a =>
        {
            var f = Arg(a, 0, "filter");
            return new PyList(Iterate(Arg(a, 1, "filter")).Where(x => PyOps.Truthy(f is null ? x : Call(f, new PyArgs([x], PyArgs.NoKw)))).ToList());
        });
        Def("any", a => Iterate(Arg(a, 0, "any")).Any(PyOps.Truthy));
        Def("all", a => Iterate(Arg(a, 0, "all")).All(PyOps.Truthy));
        Def("chr", a => ((char)PyOps.ToLong(Arg(a, 0, "chr"))).ToString());
        Def("ord", a => Arg(a, 0, "ord") is string { Length: 1 } s ? (long)s[0] : throw new PyError("TypeError", "ord() necesita un solo carácter"));
        Def("hex", a => Prefixed(PyOps.ToLong(Arg(a, 0, "hex")), 16, "0x"));
        Def("bin", a => Prefixed(PyOps.ToLong(Arg(a, 0, "bin")), 2, "0b"));
        Def("oct", a => Prefixed(PyOps.ToLong(Arg(a, 0, "oct")), 8, "0o"));
        Def("divmod", a => new PyTuple([Binary("//", Arg(a, 0, "divmod"), Arg(a, 1, "divmod")), Binary("%", a.Pos[0], a.Pos[1])]));
        Def("pow", a => a.Pos.Count > 2
            ? ModPow(PyOps.ToLong(a.Pos[0]), PyOps.ToLong(a.Pos[1]), PyOps.ToLong(a.Pos[2]))
            : Binary("**", Arg(a, 0, "pow"), Arg(a, 1, "pow")));
        Def("isinstance", a => IsInstance(Arg(a, 0, "isinstance"), Arg(a, 1, "isinstance")));
        Def("type", a =>
        {
            var v = Arg(a, 0, "type");
            if (v is PyInstance i) return i.Class;
            return _builtins.TryGetValue(PyOps.TypeName(v), out var t) ? t : new PyBuiltin(PyOps.TypeName(v), _ => throw new PyError("TypeError", "no se puede crear este tipo"));
        });
        Def("hasattr", a =>
        {
            try { GetAttr(Arg(a, 0, "hasattr"), PyOps.Str(Arg(a, 1, "hasattr"))); return true; }
            catch (PyError e) when (e.Type == "AttributeError") { return false; }
        });
        Def("getattr", a =>
        {
            try { return GetAttr(Arg(a, 0, "getattr"), PyOps.Str(Arg(a, 1, "getattr"))); }
            catch (PyError e) when (e.Type == "AttributeError" && a.Pos.Count > 2) { return a.Pos[2]; }
        });
        Def("setattr", a =>
        {
            if (Arg(a, 0, "setattr") is PyInstance i) i.Attrs[PyOps.Str(Arg(a, 1, "setattr"))] = Arg(a, 2, "setattr");
            else throw new PyError("TypeError", "setattr() solo funciona con objetos de clases propias");
            return null;
        });
        Def("id", a => (long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Arg(a, 0, "id") ?? NoneKey));
        Def("hash", a => (long)PyEquality.Instance.GetHashCode(Hashable(Arg(a, 0, "hash"))));
        Def("input", a =>
        {
            if (a.Pos.Count > 0) Host.SerialWrite(PyOps.Str(a.Pos[0]));
            var sb = new StringBuilder();
            while (true)
            {
                int c = Host.SerialRead();
                if (c < 0) { Sleep(10_000); continue; }
                if (c == '\n') break;
                if (c != '\r') sb.Append((char)c);
            }
            Host.SerialWrite(sb + "\n"); // eco, como el REPL
            return sb.ToString();
        });
    }

    private static object? Arg(PyArgs a, int i, string fn) =>
        i < a.Pos.Count ? a.Pos[i] : throw new PyError("TypeError", $"a {fn}() le faltan argumentos");

    private static string Prefixed(long v, int bas, string prefix) =>
        (v < 0 ? "-" : "") + prefix + Convert.ToString(Math.Abs(v), bas).ToLowerInvariant();

    private static long ModPow(long b, long e, long m)
    {
        if (m == 0) throw new PyError("ValueError", "pow() con módulo 0");
        long r = 1;
        b %= m;
        while (e > 0)
        {
            if ((e & 1) == 1) r = (long)((Int128)r * b % m);
            b = (long)((Int128)b * b % m);
            e >>= 1;
        }
        return r;
    }

    private object? MinMax(PyArgs a, int sign)
    {
        var items = a.Pos.Count == 1 ? Iterate(a.Pos[0]).ToList() : a.Pos.ToList();
        if (items.Count == 0)
        {
            if (a.Kw.TryGetValue("default", out var def)) return def;
            throw new PyError("ValueError", (sign > 0 ? "max" : "min") + "() de una secuencia vacía");
        }
        var key = a.Kw.GetValueOrDefault("key");
        object? best = items[0], bestKey = key is null ? best : Call(key, new PyArgs([best], PyArgs.NoKw));
        foreach (var x in items.Skip(1))
        {
            var k = key is null ? x : Call(key, new PyArgs([x], PyArgs.NoKw));
            if (PyOps.Compare(k, bestKey) * sign > 0) { best = x; bestKey = k; }
        }
        return best;
    }

    private void SortList(List<object?> items, object? key, bool reverse)
    {
        var keys = key is null ? items : items.Select(x => Call(key, new PyArgs([x], PyArgs.NoKw))).ToList();
        var idx = Enumerable.Range(0, items.Count).ToArray();
        // Ordenación estable, como la de Python.
        var sorted = idx.OrderBy(i => i, Comparer<int>.Create((x, y) => PyOps.Compare(keys[x], keys[y]))).ToList();
        if (reverse)
            sorted = idx.OrderByDescending(i => i, Comparer<int>.Create((x, y) => PyOps.Compare(keys[x], keys[y]))).ToList();
        var copy = sorted.Select(i => items[i]).ToList();
        items.Clear();
        items.AddRange(copy);
        Spend(items.Count * 5);
    }

    private bool IsInstance(object? v, object? type)
    {
        if (type is PyTuple t) return t.Items.Any(x => IsInstance(v, x));
        return type switch
        {
            PyBuiltin b => b.Name switch
            {
                "int" => v is long or bool,
                "float" => v is double,
                "str" => v is string,
                "bool" => v is bool,
                "list" => v is PyList,
                "tuple" => v is PyTuple,
                "dict" => v is PyDict,
                _ => false,
            },
            PyClass c => v is PyInstance i && IsSubclass(i.Class, c),
            PyExceptionType et => v is PyExceptionObj o && (et.Name is "Exception" or "BaseException" || o.Type == et.Name),
            _ => false,
        };
    }

    private static bool IsSubclass(PyClass cls, PyClass target)
    {
        if (cls == target) return true;
        return cls.Attrs.TryGetValue("__bases__", out var b) && b is PyTuple t && t.Items.OfType<PyClass>().Any(x => IsSubclass(x, target));
    }

    // ---- Atributos ----

    internal object? GetAttr(object? obj, string name)
    {
        switch (obj)
        {
            case PyModuleObj m:
                if (m.Attrs.TryGetValue(name, out var mv)) return mv;
                throw new PyError("AttributeError", $"el módulo '{m.Name}' no tiene '{name}'");
            case PyInstance i:
                if (i.Attrs.TryGetValue(name, out var iv)) return iv;
                if (i.Class.Attrs.TryGetValue(name, out var cv)) return cv is PyFunction ? new PyBound(i, cv) : cv;
                break;
            case PyClass c:
                if (c.Attrs.TryGetValue(name, out var cav)) return cav;
                break;
            case PyNative n:
                var nv = n.GetAttr(name);
                if (nv is not null || n.HasAttr(name)) return nv;
                break;
            case PyExceptionObj e when name == "args":
                return new PyTuple(e.Message.Length == 0 ? [] : [e.Message]);
            case string s: return StrMethod(s, name) ?? Missing(obj, name);
            case PyList l: return ListMethod(l, name) ?? Missing(obj, name);
            case PyDict d: return DictMethod(d, name) ?? Missing(obj, name);
            case double dd when name == "is_integer": return new PyBuiltin(name, _ => dd == Math.Floor(dd));
            case PyTuple t when name is "index" or "count":
                return ListMethod(new PyList([.. t.Items]), name);
        }
        return Missing(obj, name);
    }

    private static object Missing(object? obj, string name) =>
        throw new PyError("AttributeError", $"'{PyOps.TypeName(obj)}' no tiene el atributo '{name}'");

    private PyBuiltin M(string name, Func<PyArgs, object?> fn) => new(name, fn);

    private PyBuiltin? StrMethod(string s, string name) => name switch
    {
        "upper" => M(name, _ => s.ToUpperInvariant()),
        "lower" => M(name, _ => s.ToLowerInvariant()),
        "strip" => M(name, a => a.Pos.Count > 0 && a.Pos[0] is string cs ? s.Trim(cs.ToCharArray()) : s.Trim()),
        "lstrip" => M(name, a => a.Pos.Count > 0 && a.Pos[0] is string cs ? s.TrimStart(cs.ToCharArray()) : s.TrimStart()),
        "rstrip" => M(name, a => a.Pos.Count > 0 && a.Pos[0] is string cs ? s.TrimEnd(cs.ToCharArray()) : s.TrimEnd()),
        "split" => M(name, a =>
        {
            var sep = a.Get(0, "sep");
            int max = a.Has(1, "maxsplit") ? (int)PyOps.ToLong(a.Get(1, "maxsplit")) : -1;
            string[] parts = sep is string ss
                ? (max >= 0 ? s.Split(ss, max + 1) : s.Split(ss))
                : (max >= 0 ? s.Split((char[]?)null, max + 1, StringSplitOptions.RemoveEmptyEntries) : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return new PyList(parts.Select(p => (object?)p).ToList());
        }),
        "join" => M(name, a => string.Join(s, Iterate(Arg(a, 0, "join")).Select(x => x as string ?? throw new PyError("TypeError", "join() solo une textos (str)")))),
        "replace" => M(name, a => s.Replace(PyOps.Str(Arg(a, 0, "replace")), PyOps.Str(Arg(a, 1, "replace")), StringComparison.Ordinal)),
        "startswith" => M(name, a => s.StartsWith(PyOps.Str(Arg(a, 0, "startswith")), StringComparison.Ordinal)),
        "endswith" => M(name, a => s.EndsWith(PyOps.Str(Arg(a, 0, "endswith")), StringComparison.Ordinal)),
        "find" => M(name, a => (long)s.IndexOf(PyOps.Str(Arg(a, 0, "find")), StringComparison.Ordinal)),
        "index" => M(name, a => s.IndexOf(PyOps.Str(Arg(a, 0, "index")), StringComparison.Ordinal) is var i and >= 0 ? (long)i : throw new PyError("ValueError", "no se encontró el texto")),
        "count" => M(name, a => { string sub = PyOps.Str(Arg(a, 0, "count")); return sub.Length == 0 ? s.Length + 1L : (long)((s.Length - s.Replace(sub, "", StringComparison.Ordinal).Length) / sub.Length); }),
        "format" => M(name, a => PyOps.FormatMethod(s, a)),
        "isdigit" => M(name, _ => s.Length > 0 && s.All(char.IsDigit)),
        "isalpha" => M(name, _ => s.Length > 0 && s.All(char.IsLetter)),
        "isspace" => M(name, _ => s.Length > 0 && s.All(char.IsWhiteSpace)),
        "isupper" => M(name, _ => s.Any(char.IsLetter) && s.Where(char.IsLetter).All(char.IsUpper)),
        "islower" => M(name, _ => s.Any(char.IsLetter) && s.Where(char.IsLetter).All(char.IsLower)),
        "capitalize" => M(name, _ => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()),
        "title" => M(name, _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())),
        "center" => M(name, a => PyOps.Format(s, "^" + PyOps.ToLong(Arg(a, 0, "center")))),
        "ljust" => M(name, a => s.PadRight((int)PyOps.ToLong(Arg(a, 0, "ljust")), a.Pos.Count > 1 ? PyOps.Str(a.Pos[1])[0] : ' ')),
        "rjust" => M(name, a => s.PadLeft((int)PyOps.ToLong(Arg(a, 0, "rjust")), a.Pos.Count > 1 ? PyOps.Str(a.Pos[1])[0] : ' ')),
        "zfill" => M(name, a => PyOps.Format(s, "0>" + PyOps.ToLong(Arg(a, 0, "zfill")))),
        "encode" or "decode" => M(name, _ => s),
        _ => null,
    };

    private PyBuiltin? ListMethod(PyList l, string name) => name switch
    {
        "append" => M(name, a => { l.Items.Add(Arg(a, 0, "append")); return null; }),
        "extend" => M(name, a => { l.Items.AddRange(Iterate(Arg(a, 0, "extend")).ToList()); return null; }),
        "insert" => M(name, a =>
        {
            long i = PyOps.ToLong(Arg(a, 0, "insert"));
            if (i < 0) i += l.Items.Count;
            l.Items.Insert((int)Math.Clamp(i, 0, l.Items.Count), Arg(a, 1, "insert"));
            return null;
        }),
        "pop" => M(name, a =>
        {
            if (l.Items.Count == 0) throw new PyError("IndexError", "pop() de una lista vacía");
            int i = a.Pos.Count > 0 ? NormIndex(a.Pos[0], l.Items.Count) : l.Items.Count - 1;
            var v = l.Items[i];
            l.Items.RemoveAt(i);
            return v;
        }),
        "remove" => M(name, a =>
        {
            var x = Arg(a, 0, "remove");
            int i = l.Items.FindIndex(y => PyOps.Eq(x, y));
            if (i < 0) throw new PyError("ValueError", $"{PyOps.Repr(x)} no está en la lista");
            l.Items.RemoveAt(i);
            return null;
        }),
        "index" => M(name, a =>
        {
            var x = Arg(a, 0, "index");
            int i = l.Items.FindIndex(y => PyOps.Eq(x, y));
            return i >= 0 ? (long)i : throw new PyError("ValueError", $"{PyOps.Repr(x)} no está en la lista");
        }),
        "count" => M(name, a => (long)l.Items.Count(y => PyOps.Eq(Arg(a, 0, "count"), y))),
        "sort" => M(name, a => { SortList(l.Items, a.Kw.GetValueOrDefault("key"), a.Kw.TryGetValue("reverse", out var r) && PyOps.Truthy(r)); return null; }),
        "reverse" => M(name, _ => { l.Items.Reverse(); return null; }),
        "clear" => M(name, _ => { l.Items.Clear(); return null; }),
        "copy" => M(name, _ => new PyList([.. l.Items])),
        _ => null,
    };

    private PyBuiltin? DictMethod(PyDict d, string name) => name switch
    {
        "get" => M(name, a => d.Items.TryGetValue(Hashable(Arg(a, 0, "get")), out var v) ? v : a.Pos.Count > 1 ? a.Pos[1] : null),
        "keys" => M(name, _ => new PyList(d.Items.Keys.Select(Unkey).ToList())),
        "values" => M(name, _ => new PyList([.. d.Items.Values])),
        "items" => M(name, _ => new PyList(d.Items.Select(kv => (object?)new PyTuple([Unkey(kv.Key), kv.Value])).ToList())),
        "pop" => M(name, a =>
        {
            var k = Hashable(Arg(a, 0, "pop"));
            if (d.Items.Remove(k, out var v)) return v;
            return a.Pos.Count > 1 ? a.Pos[1] : throw new PyError("KeyError", PyOps.Repr(a.Pos[0]));
        }),
        "update" => M(name, a =>
        {
            if (a.Pos.Count > 0 && a.Pos[0] is PyDict o) foreach (var kv in o.Items) d.Items[kv.Key] = kv.Value;
            foreach (var (k, v) in a.Kw) d.Items[k] = v;
            return null;
        }),
        "setdefault" => M(name, a =>
        {
            var k = Hashable(Arg(a, 0, "setdefault"));
            if (!d.Items.TryGetValue(k, out var v)) d.Items[k] = v = a.Pos.Count > 1 ? a.Pos[1] : null;
            return v;
        }),
        "clear" => M(name, _ => { d.Items.Clear(); return null; }),
        "copy" => M(name, _ => { var c = new PyDict(); foreach (var kv in d.Items) c.Items[kv.Key] = kv.Value; return c; }),
        _ => null,
    };
}
