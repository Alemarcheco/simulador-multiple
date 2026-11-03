using System.Text.Json.Serialization;

namespace Simulador.Plc.Runtime.Fbd;

// Diagrama de bloques de función (FBD / FUP / LOGO). Cada red es un árbol: el bloque de la derecha es la raíz y sus
// entradas son operandos u otros bloques. La salida de la raíz se asigna a una o varias variables (=, S, R).
// Las clases son mutables para que el editor gráfico las modifique directamente.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "node")]
[JsonDerivedType(typeof(FbdOperand), "operand")]
[JsonDerivedType(typeof(FbdGate), "gate")]
[JsonDerivedType(typeof(FbdInstance), "fb")]
public abstract class FbdNode
{
    /// <summary>Variable oculta donde el compilador deja el valor del nodo, para verlo en vivo.</summary>
    [JsonIgnore]
    public string? RuntimeName { get; internal set; }
}

/// <summary>Entrada directa: variable, dirección (I0.0, X0…), salida de bloque (T1.Q) o constante.</summary>
public sealed class FbdOperand : FbdNode
{
    public FbdOperand() { }
    public FbdOperand(string text, bool negated = false)
    {
        Text = text;
        Negated = negated;
    }

    public string Text { get; set; } = "";
    /// <summary>Círculo de negación en la entrada.</summary>
    public bool Negated { get; set; }
}

/// <summary>Tipos de bloque sin memoria.</summary>
public enum GateKind
{
    And, Or, Xor, Not,
    Gt, Ge, Eq, Ne, Lt, Le,
    Add, Sub, Mul, Div,
    Max, Min, Sel, Move,
}

/// <summary>Bloque lógico, de comparación o aritmético.</summary>
public sealed class FbdGate : FbdNode
{
    public FbdGate() { }
    public FbdGate(GateKind kind, params FbdNode[] inputs)
    {
        Kind = kind;
        Inputs.AddRange(inputs);
    }

    public GateKind Kind { get; set; }
    public List<FbdNode> Inputs { get; set; } = [];
    /// <summary>Negación de la salida.</summary>
    public bool Negated { get; set; }

    public static string Symbol(GateKind k) => k switch
    {
        GateKind.And => "&",
        GateKind.Or => "≥1",
        GateKind.Xor => "=1",
        GateKind.Not => "NOT",
        GateKind.Gt => ">",
        GateKind.Ge => "≥",
        GateKind.Eq => "=",
        GateKind.Ne => "≠",
        GateKind.Lt => "<",
        GateKind.Le => "≤",
        GateKind.Add => "ADD",
        GateKind.Sub => "SUB",
        GateKind.Mul => "MUL",
        GateKind.Div => "DIV",
        GateKind.Max => "MAX",
        GateKind.Min => "MIN",
        GateKind.Sel => "SEL",
        _ => "MOVE",
    };

    /// <summary>Número de entradas fijo, o null si admite varias (AND, OR, ADD, MAX…).</summary>
    public static int? FixedInputs(GateKind k) => k switch
    {
        GateKind.Not or GateKind.Move => 1,
        GateKind.Sel => 3,
        GateKind.Gt or GateKind.Ge or GateKind.Eq or GateKind.Ne or GateKind.Lt or GateKind.Le or GateKind.Sub or GateKind.Div => 2,
        _ => null,
    };

    public static bool IsBoolean(GateKind k) => k <= GateKind.Le;
}

/// <summary>Bloque con memoria (temporizador, contador, biestable, flanco). Su valor en la red es la salida elegida.</summary>
public sealed class FbdInstance : FbdNode
{
    public FbdInstance() { }
    public FbdInstance(string type, string instance, string output = "Q")
    {
        Type = type;
        Instance = instance;
        Output = output;
    }

    /// <summary>TON, TOF, TP, CTU, CTD, SR, RS, R_TRIG, F_TRIG.</summary>
    public string Type { get; set; } = "TON";
    public string Instance { get; set; } = "T1";
    /// <summary>Salida que sigue la red: Q, ET, CV, Q1…</summary>
    public string Output { get; set; } = "Q";
    public Dictionary<string, FbdNode> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum FbdAssignKind { Assign, Set, Reset }

public sealed class FbdAssignment
{
    public FbdAssignment() { }
    public FbdAssignment(string target, FbdAssignKind kind = FbdAssignKind.Assign)
    {
        Target = target;
        Kind = kind;
    }

    public string Target { get; set; } = "";
    public FbdAssignKind Kind { get; set; }
}

public sealed class FbdNetwork
{
    public FbdNode? Root { get; set; }
    public List<FbdAssignment> Outputs { get; set; } = [];
    public string? Comment { get; set; }
}

public sealed class FbdProgram
{
    public int Version { get; set; } = 1;
    public string? Profile { get; set; }
    public List<FbdNetwork> Networks { get; set; } = [];

    /// <summary>Todos los nodos del programa, recorriendo cada árbol.</summary>
    public IEnumerable<FbdNode> Nodes() => Networks.Where(n => n.Root is not null).SelectMany(n => Walk(n.Root!));

    public static IEnumerable<FbdNode> Walk(FbdNode node)
    {
        yield return node;
        var children = node switch
        {
            FbdGate g => g.Inputs,
            FbdInstance fb => fb.Inputs.Values.ToList(),
            _ => [],
        };
        foreach (var c in children)
            foreach (var d in Walk(c)) yield return d;
    }

    /// <summary>Cambia la nomenclatura de operandos y destinos (por ejemplo al cambiar de marca de PLC).</summary>
    public void TranslateOperands(Func<string, string> map)
    {
        foreach (var o in Nodes().OfType<FbdOperand>()) o.Text = map(o.Text);
        foreach (var a in Networks.SelectMany(n => n.Outputs)) a.Target = map(a.Target);
    }

    /// <summary>Pines de entrada de cada bloque con memoria, en el orden en que se dibujan.</summary>
    public static IReadOnlyList<string> InputPins(string type) => type.ToUpperInvariant() switch
    {
        "TON" or "TOF" or "TP" => ["IN", "PT"],
        "CTU" => ["CU", "R", "PV"],
        "CTD" => ["CD", "LD", "PV"],
        "SR" => ["S1", "R"],
        "RS" => ["S", "R1"],
        "R_TRIG" or "F_TRIG" => ["CLK"],
        _ => [],
    };

    public static IReadOnlyList<string> OutputPins(string type) => type.ToUpperInvariant() switch
    {
        "TON" or "TOF" or "TP" => ["Q", "ET"],
        "CTU" or "CTD" => ["Q", "CV"],
        "SR" or "RS" => ["Q1"],
        _ => ["Q"],
    };

    public static readonly string[] BlockTypes = ["TON", "TOF", "TP", "CTU", "CTD", "SR", "RS", "R_TRIG", "F_TRIG"];
}
