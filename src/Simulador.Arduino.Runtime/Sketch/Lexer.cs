using System.Globalization;
using System.Text;

namespace Simulador.Arduino.Runtime.Sketch;

/// <summary>Error de compilación del sketch, con la línea donde está.</summary>
public sealed class SketchCompileException(string message, int line) : Exception($"Línea {line}: {message}")
{
    public int Line { get; } = line;
    public string Detail { get; } = message;
}

/// <summary>Error durante la ejecución (división por cero, índice fuera del array…).</summary>
public sealed class SketchRuntimeException(string message, int line) : Exception($"Línea {line}: {message}")
{
    public int Line { get; } = line;
}

internal enum Tk { Ident, Int, Float, Char, String, Op, Eof }

internal readonly record struct Token(Tk Kind, string Text, int Line, long Int = 0, double Float = 0)
{
    public override string ToString() => Kind == Tk.Eof ? "fin del archivo" : $"'{Text}'";
}

/// <summary>
/// Analizador léxico de C/C++ para sketches de Arduino. Resuelve el preprocesador sencillo: <c>#include</c> se
/// ignora y <c>#define NOMBRE valor</c> sustituye el nombre por sus símbolos.
/// </summary>
internal static class Lexer
{
    private static readonly string[] Ops =
    [
        "<<=", ">>=", "...",
        "++", "--", "->", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "==", "!=", "<=", ">=", "&&", "||", "<<", ">>", "::",
        "+", "-", "*", "/", "%", "&", "|", "^", "~", "!", "=", "<", ">", "?", ":", ";", ",", ".", "(", ")", "[", "]", "{", "}",
    ];

    public static List<Token> Tokenize(string source)
    {
        var raw = new List<Token>();
        var macros = new Dictionary<string, List<Token>>(StringComparer.Ordinal);
        var lines = source.Replace("\r\n", "\n").Split('\n');
        bool inComment = false;
        for (int ln = 0; ln < lines.Length; ln++)
        {
            string text = lines[ln];
            int line = ln + 1;
            if (!inComment && text.TrimStart().StartsWith('#'))
            {
                Directive(text.Trim(), line, macros);
                continue;
            }
            int i = 0;
            while (i < text.Length)
            {
                if (inComment)
                {
                    int end = text.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0) { i = text.Length; continue; }
                    inComment = false;
                    i = end + 2;
                    continue;
                }
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') break;
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { inComment = true; i += 2; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string id = text[s..i];
                    if (macros.TryGetValue(id, out var body))
                        raw.AddRange(body.Select(t => t with { Line = line }));
                    else
                        raw.Add(new Token(Tk.Ident, id, line));
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                {
                    raw.Add(Number(text, ref i, line));
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    long v = ReadChar(text, ref i, line);
                    if (i >= text.Length || text[i] != '\'') throw new SketchCompileException("Falta la comilla de cierre del carácter.", line);
                    i++;
                    raw.Add(new Token(Tk.Char, ((char)v).ToString(), line, v));
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < text.Length && text[i] != '"') sb.Append((char)ReadChar(text, ref i, line));
                    if (i >= text.Length) throw new SketchCompileException("Falta la comilla de cierre del texto.", line);
                    i++;
                    // "a" "b" seguidos se unen, como en C.
                    if (raw.Count > 0 && raw[^1].Kind == Tk.String) raw[^1] = raw[^1] with { Text = raw[^1].Text + sb };
                    else raw.Add(new Token(Tk.String, sb.ToString(), line));
                    continue;
                }
                string? op = Ops.FirstOrDefault(o => string.CompareOrdinal(text, i, o, 0, o.Length) == 0);
                if (op is null) throw new SketchCompileException($"Carácter no válido '{c}'.", line);
                raw.Add(new Token(Tk.Op, op, line));
                i += op.Length;
            }
        }
        raw.Add(new Token(Tk.Eof, "", lines.Length));
        return raw;
    }

    private static void Directive(string text, int line, Dictionary<string, List<Token>> macros)
    {
        var parts = text[1..].TrimStart();
        if (parts.StartsWith("define", StringComparison.Ordinal))
        {
            var rest = parts[6..].Trim();
            int i = 0;
            while (i < rest.Length && (char.IsLetterOrDigit(rest[i]) || rest[i] == '_')) i++;
            if (i == 0) throw new SketchCompileException("#define sin nombre.", line);
            string name = rest[..i];
            if (i < rest.Length && rest[i] == '(')
                throw new SketchCompileException($"Las macros con parámetros ({name}(…)) no están soportadas; usa una función.", line);
            var body = rest[i..];
            int comment = body.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) body = body[..comment];
            var tokens = Tokenize(body);
            tokens.RemoveAt(tokens.Count - 1);
            macros[name] = tokens;
        }
        // #include, #ifdef, #pragma…: se ignoran (las librerías habituales están incorporadas).
    }

    private static Token Number(string text, ref int i, int line)
    {
        int s = i;
        if (text[i] == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X' or 'b' or 'B'))
        {
            bool hex = text[i + 1] is 'x' or 'X';
            i += 2;
            int ds = i;
            while (i < text.Length && (hex ? Uri.IsHexDigit(text[i]) : text[i] is '0' or '1')) i++;
            string digits = text[ds..i];
            SkipSuffix(text, ref i);
            if (digits.Length == 0) throw new SketchCompileException("Número mal escrito.", line);
            long v = hex ? Convert.ToInt64(digits, 16) : Convert.ToInt64(digits, 2);
            return new Token(Tk.Int, text[s..i], line, v);
        }
        bool isFloat = false;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || ((text[i] is 'e' or 'E') && i + 1 < text.Length && (char.IsDigit(text[i + 1]) || text[i + 1] is '-' or '+'))))
        {
            if (text[i] is '.' or 'e' or 'E') isFloat = true;
            if (text[i] is 'e' or 'E') i++;
            i++;
        }
        string num = text[s..i];
        if (i < text.Length && text[i] is 'f' or 'F') { isFloat = true; i++; }
        else SkipSuffix(text, ref i);
        if (isFloat)
        {
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) throw new SketchCompileException($"Número mal escrito '{num}'.", line);
            return new Token(Tk.Float, num, line, 0, f);
        }
        // 012 es octal en C, pero casi siempre es un error de quien empieza: se toma como decimal.
        if (!long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) throw new SketchCompileException($"Número mal escrito '{num}'.", line);
        return new Token(Tk.Int, num, line, n);
    }

    private static void SkipSuffix(string text, ref int i)
    {
        while (i < text.Length && text[i] is 'u' or 'U' or 'l' or 'L') i++;
    }

    private static long ReadChar(string text, ref int i, int line)
    {
        if (i >= text.Length) throw new SketchCompileException("Carácter incompleto.", line);
        char c = text[i++];
        if (c != '\\') return c;
        if (i >= text.Length) throw new SketchCompileException("Secuencia de escape incompleta.", line);
        char e = text[i++];
        return e switch
        {
            'n' => '\n', 't' => '\t', 'r' => '\r', '0' => 0, '\\' => '\\', '\'' => '\'', '"' => '"', 'a' => 7, 'b' => 8, 'f' => 12, 'v' => 11,
            _ => e,
        };
    }
}
