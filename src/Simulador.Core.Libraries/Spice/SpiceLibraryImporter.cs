using System.Text;
using System.Text.RegularExpressions;
using Simulador.Core.Libraries.Model;

namespace Simulador.Core.Libraries.Spice;

/// <summary>
/// Importa modelos SPICE de fabricantes (<c>.model</c> y <c>.subckt</c>) desde archivos .lib, .mod, .sub, .cir…
/// El texto original se conserva para pasarlo tal cual a ngspice.
/// </summary>
public sealed partial class SpiceLibraryImporter : ILibraryImporter
{
    public string Name => "Modelos SPICE";
    public IReadOnlyList<string> Extensions { get; } = [".lib", ".mod", ".sub", ".cir", ".spice", ".sp", ".inc", ".model", ".ckt"];

    private readonly record struct LogicalLine(string Text, string Raw);

    public LibraryImportResult Import(string content, string libraryName)
    {
        var result = new LibraryImportResult();
        var lines = ToLogicalLines(content);

        for (int i = 0; i < lines.Count; i++)
        {
            var text = lines[i].Text;
            if (text.StartsWith(".model", StringComparison.OrdinalIgnoreCase))
            {
                if (ParseModel(lines[i]) is { } model) result.SpiceModels.Add(model);
                else result.Warnings.Add($"{libraryName}: no se pudo leer «{Shorten(text)}».");
            }
            else if (text.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase))
            {
                int end = FindEnds(lines, i);
                if (end < 0)
                {
                    result.Warnings.Add($"{libraryName}: .subckt sin .ends: «{Shorten(text)}».");
                    break;
                }
                result.SpiceSubcircuits.Add(ParseSubckt(lines, i, end));
                i = end; // los .model internos pertenecen al subcircuito
            }
        }
        return result;
    }

    private static int FindEnds(List<LogicalLine> lines, int start)
    {
        int depth = 0;
        for (int j = start; j < lines.Count; j++)
        {
            var t = lines[j].Text;
            if (t.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) depth++;
            else if (t.StartsWith(".ends", StringComparison.OrdinalIgnoreCase) && --depth == 0) return j;
        }
        return -1;
    }

    private static SpiceModel? ParseModel(LogicalLine line)
    {
        // .model NOMBRE TIPO(params) | .model NOMBRE TIPO (params) | .model NOMBRE TIPO params
        var m = ModelHeader().Match(line.Text);
        if (!m.Success) return null;
        var rest = line.Text[(m.Index + m.Length)..];
        return new SpiceModel(m.Groups["name"].Value, m.Groups["type"].Value.ToUpperInvariant(), ParseParams(rest), line.Raw.TrimEnd());
    }

    private static SpiceSubcircuit ParseSubckt(List<LogicalLine> lines, int start, int end)
    {
        var header = lines[start].Text;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Separa "params:" (estilo PSpice/LTspice) o pares nombre=valor sueltos al final de la cabecera.
        var paramsIdx = ParamsKeyword().Match(header);
        string pinPart = header;
        if (paramsIdx.Success)
        {
            pinPart = header[..paramsIdx.Index];
            foreach (var kv in ParseParams(header[(paramsIdx.Index + paramsIdx.Length)..])) parameters[kv.Key] = kv.Value;
        }
        var tokens = pinPart.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        var name = tokens.Count > 0 ? tokens[0] : "sin_nombre";
        var pins = new List<string>();
        foreach (var tok in tokens.Skip(1))
        {
            if (tok.Contains('='))
            {
                foreach (var kv in ParseParams(string.Join(' ', tokens.SkipWhile(t => t != tok)))) parameters[kv.Key] = kv.Value;
                break;
            }
            pins.Add(tok);
        }

        var sb = new StringBuilder();
        for (int j = start; j <= end; j++) sb.AppendLine(lines[j].Raw.TrimEnd());
        return new SpiceSubcircuit(name, pins, parameters, sb.ToString().TrimEnd());
    }

    private static Dictionary<string, string> ParseParams(string text)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ParamPair().Matches(text))
            dict[m.Groups["k"].Value] = m.Groups["v"].Value;
        return dict;
    }

    /// <summary>Une las líneas de continuación (+), quita comentarios y conserva el texto original.</summary>
    private static List<LogicalLine> ToLogicalLines(string content)
    {
        var result = new List<LogicalLine>();
        var physical = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var rawLine in physical)
        {
            var trimmed = rawLine.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '*') continue;
            var code = StripInlineComment(trimmed).Trim();
            if (code.StartsWith('+'))
            {
                if (result.Count == 0) continue;
                var prev = result[^1];
                result[^1] = new LogicalLine(prev.Text + " " + code[1..].Trim(), prev.Raw + "\n" + rawLine);
            }
            else if (code.Length > 0)
            {
                result.Add(new LogicalLine(code, rawLine));
            }
        }
        return result;
    }

    private static string StripInlineComment(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == ';') return line[..i];
            if (c == '$' && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return line[..i];
        }
        return line;
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "...";

    [GeneratedRegex(@"^\.model\s+(?<name>\S+)\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ModelHeader();

    [GeneratedRegex(@"\bparams\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ParamsKeyword();

    [GeneratedRegex(@"(?<k>[A-Za-z_][A-Za-z0-9_.]*)\s*=\s*(?<v>\{[^}]*\}|'[^']*'|[^\s=(),]+)")]
    private static partial Regex ParamPair();
}
