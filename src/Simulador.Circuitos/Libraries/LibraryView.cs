using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Simulador.Circuitos.Catalog;
using Simulador.Core.Libraries.Model;
using Simulador.Core.Modules;

namespace Simulador.Circuitos.Libraries;

/// <summary>Importa librerías descargadas (KiCad, SPICE) y muestra su contenido.</summary>
internal sealed class LibraryView : DockPanel
{
    private readonly IModuleHost _host;
    private readonly ComponentCatalog _catalog;
    private readonly ListBox _list = new();
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
    private readonly TextBox _filter = new() { Watermark = "Buscar…", Width = 220 };

    public LibraryView(IModuleHost host, ComponentCatalog catalog)
    {
        _host = host;
        _catalog = catalog;
        _catalog.Changed += (_, _) => Refresh();
        var importFiles = new Button { Content = "Importar archivos…" };
        importFiles.Click += async (_, _) => await ImportFilesAsync();
        var importFolder = new Button { Content = "Importar carpeta…" };
        importFolder.Click += async (_, _) => await ImportFolderAsync();
        _filter.TextChanged += (_, _) => Refresh();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { importFiles, importFolder, _filter, _summary },
        };
        SetDock(toolbar, Dock.Top);

        var help = new TextBlock
        {
            Text = "Formatos: símbolos KiCad (.kicad_sym), huellas KiCad (.kicad_mod o carpetas .pretty) y modelos SPICE (.lib, .mod, .sub, .cir).",
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        SetDock(help, Dock.Bottom);

        Children.Add(toolbar);
        Children.Add(help);
        Children.Add(_list);
        Refresh();
    }

    private async Task ImportFilesAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar librerías",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Librerías admitidas")
                {
                    Patterns = _catalog.Registry.Importers.SelectMany(i => i.Extensions).Select(e => "*" + e).ToList(),
                },
                FilePickerFileTypes.All,
            ],
        });
        Import(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async Task ImportFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Importar carpeta de librerías" });
        Import(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private void Import(IEnumerable<string> paths)
    {
        var added = new LibraryImportResult();
        foreach (var path in paths)
            added.Merge(_catalog.Import(path));
        _host.SetStatus(
            $"Importados {added.Symbols.Count} símbolos, {added.Footprints.Count} huellas, " +
            $"{added.SpiceModels.Count + added.SpiceSubcircuits.Count} modelos SPICE" +
            (added.Warnings.Count > 0 ? $" ({added.Warnings.Count} avisos: {added.Warnings[0]})" : "."));
    }

    private void Refresh()
    {
        var f = _filter.Text?.Trim() ?? "";
        bool Match(string s) => f.Length == 0 || s.Contains(f, StringComparison.OrdinalIgnoreCase);

        var rows = new List<string>();
        rows.AddRange(_catalog.Symbols.Where(s => Match(s.FullName) || Match(s.Description))
            .Select(s => $"Símbolo   {s.FullName}   ({s.Pins.Count} pines)  {s.Description}"));
        rows.AddRange(_catalog.Footprints.Where(p => Match(p.FullName) || Match(p.Description))
            .Select(p => $"Huella    {p.FullName}   ({p.Pads.Count} pads, {(p.IsSmd ? "SMD" : "pasante")})"));
        rows.AddRange(_catalog.SpiceModels.Where(m => Match(m.Name))
            .Select(m => $"Modelo    {m.Name}   ({m.Type}, {m.Parameters.Count} parámetros)"));
        rows.AddRange(_catalog.SpiceSubcircuits.Where(s => Match(s.Name))
            .Select(s => $"Subcircuito {s.Name}   (pines: {string.Join(' ', s.Pins)})"));
        _list.ItemsSource = rows;

        _summary.Text =
            $"{_catalog.Symbols.Count()} símbolos · {_catalog.Footprints.Count()} huellas · " +
            $"{_catalog.SpiceModels.Count() + _catalog.SpiceSubcircuits.Count()} modelos SPICE · " +
            $"{_catalog.ImportedPaths.Count} librerías importadas (se recuerdan al reiniciar)";
    }
}
