using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Sketch;
using Simulador.Core.Modules;
using Simulador.Electrico.Editor;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.Profiles;

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
    private readonly ToggleButton _showCurrent = new() { Content = "• Corriente", IsChecked = true };
    private readonly ToggleButton _showVoltage = new() { Content = "⚡ Tensiones", IsChecked = true };
    private readonly Dictionary<Guid, PlcCpu> _plcs = [];
    private readonly Dictionary<Guid, McuIoBoard> _mcus = [];
    private readonly Dictionary<Guid, int> _mcuSerialShown = [];
    private readonly HashSet<Guid> _plcFaultShown = [];
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
        LoadImportedSymbols();

        _canvas = new SchematicCanvas(_catalog);
        _props = new PropertiesPanel(_catalog, () => _presets);
        _canvas.SelectionChanged += () => _props.Show(_canvas.Selected, _sim is not null);
        _canvas.Modified += () => { _dirty = true; UpdateTitle(); };
        _canvas.Hint += t => _host.SetStatus(t);
        _props.Changed += () => { _dirty = true; _canvas.InvalidateVisual(); UpdateTitle(); };
        _props.MotorLoadChanged += (id, pct) => _sim?.SetMotorLoad(id, pct);
        _props.VfdReferenceChanged += (id, hz) => _sim?.SetVfdReference(id, hz);
        _props.PlcStatus = PlcStatusText;
        _props.TransmitterValue = id => _sim?.Status(id).Percent ?? 50;
        _props.TransmitterChanged += (id, pct) => _sim?.SetTransmitter(id, pct);
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
        ToolTip.SetTip(_showCurrent, "Puntos amarillos que recorren los cables con la corriente: en continua avanzan; en alterna van y vienen (ralentizado). Más rápido = más corriente.");
        ToolTip.SetTip(_showVoltage, "Color de los cables según su tensión en cada instante: verde positiva, rojo negativa, gris sin tensión (como Falstad).");
        _showCurrent.IsCheckedChanged += (_, _) => { _canvas.ShowCurrent = _showCurrent.IsChecked == true; _canvas.InvalidateVisual(); };
        _showVoltage.IsCheckedChanged += (_, _) => { _canvas.ShowVoltage = _showVoltage.IsChecked == true; _canvas.InvalidateVisual(); };

        // WrapPanel: en pantallas estrechas los botones pasan a una segunda fila en vez de cortarse.
        var bar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
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
                _showCurrent,
                _showVoltage,
                new Separator { Width = 12 },
                B("Importar catálogo CSV…", () => _ = ImportCatalogAsync(), "Añade aparatos reales de fabricante (contactores, guardamotores, motores…) desde un CSV"),
                B("Importar símbolos QElectroTech…", () => _ = ImportQetAsync(),
                    "Añade símbolos .elmt de QElectroTech (la librería libre con miles de aparatos de Schneider, Siemens, ABB, Legrand…). " +
                    "Bobinas y contactos NA/NC se simulan; el resto queda como dibujo con bornes."),
                B("Girar (R)", RotateSelected, "Gira 90° el elemento seleccionado"),
                B("Borrar (Supr)", () => _canvas.DeleteSelection(), "Borra el elemento o conductor seleccionado"),
            },
        };
        foreach (var c in bar.Children.OfType<Control>()) c.Margin = new Thickness(0, 2, 6, 2);
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
        StartPlcs();
        _run.Content = "⏸ Editar";
        _props.Show(_canvas.Selected, true);
        _timer.Start();
        _host.SetStatus($"Simulando «{_canvas.Schematic.Name}». Haz clic en los pulsadores y protecciones.");
        Tick();
    }

    /// <summary>
    /// Cada PLC del esquema lleva dentro una CPU del módulo PLC (perfil genérico IEC, direcciones %IX/%QX) con su
    /// programa. El simulador eléctrico la avanza con su tiempo de ciclo y le pasa la tensión real de cada entrada.
    /// </summary>
    private void StartPlcs()
    {
        _plcs.Clear();
        DisposeMcus();
        _plcFaultShown.Clear();
        if (_sim is null) return;
        foreach (var e in _canvas.Schematic.Elements.Where(x => _catalog.TryGet(x.Type, out var t) && t.Behavior == Behavior.Plc))
        {
            var tag = string.IsNullOrWhiteSpace(e.Tag) ? "KF" : e.Tag.Trim();
            if (e.Type == ElementCatalog.McuTypeId)
            {
                StartMcu(e, tag);
                continue;
            }
            var cpu = new PlcCpu(tag, PlcProfiles.Get("generico-iec"));
            try
            {
                var ladder = e.Text(PlcProgramKeys.Ladder);
                if (ladder.Length > 0) cpu.LoadLadder(LadderFile.Load(ladder).Program);
                else cpu.LoadStructuredText(e.Text(PlcProgramKeys.StructuredText, PlcProgramKeys.Template));
                cpu.Start();
                foreach (var w in _sim.AttachController(tag, cpu)) AddLogLine($"{tag}: {w}", EventSeverity.Warning);
                _plcs[e.Id] = cpu;
                AddLogLine($"{tag}: programa cargado, PLC en RUN.", EventSeverity.Info);
            }
            catch (Exception ex)
            {
                AddLogLine($"{tag}: el programa tiene errores y el PLC queda en STOP. {ex.Message}", EventSeverity.Fault);
                _host.SetStatus($"{tag}: error en el programa del PLC: {ex.Message}");
            }
        }
    }

    /// <summary>Microcontrolador con placa de relés: carga su programa (C++ o MicroPython) y lo une a los bornes.</summary>
    private void StartMcu(PlacedElement e, string tag)
    {
        if (_sim is null || !_catalog.TryGet(e.Type, out var type)) return;
        var board = McuProgramKeys.Board((int)e.Param(type, "placa"));
        var io = new McuIoBoard(tag, board);
        try
        {
            io.Load(e.Text(McuProgramKeys.Code, McuIoBoard.Template(board)));
            foreach (var w in _sim.AttachController(tag, io)) AddLogLine($"{tag}: {w}", EventSeverity.Warning);
            _mcus[e.Id] = io;
            _mcuSerialShown[e.Id] = 0;
            AddLogLine($"{tag}: programa cargado en {board.Name}, en marcha.", EventSeverity.Info);
        }
        catch (SketchCompileException ex)
        {
            io.Dispose();
            AddLogLine($"{tag}: error en la línea {ex.Line} del programa: {ex.Detail}. La placa queda parada.", EventSeverity.Fault);
            _host.SetStatus($"{tag}: error en la línea {ex.Line} del programa de {board.Name}: {ex.Detail}");
        }
    }

    private void DisposeMcus()
    {
        foreach (var m in _mcus.Values) m.Dispose();
        _mcus.Clear();
        _mcuSerialShown.Clear();
    }

    /// <summary>Lo que imprime cada placa (Serial.println o print) va al registro, y sus errores también.</summary>
    private void ShowMcuOutput()
    {
        foreach (var (id, io) in _mcus)
        {
            var mcu = io.Mcu;
            var serial = mcu.SerialOutput;
            int shown = _mcuSerialShown.GetValueOrDefault(id);
            if (serial.Length < shown) shown = 0;
            int end = serial.LastIndexOf('\n');
            if (end >= shown)
            {
                foreach (var line in serial[shown..end].Split('\n'))
                    if (line.Trim().Length > 0) AddLogLine($"{io.Id}: {line.TrimEnd('\r')}", EventSeverity.Info);
                _mcuSerialShown[id] = end + 1;
            }
            if (mcu.State == McuState.Fault && _plcFaultShown.Add(id))
                AddLogLine($"{io.Id}: error en la línea {mcu.FaultLine}: {mcu.FaultMessage}", EventSeverity.Fault);
        }
    }

    private string PlcStatusText(PlacedElement e)
    {
        if (_mcus.TryGetValue(e.Id, out var io))
        {
            var m = io.Mcu;
            var state = m.State switch
            {
                McuState.Running => $"en marcha, línea {m.CurrentLine}",
                McuState.Fault => $"parado por un error en la línea {m.FaultLine}: {m.FaultMessage}",
                _ when m.ProgramEnded => "el programa terminó (las salidas se quedan como estaban)",
                _ => "parado",
            };
            return $"{m.Board.Name}: {state}. Tiempo: {m.Uptime.TotalSeconds:0.0} s.";
        }
        if (e.Type == ElementCatalog.McuTypeId) return "La placa no tiene un programa válido (mira el registro).";
        if (!_plcs.TryGetValue(e.Id, out var cpu)) return "El PLC no tiene un programa válido (mira el registro).";
        var text = $"Estado: {cpu.State}. Ciclos ejecutados: {cpu.ScanCount}.";
        if (cpu.FaultMessage is not null) text += $" Fallo: {cpu.FaultMessage}";
        if (cpu.Diagnostics.Count > 0) text += " " + string.Join(" ", cpu.Diagnostics);
        return text;
    }

    private void AddLogLine(string text, EventSeverity severity)
    {
        var line = new TextBlock
        {
            Text = $"{_sim?.Time ?? 0,7:0.000} s  {text}",
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
        };
        if (severity == EventSeverity.Fault) line.Foreground = SymbolPainter.Fault;
        else if (severity == EventSeverity.Warning) line.Foreground = new SolidColorBrush(Color.Parse("#E08A00"));
        _events.Items.Insert(0, line);
    }

    private void StopSim()
    {
        _timer.Stop();
        _plcs.Clear();
        DisposeMcus();
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
        _plcFaultShown.Clear();
        foreach (var cpu in _plcs.Values) { try { cpu.Start(); } catch (InvalidOperationException) { } }
        foreach (var io in _mcus.Values) { io.Reset(); io.Mcu.Start(); }
        foreach (var id in _mcus.Keys) _mcuSerialShown[id] = 0;
        Tick();
    }

    private void Tick()
    {
        if (_sim is null) return;
        var speed = Speeds[Math.Max(0, _speed.SelectedIndex)];
        _sim.Run(0.033 * speed);
        _canvas.Animate(0.033);
        _clock.Text = $"t = {_sim.Time:0.0} s";
        foreach (var (id, cpu) in _plcs)
            if ((cpu.State == PlcState.Fault || cpu.Diagnostics.Count > 0) && _plcFaultShown.Add(id))
                AddLogLine($"{cpu.Id}: {cpu.FaultMessage ?? string.Join(" ", cpu.Diagnostics)}", EventSeverity.Fault);
        ShowMcuOutput();
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

    private string SymbolDir => Path.Combine(_host.GetDataDirectory("electrico"), "simbolos");

    /// <summary>Símbolos de QElectroTech importados en sesiones anteriores (se guardan copiados en la carpeta de datos).</summary>
    private void LoadImportedSymbols()
    {
        try
        {
            if (!Directory.Exists(SymbolDir)) return;
            foreach (var f in Directory.GetFiles(SymbolDir, "*.elmt"))
            {
                try { _catalog.Register(QetImporter.Parse(File.ReadAllText(f), f, _catalog).Type); }
                catch (FormatException) { }
            }
        }
        catch (IOException) { }
    }

    private async Task ImportQetAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar símbolos de QElectroTech (.elmt)",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Elemento QElectroTech") { Patterns = ["*.elmt"] }, FilePickerFileTypes.All],
        });
        int simulated = 0, drawn = 0;
        var errors = new List<string>();
        foreach (var path in files.Select(f => f.TryGetLocalPath()).OfType<string>())
        {
            try
            {
                var r = QetImporter.Parse(await File.ReadAllTextAsync(path), path, _catalog);
                _catalog.Register(r.Type);
                if (r.Type.Behavior == Behavior.Graphic) drawn++; else simulated++;
                Directory.CreateDirectory(SymbolDir);
                File.Copy(path, Path.Combine(SymbolDir, Path.GetFileName(path)), overwrite: true);
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                errors.Add(ex.Message);
            }
        }
        FillPalette();
        var msg = $"{simulated + drawn} símbolos importados en «{ElementCatalog.CatImported}»: {simulated} se simulan y {drawn} son solo dibujo.";
        if (errors.Count > 0) msg += $" {errors.Count} con errores: {string.Join(" | ", errors.Take(2))}";
        _host.SetStatus(msg);
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
