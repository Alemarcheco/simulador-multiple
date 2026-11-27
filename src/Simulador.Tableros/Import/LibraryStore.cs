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


    /// <summary>
    /// Lee los archivos elegidos y los añade a la librería: los .edz se copian a la carpeta del usuario; los STEP del
    /// fabricante (.stp/.step, sueltos o en .zip) se giran automáticamente y se guardan como .edz.
    /// </summary>
    public static EdzImporter.Result Import(IEnumerable<string> paths, string? targetFolder = null)
    {
        var (packages, steps, notes) = Split(paths);
        var r = ImportPackages(packages, targetFolder);
        r.Notes.InsertRange(0, notes);
        var s = SaveSteps(steps, targetFolder);
        r.Items.AddRange(s.Items);
        r.Notes.AddRange(s.Notes);
        return r;
    }

    /// <summary>Separa paquetes .edz (o .zip con datos de pieza) de los modelos STEP, que se leen aquí.</summary>
    public static (List<string> Packages, List<StepImporter.Candidate> Steps, List<string> Notes) Split(IEnumerable<string> paths)
    {
        var packages = new List<string>();
        var steps = new List<StepImporter.Candidate>();
        var notes = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (StepImporter.IsStep(path)) steps.AddRange(StepImporter.Prepare(path));
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !IsPartPackage(path))
                {
                    var found = StepImporter.Prepare(path);
                    if (found.Count == 0) notes.Add($"{Path.GetFileName(path)}: no trae modelos STEP (.stp/.step) ni datos de pieza.");
                    steps.AddRange(found);
                }
                else packages.Add(path);
            }
            catch (Exception ex) { notes.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        foreach (var c in steps) notes.AddRange(c.Notes);
        return (packages, steps, notes);
    }

    private static bool IsPartPackage(string zipPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        return zip.Entries.Any(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".ema", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Guarda los modelos STEP (con los ajustes de cada uno) como .edz en la carpeta del usuario.</summary>
    public static EdzImporter.Result SaveSteps(IEnumerable<StepImporter.Candidate> steps, string? targetFolder = null)
    {
        var target = targetFolder ?? UserFolder;
        var items = new List<CatalogItem>();
        var notes = new List<string>();
        foreach (var c in steps)
        {
            try
            {
                if (c.Raw.Triangles.Count == 0) { notes.Add($"{c.FileName}: no se encontró geometría que dibujar."); continue; }
                var item = StepImporter.ToItem(c);
                StepImporter.WriteEdz(item, c, target);
                items.Add(item);
            }
            catch (Exception ex) { notes.Add($"{c.FileName}: {ex.Message}"); }
        }
        ManufacturerCatalog.Register(items);
        return new(items, notes);
    }

    /// <summary>Copia los paquetes .edz a la carpeta del usuario y los añade a la librería.</summary>
    public static EdzImporter.Result ImportPackages(IEnumerable<string> paths, string? targetFolder = null)
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
