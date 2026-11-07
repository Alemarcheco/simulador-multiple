using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulador.Arduino.Runtime.Blocks;

/// <summary>
/// Un bloque del editor visual. Es genérico: su <see cref="Op"/> dice qué es (ver <see cref="BlockCatalog"/>), los
/// <see cref="Fields"/> guardan los valores escritos o elegidos (pin, número, nombre de variable…), los
/// <see cref="Inputs"/> los huecos donde va otro bloque de valor y <see cref="Bodies"/> las listas de bloques de dentro
/// (el «entonces» de un si, el cuerpo de un repetir…).
/// </summary>
public sealed class Block
{
    public Block() { }

    public Block(string op) => Op = op;

    public string Op { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = [];
    public Dictionary<string, Block> Inputs { get; set; } = [];
    public Dictionary<string, List<Block>> Bodies { get; set; } = [];

    [JsonIgnore]
    public BlockDef Def => BlockCatalog.Get(Op);

    public string Field(string name) => Fields.TryGetValue(name, out var v) ? v : Def.Field(name)?.Default ?? "";

    public List<Block> Body(string name)
    {
        if (!Bodies.TryGetValue(name, out var list)) Bodies[name] = list = [];
        return list;
    }

    /// <summary>Bloque nuevo con sus valores por defecto: cada hueco lleva un número (o lo que diga el catálogo).</summary>
    public static Block Create(string op)
    {
        var def = BlockCatalog.Get(op);
        var b = new Block(op);
        foreach (var f in def.Fields) b.Fields[f.Name] = f.Default;
        foreach (var i in def.Inputs) b.Inputs[i.Name] = Literal(i.Default);
        foreach (var body in def.Bodies) b.Bodies[body.Name] = [];
        return b;
    }

    public static Block Literal(string number) => new("num") { Fields = { ["n"] = number } };

    public Block Clone()
    {
        var c = new Block(Op) { Fields = new(Fields) };
        foreach (var (k, v) in Inputs) c.Inputs[k] = v.Clone();
        foreach (var (k, v) in Bodies) c.Bodies[k] = v.Select(x => x.Clone()).ToList();
        return c;
    }

    /// <summary>Este bloque y todos los que lleva dentro.</summary>
    public IEnumerable<Block> Walk()
    {
        yield return this;
        foreach (var i in Inputs.Values)
            foreach (var x in i.Walk()) yield return x;
        foreach (var body in Bodies.Values)
            foreach (var s in body)
                foreach (var x in s.Walk()) yield return x;
    }
}

/// <summary>Programa de bloques: lo que se hace una vez al encender y lo que se repite siempre.</summary>
public sealed class BlockProgram
{
    public List<Block> Setup { get; set; } = [];
    public List<Block> Loop { get; set; } = [];

    public IEnumerable<Block> Walk() => Setup.Concat(Loop).SelectMany(b => b.Walk());

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BlockProgram FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<BlockProgram>(json) ?? throw new BlockException("El archivo está vacío.", null);
        foreach (var b in p.Walk())
            if (!BlockCatalog.Exists(b.Op)) throw new BlockException($"Bloque desconocido «{b.Op}».", null);
        return p;
    }
}

public sealed class BlockException(string message, Block? block) : Exception(message)
{
    public Block? Block { get; } = block;
}
