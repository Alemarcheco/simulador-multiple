using System.Globalization;

namespace Simulador.Arduino.Runtime.Blocks;

/// <summary>
/// Los mismos bloques, convertidos a MicroPython para el ESP32 y la Raspberry Pi Pico. Los bloques mantienen su
/// significado de Arduino (lecturas analógicas de 0 a 1023, PWM de 0 a 255, ALTO = 1), así un programa de bloques
/// funciona igual en cualquier placa; el código generado hace la conversión.
/// </summary>
public static partial class BlockCodeGenerator
{
    private static readonly HashSet<string> PyReserved =
    [
        "and", "or", "not", "if", "elif", "else", "for", "while", "in", "is", "def", "return", "pass", "break", "continue",
        "class", "import", "from", "as", "global", "nonlocal", "lambda", "try", "except", "finally", "raise", "del", "with",
        "yield", "assert", "async", "await", "True", "False", "None", "print", "range", "int", "float", "str", "len", "min",
        "max", "abs", "time", "random", "machine", "Pin", "PWM", "ADC", "convertir", "tono", "mover_servo", "leer_analogico",
    ];

    private sealed class PyGen(ArduinoBoard board)
    {
        private readonly List<string> _lines = [];
        private readonly Dictionary<int, Block> _map = [];
        private bool _esp => board == ArduinoBoard.Esp32;

        public BlockCode Run(BlockProgram p)
        {
            var all = p.Walk().ToList();
            var variables = all.Where(b => b.Op is "set_var" or "change_var" or "var").Select(b => Var(b)).Distinct().ToList();
            var digital = all.Where(b => b.Op is "pin_mode" or "digital_write" or "digital_read").Select(Pin).Distinct().Order().ToList();
            var analog = all.Where(b => b.Op == "analog_read").Select(AnalogPin).Distinct().Order().ToList();
            var pwm = all.Where(b => b.Op is "analog_write" or "tone" or "no_tone").Select(Pin).Distinct().Order().ToList();
            var servos = all.Where(b => b.Op == "servo").Select(Pin).Distinct().Order().ToList();
            foreach (var s in servos)
                if (pwm.Contains(s) || digital.Contains(s))
                    throw new BlockException($"El pin {board.PinName(s)} se usa para el servo y para otra cosa a la vez.", all.First(b => b.Op == "servo" && Pin(b) == s));
            bool rnd = all.Any(b => b.Op == "random");
            bool time = all.Any(b => b.Op is "delay" or "millis" or "tone") || p.Loop.Count == 0;

            Line(0, "# Programa hecho con bloques (MicroPython)");
            var machine = new List<string>();
            if (digital.Count + analog.Count + pwm.Count + servos.Count > 0) machine.Add("Pin");
            if (pwm.Count + servos.Count > 0) machine.Add("PWM");
            if (analog.Count > 0) machine.Add("ADC");
            if (machine.Count > 0) Line(0, "from machine import " + string.Join(", ", machine));
            if (time) Line(0, "import time");
            if (rnd) Line(0, "import random");
            Line(0, "");
            foreach (var n in digital) Line(0, $"pin{n} = Pin({n})");
            foreach (var n in pwm) Line(0, $"pwm{n} = PWM(Pin({n}), freq=1000, duty_u16=0)");
            foreach (var n in servos) Line(0, $"servo{n} = PWM(Pin({n}), freq=50, duty_u16=0)");
            foreach (var n in analog)
            {
                Line(0, $"adc{n} = ADC(Pin({n}))");
                if (_esp) Line(0, $"adc{n}.atten(ADC.ATTN_11DB)   # mide de 0 a 3,3 V");
            }
            foreach (var v in variables) Line(0, $"{v} = 0");
            if (digital.Count + pwm.Count + servos.Count + analog.Count + variables.Count > 0) Line(0, "");

            Helpers(all);

            Line(0, "# --- al encender (una vez) ---");
            if (p.Setup.Count == 0) Line(0, "pass");
            List(0, p.Setup);
            Line(0, "");
            Line(0, "# --- se repite siempre ---");
            Line(0, "while True:");
            if (p.Loop.Count == 0) Line(1, "time.sleep_ms(10)");
            List(1, p.Loop);
            return new BlockCode(string.Join("\n", _lines) + "\n", _map);
        }

        private void Helpers(List<Block> all)
        {
            if (all.Any(b => b.Op == "analog_read"))
            {
                Line(0, "def leer_analogico(adc):");
                Line(1, "return adc.read_u16() >> 6   # de 0-65535 a 0-1023, como en Arduino");
                Line(0, "");
            }
            if (all.Any(b => b.Op == "map"))
            {
                Line(0, "def convertir(valor, de_min, de_max, a_min, a_max):");
                Line(1, "return (valor - de_min) * (a_max - a_min) // (de_max - de_min) + a_min");
                Line(0, "");
            }
            if (all.Any(b => b.Op == "tone"))
            {
                Line(0, "def tono(zumbador, hz, ms):");
                Line(1, "zumbador.freq(int(hz))");
                Line(1, "zumbador.duty_u16(32768)   # onda cuadrada al 50 %");
                Line(1, "time.sleep_ms(int(ms))");
                Line(1, "zumbador.duty_u16(0)");
                Line(0, "");
            }
            if (all.Any(b => b.Op == "servo"))
            {
                Line(0, "def mover_servo(servo, grados):");
                Line(1, "grados = min(max(grados, 0), 180)");
                Line(1, "servo.duty_ns(int(500_000 + grados * 2_000_000 / 180))   # impulso de 0,5 a 2,5 ms");
                Line(0, "");
            }
        }

        private void Line(int indent, string text, Block? b = null)
        {
            _lines.Add(new string(' ', indent * 4) + text);
            if (b is not null) _map[_lines.Count] = b;
        }

        private void List(int indent, List<Block> blocks)
        {
            foreach (var b in blocks) Statement(indent, b);
        }

        private void Body(int indent, List<Block> blocks)
        {
            if (blocks.Count == 0) Line(indent, "pass");
            else List(indent, blocks);
        }

        private void Statement(int n, Block b)
        {
            if (!b.Def.IsStatement) throw new BlockException("Un bloque de valor no puede ir suelto: ponlo dentro de un hueco.", b);
            switch (b.Op)
            {
                case "pin_mode":
                {
                    int pin = Pin(b);
                    string mode = b.Field("mode") switch
                    {
                        "OUTPUT" => "Pin.OUT",
                        "INPUT_PULLUP" => "Pin.IN, Pin.PULL_UP",
                        _ => "Pin.IN",
                    };
                    if (b.Field("mode") == "OUTPUT" && board.InputOnly.Contains(pin))
                        throw new BlockException($"{board.PinName(pin)} de {board.Name} solo puede ser entrada.", b);
                    Line(n, $"pin{pin}.init({mode})", b);
                    break;
                }
                case "digital_write": Line(n, $"pin{Pin(b)}.value({(b.Field("level") == "HIGH" ? 1 : 0)})", b); break;
                case "analog_write": Line(n, $"pwm{Pin(b)}.duty_u16(min(max(int({Expr(b, "value")}), 0), 255) * 257)   # 0-255 como en Arduino", b); break;
                case "delay": Line(n, $"time.sleep_ms(int({Expr(b, "ms")}))", b); break;
                case "if":
                    Line(n, $"if {Expr(b, "cond")}:", b);
                    Body(n + 1, b.Body("do"));
                    break;
                case "if_else":
                    Line(n, $"if {Expr(b, "cond")}:", b);
                    Body(n + 1, b.Body("do"));
                    Line(n, "else:");
                    Body(n + 1, b.Body("else"));
                    break;
                case "repeat":
                    Line(n, $"for _ in range(int({Expr(b, "n")})):", b);
                    Body(n + 1, b.Body("do"));
                    break;
                case "while":
                    Line(n, $"while {Expr(b, "cond")}:", b);
                    Body(n + 1, b.Body("do"));
                    break;
                case "wait_until":
                    Line(n, $"while not {Expr(b, "cond", nested: true)}:", b);
                    Line(n + 1, "pass");
                    break;
                case "set_var": Line(n, $"{Var(b)} = {Expr(b, "value")}", b); break;
                case "change_var": Line(n, $"{Var(b)} += {Expr(b, "value")}", b); break;
                case "print": Line(n, $"print({Expr(b, "value")}{End(b)})", b); break;
                case "print_text": Line(n, $"print({Quote(b.Field("text"))}{End(b)})", b); break;
                case "tone": Line(n, $"tono(pwm{Pin(b)}, {Expr(b, "hz")}, {Expr(b, "ms")})", b); break;
                case "no_tone": Line(n, $"pwm{Pin(b)}.duty_u16(0)", b); break;
                case "servo": Line(n, $"mover_servo(servo{Pin(b)}, {Expr(b, "angle")})", b); break;
                default: throw new BlockException($"El bloque «{b.Op}» no sabe generar código.", b);
            }
        }

        private static string End(Block b) => b.Field("nl") == "print" ? ", end=\"\"" : "";

        private string Expr(Block parent, string input, bool nested = false)
        {
            if (!parent.Inputs.TryGetValue(input, out var b)) throw new BlockException("Hay un hueco vacío.", parent);
            return Expr(b, nested);
        }

        private string Expr(Block b, bool nested)
        {
            if (b.Def.IsStatement) throw new BlockException("En un hueco solo caben bloques de valor o de condición.", b);
            string Wrap(string s) => nested ? $"({s})" : s;
            return b.Op switch
            {
                "num" => NumberCode(b),
                "var" => Var(b),
                "level" => b.Field("level") == "HIGH" ? "1" : "0",
                "digital_read" => $"pin{Pin(b)}.value()",
                "analog_read" => $"leer_analogico(adc{AnalogPin(b)})",
                "millis" => "time.ticks_ms()",
                "arith" => Wrap($"{Expr(b, "a", true)} {(b.Field("op") == "/" ? "//" : b.Field("op"))} {Expr(b, "b", true)}"),
                "compare" => Wrap($"{Expr(b, "a", true)} {b.Field("op")} {Expr(b, "b", true)}"),
                "logic" => Wrap($"{Expr(b, "a", true)} {(b.Field("op") == "&&" ? "and" : "or")} {Expr(b, "b", true)}"),
                "not" => Wrap($"not {Expr(b, "a", true)}"),
                "map" => $"convertir({Expr(b, "v")}, {Expr(b, "fromLo")}, {Expr(b, "fromHi")}, {Expr(b, "toLo")}, {Expr(b, "toHi")})",
                "random" => $"random.randint({Expr(b, "min")}, {Expr(b, "max")})",
                "constrain" => $"min(max({Expr(b, "v")}, {Expr(b, "lo")}), {Expr(b, "hi")})",
                _ => throw new BlockException($"El bloque «{b.Op}» no sabe generar código.", b),
            };
        }

        private static string Var(Block b)
        {
            string name = VariableName(b.Field("var"), b);
            if (PyReserved.Contains(name)) throw new BlockException($"«{name}» es una palabra reservada de Python: elige otro nombre.", b);
            return name;
        }

        /// <summary>GPIO2 → 2, GP25 → 25. También acepta el número solo.</summary>
        private int Pin(Block b)
        {
            string name = b.Field("pin").Trim().ToUpperInvariant();
            for (int i = 0; i < board.PinCount; i++)
                if (board.PinName(i).Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int k) && k < board.PinCount) return k;
            throw new BlockException($"La placa {board.Name} no tiene el pin {b.Field("pin")}. Elige uno de la lista.", b);
        }

        private int AnalogPin(Block b)
        {
            int pin = Pin(b);
            if (!board.IsAnalog(pin)) throw new BlockException($"{board.PinName(pin)} no tiene entrada analógica (ADC) en {board.Name}.", b);
            return pin;
        }
    }
}
