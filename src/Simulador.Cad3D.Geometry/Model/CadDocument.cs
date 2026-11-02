using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulador.Cad3D.Geometry.Model;

/// <summary>Una pieza imprimible: un sólido que se construye aplicando sus operaciones en orden.</summary>
public sealed class Part
{
    public string Name { get; set; } = "Pieza";
    /// <summary>Color de visualización y de 3MF, en formato #RRGGBB.</summary>
    public string Color { get; set; } = "#4A90D9";
    public bool Visible { get; set; } = true;
    public List<Feature> Features { get; set; } = new();

    /// <summary>Copia independiente (para evaluar en segundo plano mientras se sigue editando).</summary>
    public Part DeepClone() =>
        JsonSerializer.Deserialize<Part>(JsonSerializer.Serialize(this, CadDocument.JsonOptions), CadDocument.JsonOptions)!;
}

/// <summary>Proyecto del módulo 3D (archivo .cad3d, JSON).</summary>
public sealed class CadDocument
{
    public const string FileExtension = ".cad3d";
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Sin título";
    public List<Part> Parts { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CadDocument FromJson(string json) =>
        JsonSerializer.Deserialize<CadDocument>(json, JsonOptions) ?? throw new InvalidDataException("Archivo .cad3d vacío.");

    public void Save(string path) => File.WriteAllText(path, ToJson());
    public static CadDocument Load(string path) => FromJson(File.ReadAllText(path));
}

/// <summary>Resultado de evaluar una pieza.</summary>
public sealed record PartResult(Part Part, Mesh? Mesh, IReadOnlyList<string> Errors);

/// <summary>Reconstruye las piezas aplicando el historial de operaciones con un motor geométrico.</summary>
public sealed class DocumentEvaluator
{
    private readonly IGeometryKernel _kernel;

    public DocumentEvaluator(IGeometryKernel kernel) { _kernel = kernel; }

    public PartResult Evaluate(Part part)
    {
        var errors = new List<string>();
        ISolid? current = null;
        foreach (var f in part.Features)
        {
            if (!f.Enabled) continue;
            try
            {
                var s = f.Build(_kernel);
                current = (current, f.Mode) switch
                {
                    (null, CombineMode.Solido) => s,
                    (null, _) => null, // un hueco o intersección sin nada antes no aporta material
                    (_, CombineMode.Solido) => _kernel.Union(current, s),
                    (_, CombineMode.Hueco) => _kernel.Subtract(current, s),
                    (_, CombineMode.Interseccion) => _kernel.Intersect(current, s),
                    _ => current,
                };
            }
            catch (Exception ex)
            {
                errors.Add($"{(string.IsNullOrEmpty(f.Name) ? f.KindName : f.Name)}: {ex.Message}");
            }
        }
        Mesh? mesh = null;
        if (current is not null)
        {
            try { mesh = _kernel.ToMesh(current); }
            catch (Exception ex) { errors.Add($"Teselado: {ex.Message}"); }
        }
        return new PartResult(part, mesh, errors);
    }

    /// <summary>
    /// Evalúa en un hilo con pila grande: los árboles BSP son recursivos y las piezas complejas los hacen profundos.
    /// </summary>
    public Task<PartResult> EvaluateAsync(Part part, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<PartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                tcs.SetResult(Evaluate(part));
            }
            catch (OperationCanceledException) { tcs.SetCanceled(ct); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, maxStackSize: 256 * 1024 * 1024) { IsBackground = true, Name = "Cad3D.Evaluate" };
        thread.Start();
        return tcs.Task;
    }
}
