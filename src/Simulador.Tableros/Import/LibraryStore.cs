using Simulador.Tableros.Model;

namespace Simulador.Tableros.Import;

/// <summary>
/// Dónde viven los paquetes .edz: los que trae el programa (carpeta «Librerias» junto al ejecutable) y los que importa
/// el usuario (copiados a %AppData%\SimuladorMultiple\Librerias para que sigan ahí al abrir el programa otra vez).
/// </summary>
public static class LibraryStore
{
    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "Librerias");

    public static string UserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimuladorMultiple", "Librerias");

    private static bool _loaded;

    /// <summary>Carga una sola vez las librerías de las dos carpetas. Devuelve los avisos.</summary>
    public static List<string> EnsureLoaded()
    {
        var notes = new List<string>();
        if (_loaded) return notes;
        _loaded = true;
        foreach (var dir in new[] { BundledFolder, UserFolder })
            notes.AddRange(LoadFolder(dir).Notes);
        return notes;
    }

    public static EdzImporter.Result LoadFolder(string dir)
    {
        var items = new List<CatalogItem>();
        var notes = new List<string>();
        if (!Directory.Exists(dir)) return new(items, notes);
        foreach (var f in Directory.EnumerateFiles(dir, "*.edz").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var r = EdzImporter.Read(f);
                items.AddRange(r.Items);
                notes.AddRange(r.Notes);
            }
            catch (Exception ex) { notes.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        ManufacturerCatalog.Register(items);
        return new(items, notes);
    }

    /// <summary>Lee un paquete que ya está en su sitio (p. ej. el que acaba de guardar Diseño 3D) y lo añade a la librería.</summary>
    public static EdzImporter.Result LoadFile(string path)
    {
        var r = EdzImporter.Read(path);
        ManufacturerCatalog.Register(r.Items);
        return r;
    }

    /// <summary>Lee los paquetes elegidos, los copia a la carpeta del usuario y los añade a la librería.</summary>
    public static EdzImporter.Result Import(IEnumerable<string> paths, string? targetFolder = null)
    {
        var target = targetFolder ?? UserFolder;
        var items = new List<CatalogItem>();
        var notes = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var r = EdzImporter.Read(path);
                notes.AddRange(r.Notes);
                if (r.Items.Count == 0) continue;
                items.AddRange(r.Items);
                Directory.CreateDirectory(target);
                var dest = Path.Combine(target, Path.GetFileName(path));
                if (!string.Equals(Path.GetFullPath(dest), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    File.Copy(path, dest, overwrite: true);
            }
            catch (Exception ex) { notes.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        ManufacturerCatalog.Register(items);
        return new(items, notes);
    }
}
