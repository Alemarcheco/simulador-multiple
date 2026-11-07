using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Simulador.Core.Modules;
using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Fbd;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc;

/// <summary>
/// Vista principal: marca de PLC, editor de programa (ST / Ladder), RUN/STOP, panel de E/S virtual y tabla de observación.
/// </summary>
internal sealed class PlcView : UserControl
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly IBrush LampOn = new SolidColorBrush(Color.Parse("#F5A623"));
    private static readonly IBrush LampOff = new SolidColorBrush(Color.Parse("#40808080"));

    private readonly IModuleHost _host;
    private readonly ComboBox _profileBox;
    private readonly TabControl _languages;
    private readonly TextBox _stEditor;
    private readonly TextBox _ilEditor;
    private readonly TextBox _sfcEditor;
    private readonly ContentControl _sfcHost = new();
    private SfcView? _sfcView;
    private readonly ComboBox _ladderExamples;
    private readonly LadderEditor _ladder;
    private readonly ComboBox _fbdExamples;
    private readonly FbdEditor _fbd;
    private readonly NumericUpDown _cycle;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _watch = new() { FontFamily = Mono, FontSize = 12 };
    private readonly ContentControl _ioHost = new();
    private readonly ContentControl _deviceHost = new();
    private readonly List<Action> _ioRefresh = [];
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _wall = new();

    private PlcCpu _cpu;
    private TimeSpan _simTime;
    private TimeSpan _wallAtStart;

    public PlcView(IModuleHost host)
    {
        _host = host;
        _cpu = new PlcCpu("PLC1", PlcProfiles.All[1]);

        _profileBox = new ComboBox { ItemsSource = PlcProfiles.All, SelectedIndex = 1, MinWidth = 260 };
        _profileBox.SelectionChanged += (_, _) => ChangeProfile();

        _cycle = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 10, Increment = 1, FormatString = "0", Width = 120 };
        _cycle.ValueChanged += (_, _) => _cpu.CycleTime = TimeSpan.FromMilliseconds((double)(_cycle.Value ?? 10));

        var load = new Button { Content = "Compilar y cargar" };
        load.Click += (_, _) => LoadProgram();
        var run = new Button { Content = "▶ RUN" };
        run.Click += (_, _) => StartPlc();
        var stop = new Button { Content = "■ STOP" };
        stop.Click += (_, _) => StopPlc();

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        foreach (var c in new Control[]
                 {
                     Label("PLC:"), _profileBox, Label("Ciclo (ms):"), _cycle, load, run, stop,
                 })
        {
            c.Margin = new Thickness(0, 0, 8, 4);
            c.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(c);
        }

        // --- Texto estructurado ---
        var stPanel = TextLanguagePanel(Examples.StructuredText.Select(e => (e.Name, e.Source)).ToList(), out _stEditor);
        var ilPanel = TextLanguagePanel(Examples.InstructionList.Select(e => (e.Name, e.Source)).ToList(), out _ilEditor);
        var sfcText = TextLanguagePanel(Examples.Sfc.Select(e => (e.Name, e.Source)).ToList(), out _sfcEditor);
        _sfcEditor.TextChanged += (_, _) => DrawSfc();
        var sfcGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        sfcGrid.Children.Add(sfcText);
        var sfcSplit = new GridSplitter { Width = 4, Background = Brushes.Transparent };
        Grid.SetColumn(sfcSplit, 1);
        sfcGrid.Children.Add(sfcSplit);
        Grid.SetColumn(_sfcHost, 2);
        sfcGrid.Children.Add(_sfcHost);

        // --- Ladder ---
        _ladder = new LadderEditor(ReadSafe, Display,
            () => _cpu.Profile.Format(_cpu.Profile.InputAddress(0)),
            () => _cpu.Profile.Format(_cpu.Profile.OutputAddress(0)));
        _ladderExamples = new ComboBox { ItemsSource = Examples.Ladder.Select(e => e.Name).ToList(), SelectedIndex = 0, MinWidth = 220 };
        _ladderExamples.SelectionChanged += (_, _) => ShowLadderExample();
        var newLadder = new Button { Content = "Nuevo" };
        newLadder.Click += (_, _) => { _ladder.Document = new LadderDocument(); _ladderExamples.SelectedIndex = -1; };
        var openLadder = new Button { Content = "Abrir…" };
        openLadder.Click += async (_, _) => await OpenLadderAsync();
        var saveLadder = new Button { Content = "Guardar…" };
        saveLadder.Click += async (_, _) => await SaveLadderAsync();
        var ldPanel = new DockPanel();
        var ldTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        ldTop.Children.Add(Label("Ejemplo:"));
        ldTop.Children.Add(_ladderExamples);
        ldTop.Children.Add(newLadder);
        ldTop.Children.Add(openLadder);
        ldTop.Children.Add(saveLadder);
        DockPanel.SetDock(ldTop, Dock.Top);
        ldPanel.Children.Add(ldTop);
        ldPanel.Children.Add(_ladder);

        // --- FBD ---
        _fbd = new FbdEditor(ReadSafe, Display,
            () => _cpu.Profile.Format(_cpu.Profile.InputAddress(0)),
            () => _cpu.Profile.Format(_cpu.Profile.OutputAddress(0)));
        _fbdExamples = new ComboBox { ItemsSource = Examples.Fbd.Select(e => e.Name).ToList(), SelectedIndex = 0, MinWidth = 220 };
        _fbdExamples.SelectionChanged += (_, _) => ShowFbdExample();
        var newFbd = new Button { Content = "Nuevo" };
        newFbd.Click += (_, _) => { _fbd.Program = new FbdProgram(); _fbdExamples.SelectedIndex = -1; };
        var openFbd = new Button { Content = "Abrir…" };
        openFbd.Click += async (_, _) => await OpenFbdAsync();
        var saveFbd = new Button { Content = "Guardar…" };
        saveFbd.Click += async (_, _) => await SaveFbdAsync();
        var fbdPanel = new DockPanel();
        var fbdTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        fbdTop.Children.Add(Label("Ejemplo:"));
        fbdTop.Children.Add(_fbdExamples);
        fbdTop.Children.Add(newFbd);
        fbdTop.Children.Add(openFbd);
        fbdTop.Children.Add(saveFbd);
        DockPanel.SetDock(fbdTop, Dock.Top);
        fbdPanel.Children.Add(fbdTop);
        fbdPanel.Children.Add(_fbd);

        _languages = new TabControl();
        _languages.Items.Add(new TabItem { Header = "Texto estructurado (ST)", Content = stPanel, Tag = "st" });
        _languages.Items.Add(new TabItem { Header = "Ladder (LD)", Content = ldPanel, Tag = "ld" });
        _languages.Items.Add(new TabItem { Header = "Bloques de función (FBD)", Content = fbdPanel, Tag = "fbd" });
        _languages.Items.Add(new TabItem { Header = "Lista de instrucciones (IL)", Content = ilPanel, Tag = "il" });
        _languages.Items.Add(new TabItem { Header = "SFC / GRAFCET", Content = sfcGrid, Tag = "sfc" });

        // --- Panel derecho: E/S y observación ---
        var right = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        right.Children.Add(new TextBlock { Text = "Equipo", FontSize = 18 });
        right.Children.Add(_deviceHost);
        right.Children.Add(new TextBlock { Text = "Panel de E/S", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        right.Children.Add(_ioHost);
        right.Children.Add(new TextBlock { Text = "Tabla de observación", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        right.Children.Add(_watch);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,Auto,2*") };
        grid.Children.Add(new Border { Padding = new Thickness(8), Child = _languages });
        var splitter = new GridSplitter { Width = 4, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        var rightScroll = new ScrollViewer { Content = right };
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);

        var statusBar = new Border { Padding = new Thickness(8, 4), Child = _status };

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(grid);
        Content = root;

        BuildIoPanel();
        ShowLadderExample();
        ShowFbdExample();
        DrawSfc();
        UpdateStatus();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) => OnTimer());
        AttachedToVisualTree += (_, _) => { _wall.Start(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); _wall.Stop(); };
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Redibuja el diagrama SFC a partir del texto (si el texto aún no es válido, muestra el error).</summary>
    private void DrawSfc()
    {
        try
        {
            _sfcView = new SfcView(Parser.ParseSfc(_sfcEditor.Text ?? ""), ReadSafe, Display);
            _sfcHost.Content = _sfcView;
        }
        catch (PlcCompileException ex)
        {
            _sfcView = null;
            _sfcHost.Content = new TextBlock { Text = ex.Message, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
        }
    }

    private string? Language => (_languages.SelectedItem as TabItem)?.Tag as string;

    private static readonly FontFamily EditorFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    /// <summary>Editor de texto con selector de ejemplos (para ST e IL).</summary>
    private static Control TextLanguagePanel(IReadOnlyList<(string Name, string Source)> examples, out TextBox editor)
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = EditorFont,
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            Text = examples[0].Source,
        };
        var combo = new ComboBox { ItemsSource = examples.Select(e => e.Name).ToList(), SelectedIndex = 0, MinWidth = 260 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) box.Text = examples[combo.SelectedIndex].Source;
        };
        var panel = new DockPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(Label("Ejemplo:"));
        top.Children.Add(combo);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(box);
        editor = box;
        return panel;
    }

    private string Display(string operand)
    {
        if (operand.StartsWith('%') && _cpu.Profile.TryParse(operand, out var a)) return _cpu.Profile.Format(a);
        return operand;
    }

    private void ShowLadderExample()
    {
        if (_ladderExamples.SelectedIndex < 0) return;
        _ladder.Document = LadderDocument.FromProgram(Examples.Ladder[_ladderExamples.SelectedIndex].Program);
    }

    private void ShowFbdExample()
    {
        if (_fbdExamples.SelectedIndex < 0) return;
        _fbd.Program = Examples.Fbd[_fbdExamples.SelectedIndex].Create();
    }

    private static readonly FilePickerFileType FbdFileType = new("Programa FBD") { Patterns = ["*" + FbdCompiler.Extension] };

    private async Task OpenFbdAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir programa FBD", FileTypeFilter = [FbdFileType] });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var program = FbdCompiler.Load(await reader.ReadToEndAsync());
            _fbd.Program = program;
            _fbdExamples.SelectedIndex = -1;
            if (program.Profile is not null && PlcProfiles.All.FirstOrDefault(p => p.Id == program.Profile) is { } profile)
                _profileBox.SelectedItem = profile;
            _host.SetStatus($"Abierto {files[0].Name}.");
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            _host.SetStatus("No se pudo abrir: " + ex.Message);
        }
    }

    private async Task SaveFbdAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar programa FBD",
            SuggestedFileName = "programa" + FbdCompiler.Extension,
            FileTypeChoices = [FbdFileType],
        });
        if (file is null) return;
        try
        {
            _fbd.Program.Profile = _cpu.Profile.Id;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(FbdCompiler.Save(_fbd.Program));
            _host.SetStatus($"Guardado {file.Name}.");
        }
        catch (IOException ex)
        {
            _host.SetStatus("No se pudo guardar: " + ex.Message);
        }
    }

    private static readonly FilePickerFileType LadderFileType = new("Programa Ladder") { Patterns = ["*" + LadderFile.Extension] };

    private async Task OpenLadderAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir programa Ladder",
            FileTypeFilter = [LadderFileType],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var doc = LadderFile.Load(await reader.ReadToEndAsync());
            _ladder.Document = LadderDocument.FromProgram(doc.Program);
            _ladderExamples.SelectedIndex = -1;
            if (doc.ProfileId is not null && PlcProfiles.All.FirstOrDefault(p => p.Id == doc.ProfileId) is { } profile)
                _profileBox.SelectedItem = profile;
            _host.SetStatus($"Abierto {files[0].Name}.");
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            _host.SetStatus("No se pudo abrir: " + ex.Message);
        }
    }

    private async Task SaveLadderAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar programa Ladder",
            SuggestedFileName = "programa" + LadderFile.Extension,
            FileTypeChoices = [LadderFileType],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(LadderFile.Save(_ladder.Document.ToProgram(), _cpu.Profile.Id));
            _host.SetStatus($"Guardado {file.Name}.");
        }
        catch (IOException ex)
        {
            _host.SetStatus("No se pudo guardar: " + ex.Message);
        }
    }

    private object? ReadSafe(string operand)
    {
        try { return _cpu.Program is null ? null : _cpu.Read(operand); }
        catch { return null; }
    }

    private void ChangeProfile()
    {
        if (_profileBox.SelectedItem is not PlcProfile p || p == _cpu.Profile) return;
        var old = _cpu.Profile;
        var program = _cpu.Program;
        bool wasLadder = program?.Name == "Ladder";
        bool wasFbd = program?.Name == "FBD";

        // Las direcciones del editor Ladder se traducen a la nomenclatura de la nueva marca (I0.0 → X0…).
        string Translate(string op) =>
            !op.StartsWith('%') && old.TryParse(op, out var a) && p.TryParse(p.Format(a), out _) ? p.Format(a) : op;
        _ladder.Document.TranslateOperands(Translate);
        _fbd.Program.TranslateOperands(Translate);

        _cpu = new PlcCpu("PLC1", p) { CycleTime = TimeSpan.FromMilliseconds((double)(_cycle.Value ?? 10)) };
        try
        {
            if (wasLadder) _cpu.LoadLadder(_ladder.Document.ToProgram());
            else if (wasFbd) _cpu.LoadFbd(_fbd.Program);
            else if (program is not null) _cpu.Load(program);
        }
        catch (PlcCompileException ex)
        {
            _host.SetStatus($"El programa no es válido para {p.DisplayName}: {ex.Message}");
        }
        BuildIoPanel();
        _ladder.Document = _ladder.Document; // redibuja con la nomenclatura de la nueva marca
        _fbd.Program = _fbd.Program;
        UpdateStatus();
    }

    private void LoadProgram()
    {
        try
        {
            switch (Language)
            {
                case "ld": _cpu.LoadLadder(_ladder.Document.ToProgram()); break;
                case "fbd": _cpu.LoadFbd(_fbd.Program); break;
                case "il": _cpu.LoadInstructionList(_ilEditor.Text ?? ""); break;
                case "sfc": _cpu.LoadSfc(_sfcEditor.Text ?? ""); break;
                default: _cpu.LoadStructuredText(_stEditor.Text ?? ""); break;
            }
            _host.SetStatus($"Programa cargado en {_cpu.Profile.DisplayName}.");
        }
        catch (PlcCompileException ex)
        {
            // En Ladder la "línea" es el número de segmento.
            string msg = Language switch
            {
                "ld" => ex.Message.Replace($"Línea {ex.Line}, columna {ex.Column}", $"Segmento {ex.Line}"),
                "fbd" => ex.Message.Replace($"Línea {ex.Line}, columna {ex.Column}", $"Red {ex.Line}"),
                _ => ex.Message,
            };
            _host.SetStatus("Error de compilación: " + msg);
            _status.Text = "Error de compilación: " + msg;
            return;
        }
        UpdateStatus();
    }

    private void StartPlc()
    {
        if (_cpu.Program is null) LoadProgram();
        if (_cpu.Program is null) return;
        _simTime = TimeSpan.Zero;
        _wallAtStart = _wall.Elapsed;
        _cpu.Start();
        UpdateStatus();
    }

    private void StopPlc()
    {
        _cpu.Stop();
        UpdateStatus();
    }

    /// <summary>Avanza el tiempo de simulación al ritmo del reloj real, en ciclos de scan completos.</summary>
    private void OnTimer()
    {
        var target = _wall.Elapsed - _wallAtStart;
        var cycle = _cpu.CycleTime;
        int n = 0;
        while (_simTime + cycle <= target && n++ < 100)
        {
            _simTime += cycle;
            _cpu.Scan(_simTime);
        }
        if (n >= 100) _wallAtStart = _wall.Elapsed - _simTime; // si el PC no da abasto, la simulación va más lenta en vez de saltar

        foreach (var r in _ioRefresh) r();
        _ladder.Refresh();
        _fbd.Refresh();
        _sfcView?.Refresh();
        UpdateWatch();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string state = _cpu.State switch { PlcState.Run => "RUN", PlcState.Fault => "FALLO", _ => "STOP" };
        var sb = new StringBuilder($"{_cpu.Profile.DisplayName} · {state} · ciclos: {_cpu.ScanCount}");
        if (_cpu.Program is null) sb.Append(" · sin programa cargado");
        if (_cpu.FaultMessage is not null) sb.Append(" · ").Append(_cpu.FaultMessage);
        foreach (var d in _cpu.Diagnostics) sb.Append(" · ⚠ ").Append(d);
        _status.Text = sb.ToString();
        _status.Foreground = _cpu.State == PlcState.Fault ? Brushes.IndianRed : null;
    }

    private void UpdateWatch()
    {
        var sb = new StringBuilder();
        foreach (var v in _cpu.Variables.Where(v => !v.Name.StartsWith("__", StringComparison.Ordinal)))
        {
            if (v.Block is not null)
            {
                sb.Append(v.Name).Append(" (").Append(v.TypeName).Append(")  ");
                sb.AppendJoin("  ", v.Block.Members.Select(m => $"{m.Key}={m.Value}"));
                sb.AppendLine();
            }
            else
            {
                var value = ReadSafe(v.Name);
                string at = v.Address is { } a ? $" AT {_cpu.Profile.Format(a)}" : "";
                sb.AppendLine($"{v.Name}{at} : {v.TypeName} = {value}");
            }
        }
        _watch.Text = sb.Length == 0 ? "(carga un programa para ver sus variables)" : sb.ToString();
    }

    private void BuildIoPanel()
    {
        _ioRefresh.Clear();
        var toggles = new Dictionary<ISignalPort, ToggleButton>();
        var panel = new StackPanel { Spacing = 4 };
        double nominal = _cpu.Profile.InputNominalVolts;

        panel.Children.Add(new TextBlock { Text = $"Entradas digitales ({nominal:0} V {(_cpu.Profile.InputCommon == CommonType.NPN ? "NPN" : "PNP")})", FontWeight = FontWeight.SemiBold });
        foreach (var port in _cpu.DigitalInputs)
        {
            bool held = false, latched = false;
            void Apply() => port.Value = held || latched ? nominal : 0;

            var push = new Button { Content = "Pulsar", Padding = new Thickness(8, 2) };
            push.AddHandler(PointerPressedEvent, (_, _) => { held = true; Apply(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
            push.AddHandler(PointerReleasedEvent, (_, _) => { held = false; Apply(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
            push.AddHandler(PointerCaptureLostEvent, (_, _) => { held = false; Apply(); }, RoutingStrategies.Bubble, handledEventsToo: true);
            var toggle = new ToggleButton { Content = "Fijo", Padding = new Thickness(8, 2) };
            toggles[port] = toggle;
            toggle.IsCheckedChanged += (_, _) => { latched = toggle.IsChecked == true; Apply(); };

            var lamp = new Ellipse { Width = 12, Height = 12, Fill = LampOff };
            _ioRefresh.Add(() => lamp.Fill = _cpu.Image.GetBit(Find(port)) ? Brushes.LimeGreen : LampOff);
            panel.Children.Add(Row(lamp, ShortId(port), push, toggle));
        }

        if (_cpu.AnalogInputs.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Entradas analógicas (0-10 V)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var port in _cpu.AnalogInputs)
            {
                var slider = new Slider { Minimum = 0, Maximum = 10, Width = 160 };
                var value = new TextBlock { Width = 60, VerticalAlignment = VerticalAlignment.Center, Text = "0,0 V" };
                slider.ValueChanged += (_, e) => { port.Value = e.NewValue; value.Text = $"{e.NewValue:0.0} V"; };
                panel.Children.Add(Row(null, ShortId(port), slider, value));
            }
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"Salidas digitales ({(_cpu.Profile.OutputCommon == CommonType.Relay ? "relé" : "transistor")})",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        });
        var outputs = new WrapPanel();
        foreach (var port in _cpu.DigitalOutputs)
        {
            var lamp = new Ellipse { Width = 22, Height = 22, Fill = LampOff, Stroke = Brushes.Gray, StrokeThickness = 1 };
            _ioRefresh.Add(() => lamp.Fill = port.Value > 0.5 ? LampOn : LampOff);
            outputs.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 6),
                Children = { lamp, new TextBlock { Text = ShortId(port), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } },
            });
        }
        panel.Children.Add(outputs);

        foreach (var port in _cpu.AnalogOutputs)
        {
            var value = new TextBlock();
            _ioRefresh.Add(() => value.Text = $"{ShortId(port)} = {port.Value:0.00} V");
            panel.Children.Add(value);
        }

        _ioHost.Content = panel;

        var device = new PlcDeviceView(_cpu, port =>
        {
            if (toggles.TryGetValue(port, out var t)) t.IsChecked = t.IsChecked != true;
        });
        _ioRefresh.Add(device.Refresh);
        _deviceHost.Content = device;
    }

    private IoAddress Find(ISignalPort port)
    {
        _cpu.Profile.TryParse(ShortId(port), out var a);
        return a;
    }

    private string ShortId(ISignalPort port) => port.Id[(_cpu.Id.Length + 1)..];

    private static Control Row(Control? lead, string label, params Control[] rest)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (lead is not null)
        {
            lead.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lead);
        }
        row.Children.Add(new TextBlock { Text = label, Width = 70, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center });
        foreach (var c in rest) row.Children.Add(c);
        return row;
    }
}
