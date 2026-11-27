using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Core.Modules;
using Simulador.Electrico.Engine.Model;
using Simulador.Tableros.Import;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Ui;

/// <summary>
/// Pestaña completa: barra de herramientas, sub-pestañas (placa, puerta, 3D, cableado, bornes, materiales,
/// comprobaciones) y propiedades a la derecha.
/// </summary>
internal sealed class TablerosView : DockPanel, IMenuSource
{
    private readonly IModuleHost _host;
    private readonly PanelCanvas _plate = new(PanelCanvas.Mode.Plate);
    private readonly PanelCanvas _door = new(PanelCanvas.Mode.Door);
    private readonly Panel3DView _view3d = new();
    private readonly PropertiesPanel _props = new();
    private readonly TabControl _tabs = new();
    private readonly ContentControl _wireTab = new(), _terminalTab = new(), _bomTab = new(), _checksTab = new();
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ToggleButton _showWires = new() { Content = "〰 Hilos", IsChecked = true };
    /// <summary>Armario de doble frente: qué cara se ve y edita (sin marcar, la delantera).</summary>
    private readonly ToggleButton _rearSide = new() { Content = "↻ Cara delantera", IsVisible = false };
    private bool Rear => _doc.Enclosure.DoubleFront && _rearSide.IsChecked == true;
    private readonly List<string> _history = [];
    private int _historyPos = -1;
    private PanelDocument _doc;
    private List<WireRoute> _routes = [];
    private string? _path;
    private bool _dirty;

    public TablerosView(IModuleHost host)
    {
        _host = host;
        // Paquetes .edz que trae el programa y los que importó el usuario.
        try { LibraryStore.EnsureLoaded(); }
        catch (Exception ex) { host.SetStatus($"No se pudieron cargar las librerías de fabricante: {ex.Message}"); }
        _doc = PanelBuilder.Examples[0].Create();

        _plate.SelectionChanged += () => OnSelection(_plate);
        _door.SelectionChanged += () => OnSelection(_door);
        _plate.Modified += Commit;
        _door.Modified += Commit;
        _plate.Hint += t => _host.SetStatus(t);
        _door.Hint += t => _host.SetStatus(t);
        _props.Changed += Commit;
        _props.ArrangeRequested += () => { AutoLayout.Arrange(_doc, chooseEnclosure: false); Commit("Redistribuir"); };
        _props.DoubleFrontChanged += () =>
        {
            AutoLayout.Arrange(_doc);
            PanelBuilder.SuggestFan(_doc);
            if (!_doc.Enclosure.DoubleFront) _rearSide.IsChecked = false;
            _plate.Fit();
            _door.Fit();
            Commit(_doc.Enclosure.DoubleFront ? "Doble frente: aparatos repartidos entre la cara delantera y la trasera (botón «↻ Cara» para cambiar)" : "Una sola cara");
        };
        _showWires.IsCheckedChanged += (_, _) => { _plate.ShowWires = _showWires.IsChecked == true; _plate.InvalidateVisual(); };

        var toolbar = BuildToolbar();
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);
        var status = new Border { Padding = new Thickness(8, 4), Child = _summary, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0) };
        SetDock(status, Dock.Bottom);
        Children.Add(status);
        var right = new Border { Width = 270, Child = _props, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1, 0, 0, 0) };
        SetDock(right, Dock.Right);
        Children.Add(right);

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Placa de montaje", Content = _plate },
            new TabItem { Header = "Puerta", Content = _door },
            new TabItem { Header = "Vista 3D", Content = Build3DTab() },
            new TabItem { Header = "Cableado", Content = _wireTab },
            new TabItem { Header = "Bornes y cables", Content = _terminalTab },
            new TabItem { Header = "Materiales", Content = _bomTab },
            new TabItem { Header = "Comprobaciones", Content = _checksTab },
        };
        _tabs.SelectionChanged += (_, _) => { if (_tabs.SelectedIndex == 2) _view3d.InvalidateVisual(); };
        Children.Add(_tabs);

        Refresh(resetHistory: true);
    }

    // ------------------------------------------------------------------ barra de herramientas

    private Control BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(6, 4) };
        Button B(string text, string tip, Action run)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => run();
            bar.Children.Add(b);
            return b;
        }

        var add = new Button { Content = "🧩 Añadir aparato ▾", Margin = new Thickness(0, 0, 4, 0) };
        var flyout = new MenuFlyout();
        foreach (var group in DeviceLibrary.Placeable.GroupBy(m => m.Location))
            foreach (var m in group)
            {
                var item = new MenuItem { Header = (m.Location == DeviceLocation.Door ? "Puerta · " : "Placa · ") + m.Name };
                item.Click += (_, _) => AddDevice(m.Kind);
                flyout.Items.Add(item);
            }
        _libraryStart = flyout.Items.Count;
        _addFlyout = flyout;
        FillLibraryMenu();
        add.Flyout = flyout;
        ToolTip.SetTip(add, "Añade un aparato genérico en el primer hueco libre del carril (o de la puerta).");
        bar.Children.Add(add);
        B("➖ Carril DIN", "Añade un carril DIN de 35 mm", AddRail);
        B("▭ Canaleta H", "Añade una canaleta horizontal de 40×60", () => AddDuct(false));
        B("▯ Canaleta V", "Añade una canaleta vertical de 40×60", () => AddDuct(true));
        B("🗑 Borrar", "Borra lo seleccionado (Supr)", DeleteSelected);
        bar.Children.Add(new Separator { Width = 10 });
        B("⚙ Distribuir", "Elige la envolvente y coloca todos los aparatos en filas con canaletas", () =>
        {
            AutoLayout.Arrange(_doc);
            PanelBuilder.SuggestFan(_doc);
            _plate.Fit();
            _door.Fit();
            Commit("Distribuir automáticamente");
        });
        B("🔄 Actualizar desde el esquema", "Vuelve a leer el esquema abierto en Instalaciones eléctricas y añade o quita aparatos sin mover los que ya están", UpdateFromOpenSchematic);
        bar.Children.Add(_rearSide);
        ToolTip.SetTip(_rearSide, "Armario de doble frente: cambia entre la placa y la puerta delanteras y las traseras. Lo que añadas va a la cara que estás viendo.");
        _rearSide.IsCheckedChanged += (_, _) => { _plate.Select(null, notify: false); _door.Select(null, notify: false); Refresh(); _plate.Fit(); _door.Fit(); };
        bar.Children.Add(_showWires);
        ToolTip.SetTip(_showWires, "Dibuja el recorrido de los hilos por las canaletas");
        B("⤢ Ajustar", "Encuadra la vista", () => { _plate.Fit(); _door.Fit(); _view3d.ResetView(); });
        return bar;
    }

    private Control Build3DTab()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(8, 4) };
        var door = new Slider { Minimum = 0, Maximum = 160, Value = 110, Width = 160 };
        var enclosure = new CheckBox { Content = "Envolvente", IsChecked = true };
        var wires = new CheckBox { Content = "Hilos", IsChecked = true };
        var showDoor = new CheckBox { Content = "Puerta", IsChecked = true };
        void Apply() => _view3d.SetOptions(new PanelSceneOptions(door.Value, enclosure.IsChecked == true, wires.IsChecked == true, showDoor.IsChecked == true));
        door.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Apply(); };
        enclosure.IsCheckedChanged += (_, _) => Apply();
        wires.IsCheckedChanged += (_, _) => Apply();
        showDoor.IsCheckedChanged += (_, _) => Apply();
        bar.Children.Add(new TextBlock { Text = "Apertura de la puerta", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(door);
        bar.Children.Add(showDoor);
        bar.Children.Add(enclosure);
        bar.Children.Add(wires);
        bar.Children.Add(new TextBlock { Text = "Arrastra para girar · botón derecho para mover · rueda para acercar", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        var dock = new DockPanel();
        SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(_view3d);
        return dock;
    }

    // ------------------------------------------------------------------ menús

    public IEnumerable<MenuCommand> GetMenuCommands()
    {
        yield return new MenuCommand("Nuevo tablero vacío", NewEmpty) { ToolTip = "Envolvente de 600×800 con canaletas y carriles, sin aparatos" };
        yield return new MenuCommand("Nuevo desde el esquema de Instalaciones eléctricas", FromOpenSchematic)
        { ToolTip = "Monta el tablero del esquema que tienes abierto en la pestaña Instalaciones eléctricas" };
        yield return new MenuCommand("Nuevo desde un esquema .selx…", () => _ = FromSelxAsync());
        yield return new MenuCommand("Importar plano PDF…", () => _ = ImportPdfAsync(null))
        { ToolTip = "Lee un plano eléctrico en PDF (de cualquier programa de CAD) y monta el tablero solo: aparatos, bornes, hilos, envolvente y canaletas. Si a una cara sale de más de 1200 mm de ancho, propone doble frente" };
        yield return new MenuCommand("Importar plano PDF en armario de doble frente…", () => _ = ImportPdfAsync(true))
        { ToolTip = "Igual, pero reparte los aparatos entre la placa delantera y la trasera de un armario de doble frente" };
        yield return new MenuCommand("Importar librería de fabricante (.edz, .stp, .step)…", () => _ = ImportLibraryAsync())
        {
            ToolTip = "Añade a la librería los aparatos de paquetes .edz (EPLAN Data Portal o los del simulador) y los modelos 3D STEP tal como se descargan de las webs de los fabricantes (.stp, .step o dentro de un .zip)",
        };
        yield return new MenuCommand("Abrir…", () => _ = OpenAsync()) { ToolTip = "Abrir un tablero .tablero", SeparatorBefore = true };
        yield return new MenuCommand("Guardar", () => _ = SaveAsync(false)) { Shortcut = "Ctrl+S" };
        yield return new MenuCommand("Guardar como…", () => _ = SaveAsync(true));
        yield return MenuCommand.Submenu("Ejemplos", PanelBuilder.Examples.Select(e => new MenuCommand(e.Name, () => Load(e.Create(), null))));
        yield return MenuCommand.Submenu("Exportar listas (CSV)", new[]
        {
            new MenuCommand("Lista de materiales…", () => _ = ExportAsync(Reports.BillOfMaterials(_doc), "materiales")),
            new MenuCommand("Lista de cableado…", () => _ = ExportAsync(Reports.WireList(_doc), "cableado")),
            new MenuCommand("Plano de bornes…", () => _ = ExportAsync(Reports.TerminalPlan(_doc), "bornes")),
            new MenuCommand("Cables de campo…", () => _ = ExportAsync(Reports.CableList(_doc), "cables")),
        }) with { SeparatorBefore = true };
        yield return new MenuCommand("Deshacer", Undo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Z", IsEnabled = () => _historyPos > 0 };
        yield return new MenuCommand("Rehacer", Redo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Y", IsEnabled = () => _historyPos < _history.Count - 1 };
        yield return new MenuCommand("Borrar", DeleteSelected) { Menu = MenuNames.Edit, Shortcut = "Supr", SeparatorBefore = true };
        yield return new MenuCommand("Distribuir automáticamente", () => { AutoLayout.Arrange(_doc); PanelBuilder.SuggestFan(_doc); Commit("Distribuir"); }) { Menu = MenuNames.Edit };
        yield return new MenuCommand("Actualizar desde el esquema", UpdateFromOpenSchematic) { Menu = MenuNames.Edit };
        yield return new MenuCommand("Ajustar a la ventana", () => { _plate.Fit(); _door.Fit(); _view3d.ResetView(); }) { Menu = MenuNames.View };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { _ = SaveAsync(false); e.Handled = true; }
    }

    // ------------------------------------------------------------------ cambios

    private PanelCanvas ActiveCanvas => _tabs.SelectedIndex == 1 ? _door : _plate;

    private void OnSelection(PanelCanvas source)
    {
        _props.Show(_doc, source.Selected);
        var other = source == _plate ? _door : _plate;
        if (other.Selected is not null) other.Select(null, notify: false);
    }

    /// <summary>Tras un cambio: rehace cableado y listas, y guarda el paso para deshacer.</summary>
    private void Commit(string what)
    {
        _dirty = true;
        Refresh();
        if (_historyPos < _history.Count - 1) _history.RemoveRange(_historyPos + 1, _history.Count - _historyPos - 1);
        _history.Add(_doc.ToJson());
        if (_history.Count > 60) _history.RemoveAt(0);
        _historyPos = _history.Count - 1;
        _host.SetStatus(what);
    }

    private void Refresh(bool resetHistory = false)
    {
        _routes = DuctRouter.Rebuild(_doc);
        var checks = PanelChecks.Run(_doc, _routes);
        var problems = checks.Where(c => c.Level != CheckLevel.Info && c.Tag is not null).Select(c => c.Tag!);
        _rearSide.IsVisible = _doc.Enclosure.DoubleFront;
        _rearSide.Content = Rear ? "↻ Cara trasera" : "↻ Cara delantera";
        _plate.Rear = _door.Rear = Rear;
        _plate.Load(_doc, _routes, problems);
        _door.Load(_doc, _routes, problems);
        _view3d.Load(_doc, _routes);
        _props.Show(_doc, _tabs.SelectedIndex == 1 ? _door.Selected : _plate.Selected);
        _summary.Text = $"{_doc.Name}{(_dirty ? " •" : "")}  —  {Reports.Summary(_doc)}";

        _wireTab.Content = TableView(Reports.WireList(_doc), "Cada hilo de borne a borne, con el número de su potencial, sección, color y largo por las canaletas (incluye 60 mm por punta).");
        _terminalTab.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 16,
                Children = { TableView(Reports.TerminalPlan(_doc), "Por dentro, el hilo que llega a cada borne; por fuera, el cable hacia el campo.", scroll: false), TableView(Reports.CableList(_doc), null, scroll: false) },
            },
        };
        _bomTab.Content = TableView(Reports.BillOfMaterials(_doc), "Aparatos agrupados, montaje, bornes y cableado (+10 % de recortes). Escribe la referencia del fabricante en las propiedades de cada aparato.");
        _checksTab.Content = ChecksView(checks);
        var errors = checks.Count(c => c.Level == CheckLevel.Error);
        var warnings = checks.Count(c => c.Level == CheckLevel.Aviso);
        if (_tabs.ItemsSource is TabItem[] items)
            items[6].Header = errors + warnings == 0 ? "Comprobaciones ✓" : $"Comprobaciones ({errors + warnings})";
        if (resetHistory)
        {
            _history.Clear();
            _history.Add(_doc.ToJson());
            _historyPos = 0;
        }
    }

    private void Load(PanelDocument doc, string? path)
    {
        _doc = doc;
        _path = path;
        _dirty = false;
        _plate.Select(null);
        _plate.Fit();
        _door.Fit();
        _view3d.ResetView();
        Refresh(resetHistory: true);
        _host.SetStatus($"Abierto «{doc.Name}». Mira la Vista 3D y las listas de Cableado y Materiales.");
    }

    private void Undo()
    {
        if (_historyPos <= 0) return;
        _historyPos--;
        Restore();
    }

    private void Redo()
    {
        if (_historyPos >= _history.Count - 1) return;
        _historyPos++;
        Restore();
    }

    private void Restore()
    {
        _doc = PanelDocument.FromJson(_history[_historyPos]);
        _plate.Select(null);
        _dirty = true;
        Refresh();
    }

    private void AddDevice(string kind)
    {
        var model = DeviceLibrary.Get(kind);
        var n = 1;
        while (_doc.FindTag($"{model.TagPrefix}{n}") is not null) n++;
        var d = DeviceLibrary.Create(kind, $"{model.TagPrefix}{n}");
        d.Rear = Rear;
        _doc.Devices.Add(d);
        AutoLayout.PlaceInFreeSpot(_doc, d);
        _tabs.SelectedIndex = d.Location == DeviceLocation.Door ? 1 : 0;
        Commit($"Añadido {d.Tag}");
        (d.Location == DeviceLocation.Door ? _door : _plate).Select(d);
    }

    private MenuFlyout? _addFlyout;
    private int _libraryStart;

    /// <summary>Librerías por fabricante, en submenús por marca (se rehace al importar un .edz).</summary>
    private void FillLibraryMenu()
    {
        if (_addFlyout is not { } flyout) return;
        while (flyout.Items.Count > _libraryStart) flyout.Items.RemoveAt(flyout.Items.Count - 1);
        foreach (var brand in ManufacturerCatalog.Brands)
        {
            var sub = new MenuItem { Header = $"Librería {brand}" };
            foreach (var it in ManufacturerCatalog.OfBrand(brand).OrderBy(i => i.Reference, StringComparer.OrdinalIgnoreCase))
            {
                var item = new MenuItem { Header = $"{it.Reference} · {it.Description}{(it.Mesh is null ? "" : "  [3D]")}" };
                item.Click += (_, _) => AddCatalogDevice(it);
                sub.Items.Add(item);
            }
            flyout.Items.Add(sub);
        }
    }

    private async Task ImportLibraryAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Librería o modelo 3D de fabricante", AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Librería o modelo 3D (.edz, .stp, .step, .zip)") { Patterns = ["*.edz", "*.stp", "*.step", "*.zip"] },
                new FilePickerFileType("Paquete de fabricante (.edz)") { Patterns = ["*.edz"] },
                new FilePickerFileType("Modelo STEP de la web del fabricante (.stp, .step, .zip)") { Patterns = ["*.stp", "*.step", "*.zip"] },
                FilePickerFileTypes.All,
            ],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        _host.SetStatus($"Leyendo {paths.Count} archivo(s)…");
        try
        {
            var (packages, steps, notes) = await Task.Run(() => LibraryStore.Split(paths));
            var r = await Task.Run(() => LibraryStore.ImportPackages(packages));
            r.Notes.InsertRange(0, notes);

            // Los STEP se revisan uno a uno (marca, referencia, tipo y frente) antes de guardarlos.
            var chosen = new List<StepImporter.Candidate>();
            var askAll = true;
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Raw.Triangles.Count == 0) { r.Notes.Add($"{steps[i].FileName}: no se encontró geometría que dibujar."); continue; }
                if (!askAll || TopLevel.GetTopLevel(this) is not Window owner) { chosen.Add(steps[i]); continue; }
                var answer = await new StepImportDialog(steps[i], i, steps.Count).ShowDialog<StepDialogResult?>(owner) ?? StepDialogResult.Cancel;
                if (answer == StepDialogResult.Cancel) break;
                if (answer == StepDialogResult.Skip) continue;
                chosen.Add(steps[i]);
                if (answer == StepDialogResult.SaveAll) askAll = false;
            }
            if (chosen.Count > 0)
            {
                _host.SetStatus($"Guardando {chosen.Count} modelo(s) en la librería…");
                var s = await Task.Run(() => LibraryStore.SaveSteps(chosen));
                r.Items.AddRange(s.Items);
                r.Notes.AddRange(s.Notes);
            }
            FillLibraryMenu();
            Refresh();
            var withMesh = r.Items.Count(i => i.Mesh is not null);
            var msg = r.Items.Count == 0
                ? "No se importó nada. " + string.Join(" ", r.Notes.Take(2))
                : $"Importados {r.Items.Count} modelos ({withMesh} con 3D) de {string.Join(", ", r.Items.Select(i => i.Brand).Distinct())}. Están en «Añadir aparato › Librería …».";
            if (r.Items.Count > 0 && r.Notes.Count > 0) msg += $" Avisos: {string.Join(" ", r.Notes.Take(2))}";
            _host.SetStatus(msg);
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo importar: {ex.Message}");
        }
    }

    private void AddCatalogDevice(CatalogItem it)
    {
        var model = DeviceLibrary.Get(it.Kind);
        var n = 1;
        while (_doc.FindTag($"{model.TagPrefix}{n}") is not null) n++;
        var d = DeviceLibrary.Create(it.Kind, $"{model.TagPrefix}{n}");
        ManufacturerCatalog.Apply(d, it);
        d.Rear = Rear;
        _doc.Devices.Add(d);
        AutoLayout.PlaceInFreeSpot(_doc, d);
        _tabs.SelectedIndex = d.Location == DeviceLocation.Door ? 1 : 0;
        Commit($"Añadido {d.Tag} ({it.FullReference})");
        (d.Location == DeviceLocation.Door ? _door : _plate).Select(d);
    }

    private void AddRail()
    {
        var y = _doc.RailsOf(Rear).Select(r => r.Y + 150).DefaultIfEmpty(100).Max();
        var rail = new DinRail { Rear = Rear, X = 45, Y = Math.Min(y, _doc.Enclosure.PlateHeight - 60), Length = Math.Max(100, _doc.Enclosure.PlateWidth - 90) };
        _doc.Rails.Add(rail);
        _tabs.SelectedIndex = 0;
        Commit("Añadido carril");
        _plate.Select(rail);
    }

    private void AddDuct(bool vertical)
    {
        var e = _doc.Enclosure;
        var k = vertical
            ? new Duct { Rear = Rear, Vertical = true, X = e.PlateWidth / 2 - 20, Y = 40, Length = e.PlateHeight - 40 }
            : new Duct { Rear = Rear, X = 40, Y = e.PlateHeight / 2, Length = e.PlateWidth - 80 };
        _doc.Ducts.Add(k);
        _tabs.SelectedIndex = 0;
        Commit("Añadida canaleta");
        _plate.Select(k);
    }

    private void DeleteSelected()
    {
        var canvas = ActiveCanvas;
        switch (canvas.Selected)
        {
            case PanelDevice d: _doc.Devices.Remove(d); break;
            case DinRail r: _doc.Rails.Remove(r); break;
            case Duct k: _doc.Ducts.Remove(k); break;
            default: return;
        }
        canvas.Select(null);
        Commit("Borrado");
    }

    // ------------------------------------------------------------------ esquema

    private Schematic? OpenSchematic()
    {
        var json = ModuleExchange.TryGet(ModuleExchange.ElectricSchematic);
        if (json is null)
        {
            _host.SetStatus("Abre primero la pestaña «Instalaciones eléctricas» con el esquema que quieres montar (o usa «Nuevo desde un esquema .selx…»).");
            return null;
        }
        var s = Schematic.FromJson(json);
        if (s.Elements.Count == 0)
        {
            _host.SetStatus("El esquema de Instalaciones eléctricas está vacío.");
            return null;
        }
        return s;
    }

    private void FromOpenSchematic()
    {
        if (OpenSchematic() is { } s) Load(PanelBuilder.FromSchematic(s), null);
    }

    private void UpdateFromOpenSchematic()
    {
        var s = OpenSchematic();
        if (s is null) return;
        var msg = PanelBuilder.UpdateFromSchematic(_doc, s);
        Commit(msg);
    }

    private void NewEmpty()
    {
        var doc = new PanelDocument();
        AutoLayout.Arrange(doc, chooseEnclosure: false);
        doc.Rails.Add(new DinRail { X = 45, Y = 100, Length = doc.Enclosure.PlateWidth - 90 });
        Load(doc, null);
    }

    // ------------------------------------------------------------------ archivos

    private static readonly FilePickerFileType PanelType = new("Tablero (.tablero)") { Patterns = ["*.tablero"] };
    private static readonly FilePickerFileType SelxType = new("Esquema eléctrico (.selx)") { Patterns = ["*.selx"] };

    private async Task FromSelxAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Esquema del tablero", FileTypeFilter = [SelxType, FilePickerFileTypes.All] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            Load(PanelBuilder.FromSchematic(Schematic.FromJson(await File.ReadAllTextAsync(path))), null);
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo leer {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private async Task ImportPdfAsync(bool? doubleFront)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Plano eléctrico en PDF", FileTypeFilter = [new FilePickerFileType("Plano PDF") { Patterns = ["*.pdf"] }, FilePickerFileTypes.All],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _host.SetStatus($"Leyendo {Path.GetFileName(path)}…");
        try
        {
            // Un plano de cien páginas tarda unos segundos (varios minutos si hay que reconocer letras dibujadas):
            // se lee fuera de la interfaz y la barra de estado dice por qué página va.
            var progress = new Progress<string>(m => _host.SetStatus($"{Path.GetFileName(path)}: {m}"));
            var result = await Task.Run(() => PlanImporter.Import(path, PlanOcr.Default(), progress, doubleFront: doubleFront));
            _rearSide.IsChecked = false;
            Load(result.Document, null);
            _dirty = true;
            Refresh();
            _tabs.SelectedIndex = result.Document.Devices.Count > 0 ? 0 : ChecksTabIndex;
            var d = result.Document;
            _host.SetStatus(d.Devices.Count == 0
                ? $"{Path.GetFileName(path)}: no se encontraron aparatos. Mira el informe en Comprobaciones."
                : $"{Path.GetFileName(path)}: {result.Pages} páginas, {d.Devices.Count} aparatos y {d.Wires.Count} hilos. Informe completo en Comprobaciones.");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo leer {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private const int ChecksTabIndex = 6;

    private async Task OpenAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir tablero", FileTypeFilter = [PanelType, FilePickerFileTypes.All] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            Load(PanelDocument.FromJson(await File.ReadAllTextAsync(path)), path);
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
                Title = "Guardar tablero", DefaultExtension = "tablero", SuggestedFileName = _doc.Name, FileTypeChoices = [PanelType],
            });
            path = file?.TryGetLocalPath();
            if (path is null) return;
        }
        await File.WriteAllTextAsync(path, _doc.ToJson());
        _path = path;
        _dirty = false;
        Refresh();
        _host.SetStatus($"Guardado {Path.GetFileName(path)}");
    }

    private async Task ExportAsync(ReportTable table, string suffix)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Exportar {table.Title.ToLowerInvariant()}", DefaultExtension = "csv", SuggestedFileName = $"{_doc.Name} - {suffix}",
            FileTypeChoices = [new FilePickerFileType("CSV para Excel") { Patterns = ["*.csv"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, table.ToCsv(), new System.Text.UTF8Encoding(true));
        _host.SetStatus($"Exportado {Path.GetFileName(path)}");
    }

    // ------------------------------------------------------------------ tablas

    private Control TableView(ReportTable table, string? note, bool scroll = true)
    {
        var grid = new Grid { Margin = new Thickness(0, 4) };
        for (var c = 0; c < table.Headers.Count; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < table.Headers.Count; c++)
        {
            var h = new Border
            {
                Background = Application.Current?.FindResource("SimAccentSoftBrush") as IBrush ?? Brushes.AliceBlue,
                Padding = new Thickness(8, 4),
                Child = new TextBlock { Text = table.Headers[c], FontWeight = FontWeight.SemiBold },
            };
            Grid.SetColumn(h, c);
            grid.Children.Add(h);
        }
        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                var cell = new Border
                {
                    Padding = new Thickness(8, 3),
                    Background = r % 2 == 1 ? new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA)) : Brushes.Transparent,
                    Child = new SelectableTextBlock { Text = table.Rows[r][c], MaxWidth = 460, TextWrapping = TextWrapping.Wrap },
                };
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = table.Title, FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = $"{table.Rows.Count} líneas", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        var copy = new Button { Content = "📋 Copiar para Excel" };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(table.ToTabbed());
            _host.SetStatus($"{table.Title} copiada: pégala en Excel.");
        };
        var csv = new Button { Content = "💾 Guardar CSV…" };
        csv.Click += (_, _) => _ = ExportAsync(table, table.Title.ToLowerInvariant());
        header.Children.Add(copy);
        header.Children.Add(csv);

        var stack = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        stack.Children.Add(header);
        if (note is not null) stack.Children.Add(new TextBlock { Text = note, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(grid);
        return scroll ? new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } : stack;
    }

    private Control ChecksView(List<PanelCheck> checks)
    {
        var stack = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = "Comprobaciones", FontSize = 16, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = "Se repasan solas con cada cambio. Haz clic en una línea con designación para seleccionar el aparato. " +
                   "El cálculo de temperatura es una estimación simplificada (método de superficie eficaz de IEC 60890).",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
        });
        foreach (var c in checks)
        {
            var (icon, color) = c.Level switch
            {
                CheckLevel.Error => ("⛔", Color.FromRgb(0xFD, 0xEC, 0xEA)),
                CheckLevel.Aviso => ("⚠", Color.FromRgb(0xFF, 0xF6, 0xE0)),
                _ => ("ℹ", Color.FromRgb(0xEE, 0xF4, 0xFB)),
            };
            var row = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(color),
                Content = new TextBlock { Text = $"{icon}  {c.Message}", TextWrapping = TextWrapping.Wrap },
            };
            row.Click += (_, _) =>
            {
                if (c.Tag is null || _doc.FindTag(c.Tag) is not { } d || d.Location == DeviceLocation.Field) return;
                _tabs.SelectedIndex = d.Location == DeviceLocation.Door ? 1 : 0;
                (d.Location == DeviceLocation.Door ? _door : _plate).Select(d);
            };
            stack.Children.Add(row);
        }
        return new ScrollViewer { Content = stack };
    }
}
