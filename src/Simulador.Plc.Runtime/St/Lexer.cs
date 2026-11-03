using System.Globalization;
using System.Text;

namespace Simulador.Plc.Runtime.St;

public enum TokenKind { Ident, Int, Real, Time, Address, Symbol, End }

public readonly record struct Token(TokenKind Kind, string Text, int Line, int Column)
{
    public bool Is(string symbolOrKeyword) =>
        (Kind is TokenKind.Symbol or TokenKind.Ident) &&
        string.Equals(Text, symbolOrKeyword, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Kind == TokenKind.End ? "fin del texto" : $"'{Text}'";
}

/// <summary>Error de compilación con posición en el texto.</summary>
public sealed class PlcCompileException(string message, int line, int column)
    : Exception($"Línea {line}, columna {column}: {message}")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>Analizador léxico de texto estructurado (IEC 61131-3).</summary>
public static class Lexer
{
    private static readonly string[] Symbols = [":=", "=>", "<>", "<=", ">=", "**", "(", ")", ",", ";", ":", ".", "=", "<", ">", "+", "-", "*", "/", "&", "[", "]"];

    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0, line = 1, col = 1;

        void Advance(int n)
        {
            for (int k = 0; k < n; k++)
            {
                if (source[i] == '\n') { line++; col = 1; }
                else col++;
                i++;
            }
        }

        while (i < source.Length)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c)) { Advance(1); continue; }

            if (c == '(' && Peek(source, i + 1) == '*')
            {
                int startLine = line, startCol = col;
                int end = source.IndexOf("*)", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new PlcCompileException("Comentario sin cerrar '(*'.", startLine, startCol);
                Advance(end + 2 - i);
                continue;
            }
            if (c == '/' && Peek(source, i + 1) == '/')
            {
                while (i < source.Length && source[i] != '\n') Advance(1);
                continue;
            }

            int tl = line, tc = col;

            if (c == '%')
            {
                int s = i;
                Advance(1);
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '.')) Advance(1);
                tokens.Add(new Token(TokenKind.Address, source[s..i], tl, tc));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int s = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) Advance(1);
                string word = source[s..i];
                // Literales de tiempo: T#5s, TIME#1m30s
                if (i < source.Length && source[i] == '#' &&
                    (word.Equals("T", StringComparison.OrdinalIgnoreCase) || word.Equals("TIME", StringComparison.OrdinalIgnoreCase)))
                {
                    Advance(1);
                    int ts = i;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] is '_' or '.')) Advance(1);
                    tokens.Add(new Token(TokenKind.Time, source[ts..i], tl, tc));
                    continue;
                }
                tokens.Add(new Token(TokenKind.Ident, word, tl, tc));
                continue;
            }

            if (char.IsDigit(c))
            {
                int s = i;
                bool real = false;
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '_')) Advance(1);
                if (i + 1 < source.Length && source[i] == '.' && char.IsDigit(source[i + 1]))
                {
                    real = true;
                    Advance(1);
                    while (i < source.Length && char.IsDigit(source[i])) Advance(1);
                }
                if (i < source.Length && (source[i] is 'e' or 'E'))
                {
                    real = true;
                    Advance(1);
                    if (i < source.Length && source[i] is '+' or '-') Advance(1);
                    while (i < source.Length && char.IsDigit(source[i])) Advance(1);
                }
                tokens.Add(new Token(real ? TokenKind.Real : TokenKind.Int, source[s..i].Replace("_", ""), tl, tc));
                continue;
            }

            string? sym = Symbols.FirstOrDefault(sy => string.CompareOrdinal(source, i, sy, 0, sy.Length) == 0);
            if (sym is null) throw new PlcCompileException($"Carácter no válido '{c}'.", tl, tc);
            Advance(sym.Length);
            tokens.Add(new Token(TokenKind.Symbol, sym, tl, tc));
        }

        tokens.Add(new Token(TokenKind.End, "", line, col));
        return tokens;
    }

    private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

    /// <summary>Convierte "1m30s", "500ms", "1.5s", "2h" en un intervalo.</summary>
    public static TimeSpan ParseTime(string text, Token at)
    {
        var total = TimeSpan.Zero;
        int i = 0;
        text = text.Replace("_", "");
        if (text.Length == 0) throw new PlcCompileException("Literal de tiempo vacío.", at.Line, at.Column);
        while (i < text.Length)
        {
            var num = new StringBuilder();
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) num.Append(text[i++]);
            var unit = new StringBuilder();
            while (i < text.Length && char.IsLetter(text[i])) unit.Append(text[i++]);
            if (num.Length == 0 || !double.TryParse(num.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new PlcCompileException($"Literal de tiempo no válido 'T#{text}'.", at.Line, at.Column);
            total += unit.ToString().ToLowerInvariant() switch
            {
                "d" => TimeSpan.FromDays(v),
                "h" => TimeSpan.FromHours(v),
                "m" => TimeSpan.FromMinutes(v),
                "s" => TimeSpan.FromSeconds(v),
                "ms" => TimeSpan.FromMilliseconds(v),
                _ => throw new PlcCompileException($"Unidad de tiempo no válida en 'T#{text}' (usa d, h, m, s, ms).", at.Line, at.Column),
            };
        }
        return total;
    }
}
