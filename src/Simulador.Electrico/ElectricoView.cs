using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Core.Modules;
using Simulador.Electrico.Editor;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico;

/// <summary>Pestaña completa: librería a la izquierda, hoja en el centro, propiedades a la derecha y registro de eventos abajo.</summary>
internal sealed class ElectricoView : DockPanel
{
    private readonly IModuleHost _host;
    private readonly ElementCatalog _catalog = ElementCatalog.Default;
    private readonly ManufacturerCatalog _presets = new();
    private readonly SchematicCanvas _canvas;
    private readonly PropertiesPanel _props;
    private readonly ListBox _events = new() { FontSize = 12 };
    private readonly TextBox _search = new() { Watermark = "Buscar componente…" };
    private readonly TreeView _palette = new();
    private readonly Decorator _preview = new() { MinHeight = 120 };
    private readonly ToggleButton _run = new() { Content = "▶ Simular" };
    private readonly ComboBox _speed = new() { Width = 140 };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8, MinWidth = 90 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Simulator? _sim;
    private string? _path;
    private int _shownEvents;
    private bool _dirty;

    private static readonly double[] Speeds = [0.1, 0.25, 1, 2, 5, 10];

    public ElectricoView(IModuleHost host)
    {
        _host = host;
        LoadCatalogs();

        _canvas = new SchematicCanvas(_catalog);
        _props = new PropertiesPanel(_catalog, () => _presets);
        _canvas.SelectionChanged += () => _props.Show(_canvas.Selected, _sim is not null);
        _canvas.Modified += () => { _dirty = true; UpdateTitle(); };
        _canvas.Hint += t => _host.SetStatus(t);
        _props.Changed += () => { _dirty = true; _canvas.InvalidateVisual(); UpdateTitle(); };
        _props.MotorLoadChanged += (id, pct) => _sim?.SetMotorLoad(id, pct);
        _timer.Tick += (_, _) => Tick();

        Children.Add(BuildToolbar());
        Children.Add(BuildPalette());
        var right = new Border { Width = 280, Child = _props, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1, 0, 0, 0) };
        SetDock(right, Dock.Right);
        Children.Add(right);
        var log = new Border
        {
            Height = 130,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = "Registro de la simulación", Margin = new Thickness(8, 4), Opacity = 0.7, [DockPanel.DockProperty] = Dock.Top },
                    _events,
                },
            },
        };
        SetDock(log, Dock.Bottom);
        Children.Add(log);
        Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _canvas,
        });

        _canvas.Load(Examples.DirectOnLine());
        UpdateTitle();
    }

    // ------------------------------------------------------------------ interfaz

    private Control BuildToolbar()
    {
        Button B(string text, Action onClick, string tip)
        {
            var b = new Button { Content = text };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => onClick();
            return b;
        }

        var examples = new Button { Content = "Ejemplos ▾" };
        var menu = new MenuFlyout();
        foreach (var (name, create) in Examples.All)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => { StopSim(); _path = null; _canvas.Load(create()); _dirty = false; UpdateTitle(); };
            menu.Items.Add(item);
        }
        examples.Flyout = menu;

        foreach (var s in Speeds) _speed.Items.Add(s == 1 ? "Tiempo real" : $"× {s:0.##}");
        _speed.SelectedIndex = 2;
        ToolTip.SetTip(_speed, "Velocidad de la simulación");

        _run.IsCheckedChanged += (_, _) => { if (_run.IsChecked == true) StartSim(); else StopSim(); };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 6),
            Children =
            {
                B("Nuevo", NewSchematic, "Esquema vacío"),
                B("Abrir…", () => _ = OpenAsync(), "Abrir un esquema .selx"),
                B("Guardar", () => _ = SaveAsync(false), "Guardar el esquema"),
                B("Guardar como…", () => _ = SaveAsync(true), "Guardar con otro nombre"),
                examples,
                new Separator { Width = 12 },
                _run,
                B("⟲ Reiniciar", ResetSim, "Vuelve todo a reposo y pone el tiempo a cero"),
                _speed,
                _clock,
                new Separator { Width = 12 },
                B("Importar catálogo CSV…", () => _ = ImportCatalogAsync(), "Añade aparatos reales de fabricante (contactores, guardamotores, motores…) desde un CSV"),
                B("Girar (R)", RotateSelected, "Gira 90° el elemento seleccionado"),
                B("Borrar (Supr)", () => _canvas.DeleteSelection(), "Borra el elemento o conductor seleccionado"),
            },
        };
        var border = new Border { Child = bar, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1) };
        SetDock(border, Dock.Top);
        return border;
    }

    private Control BuildPalette()
    {
        _search.TextChanged += (_, _) => FillPalette();
        _palette.SelectionChanged += (_, _) =>
        {
            if (_palette.SelectedItem is TreeViewItem { Tag: string typeId })
            {
                ShowPreview(_catalog.Get(typeId));
                if (_sim is null) _canvas.Placing = typeId;
            }
        };
        FillPalette();
        ShowPreview(null);
        var panel = new DockPanel { Width = 260 };
        var top = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(8),
            Children =
            {
                new TextBlock { Text = "Librería de componentes", FontWeight = FontWeight.SemiBold },
                _search,
            },
        };
        SetDock(top, Dock.Top);
        panel.Children.Add(top);
        var preview = new Border
        {
            Child = _preview,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8),
        };
        SetDock(preview, Dock.Bottom);
        panel.Children.Add(preview);
        panel.Children.Add(_palette);
        var border = new Border { Child = panel, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1, 0) };
        SetDock(border, Dock.Left);
        return border;
    }

    private void FillPalette()
    {
        var filter = _search.Text?.Trim() ?? "";
        _palette.Items.Clear();
        foreach (var group in _catalog.ByCategory())
        {
            var matches = group.Where(t => filter.Length == 0
                || t.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || t.Description.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || _presets.ForType(t.Id).Any(p => p.Display.Contains(filter, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (matches.Count == 0) continue;
            var node = new TreeViewItem { Header = group.Key, IsExpanded = filter.Length > 0 || group.Key is ElementCatalog.CatControl or ElementCatalog.CatCoils };
            foreach (var t in matches)
            {
                var n = _presets.ForType(t.Id).Count();
                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new SymbolPreview(t, 46),
                        new TextBlock
                        {
                            Text = n > 0 ? $"{t.Name}  ({n} ref.)" : t.Name,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 150,
                        },
                    },
                };
                var item = new TreeViewItem { Header = header, Tag = t.Id };
                var type = t;
                item.PointerEntered += (_, _) => ShowPreview(type);
                node.Items.Add(item);
            }
            _palette.Items.Add(node);
        }
    }

    /// <summary>Vista ampliada del componente bajo el ratón o elegido, para verlo antes de colocarlo.</summary>
    private void ShowPreview(ElementType? t)
    {
        t ??= _palette.SelectedItem is TreeViewItem { Tag: string id } ? _catalog.Get(id) : null;
        if (t is null)
        {
            _preview.Child = new TextBlock
            {
                Text = "Pasa el ratón por un componente para ver su símbolo y para qué sirve.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                FontSize = 12,
            };
            return;
        }
        var refs = _presets.ForType(t.Id).ToList();
        var stack = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = t.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new SymbolPreview(t, 124, showTerminals: true) { HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = $"Designación: {t.TagPrefix}1, {t.TagPrefix}2…", FontSize = 12, Opacity = 0.75 },
                new TextBlock { Text = t.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
            },
        };
        if (refs.Count > 0)
            stack.Children.Add(new TextBlock
            {
                Text = $"Referencias de catálogo: {string.Join(", ", refs.Take(4).Select(r => r.Display))}{(refs.Count > 4 ? "…" : "")}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.75,
            });
        _preview.Child = stack;
    }

    private void UpdateTitle()
    {
        var name = _path is null ? _canvas.Schematic.Name : Path.GetFileName(_path);
        _host.SetStatus($"{name}{(_dirty ? " (sin guardar)" : "")} · {_canvas.Schematic.Elements.Count} elementos, {_canvas.Schematic.Wires.Count} conductores");
    }

    private void RotateSelected()
    {
        if (_canvas.Selected is null || _sim is not null) return;
        _canvas.Selected.Rotation = (_canvas.Selected.Rotation + 1) % 4;
        _dirty = true;
        _canvas.InvalidateVisual();
    }

    // ------------------------------------------------------------------ simulación

    private void StartSim()
    {
        _canvas.Placing = null;
        _palette.SelectedItem = null;
        _sim = new Simulator(_canvas.Schematic, _catalog);
        _canvas.Sim = _sim;
        _events.Items.Clear();
        _shownEvents = 0;
        _run.Content = "⏸ Editar";
        _props.Show(_canvas.Selected, true);
        _timer.Start();
        _host.SetStatus($"Simulando «{_canvas.Schematic.Name}». Haz clic en los pulsadores y protecciones.");
        Tick();
    }

    private void StopSim()
    {
        _timer.Stop();
        _sim = null;
        _canvas.Sim = null;
        if (_run.IsChecked == true) _run.IsChecked = false;
        _run.Content = "▶ Simular";
        _props.Show(_canvas.Selected, false);
        _canvas.InvalidateVisual();
    }

    private void ResetSim()
    {
        if (_sim is null) return;
        _sim.Reset();
        _events.Items.Clear();
        _shownEvents = 0;
        Tick();
    }

    private void Tick()
    {
        if (_sim is null) return;
        var speed = Speeds[Math.Max(0, _speed.SelectedIndex)];
        _sim.Run(0.033 * speed);
        _clock.Text = $"t = {_sim.Time:0.0} s";
        var events = _sim.Events;
        if (events.Count < _shownEvents) _shownEvents = 0;
        for (; _shownEvents < events.Count; _shownEvents++)
        {
            var ev = events[_shownEvents];
            var line = new TextBlock
            {
                Text = $"{ev.Time,7:0.000} s  {ev.Text}",
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            };
            if (ev.Severity == EventSeverity.Fault) line.Foreground = SymbolPainter.Fault;
            else if (ev.Severity == EventSeverity.Warning) line.Foreground = new SolidColorBrush(Color.Parse("#E08A00"));
            _events.Items.Insert(0, line);
            if (ev.Severity == EventSeverity.Fault) _host.SetStatus(ev.Text);
        }
        while (_events.Items.Count > 300) _events.Items.RemoveAt(_events.Items.Count - 1);
        _canvas.InvalidateVisual();
    }

    // ------------------------------------------------------------------ archivos

    private void NewSchematic()
    {
        StopSim();
        _path = null;
        _canvas.Load(new Schematic());
        _dirty = false;
        UpdateTitle();
    }

    private static readonly FilePickerFileType SelxType = new("Esquema eléctrico (.selx)") { Patterns = ["*.selx"] };

    private async Task OpenAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir esquema", FileTypeFilter = [SelxType, FilePickerFileTypes.All] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            StopSim();
            _canvas.Load(Schematic.FromJson(await File.ReadAllTextAsync(path)));
            _path = path;
            _dirty = false;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo abrir {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private async Task SaveAsync(bool askName)
    {
        var path = _path;
        if (askName || path is null)
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar esquema",
                DefaultExtension = "selx",
                SuggestedFileName = _canvas.Schematic.Name,
                FileTypeChoices = [SelxType],
            });
            path = file?.TryGetLocalPath();
            if (path is null) return;
        }
        _canvas.Schematic.Name = Path.GetFileNameWithoutExtension(path);
        await File.WriteAllTextAsync(path, _canvas.Schematic.ToJson());
        _path = path;
        _dirty = false;
        UpdateTitle();
    }

    // ------------------------------------------------------------------ catálogos de fabricante

    private string CatalogDir => Path.Combine(_host.GetDataDirectory("electrico"), "catalogos");

    private void LoadCatalogs()
    {
        var files = new List<string>();
        var bundled = Path.Combine(AppContext.BaseDirectory, "Catalogos");
        if (Directory.Exists(bundled)) files.AddRange(Directory.GetFiles(bundled, "*.csv"));
        try
        {
            Directory.CreateDirectory(CatalogDir);
            files.AddRange(Directory.GetFiles(CatalogDir, "*.csv"));
        }
        catch (IOException) { }
        foreach (var f in files)
        {
            try { _presets.ImportCsv(File.ReadAllText(f), _catalog); }
            catch (IOException) { }
        }
    }

    private async Task ImportCatalogAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar catálogo de fabricante (CSV)",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv", "*.txt"] }, FilePickerFileTypes.All],
        });
        int total = 0;
        var errors = new List<string>();
        foreach (var path in files.Select(f => f.TryGetLocalPath()).OfType<string>())
        {
            var text = await File.ReadAllTextAsync(path);
            var r = _presets.ImportCsv(text, _catalog);
            total += r.Imported;
            errors.AddRange(r.Errors.Select(e => $"{Path.GetFileName(path)}: {e}"));
            if (r.Imported > 0)
            {
                Directory.CreateDirectory(CatalogDir);
                File.Copy(path, Path.Combine(CatalogDir, Path.GetFileName(path)), overwrite: true);
            }
        }
        FillPalette();
        _host.SetStatus(errors.Count == 0
            ? $"{total} referencias importadas. Aparecen en «Aparato de catálogo» al seleccionar un componente."
            : $"{total} referencias importadas, {errors.Count} líneas con errores: {string.Join(" | ", errors.Take(3))}");
    }
}
