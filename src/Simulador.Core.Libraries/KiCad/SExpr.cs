using System.Globalization;
using System.Text;

namespace Simulador.Core.Libraries.KiCad;

/// <summary>Nodo de una S-expresión de KiCad: un átomo (texto) o una lista.</summary>
public sealed class SExpr
{
    public string? Atom { get; }
    public IReadOnlyList<SExpr> Items { get; }
    public bool IsList => Atom is null;

    private SExpr(string? atom, IReadOnlyList<SExpr> items) { Atom = atom; Items = items; }

    public static SExpr FromAtom(string atom) => new(atom, []);
    public static SExpr FromList(IReadOnlyList<SExpr> items) => new(null, items);

    /// <summary>Nombre de la lista: su primer átomo, p. ej. "symbol" en (symbol "R" …).</summary>
    public string Head => IsList && Items.Count > 0 ? Items[0].Atom ?? "" : "";

    /// <summary>Átomo en la posición indicada (1 = primer argumento).</summary>
    public string? Arg(int index) => index < Items.Count ? Items[index].Atom : null;

    public double Num(int index, double fallback = 0) =>
        Arg(index) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public SExpr? Child(string head) => Items.FirstOrDefault(i => i.Head == head);
    public IEnumerable<SExpr> Children(string head) => Items.Where(i => i.Head == head);
    public bool HasFlag(string atom) => Items.Any(i => i.Atom == atom);

    public override string ToString() => IsList ? "(" + string.Join(' ', Items) + ")" : Atom!;

    /// <summary>Lee el texto completo. Lanza <see cref="FormatException"/> si los paréntesis no cuadran.</summary>
    public static SExpr Parse(string text)
    {
        int pos = 0;
        SkipWs(text, ref pos);
        if (pos >= text.Length || text[pos] != '(')
            throw new FormatException("El archivo no empieza con '(' (no es un archivo KiCad válido).");
        var root = ReadList(text, ref pos);
        return root;
    }

    private static SExpr ReadList(string s, ref int pos)
    {
        pos++; // '('
        var items = new List<SExpr>();
        while (true)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length) throw new FormatException("Fin de archivo inesperado: falta ')'.");
            char c = s[pos];
            if (c == ')') { pos++; return FromList(items); }
            if (c == '(') items.Add(ReadList(s, ref pos));
            else if (c == '"') items.Add(FromAtom(ReadQuoted(s, ref pos)));
            else items.Add(FromAtom(ReadBare(s, ref pos)));
        }
    }

    private static string ReadQuoted(string s, ref int pos)
    {
        pos++; // '"'
        var sb = new StringBuilder();
        while (pos < s.Length)
        {
            char c = s[pos++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && pos < s.Length)
            {
                char e = s[pos++];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
            }
            else sb.Append(c);
        }
        throw new FormatException("Cadena sin cerrar.");
    }

    private static string ReadBare(string s, ref int pos)
    {
        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '(' && s[pos] != ')') pos++;
        return s[start..pos];
    }

    private static void SkipWs(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }
}
