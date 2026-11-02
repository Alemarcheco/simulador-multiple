using Simulador.Core.Libraries.Model;

namespace Simulador.Core.Libraries;

/// <summary>Importador de un formato de librería. Para añadir un formato, implementa esta interfaz y regístrala.</summary>
public interface ILibraryImporter
{
    /// <summary>Nombre visible, p. ej. "Símbolos KiCad".</summary>
    string Name { get; }
    /// <summary>Extensiones admitidas, en minúsculas y con punto: ".kicad_sym".</summary>
    IReadOnlyList<string> Extensions { get; }
    /// <param name="content">Texto del archivo.</param>
    /// <param name="libraryName">Nombre de la librería (normalmente el nombre del archivo o de la carpeta).</param>
    LibraryImportResult Import(string content, string libraryName);
}

/// <summary>Elige el importador según la extensión e importa archivos o carpetas completas.</summary>
public sealed class LibraryImporterRegistry
{
    private readonly List<ILibraryImporter> _importers = [];

    public IReadOnlyList<ILibraryImporter> Importers => _importers;

    /// <summary>Registro con los importadores incluidos (KiCad símbolos y huellas, SPICE).</summary>
    public static LibraryImporterRegistry CreateDefault()
    {
        var r = new LibraryImporterRegistry();
        r.Register(new KiCad.KiCadSymbolImporter());
        r.Register(new KiCad.KiCadFootprintImporter());
        r.Register(new Spice.SpiceLibraryImporter());
        return r;
    }

    public void Register(ILibraryImporter importer) => _importers.Add(importer);

    public ILibraryImporter? FindFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return _importers.FirstOrDefault(i => i.Extensions.Contains(ext));
    }

    /// <summary>Importa un archivo, o una carpeta (p. ej. una librería de huellas <c>.pretty</c>) recursivamente.</summary>
    public LibraryImportResult ImportPath(string path)
    {
        var result = new LibraryImportResult();
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                if (FindFor(file) is not null)
                    result.Merge(ImportFile(file));
            return result;
        }
        return ImportFile(path);
    }

    private LibraryImportResult ImportFile(string file)
    {
        var importer = FindFor(file);
        if (importer is null)
        {
            var r = new LibraryImportResult();
            r.Warnings.Add($"Formato no admitido: {Path.GetFileName(file)}");
            return r;
        }

        // Las huellas KiCad viven en carpetas "Libreria.pretty"; el nombre de la librería es el de la carpeta.
        var dir = Path.GetDirectoryName(file) ?? "";
        var libName = dir.EndsWith(".pretty", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(dir)
            : Path.GetFileNameWithoutExtension(file);

        try
        {
            return importer.Import(File.ReadAllText(file), libName);
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            var r = new LibraryImportResult();
            r.Warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
            return r;
        }
    }
}
