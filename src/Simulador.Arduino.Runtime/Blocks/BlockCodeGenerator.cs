using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Simulador.Arduino.Runtime.Blocks;

/// <summary>Sketch generado a partir de los bloques y qué bloque dio cada línea (para marcar el que se ejecuta).</summary>
public sealed record BlockCode(string Source, IReadOnlyDictionary<int, Block> LineToBlock);

/// <summary>
/// Convierte un programa de bloques en un sketch de Arduino normal y legible, para que se vea el código equivalente y
/// se ejecute con el mismo intérprete que el resto de sketches.
/// </summary>
public static partial class BlockCodeGenerator
{
    private static readonly HashSet<string> Reserved =
    [
        "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return", "int", "long", "float", "double",
        "char", "bool", "byte", "void", "const", "static", "unsigned", "true", "false", "HIGH", "LOW", "INPUT", "OUTPUT",
        "INPUT_PULLUP", "setup", "loop", "delay", "millis", "micros", "random", "map", "constrain", "min", "max", "abs",
        "Serial", "Servo", "String", "tone", "noTone", "pinMode", "digitalWrite", "digitalRead", "analogWrite", "analogRead",
    ];

    public static BlockCode Generate(BlockProgram program, ArduinoBoard? board = null)
    {
        if (board is { IsPython: true }) return new PyGen(board).Run(program);
        return new Gen(board).Run(program);
    }

    /// <summary>Nombre de variable válido en C a partir de lo que escribió el usuario (quita tildes y espacios).</summary>
    public static string VariableName(string text, Block? block = null)
    {
        var plain = new StringBuilder();
        foreach (char ch in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(ch is ' ' or '-' ? '_' : ch == 'ñ' ? 'n' : ch == 'Ñ' ? 'N' : ch);
        }
        string name = plain.ToString();
        if (name.Length == 0) throw new BlockException("Falta el nombre de la variable.", block);
        if (!Identifier().IsMatch(name))
            throw new BlockException($"«{text}» no vale como nombre de variable: usa letras, números y _, sin empezar por número.", block);
        if (Reserved.Contains(name)) throw new BlockException($"«{text}» es una palabra reservada de Arduino: elige otro nombre.", block);
        return name;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^-?\d+(\.\d+)?$")]
    private static partial Regex Number();

    private static string NumberCode(Block b)
    {
        string text = b.Field("n").Trim().Replace(',', '.');
        if (!Number().IsMatch(text)) throw new BlockException($"«{b.Field("n")}» no es un número.", b);
        return text;
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed class Gen(ArduinoBoard? board)
    {
        private readonly List<string> _lines = [];
        private readonly Dictionary<int, Block> _map = [];

        public BlockCode Run(BlockProgram p)
        {
            var all = p.Walk().ToList();
            var variables = all.Where(b => b.Op is "set_var" or "change_var" or "var")
                .Select(b => VariableName(b.Field("var"), b)).Distinct().ToList();
            var servos = all.Where(b => b.Op == "servo").Select(b => PinCode(b)).Distinct().ToList();
            bool serial = all.Any(b => b.Op is "print" or "print_text");

            Line(0, "// Programa hecho con bloques");
            if (servos.Count > 0) Line(0, "#include <Servo.h>");
            if (servos.Count > 0 || variables.Count > 0) Line(0, "");
            foreach (var s in servos) Line(0, $"Servo servo{s};");
            foreach (var v in variables) Line(0, $"long {v} = 0;");
            Line(0, "");
            Line(0, "void setup() {");
            if (serial) Line(1, "Serial.begin(9600);");
            foreach (var s in servos) Line(1, $"servo{s}.attach({s});");
            List(1, p.Setup);
            Line(0, "}");
            Line(0, "");
            Line(0, "void loop() {");
            List(1, p.Loop);
            Line(0, "}");
            return new BlockCode(string.Join("\n", _lines) + "\n", _map);
        }

        private void Line(int indent, string text, Block? b = null)
        {
            _lines.Add(new string(' ', indent * 2) + text);
            if (b is not null) _map[_lines.Count] = b;
        }

        private void List(int indent, List<Block> blocks)
        {
            foreach (var b in blocks) Statement(indent, b);
        }

        private void Statement(int n, Block b)
        {
            if (!b.Def.IsStatement) throw new BlockException("Un bloque de valor no puede ir suelto: ponlo dentro de un hueco.", b);
            switch (b.Op)
            {
                case "pin_mode": Line(n, $"pinMode({PinCode(b)}, {b.Field("mode")});", b); break;
                case "digital_write": Line(n, $"digitalWrite({PinCode(b)}, {b.Field("level")});", b); break;
                case "analog_write": Line(n, $"analogWrite({PinCode(b)}, {Expr(b, "value")});", b); break;
                case "delay": Line(n, $"delay({Expr(b, "ms")});", b); break;
                case "if":
                    Line(n, $"if ({Expr(b, "cond")}) {{", b);
                    List(n + 1, b.Body("do"));
                    Line(n, "}");
                    break;
                case "if_else":
                    Line(n, $"if ({Expr(b, "cond")}) {{", b);
                    List(n + 1, b.Body("do"));
                    Line(n, "} else {");
                    List(n + 1, b.Body("else"));
                    Line(n, "}");
                    break;
                case "repeat":
                    Line(n, $"for (int i{n} = 0; i{n} < {Expr(b, "n")}; i{n}++) {{", b);
                    List(n + 1, b.Body("do"));
                    Line(n, "}");
                    break;
                case "while":
                    Line(n, $"while ({Expr(b, "cond")}) {{", b);
                    List(n + 1, b.Body("do"));
                    Line(n, "}");
                    break;
                case "wait_until": Line(n, $"while (!{Expr(b, "cond", nested: true)}) {{ }}", b); break;
                case "set_var": Line(n, $"{VariableName(b.Field("var"), b)} = {Expr(b, "value")};", b); break;
                case "change_var": Line(n, $"{VariableName(b.Field("var"), b)} += {Expr(b, "value")};", b); break;
                case "print": Line(n, $"Serial.{b.Field("nl")}({Expr(b, "value")});", b); break;
                case "print_text": Line(n, $"Serial.{b.Field("nl")}({Quote(b.Field("text"))});", b); break;
                case "tone": Line(n, $"tone({PinCode(b)}, {Expr(b, "hz")}, {Expr(b, "ms")});", b); break;
                case "no_tone": Line(n, $"noTone({PinCode(b)});", b); break;
                case "servo": Line(n, $"servo{PinCode(b)}.write({Expr(b, "angle")});", b); break;
                default: throw new BlockException($"El bloque «{b.Op}» no sabe generar código.", b);
            }
        }

        private string Expr(Block parent, string input, bool nested = false)
        {
            if (!parent.Inputs.TryGetValue(input, out var b))
                throw new BlockException("Hay un hueco vacío.", parent);
            return Expr(b, nested);
        }

        private string Expr(Block b, bool nested)
        {
            if (b.Def.IsStatement) throw new BlockException("En un hueco solo caben bloques de valor o de condición.", b);
            string Wrap(string s) => nested ? $"({s})" : s;
            return b.Op switch
            {
                "num" => NumberCode(b),
                "var" => VariableName(b.Field("var"), b),
                "level" => b.Field("level"),
                "digital_read" => $"digitalRead({PinCode(b)})",
                "analog_read" => $"analogRead({PinCode(b)})",
                "millis" => "millis()",
                "arith" => Wrap($"{Expr(b, "a", true)} {b.Field("op")} {Expr(b, "b", true)}"),
                "compare" => Wrap($"{Expr(b, "a", true)} {b.Field("op")} {Expr(b, "b", true)}"),
                "logic" => Wrap($"{Expr(b, "a", true)} {b.Field("op")} {Expr(b, "b", true)}"),
                "not" => $"!{Expr(b, "a", true)}",
                "map" => $"map({Expr(b, "v")}, {Expr(b, "fromLo")}, {Expr(b, "fromHi")}, {Expr(b, "toLo")}, {Expr(b, "toHi")})",
                "random" => $"random({Expr(b, "min")}, {Expr(b, "max", true)} + 1)",
                "constrain" => $"constrain({Expr(b, "v")}, {Expr(b, "lo")}, {Expr(b, "hi")})",
                _ => throw new BlockException($"El bloque «{b.Op}» no sabe generar código.", b),
            };
        }


        /// <summary>D13 → 13, A0 → A0 (como se escribe en un sketch).</summary>
        private string PinCode(Block b)
        {
            string name = b.Field("pin").Trim().ToUpperInvariant();
            if (board is not null)
            {
                bool exists = Enumerable.Range(0, board.PinCount).Any(i => board.PinName(i) == name);
                if (!exists) throw new BlockException($"La placa {board.Name} no tiene el pin {name}.", b);
            }
            if (name.StartsWith('D') && int.TryParse(name[1..], out int d)) return d.ToString(CultureInfo.InvariantCulture);
            if (name.StartsWith('A') && int.TryParse(name[1..], out _)) return name;
            throw new BlockException($"«{b.Field("pin")}» no es un pin (usa D2, D13, A0…).", b);
        }

    }
}
