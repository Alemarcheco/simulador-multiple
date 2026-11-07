namespace Simulador.Arduino.Runtime.Blocks;

public enum BlockShape
{
    /// <summary>Instrucción: se encaja en una lista (setup, loop o el cuerpo de otro bloque).</summary>
    Statement,
    /// <summary>Valor numérico: va dentro de un hueco.</summary>
    Value,
    /// <summary>Condición (verdadero/falso): va en un hueco, se dibuja con puntas.</summary>
    Condition,
}

public enum FieldKind { Pin, AnalogPin, Choice, Number, Text, Variable }

/// <summary>Opción de un desplegable: lo que se ve y lo que se escribe en el código.</summary>
public sealed record Choice(string Label, string Code);

public sealed record FieldDef(string Name, FieldKind Kind, string Default, IReadOnlyList<Choice>? Options = null);

public sealed record InputDef(string Name, string Default = "0");

/// <summary>Lista de bloques de dentro, con el texto que se ve encima («si no», por ejemplo).</summary>
public sealed record BodyDef(string Name, string Label = "");

public sealed record BlockCategory(string Name, string Color);

/// <summary>
/// Definición de un tipo de bloque. <see cref="Template"/> es el texto que se ve, con <c>{nombre}</c> donde va un
/// campo o un hueco.
/// </summary>
public sealed record BlockDef(
    string Op,
    BlockCategory Category,
    BlockShape Shape,
    string Template,
    IReadOnlyList<FieldDef> Fields,
    IReadOnlyList<InputDef> Inputs,
    IReadOnlyList<BodyDef> Bodies,
    string Help)
{
    public FieldDef? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
    public bool IsStatement => Shape == BlockShape.Statement;

    /// <summary>Trozos de la plantilla: texto suelto o el nombre de un campo/hueco.</summary>
    public IEnumerable<(string Text, bool IsSlot)> Parts()
    {
        int i = 0;
        while (i < Template.Length)
        {
            int open = Template.IndexOf('{', i);
            if (open < 0) { yield return (Template[i..], false); yield break; }
            if (open > i) yield return (Template[i..open], false);
            int close = Template.IndexOf('}', open);
            yield return (Template[(open + 1)..close], true);
            i = close + 1;
        }
    }
}

/// <summary>Todos los bloques disponibles, agrupados por categoría y con colores propios.</summary>
public static class BlockCatalog
{
    public static readonly BlockCategory Pins = new("Pines", "#3D7DD8");
    public static readonly BlockCategory Control = new("Control", "#E09A12");
    public static readonly BlockCategory Operators = new("Operadores", "#3E9E4F");
    public static readonly BlockCategory Variables = new("Variables", "#D9622B");
    public static readonly BlockCategory Serial = new("Monitor serie", "#7A55C7");
    public static readonly BlockCategory Actuators = new("Sonido y servo", "#B5468E");

    public static IReadOnlyList<BlockCategory> Categories { get; } = [Pins, Control, Operators, Variables, Serial, Actuators];

    private static readonly Choice[] Modes = [new("salida", "OUTPUT"), new("entrada", "INPUT"), new("entrada con pull-up", "INPUT_PULLUP")];
    private static readonly Choice[] Levels = [new("ALTO", "HIGH"), new("BAJO", "LOW")];
    private static readonly Choice[] Arith = [new("+", "+"), new("−", "-"), new("×", "*"), new("÷", "/"), new("resto", "%")];
    private static readonly Choice[] Compare = [new("=", "=="), new("≠", "!="), new("<", "<"), new("≤", "<="), new(">", ">"), new("≥", ">=")];
    private static readonly Choice[] Logic = [new("y", "&&"), new("o", "||")];
    private static readonly Choice[] NewLine = [new("y salta de línea", "println"), new("en la misma línea", "print")];

    private static FieldDef Pin(string def = "D13") => new("pin", FieldKind.Pin, def);
    private static FieldDef Var(string def = "contador") => new("var", FieldKind.Variable, def);
    private static FieldDef Pick(string name, Choice[] options, int def = 0) => new(name, FieldKind.Choice, options[def].Code, options);

    private static BlockDef S(string op, BlockCategory c, string t, string help, FieldDef[]? f = null, InputDef[]? i = null, BodyDef[]? b = null)
        => new(op, c, BlockShape.Statement, t, f ?? [], i ?? [], b ?? [], help);

    private static BlockDef V(string op, BlockCategory c, string t, string help, FieldDef[]? f = null, InputDef[]? i = null, BlockShape shape = BlockShape.Value)
        => new(op, c, shape, t, f ?? [], i ?? [], [], help);

    public static IReadOnlyList<BlockDef> All { get; } =
    [
        // ---- Pines ----
        S("pin_mode", Pins, "configurar pin {pin} como {mode}", "Dice si el pin es una salida (LED, relé…) o una entrada (pulsador…).",
            [Pin(), Pick("mode", Modes)]),
        S("digital_write", Pins, "poner pin {pin} en {level}", "Enciende (ALTO = 5 V) o apaga (BAJO = 0 V) una salida.",
            [Pin(), Pick("level", Levels)]),
        S("analog_write", Pins, "PWM en pin {pin} a {value}", "Salida «analógica» de 0 a 255: regula el brillo de un LED o la velocidad de un motor. Solo en pines con ~.",
            [Pin("D9")], [new("value", "128")]),
        V("digital_read", Pins, "leer pin digital {pin}", "Da 1 si el pin recibe 5 V y 0 si recibe 0 V.", [Pin("D2")]),
        V("analog_read", Pins, "leer pin analógico {pin}", "Da un número de 0 (0 V) a 1023 (5 V).", [new("pin", FieldKind.AnalogPin, "A0")]),
        V("level", Pins, "{level}", "ALTO (1) o BAJO (0), para comparar con lo que se lee de un pin.", [Pick("level", Levels)]),

        // ---- Control ----
        S("delay", Control, "esperar {ms} ms", "Detiene el programa ese tiempo (1000 ms = 1 segundo).", i: [new("ms", "1000")]),
        S("if", Control, "si {cond} entonces", "Hace lo de dentro solo si se cumple la condición.", i: [new("cond", "1")], b: [new("do")]),
        S("if_else", Control, "si {cond} entonces", "Si se cumple la condición hace lo primero; si no, lo segundo.",
            i: [new("cond", "1")], b: [new("do"), new("else", "si no")]),
        S("repeat", Control, "repetir {n} veces", "Repite lo de dentro ese número de veces.", i: [new("n", "10")], b: [new("do")]),
        S("while", Control, "mientras {cond}", "Repite lo de dentro mientras se cumpla la condición.", i: [new("cond", "1")], b: [new("do")]),
        S("wait_until", Control, "esperar hasta que {cond}", "Se queda parado hasta que se cumpla la condición (por ejemplo, soltar un pulsador).",
            i: [new("cond", "1")]),
        V("millis", Control, "milisegundos desde el inicio", "Tiempo que lleva funcionando la placa, en ms."),

        // ---- Operadores ----
        V("num", Operators, "{n}", "Un número.", [new("n", FieldKind.Number, "0")]),
        V("arith", Operators, "{a} {op} {b}", "Cuenta con dos números. El ÷ entre enteros quita los decimales.",
            [Pick("op", Arith)], [new("a"), new("b", "1")]),
        V("compare", Operators, "{a} {op} {b}", "Compara dos números: da verdadero o falso.",
            [Pick("op", Compare)], [new("a"), new("b")], BlockShape.Condition),
        V("logic", Operators, "{a} {op} {b}", "«y»: las dos condiciones a la vez. «o»: basta con una.",
            [Pick("op", Logic)], [new("a", "1"), new("b", "1")], BlockShape.Condition),
        V("not", Operators, "no {a}", "Da lo contrario de la condición.", i: [new("a", "0")], shape: BlockShape.Condition),
        V("map", Operators, "convertir {v} de {fromLo}…{fromHi} a {toLo}…{toHi}", "Pasa un valor de una escala a otra (por ejemplo, 0…1023 a 0…255).",
            i: [new("v"), new("fromLo", "0"), new("fromHi", "1023"), new("toLo", "0"), new("toHi", "255")]),
        V("random", Operators, "número al azar entre {min} y {max}", "Un número entero al azar, ambos incluidos.",
            i: [new("min", "1"), new("max", "6")]),
        V("constrain", Operators, "limitar {v} entre {lo} y {hi}", "Si el valor se sale del rango, lo deja en el borde.",
            i: [new("v"), new("lo", "0"), new("hi", "255")]),

        // ---- Variables ----
        S("set_var", Variables, "fijar {var} a {value}", "Guarda un número en la variable. Se crea sola la primera vez que se usa.",
            [Var()], [new("value", "0")]),
        S("change_var", Variables, "sumar a {var} {value}", "Suma (o resta, con un número negativo) a la variable.",
            [Var()], [new("value", "1")]),
        V("var", Variables, "{var}", "El valor guardado en la variable.", [Var()]),

        // ---- Monitor serie ----
        S("print", Serial, "mostrar {value} {nl}", "Escribe un número en el monitor serie.",
            [Pick("nl", NewLine)], [new("value", "0")]),
        S("print_text", Serial, "mostrar texto {text} {nl}", "Escribe un texto en el monitor serie.",
            [new("text", FieldKind.Text, "Hola"), Pick("nl", NewLine)]),

        // ---- Sonido y servo ----
        S("tone", Actuators, "tocar en pin {pin} {hz} Hz durante {ms} ms", "Hace sonar un zumbador. 440 Hz es la nota La.",
            [Pin("D8")], [new("hz", "440"), new("ms", "200")]),
        S("no_tone", Actuators, "silenciar pin {pin}", "Para el sonido del zumbador.", [Pin("D8")]),
        S("servo", Actuators, "mover servo del pin {pin} a {angle} grados", "Gira el brazo del servo a un ángulo de 0 a 180°.",
            [Pin("D9")], [new("angle", "90")]),
    ];

    private static readonly Dictionary<string, BlockDef> ByOp = All.ToDictionary(d => d.Op);

    public static bool Exists(string op) => ByOp.ContainsKey(op);

    public static BlockDef Get(string op) => ByOp.TryGetValue(op, out var d) ? d : throw new BlockException($"Bloque desconocido «{op}».", null);

    public static IEnumerable<BlockDef> In(BlockCategory c) => All.Where(d => d.Category == c);
}
