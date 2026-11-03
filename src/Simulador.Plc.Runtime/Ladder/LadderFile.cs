using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulador.Plc.Runtime.Ladder;

/// <summary>
/// Guarda y abre programas Ladder en JSON (extensión .ladder.json). El formato es propio y estable; PLCopen XML se
/// añadirá como formato de intercambio.
/// </summary>
public static class LadderFile
{
    public const int CurrentVersion = 1;
    public const string Extension = ".ladder.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record Document(LadderProgram Program, string? ProfileId);

    public static string Save(LadderProgram program, string? profileId = null)
    {
        var dto = new FileDto
        {
            Version = CurrentVersion,
            Profile = profileId,
            Rungs = program.Rungs.Select(r => new RungDto
            {
                Comment = r.Comment,
                Condition = r.Condition is null ? null : ToDto(r.Condition),
                Outputs = r.Outputs.Select(ToDto).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Lee un archivo. Lanza <see cref="FormatException"/> con un mensaje claro si no es válido.</summary>
    public static Document Load(string json)
    {
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"El archivo no es un programa Ladder válido: {ex.Message}", ex);
        }
        if (dto is null) throw new FormatException("El archivo está vacío.");
        if (dto.Version > CurrentVersion)
            throw new FormatException($"El archivo es de una versión más nueva ({dto.Version}) del simulador.");
        var rungs = (dto.Rungs ?? []).Select(r => new Rung(
            r.Condition is null ? null : FromDto(r.Condition),
            (r.Outputs ?? []).Select(FromDto).ToList(),
            r.Comment)).ToList();
        return new Document(new LadderProgram([], rungs), dto.Profile);
    }

    private static NodeDto ToDto(LadderNode n) => n switch
    {
        Contact c => new NodeDto { Type = "contact", Kind = c.Kind.ToString(), Operand = c.Operand },
        CompareContact c => new NodeDto { Type = "compare", Left = c.Left, Op = c.Op, Right = c.Right },
        Series s => new NodeDto { Type = "series", Items = s.Items.Select(ToDto).ToList() },
        ParallelBranch p => new NodeDto { Type = "parallel", Items = p.Branches.Select(ToDto).ToList() },
        _ => throw new InvalidOperationException(),
    };

    private static LadderNode FromDto(NodeDto d) => d.Type switch
    {
        "contact" => new Contact(ParseEnum<ContactKind>(d.Kind), Required(d.Operand, "operand")),
        "compare" => new CompareContact(Required(d.Left, "left"), Required(d.Op, "op"), Required(d.Right, "right")),
        "series" => new Series((d.Items ?? []).Select(FromDto).ToList()),
        "parallel" => new ParallelBranch((d.Items ?? []).Select(FromDto).ToList()),
        _ => throw new FormatException($"Elemento desconocido '{d.Type}'."),
    };

    private static OutputDto ToDto(LadderOutput o) => o switch
    {
        Coil c => new OutputDto { Type = "coil", Kind = c.Kind.ToString(), Operand = c.Operand },
        TimerBox t => new OutputDto { Type = "timer", Kind = t.Kind.ToString(), Instance = t.Instance, PresetMs = t.Preset.TotalMilliseconds },
        CounterBox k => new OutputDto { Type = "counter", Kind = k.Kind.ToString(), Instance = k.Instance, Preset = k.Preset, Reset = k.ResetOperand },
        MoveBox m => new OutputDto { Type = "move", Source = m.Source, Destination = m.Destination },
        _ => throw new InvalidOperationException(),
    };

    private static LadderOutput FromDto(OutputDto d) => d.Type switch
    {
        "coil" => new Coil(ParseEnum<CoilKind>(d.Kind), Required(d.Operand, "operand")),
        "timer" => new TimerBox(ParseEnum<TimerKind>(d.Kind), Required(d.Instance, "instance"), TimeSpan.FromMilliseconds(d.PresetMs ?? 0)),
        "counter" => new CounterBox(ParseEnum<CounterKind>(d.Kind), Required(d.Instance, "instance"), d.Preset ?? 0, d.Reset),
        "move" => new MoveBox(Required(d.Source, "source"), Required(d.Destination, "destination")),
        _ => throw new FormatException($"Salida desconocida '{d.Type}'."),
    };

    private static T ParseEnum<T>(string? s) where T : struct, Enum =>
        Enum.TryParse<T>(s, ignoreCase: true, out var v) ? v : throw new FormatException($"Valor '{s}' no válido para {typeof(T).Name}.");

    private static string Required(string? s, string name) =>
        s ?? throw new FormatException($"Falta el campo '{name}'.");

    private sealed class FileDto
    {
        public int Version { get; set; }
        public string? Profile { get; set; }
        public List<RungDto>? Rungs { get; set; }
    }

    private sealed class RungDto
    {
        public string? Comment { get; set; }
        public NodeDto? Condition { get; set; }
        public List<OutputDto>? Outputs { get; set; }
    }

    private sealed class NodeDto
    {
        public string Type { get; set; } = "";
        public string? Kind { get; set; }
        public string? Operand { get; set; }
        public string? Left { get; set; }
        public string? Op { get; set; }
        public string? Right { get; set; }
        public List<NodeDto>? Items { get; set; }
    }

    private sealed class OutputDto
    {
        public string Type { get; set; } = "";
        public string? Kind { get; set; }
        public string? Operand { get; set; }
        public string? Instance { get; set; }
        public double? PresetMs { get; set; }
        public long? Preset { get; set; }
        public string? Reset { get; set; }
        public string? Source { get; set; }
        public string? Destination { get; set; }
    }
}
