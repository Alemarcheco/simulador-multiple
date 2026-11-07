namespace Simulador.Arduino.Runtime.Blocks;

/// <summary>Ejemplo de bloques con su montaje. <see cref="Build"/> da una copia nueva cada vez.</summary>
public sealed record BlockExample(string Name, string Description, Func<BlockProgram> Build, IReadOnlyList<PartSpec> Parts);

/// <summary>
/// Programas de bloques de ejemplo, de más fácil a más difícil. Están escritos para Uno, Nano y Mega; con
/// <see cref="For"/> se pasan a los pines equivalentes del ESP32 o de la Pico (y entonces generan MicroPython).
/// </summary>
public static partial class BlockExamples
{
    /// <summary>Pin del Arduino → pin equivalente: LED integrado, pulsador, PWM, semáforo, zumbador y ADC.</summary>
    private static readonly Dictionary<string, int> Esp32Pins = new()
    {
        ["D13"] = 2, ["D2"] = 4, ["D9"] = 18, ["D10"] = 25, ["D11"] = 26, ["D12"] = 27, ["D8"] = 19, ["A0"] = 34,
    };

    private static readonly Dictionary<string, int> PicoPins = new()
    {
        ["D13"] = 25, ["D2"] = 14, ["D9"] = 15, ["D10"] = 13, ["D11"] = 12, ["D12"] = 11, ["D8"] = 18, ["A0"] = 26,
    };

    /// <summary>Pin equivalente en la placa a uno del Arduino (D13 → GPIO2 en el ESP32, GP25 en la Pico).</summary>
    public static string PinFor(ArduinoBoard board, string arduinoPin)
    {
        var map = board == ArduinoBoard.Esp32 ? Esp32Pins : board == ArduinoBoard.Pico ? PicoPins : null;
        return map is not null && map.TryGetValue(arduinoPin, out int n) ? board.PinName(n) : arduinoPin;
    }

    /// <summary>Ejemplos con los pines de la placa: los mismos de <see cref="All"/> en las placas Arduino.</summary>
    public static IReadOnlyList<BlockExample> For(ArduinoBoard board)
    {
        var map = board == ArduinoBoard.Esp32 ? Esp32Pins : board == ArduinoBoard.Pico ? PicoPins : null;
        if (map is null) return All;
        string Pin(string arduino) => map.TryGetValue(arduino, out int n) ? board.PinName(n) : arduino;
        return All.Select(e => new BlockExample(
            e.Name,
            PinNumbers().Replace(e.Description, m => Pin("D" + m.Value)).Replace(" A0", " " + Pin("A0")),
            () =>
            {
                var p = e.Build();
                foreach (var b in p.Walk())
                {
                    if (b.Fields.TryGetValue("pin", out var pin)) b.Fields["pin"] = Pin(pin);
                    if (b.Op == "print_text") b.Fields["text"] = b.Field("text").Replace("A0", Pin("A0"));
                    // LM35: 1023 son 5 V (500 °C) en Arduino, pero 3,3 V (330 °C) en el ESP32 y la Pico.
                    if (b.Op == "map" && b.Inputs.TryGetValue("toHi", out var hi) && hi.Op == "num" && hi.Field("n") == "500")
                        hi.Fields["n"] = ((int)Math.Round(board.Vcc * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return p;
            },
            e.Parts.Select(s => s with { Pin = Pin(s.Pin) }).ToList())).ToList();
    }

    // Números de pin en las descripciones: «el pin 13», «los pines 12, 11 y 10».
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<=\bpin(?:es)? (?:\d+(?:, | y ))*)\d+")]
    private static partial System.Text.RegularExpressions.Regex PinNumbers();

    /// <summary>
    /// Crea un bloque. Cada argumento es (nombre, valor): un <c>string</c> va a un campo, un <c>int</c> o un
    /// <see cref="Block"/> a un hueco y un <c>Block[]</c> a una lista de dentro.
    /// </summary>
    public static Block B(string op, params (string Key, object Value)[] args)
    {
        var b = Block.Create(op);
        foreach (var (k, v) in args)
        {
            switch (v)
            {
                case Block x: b.Inputs[k] = x; break;
                case Block[] list: b.Bodies[k] = [.. list]; break;
                case int i: b.Inputs[k] = Block.Literal(i.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case string s: b.Fields[k] = s; break;
                default: throw new ArgumentException($"Valor no válido para {k}.");
            }
        }
        return b;
    }

    private static BlockProgram P(Block[] setup, Block[] loop) => new() { Setup = [.. setup], Loop = [.. loop] };

    private static Block Mode(string pin, string mode = "OUTPUT") => B("pin_mode", ("pin", pin), ("mode", mode));
    private static Block Write(string pin, string level) => B("digital_write", ("pin", pin), ("level", level));
    private static Block Wait(int ms) => B("delay", ("ms", ms));
    private static Block Var(string name) => B("var", ("var", name));
    private static Block Read(string pin) => B("digital_read", ("pin", pin));
    private static Block Analog(string pin) => B("analog_read", ("pin", pin));
    private static Block Level(string level) => B("level", ("level", level));
    private static Block Cmp(Block a, string op, object b) => B("compare", ("a", a), ("op", op), ("b", b));
    private static Block Set(string name, object value) => B("set_var", ("var", name), ("value", value));
    private static Block Add(string name, int by) => B("change_var", ("var", name), ("value", by));
    private static Block Print(object value, bool newLine = true) => B("print", ("value", value), ("nl", newLine ? "println" : "print"));
    private static Block Text(string text, bool newLine = true) => B("print_text", ("text", text), ("nl", newLine ? "println" : "print"));

    private static Block Map(Block v, int fromHi, int toHi) =>
        B("map", ("v", v), ("fromLo", 0), ("fromHi", fromHi), ("toLo", 0), ("toHi", toHi));

    public static IReadOnlyList<BlockExample> All { get; } =
    [
        new("Parpadeo", "El LED del pin 13 se enciende y se apaga cada segundo.", () => P(
            [Mode("D13")],
            [Write("D13", "HIGH"), Wait(1000), Write("D13", "LOW"), Wait(1000)]),
            [new(PartKind.Led, "D13")]),

        new("Pulsador y LED", "Mientras se pulsa el botón del pin 2 se enciende el LED del pin 13. El pulsador va a GND, por eso se usa pull-up.", () => P(
            [Mode("D2", "INPUT_PULLUP"), Mode("D13")],
            [B("if_else", ("cond", Cmp(Read("D2"), "==", Level("LOW"))),
                ("do", new[] { Write("D13", "HIGH") }),
                ("else", new[] { Write("D13", "LOW") }))]),
            [new(PartKind.Button, "D2"), new(PartKind.Led, "D13")]),

        new("Semáforo", "Verde, ámbar y rojo en los pines 12, 11 y 10, cada uno con su tiempo.", () => P(
            [Mode("D10"), Mode("D11"), Mode("D12")],
            [
                Write("D12", "HIGH"), Wait(3000), Write("D12", "LOW"),
                Write("D11", "HIGH"), Wait(1000), Write("D11", "LOW"),
                Write("D10", "HIGH"), Wait(3000), Write("D10", "LOW"),
            ]),
            [new(PartKind.Led, "D10", "#FF3B30", "Rojo"), new(PartKind.Led, "D11", "#FFB000", "Ámbar"), new(PartKind.Led, "D12", "#34C759", "Verde")]),

        new("Potenciómetro regula un LED", "Lee el potenciómetro (0-1023), lo pasa a 0-255 y regula el brillo del LED del pin 9.", () => P(
            [Mode("D9")],
            [
                Set("lectura", Analog("A0")),
                B("analog_write", ("pin", "D9"), ("value", Map(Var("lectura"), 1023, 255))),
                Text("A0 = ", false), Print(Var("lectura")),
                Wait(200),
            ]),
            [new(PartKind.Potentiometer, "A0"), new(PartKind.Led, "D9")]),

        new("Contador de pulsaciones", "Cuenta cuántas veces se pulsa el botón y lo muestra en el monitor serie. Espera a que se suelte para no contar dos veces.", () => P(
            [Mode("D2", "INPUT_PULLUP"), Text("Pulsa el botón")],
            [B("if", ("cond", Cmp(Read("D2"), "==", Level("LOW"))), ("do", new[]
            {
                Add("pulsaciones", 1),
                Text("Pulsaciones: ", false), Print(Var("pulsaciones")),
                B("wait_until", ("cond", Cmp(Read("D2"), "==", Level("HIGH")))),
                Wait(50),
            }))]),
            [new(PartKind.Button, "D2")]),

        new("Servo de barrido", "El servo del pin 9 va de 0° a 180° y vuelve, de 2 en 2 grados.", () => P(
            [Set("angulo", 0)],
            [
                B("repeat", ("n", 90), ("do", new[] { Add("angulo", 2), B("servo", ("pin", "D9"), ("angle", Var("angulo"))), Wait(15) })),
                B("repeat", ("n", 90), ("do", new[] { Add("angulo", -2), B("servo", ("pin", "D9"), ("angle", Var("angulo"))), Wait(15) })),
            ]),
            [new(PartKind.Servo, "D9")]),

        new("Servo con potenciómetro", "El brazo del servo sigue al mando del potenciómetro.", () => P(
            [],
            [B("servo", ("pin", "D9"), ("angle", Map(Analog("A0"), 1023, 180))), Wait(20)]),
            [new(PartKind.Potentiometer, "A0"), new(PartKind.Servo, "D9")]),

        new("Luz automática", "Si el sensor de luz (LDR) ve poca luz, enciende la lámpara del pin 13.", () => P(
            [Mode("D13")],
            [
                Set("luz", Analog("A0")),
                B("if_else", ("cond", Cmp(Var("luz"), "<", 300)),
                    ("do", new[] { Write("D13", "HIGH") }),
                    ("else", new[] { Write("D13", "LOW") })),
                Wait(100),
            ]),
            [new(PartKind.Ldr, "A0"), new(PartKind.Led, "D13", "#FFD60A", "Lámpara")]),

        new("Dado electrónico", "Al pulsar el botón sale un número del 1 al 6 en el monitor serie.", () => P(
            [Mode("D2", "INPUT_PULLUP")],
            [B("if", ("cond", Cmp(Read("D2"), "==", Level("LOW"))), ("do", new[]
            {
                Set("dado", B("random", ("min", 1), ("max", 6))),
                Text("Ha salido un ", false), Print(Var("dado")),
                B("wait_until", ("cond", Cmp(Read("D2"), "==", Level("HIGH")))),
            }))]),
            [new(PartKind.Button, "D2")]),

        new("Alarma de temperatura", "Lee el LM35 (10 mV por grado). Si pasa de 30 °C pita el zumbador del pin 8.", () => P(
            [],
            [
                Set("grados", Map(Analog("A0"), 1023, 500)),
                Text("Temperatura: ", false), Print(Var("grados")),
                B("if", ("cond", Cmp(Var("grados"), ">", 30)), ("do", new[]
                {
                    Text("¡Demasiado calor!"),
                    B("tone", ("pin", "D8"), ("hz", 880), ("ms", 300)),
                })),
                Wait(500),
            ]),
            [new(PartKind.Lm35, "A0"), new(PartKind.Buzzer, "D8")]),
    ];
}
