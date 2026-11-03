using System.Reflection;
using System.Text.Json;
using Simulador.Core.Libraries;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Catalog;

/// <summary>
/// Todos los componentes disponibles en el módulo: los incluidos de serie y los que el usuario importa.
/// Las rutas importadas se recuerdan y se vuelven a cargar al abrir la aplicación.
/// </summary>
public sealed class ComponentCatalog
{
    public const string BuiltInLibrary = "Basicos";

    private readonly Dictionary<string, SymbolDefinition> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FootprintDefinition> _footprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpiceModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpiceSubcircuit> _subcircuits = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _importedPaths = [];
    private readonly LibraryImporterRegistry _registry = LibraryImporterRegistry.CreateDefault();
    private readonly string? _settingsFile;

    public event EventHandler? Changed;

    public IEnumerable<SymbolDefinition> Symbols => _symbols.Values;
    public IEnumerable<FootprintDefinition> Footprints => _footprints.Values;
    public IEnumerable<SpiceModel> SpiceModels => _models.Values;
    public IEnumerable<SpiceSubcircuit> SpiceSubcircuits => _subcircuits.Values;
    public IReadOnlyList<string> ImportedPaths => _importedPaths;
    public LibraryImporterRegistry Registry => _registry;

    /// <param name="dataDirectory">Carpeta donde recordar las librerías importadas; null para no recordar (pruebas).</param>
    public ComponentCatalog(string? dataDirectory = null)
    {
        LoadBuiltIn();
        if (dataDirectory is null) return;
        _settingsFile = Path.Combine(dataDirectory, "librerias.json");
        foreach (var path in ReadSettings())
            if (File.Exists(path) || Directory.Exists(path))
                Import(path, remember: true, notify: false);
    }

    public SymbolDefinition? FindSymbol(string fullName) =>
        _symbols.GetValueOrDefault(fullName) ??
        // Si el nombre no tiene librería o la librería cambió, busca solo por nombre.
        _symbols.Values.FirstOrDefault(s => s.Name.Equals(fullName.Split(':').Last(), StringComparison.OrdinalIgnoreCase));

    public FootprintDefinition? FindFootprint(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? null :
        _footprints.GetValueOrDefault(fullName) ??
        _footprints.Values.FirstOrDefault(f => f.Name.Equals(fullName.Split(':').Last(), StringComparison.OrdinalIgnoreCase));

    public SpiceModel? FindModel(string name) => _models.GetValueOrDefault(name);
    public SpiceSubcircuit? FindSubcircuit(string name) => _subcircuits.GetValueOrDefault(name);

    /// <summary>Importa un archivo o carpeta y devuelve lo que se añadió.</summary>
    public LibraryImportResult Import(string path, bool remember = true, bool notify = true)
    {
        var result = _registry.ImportPath(path);
        Add(result);
        if (remember && !result.IsEmpty && !_importedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _importedPaths.Add(path);
            WriteSettings();
        }
        if (notify) Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void Add(LibraryImportResult r)
    {
        foreach (var s in r.Symbols) _symbols[s.FullName] = s;
        foreach (var f in r.Footprints) _footprints[f.FullName] = f;
        foreach (var m in r.SpiceModels) _models[m.Name] = m;
        foreach (var s in r.SpiceSubcircuits) _subcircuits[s.Name] = s;
    }

    private void LoadBuiltIn()
    {
        var asm = typeof(ComponentCatalog).Assembly;
        Add(new Core.Libraries.KiCad.KiCadSymbolImporter().Import(ReadResource(asm, "Basicos.kicad_sym"), BuiltInLibrary));
        Add(new Core.Libraries.Spice.SpiceLibraryImporter().Import(ReadResource(asm, "Basicos.lib"), BuiltInLibrary));
        var fpImporter = new Core.Libraries.KiCad.KiCadFootprintImporter();
        const string prefix = "Simulador.Circuitos.Resources.";
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".kicad_mod", StringComparison.Ordinal)))
            Add(fpImporter.Import(ReadResource(asm, name[prefix.Length..]), BuiltInLibrary));
    }

    private static string ReadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream("Simulador.Circuitos.Resources." + name)
            ?? throw new InvalidOperationException($"Falta el recurso {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private List<string> ReadSettings()
    {
        try
        {
            return _settingsFile is not null && File.Exists(_settingsFile)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_settingsFile)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    private void WriteSettings()
    {
        if (_settingsFile is null) return;
        try
        {
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(_importedPaths, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // No poder recordar las librerías no impide usarlas en esta sesión.
        }
    }
}
