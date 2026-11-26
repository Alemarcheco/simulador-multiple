using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Cad3D.Geometry.Csg;

namespace Simulador.Cad3D.Geometry.Model;

/// <summary>Una pieza imprimible: un sólido que se construye aplicando sus operaciones en orden.</summary>
public sealed class Part
{
    public string Name { get; set; } = "Pieza";
    /// <summary>Color de visualización y de 3MF, en formato #RRGGBB.</summary>
    public string Color { get; set; } = "#4A90D9";
    public bool Visible { get; set; } = true;
    /// <summary>En una pieza escaneada con colores, verlos (true) o verla toda del color de la pieza (false).</summary>
    public bool ShowScanColors { get; set; } = true;
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
/// <param name="TriangleColors">Color de cada triángulo (ARGB) si la pieza es un escaneo con colores; si no, null.</param>
public sealed record PartResult(Part Part, Mesh? Mesh, IReadOnlyList<string> Errors, IReadOnlyList<uint>? TriangleColors = null);

/// <summary>Reconstruye las piezas aplicando el historial de operaciones con un motor geométrico.</summary>
public sealed class DocumentEvaluator
{
    private readonly IGeometryKernel _kernel;

    public DocumentEvaluator(IGeometryKernel kernel) { _kernel = kernel; }

    public IGeometryKernel Kernel => _kernel;

    /// <summary>Construye el sólido de la pieza (sin teselar), p. ej. para exportar a STEP.</summary>
    public ISolid? BuildSolid(Part part, List<string>? errors = null) => Build(part, errors ?? new List<string>());

    private MeshKernel? _meshKernel;

    /// <summary>true si la pieza tiene alguna pieza escaneada activa.</summary>
    public static bool HasScan(Part part) => part.Features.Any(f => f.Enabled && f is ScanFeature);

    /// <summary>
    /// Pieza hecha solo de escaneos que suman: su malla sale directamente, sin pasar por el motor (rápido aunque el
    /// escaneo tenga muchos triángulos).
    /// </summary>
    public static bool IsScanOnly(Part part) =>
        HasScan(part) && part.Features.Where(f => f.Enabled).All(f => f is ScanFeature { Mode: CombineMode.Solido });

    /// <summary>Malla con colores de una pieza hecha solo de escaneos; null si la pieza tiene otras operaciones.</summary>
    public static Scan.ScanMesh? ScanOnlyMesh(Part part) =>
        IsScanOnly(part) ? Scan.ScanMesh.Merge(part.Features.Where(f => f.Enabled).Cast<ScanFeature>().Select(s => s.BuildPlacedMesh())) : null;

    public PartResult Evaluate(Part part)
    {
        var errors = new List<string>();
        if (IsScanOnly(part))
        {
            try
            {
                var scan = ScanOnlyMesh(part)!;
                return new PartResult(part, scan.ToMesh(), errors, scan.TriangleColors());
            }
            catch (Exception ex) { errors.Add($"Pieza escaneada: {ex.Message}"); return new PartResult(part, null, errors); }
        }
        // Un escaneo combinado con otras operaciones (p. ej. un taladro) se calcula con el motor de mallas.
        var kernel = HasScan(part) && _kernel is not MeshKernel ? (_meshKernel ??= new MeshKernel()) : _kernel;
        var current = Build(part, errors, kernel);
        Mesh? mesh = null;
        if (current is not null)
        {
            try { mesh = kernel.ToMesh(current); }
            catch (Exception ex) { errors.Add($"Teselado: {ex.Message}"); }
        }
        return new PartResult(part, mesh, errors);
    }

    private ISolid? Build(Part part, List<string> errors) => Build(part, errors, _kernel);

    private ISolid? Build(Part part, List<string> errors, IGeometryKernel k)
    {
        ISolid? current = null;
        var built = new List<Feature>(); // operaciones con forma propia ya aplicadas, para las repeticiones
        foreach (var f in part.Features)
        {
            if (!f.Enabled) continue;
            try
            {
                if (f is PatternFeature pattern)
                {
                    var group = built.Skip(Math.Max(0, built.Count - Math.Max(1, pattern.Operations))).ToList();
                    if (group.Count == 0) throw new InvalidOperationException("no hay ninguna operación antes que repetir");
                    foreach (var t in pattern.CopyTransforms())
                        foreach (var g in group)
                            current = Combine(k, current, k.Transform(g.Build(k), t), g.Mode);
                    continue;
                }
                if (f is ModifierFeature mod)
                {
                    if (current is null) throw new InvalidOperationException("no hay nada antes que modificar");
                    current = mod.Apply(k, current);
                    continue;
                }
                current = Combine(k, current, f.Build(k), f.Mode);
                built.Add(f);
            }
            catch (Exception ex)
            {
                errors.Add($"{(string.IsNullOrEmpty(f.Name) ? f.KindName : f.Name)}: {ex.Message}");
            }
        }
        return current;
    }

    private static ISolid? Combine(IGeometryKernel k, ISolid? current, ISolid s, CombineMode mode) => (current, mode) switch
    {
        (null, CombineMode.Solido) => s,
        (null, _) => null, // un hueco o intersección sin nada antes no aporta material
        (_, CombineMode.Solido) => k.Union(current, s),
        (_, CombineMode.Hueco) => k.Subtract(current, s),
        (_, CombineMode.Interseccion) => k.Intersect(current, s),
        _ => current,
    };

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
