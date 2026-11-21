namespace Simulador.Banco.Mesa;

/// <summary>Un tipo de pieza del catálogo de la mesa: nombre, bornes, parámetros y cómo se crea para simular.</summary>
public sealed class PartType
{
    public required string TypeId { get; init; }
    public required string Name { get; init; }
    /// <summary>Grupo de la lista de piezas: "Mando y señales", "Eléctrico"…</summary>
    public required string Category { get; init; }
    /// <summary>Prefijo del identificador de las piezas nuevas: "M" → M1, M2…</summary>
    public required string Prefix { get; init; }
    public string Icon { get; init; } = "▪";
    public string Description { get; init; } = "";
    /// <summary>Módulo de origen (se muestra en la ficha): "banco", "plc", "hidraulica"…</summary>
    public string Module { get; init; } = BancoModule.ModuleId;
    public IReadOnlyList<ParamDef> Params { get; init; } = [];
    public IReadOnlyList<PortDef> FixedPorts { get; init; } = [];
    /// <summary>Bornes que dependen de los parámetros (p. ej. el PLC). Si es null se usan <see cref="FixedPorts"/>.</summary>
    public Func<BenchPart, IReadOnlyList<PortDef>>? DynamicPorts { get; init; }
    /// <summary>Tipo de la fábrica hidráulica, si la pieza es de una red de tuberías.</summary>
    public string? HydraulicType { get; init; }
    public InstrumentKind? Instrument { get; init; }
    /// <summary>Crea la pieza para simular (no se usa en piezas hidráulicas ni instrumentos).</summary>
    public Func<BenchPart, BuiltPart>? Create { get; init; }

    public IReadOnlyList<PortDef> PortsFor(BenchPart part) => DynamicPorts?.Invoke(part) ?? FixedPorts;

    public PortDef? Port(BenchPart part, string name) => PortsFor(part).FirstOrDefault(p => p.Name == name);

    /// <summary>Pieza nueva con sus parámetros por defecto.</summary>
    public BenchPart NewPart(string id, double x, double y)
    {
        var p = new BenchPart { Id = id, Type = TypeId, X = x, Y = y };
        foreach (var d in Params) p.Params[d.Key] = d.Default;
        return p;
    }

    public override string ToString() => Name;
}
