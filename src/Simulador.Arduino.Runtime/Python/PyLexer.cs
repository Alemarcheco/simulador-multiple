using System.Globalization;
using System.Text;
using Simulador.Arduino.Runtime.Sketch;

namespace Simulador.Arduino.Runtime.Python;

internal enum PT { Name, Int, Float, Str, FStr, Op, Newline, Indent, Dedent, Eof }

internal readonly record struct PyToken(PT Kind, string Text, int Line, object? Value = null)
{
    public override string ToString() => Kind switch
    {
        PT.Eof => "fin del archivo",
        PT.Newline => "fin de línea",
        PT.Indent => "sangría",
        PT.Dedent => "fin de sangría",
        _ => $"'{Text}'",
    };
}

/// <summary>Analizador léxico de MicroPython: genera NEWLINE, INDENT y DEDENT según la sangría, como Python.</summary>
internal static class PyLexer
{
    private static readonly string[] Ops =
    [
        "**=", "//=", ">>=", "<<=", "**", "//", "==", "!=", "<=", ">=", "<<", ">>", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "->",
        "+", "-", "*", "/", "%", "<", ">", "=", "(", ")", "[", "]", "{", "}", ",", ":", ".", "&", "|", "^", "~", ";", "@",
    ];

    public static List<PyToken> Tokenize(string source)
    {
        var tokens = new List<PyToken>();
        var indents = new Stack<int>();
        indents.Push(0);
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int depth = 0; // dentro de paréntesis la sangría no cuenta
        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li];
            int lineNo = li + 1;
            int i = 0;
            if (depth == 0)
            {
                int col = 0;
                while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) { col += line[i] == '\t' ? 8 - col % 8 : 1; i++; }
                if (i >= line.Length || line[i] == '#') continue; // línea vacía o comentario
                if (col > indents.Peek())
                {
                    indents.Push(col);
                    tokens.Add(new PyToken(PT.Indent, "", lineNo));
                }
                while (col < indents.Peek())
                {
                    indents.Pop();
                    tokens.Add(new PyToken(PT.Dedent, "", lineNo));
                }
                if (col != indents.Peek()) throw new SketchCompileException("La sangría no coincide con ningún bloque anterior (IndentationError).", lineNo);
            }
            while (i < line.Length)
            {
                char c = line[i];
                if (c is ' ' or '\t') { i++; continue; }
                if (c == '#') break;
                if (c == '\\' && i == line.Length - 1) { i++; goto continued; }
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                    string word = line[s..i];
                    if (i < line.Length && (line[i] == '"' || line[i] == '\'') && word.ToLowerInvariant() is "f" or "r" or "b" or "rb" or "fr" or "rf")
                    {
                        var (text, next, endLine) = ReadString(lines, li, i, raw: word.Contains('r', StringComparison.OrdinalIgnoreCase));
                        tokens.Add(new PyToken(word.Contains('f', StringComparison.OrdinalIgnoreCase) ? PT.FStr : PT.Str, text, lineNo, text));
                        if (endLine != li) { li = endLine; line = lines[li]; }
                        i = next;
                        continue;
                    }
                    tokens.Add(new PyToken(PT.Name, word, lineNo));
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
                {
                    int s = i;
                    if (c == '0' && i + 1 < line.Length && "xXbBoO".Contains(line[i + 1]))
                    {
                        char kind = char.ToLowerInvariant(line[i + 1]);
                        i += 2;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                        string digits = line[(s + 2)..i].Replace("_", "");
                        long v = Convert.ToInt64(digits, kind == 'x' ? 16 : kind == 'b' ? 2 : 8);
                        tokens.Add(new PyToken(PT.Int, line[s..i], lineNo, v));
                        continue;
                    }
                    bool isFloat = false;
                    while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '_' || line[i] == '.' || line[i] is 'e' or 'E'
                        || ((line[i] is '+' or '-') && line[i - 1] is 'e' or 'E')))
                    {
                        if (line[i] is '.' or 'e' or 'E') isFloat = true;
                        i++;
                    }
                    string num = line[s..i].Replace("_", "");
                    if (isFloat) tokens.Add(new PyToken(PT.Float, num, lineNo, double.Parse(num, CultureInfo.InvariantCulture)));
                    else tokens.Add(new PyToken(PT.Int, num, lineNo, long.Parse(num, CultureInfo.InvariantCulture)));
                    continue;
                }
                if (c is '"' or '\'')
                {
                    var (text, next, endLine) = ReadString(lines, li, i, raw: false);
                    tokens.Add(new PyToken(PT.Str, text, lineNo, text));
                    if (endLine != li) { li = endLine; line = lines[li]; }
                    i = next;
                    continue;
                }
                string? op = Ops.FirstOrDefault(o => string.CompareOrdinal(line, i, o, 0, o.Length) == 0);
                if (op is null) throw new SketchCompileException($"Carácter no válido: '{c}'.", lineNo);
                if (op is "(" or "[" or "{") depth++;
                if (op is ")" or "]" or "}") depth = Math.Max(0, depth - 1);
                tokens.Add(new PyToken(PT.Op, op, lineNo));
                i += op.Length;
            }
            if (depth == 0 && tokens.Count > 0 && tokens[^1].Kind is not (PT.Newline or PT.Indent or PT.Dedent))
                tokens.Add(new PyToken(PT.Newline, "", lineNo));
            continued:;
        }
        int last = lines.Length;
        if (tokens.Count > 0 && tokens[^1].Kind != PT.Newline && tokens[^1].Kind != PT.Dedent) tokens.Add(new PyToken(PT.Newline, "", last));
        while (indents.Count > 1) { indents.Pop(); tokens.Add(new PyToken(PT.Dedent, "", last)); }
        tokens.Add(new PyToken(PT.Eof, "", last));
        return tokens;
    }

    /// <summary>Lee una cadena (simple o triple). Devuelve el texto, la posición siguiente y la línea donde acaba.</summary>
    private static (string Text, int Next, int EndLine) ReadString(string[] lines, int li, int i, bool raw)
    {
        string line = lines[li];
        char q = line[i];
        bool triple = i + 2 < line.Length && line[i + 1] == q && line[i + 2] == q;
        int startLine = li + 1;
        i += triple ? 3 : 1;
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= line.Length)
            {
                if (!triple) throw new SketchCompileException("Falta cerrar la cadena de texto.", startLine);
                li++;
                if (li >= lines.Length) throw new SketchCompileException("Falta cerrar la cadena de texto (\"\"\").", startLine);
                sb.Append('\n');
                line = lines[li];
                i = 0;
                continue;
            }
            char c = line[i];
            if (c == q && (!triple || (i + 2 < line.Length && line[i + 1] == q && line[i + 2] == q)))
                return (sb.ToString(), i + (triple ? 3 : 1), li);
            if (c == '\\' && !raw && i + 1 < line.Length)
            {
                char e = line[i + 1];
                i += 2;
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case 'x' when i + 1 < line.Length:
                        sb.Append((char)Convert.ToInt32(line.Substring(i, 2), 16));
                        i += 2;
                        break;
                    default: sb.Append(e); break;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
    }
}
