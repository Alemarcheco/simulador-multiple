using System.Globalization;
using System.Text;

namespace Simulador.Tableros.Import.Step;

/// <summary>Una entidad del archivo STEP (ISO 10303-21): «#12=TIPO(arg, …);» o compuesta «#12=(A(…) B(…));».</summary>
public sealed class StepEntity
{
    public int Id { get; init; }
    /// <summary>Tipo en mayúsculas (en las compuestas, el primero de la lista).</summary>
    public string Type { get; init; } = "";
    public List<object?> Args { get; init; } = [];
    /// <summary>Partes de una entidad compuesta (tipo → argumentos), vacío si es simple.</summary>
    public Dictionary<string, List<object?>> Parts { get; } = new(StringComparer.Ordinal);

    public bool Is(string type) => Type == type || Parts.ContainsKey(type);
    public List<object?> ArgsOf(string type) => Parts.TryGetValue(type, out var a) ? a : Args;
}

/// <summary>Referencia a otra entidad (#n).</summary>
public readonly record struct StepRef(int Id);

/// <summary>Valor enumerado (.T., .F., .UNSPECIFIED.…).</summary>
public readonly record struct StepEnum(string Value);

/// <summary>Parámetro con tipo, p. ej. LENGTH_MEASURE(1.E-03).</summary>
public sealed record StepTyped(string Type, List<object?> Args);

/// <summary>Lector de la sección DATA de un archivo STEP. Solo interpreta la sintaxis; la geometría va aparte.</summary>
public sealed class StepFile
{
    public Dictionary<int, StepEntity> Entities { get; } = [];
    public string FileName { get; private set; } = "";

    public StepEntity? this[int id] => Entities.TryGetValue(id, out var e) ? e : null;
    public StepEntity? Get(object? arg) => arg is StepRef r ? this[r.Id] : null;

    public static StepFile Parse(string text)
    {
        var f = new StepFile();
        var p = new Parser(text);
        var head = text.IndexOf("FILE_NAME", StringComparison.OrdinalIgnoreCase);
        if (head >= 0)
        {
            var q = text.IndexOf('\'', head);
            var q2 = q >= 0 ? text.IndexOf('\'', q + 1) : -1;
            if (q >= 0 && q2 > q) f.FileName = text[(q + 1)..q2];
        }
        var data = text.IndexOf("DATA;", StringComparison.OrdinalIgnoreCase);
        p.Pos = data >= 0 ? data + 5 : 0;
        while (p.NextEntity(out var e)) f.Entities[e.Id] = e;
        return f;
    }

    private sealed class Parser(string s)
    {
        public int Pos;

        public bool NextEntity(out StepEntity e)
        {
            e = null!;
            while (true)
            {
                SkipWs();
                if (Pos >= s.Length) return false;
                if (s[Pos] == '#') break;
                if (string.CompareOrdinal(s, Pos, "ENDSEC", 0, 6) == 0) return false;
                // Línea que no es una entidad: saltar hasta el próximo «;».
                var semi = s.IndexOf(';', Pos);
                if (semi < 0) return false;
                Pos = semi + 1;
            }
            Pos++;
            var id = ReadInt();
            SkipWs();
            if (Pos >= s.Length || s[Pos] != '=') { Skip(); return NextEntity(out e); }
            Pos++;
            SkipWs();
            if (s[Pos] == '(')
            {
                // Compuesta: (TIPO(args) TIPO(args) …)
                Pos++;
                var parts = new List<(string, List<object?>)>();
                while (true)
                {
                    SkipWs();
                    if (s[Pos] == ')') { Pos++; break; }
                    var name = ReadWord();
                    SkipWs();
                    var args = s[Pos] == '(' ? ReadList() : [];
                    parts.Add((name, args));
                }
                var ent = new StepEntity { Id = id, Type = parts.Count > 0 ? parts[0].Item1 : "", Args = parts.Count > 0 ? parts[0].Item2 : [] };
                foreach (var (n, a) in parts) ent.Parts[n] = a;
                e = ent;
            }
            else
            {
                var name = ReadWord();
                SkipWs();
                e = new StepEntity { Id = id, Type = name, Args = s[Pos] == '(' ? ReadList() : [] };
            }
            Skip();
            return true;
        }

        private void Skip()
        {
            var semi = s.IndexOf(';', Pos);
            Pos = semi < 0 ? s.Length : semi + 1;
        }

        private void SkipWs()
        {
            while (Pos < s.Length)
            {
                var c = s[Pos];
                if (char.IsWhiteSpace(c)) { Pos++; continue; }
                if (c == '/' && Pos + 1 < s.Length && s[Pos + 1] == '*')
                {
                    var end = s.IndexOf("*/", Pos + 2, StringComparison.Ordinal);
                    Pos = end < 0 ? s.Length : end + 2;
                    continue;
                }
                break;
            }
        }

        private int ReadInt()
        {
            var st = Pos;
            while (Pos < s.Length && char.IsDigit(s[Pos])) Pos++;
            return int.TryParse(s.AsSpan(st, Pos - st), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
        }

        private string ReadWord()
        {
            var st = Pos;
            while (Pos < s.Length && (char.IsLetterOrDigit(s[Pos]) || s[Pos] == '_')) Pos++;
            return s[st..Pos].ToUpperInvariant();
        }

        private List<object?> ReadList()
        {
            var list = new List<object?>();
            Pos++; // (
            while (true)
            {
                SkipWs();
                if (Pos >= s.Length) return list;
                var c = s[Pos];
                if (c == ')') { Pos++; return list; }
                if (c == ',') { Pos++; continue; }
                list.Add(ReadValue());
            }
        }

        private object? ReadValue()
        {
            var c = s[Pos];
            switch (c)
            {
                case '#': Pos++; return new StepRef(ReadInt());
                case '(': return ReadList();
                case '$': Pos++; return null;
                case '*': Pos++; return null;
                case '\'':
                {
                    var sb = new StringBuilder();
                    Pos++;
                    while (Pos < s.Length)
                    {
                        if (s[Pos] == '\'')
                        {
                            if (Pos + 1 < s.Length && s[Pos + 1] == '\'') { sb.Append('\''); Pos += 2; continue; }
                            Pos++;
                            break;
                        }
                        sb.Append(s[Pos++]);
                    }
                    return sb.ToString();
                }
                case '.':
                {
                    var end = s.IndexOf('.', Pos + 1);
                    var v = s[(Pos + 1)..end];
                    Pos = end + 1;
                    return new StepEnum(v.ToUpperInvariant());
                }
                case '"':
                {
                    var end = s.IndexOf('"', Pos + 1);
                    Pos = end + 1;
                    return null;
                }
            }
            if (c == '-' || c == '+' || char.IsDigit(c))
            {
                var st = Pos;
                Pos++;
                while (Pos < s.Length && (char.IsDigit(s[Pos]) || s[Pos] is '.' or 'E' or 'e' or '-' or '+')) Pos++;
                return double.TryParse(s.AsSpan(st, Pos - st), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
            }
            if (char.IsLetter(c))
            {
                var w = ReadWord();
                SkipWs();
                return new StepTyped(w, Pos < s.Length && s[Pos] == '(' ? ReadList() : []);
            }
            Pos++;
            return null;
        }
    }
}
