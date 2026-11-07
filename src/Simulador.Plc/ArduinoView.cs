using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Avr;
using Simulador.Arduino.Runtime.Blocks;
using Simulador.Arduino.Runtime.Sketch;
using Simulador.Core.Modules;

namespace Simulador.Plc;

/// <summary>
/// Pestaña Arduino: código a la izquierda (con números de línea y la línea en ejecución marcada), la placa dibujada a la
/// derecha con sus pines en vivo, controles para las entradas, monitor serie y variables.
/// </summary>
internal sealed partial class ArduinoView : UserControl
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private const double EditorFontSize = 13;

    private readonly IModuleHost _host;
    private readonly ComboBox _boardBox;
    private IReadOnlyList<ArduinoExample> _exampleList;
    private readonly TextBlock _serialTitle = Title("Monitor serie");
    private readonly ComboBox _examples;
    private readonly TextBox _editor;
    private readonly TextBlock _gutter = new() { FontFamily = Mono, FontSize = EditorFontSize, Opacity = 0.5, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 6, 6, 0) };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl _boardHost = new();
    private readonly PartsPanel _parts = new();
    private readonly BlockEditor _blocks = new();
    private readonly TabControl _modeTabs;
    private readonly TabItem _blocksTab;
    private IReadOnlyDictionary<int, Block>? _lineToBlock;
    private Grid? _mainGrid;
    private readonly ComboBox _engine;
    private static readonly Lazy<ArduinoToolchain> Toolchain = new(ArduinoToolchain.Detect);
    private (string Source, ArduinoBoard Board, string Hex)? _compiled;
    private (string Name, string Hex)? _openedHex;
    private bool _busy;
    private readonly StackPanel _inputs = new() { Spacing = 4 };
    private readonly TextBlock _outputs = new() { FontFamily = Mono, FontSize = 12 };
    private readonly TextBox _serial = new() { IsReadOnly = true, FontFamily = Mono, FontSize = 12, Height = 150, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _serialInput = new() { FontFamily = Mono, Watermark = "Texto para enviar a la placa" };
    private readonly TextBlock _variables = new() { FontFamily = Mono, FontSize = 12 };
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _wall = new();

    private ArduinoMcu _mcu;
    private ArduinoBoardView? _board;
    private TimeSpan _simTime;
    private TimeSpan _lastWall;
    private string _inputSignature = "";
    private int _gutterLines = -1;
    private int _gutterMark = -1;

    public ArduinoView(IModuleHost host)
    {
        _host = host;
        _mcu = new ArduinoMcu("ARD1", ArduinoBoard.Uno);

        _boardBox = new ComboBox { ItemsSource = ArduinoBoard.Everything, SelectedIndex = 0, MinWidth = 180 };
        ToolTip.SetTip(_boardBox, "Arduino se programa en C++ (y con bloques). ESP32 y Raspberry Pi Pico se programan en MicroPython.");
        _boardBox.SelectionChanged += (_, _) => ChangeBoard();
        _exampleList = ArduinoExamples.All;
        _examples = new ComboBox { ItemsSource = _exampleList.Select(e => e.Name).ToList(), SelectedIndex = 0, MinWidth = 260 };
        _examples.SelectionChanged += (_, _) => ShowExample();

        _engine = new ComboBox { ItemsSource = new[] { "Intérprete", "Chip real (ATmega328P)" }, SelectedIndex = 0, MinWidth = 170 };
        ToolTip.SetTip(_engine,
            "Intérprete: arranca al instante y marca la línea que se ejecuta; admite lo más usado de Arduino.\n" +
            "Chip real: compila con el compilador de Arduino (avr-gcc) y ejecuta el programa instrucción a instrucción en un " +
            "ATmega328P emulado a 16 MHz, como la placa de verdad. Necesita el Arduino IDE instalado. Solo Uno y Nano.");
        _engine.SelectionChanged += (_, _) => { _mcu.Stop(); UpdateStatus(); };
        var verify = new Button { Content = "✔ Verificar" };
        ToolTip.SetTip(verify, "Comprueba que el código está bien escrito");
        verify.Click += async (_, _) => await CompileAsync();
        var run = new Button { Content = "▶ Ejecutar" };
        run.Click += async (_, _) => await RunSketchAsync();
        var stop = new Button { Content = "■ Detener" };
        stop.Click += (_, _) => { _mcu.Stop(); UpdateStatus(); };
        var open = new Button { Content = "Abrir…" };
        open.Click += async (_, _) => await OpenAsync();
        var save = new Button { Content = "Guardar…" };
        save.Click += async (_, _) => await SaveAsync();

        var toolbar = new WrapPanel { Margin = new Thickness(8, 8, 8, 4) };
        foreach (var c in new Control[] { Label("Placa:"), _boardBox, Label("Ejemplo:"), _examples, Label("Motor:"), _engine, verify, run, stop, open, save })
        {
            c.Margin = new Thickness(0, 0, 8, 4);
            toolbar.Children.Add(c);
        }

        // --- Editor con números de línea ---
        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = Mono,
            FontSize = EditorFontSize,
            TextWrapping = TextWrapping.NoWrap,
            BorderThickness = new Thickness(0),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_editor, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(_editor, ScrollBarVisibility.Disabled);
        _editor.TextChanged += (_, _) => UpdateGutter();
        _editor.AddHandler(KeyDownEvent, EditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var editorGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        editorGrid.Children.Add(_gutter);
        Grid.SetColumn(_editor, 1);
        editorGrid.Children.Add(_editor);
        var editorScroll = new ScrollViewer { Content = editorGrid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var editorBox = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = editorScroll };
        var codePanel = new DockPanel();
        DockPanel.SetDock(_description, Dock.Top);
        _description.Margin = new Thickness(0, 0, 0, 6);
        codePanel.Children.Add(_description);
        codePanel.Children.Add(editorBox);

        // --- Bloques: el código se genera solo y aparece en la pestaña Código ---
        _blocks.Changed += () => GenerateFromBlocks();
        _blocks.ExampleChosen += e => { if (_modeTabs?.SelectedItem == _blocksTab) MountBlockExample(e); };
        _blocksTab = new TabItem { Header = "Bloques", Content = _blocks };
        _modeTabs = new TabControl
        {
            Margin = new Thickness(8, 0, 4, 0),
            ItemsSource = new[] { new TabItem { Header = "Código", Content = codePanel }, _blocksTab },
        };
        _modeTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != _modeTabs) return;
            // Los bloques necesitan más anchura que el código.
            if (_mainGrid is not null) _mainGrid.ColumnDefinitions[0].Width = new GridLength(BlocksMode ? 8 : 5, GridUnitType.Star);
            if (_modeTabs.SelectedItem == _blocksTab)
            {
                if (_blocks.CurrentExample is { } ex) MountBlockExample(ex);
                GenerateFromBlocks();
            }
            else _lineToBlock = null;
        };
        var left = _modeTabs;

        // --- Panel derecho ---
        var send = new Button { Content = "Enviar" };
        send.Click += (_, _) => SendSerial();
        _serialInput.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) SendSerial(); };
        var clear = new Button { Content = "Borrar" };
        clear.Click += (_, _) => { _mcu.ClearSerialOutput(); _serial.Text = ""; };
        var serialSend = new DockPanel();
        DockPanel.SetDock(send, Dock.Right);
        DockPanel.SetDock(clear, Dock.Right);
        send.Margin = clear.Margin = new Thickness(6, 0, 0, 0);
        serialSend.Children.Add(clear);
        serialSend.Children.Add(send);
        serialSend.Children.Add(_serialInput);

        var right = new StackPanel { Spacing = 8, Margin = new Thickness(4, 0, 8, 8) };
        right.Children.Add(Title("Placa"));
        right.Children.Add(_boardHost);
        right.Children.Add(new TextBlock
        {
            Text = "Rojo: salida en HIGH (más tenue = PWM). Verde: entrada que recibe tensión. Azul: servo o tono. Clic en un pin de entrada para cambiarlo.",
            FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
        });
        right.Children.Add(Title("Montaje"));
        right.Children.Add(_parts);
        right.Children.Add(Title("Otras entradas"));
        right.Children.Add(_inputs);
        right.Children.Add(Title("Salidas"));
        right.Children.Add(_outputs);
        right.Children.Add(_serialTitle);
        right.Children.Add(_serial);
        right.Children.Add(serialSend);
        right.Children.Add(Title("Variables"));
        right.Children.Add(_variables);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,Auto,6*") };
        _mainGrid = grid;
        grid.Children.Add(left);
        var split = new GridSplitter { Width = 4, Background = Brushes.Transparent };
        Grid.SetColumn(split, 1);
        grid.Children.Add(split);
        var rightScroll = new ScrollViewer { Content = right };
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        var statusBar = new Border { Padding = new Thickness(8, 4), Child = _status };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(grid);
        Content = root;

        BuildBoard();
        ShowExample();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) => OnTimer());
        AttachedToVisualTree += (_, _) => { _wall.Start(); _lastWall = _wall.Elapsed; _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Title(string text) => new() { Text = text, FontSize = 16, Margin = new Thickness(0, 6, 0, 0) };

    // ---- Programa ----

    private void ShowExample()
    {
        if (_examples.SelectedIndex < 0) return;
        var e = _exampleList[_examples.SelectedIndex];
        _mcu.Stop();
        _openedHex = null;
        if (_modeTabs is not null) _modeTabs.SelectedIndex = 0;
        _lineToBlock = null;
        _editor.Text = e.Source.Replace("\r\n", "\n");
        _description.Text = e.Description;
        _parts.SetSpecs(e.Setup);
        _inputSignature = "";
        _mcu.ClearSerialOutput();
        _serial.Text = "";
        UpdateStatus();
    }

    private bool BlocksMode => _modeTabs.SelectedItem == _blocksTab;

    private void MountBlockExample(BlockExample e)
    {
        _mcu.Stop();
        _parts.SetSpecs(e.Parts);
        _inputSignature = "";
        _mcu.ClearSerialOutput();
        _serial.Text = "";
        _description.Text = "Generado desde los bloques: " + e.Description;
        UpdateStatus();
    }

    /// <summary>Pasa los bloques a código. Si algún bloque está mal, lo marca en rojo.</summary>
    private bool GenerateFromBlocks()
    {
        if (_modeTabs is null || !BlocksMode) return false;
        try
        {
            var code = BlockCodeGenerator.Generate(_blocks.Program, _mcu.Board);
            _lineToBlock = code.LineToBlock;
            if (_editor.Text != code.Source) _editor.Text = code.Source;
            _blocks.MarkError(null, null);
            return true;
        }
        catch (BlockException ex)
        {
            _lineToBlock = null;
            _blocks.MarkError(ex.Block, ex.Message);
            _status.Text = "Error en los bloques: " + ex.Message;
            return false;
        }
    }

    private Block? BlockAt(int line) => _lineToBlock is not null && _lineToBlock.TryGetValue(line, out var b) ? b : null;

    private bool ChipMode => _engine.SelectedIndex == 1;

    /// <summary>Compila con el intérprete o, en modo chip, con avr-gcc (en segundo plano).</summary>
    private async Task<bool> CompileAsync()
    {
        if (!ChipMode) return Compile();
        if (_busy) return false;
        if (!ArduinoMcu.SupportsChip(_mcu.Board))
        {
            _status.Text = $"El modo chip real de momento es para Arduino Uno y Nano (ATmega328P). Para {_mcu.Board.Name} usa el intérprete.";
            return false;
        }
        if (_openedHex is { } opened)
        {
            _mcu.LoadHex(opened.Hex);
            _status.Text = $"Programa compilado {opened.Name} cargado en el chip.";
            return true;
        }
        if (BlocksMode && !GenerateFromBlocks()) return false;
        string source = _editor.Text ?? "";
        var board = _mcu.Board;
        try
        {
            if (_compiled is { } c && c.Source == source && c.Board == board)
            {
                _mcu.LoadHex(c.Hex);
                return true;
            }
            _busy = true;
            _status.Text = $"Compilando para {board.Name} con el compilador de Arduino… (la primera vez tarda un poco)";
            var result = await Task.Run(() => Toolchain.Value.CompileAsync(source, board));
            if (!result.Success)
            {
                if (result.ErrorLine is { } line) ShowError("Error de compilación", result.ErrorMessage ?? "", line);
                else _status.Text = "Error de compilación: " + result.ErrorMessage;
                _host.SetStatus(_status.Text ?? "");
                return false;
            }
            _compiled = (source, board, result.Hex!);
            _mcu.LoadHex(result.Hex!);
            _status.Text = $"Compilado para {board.Name} con {Toolchain.Value.Description}.";
            _host.SetStatus("Sketch compilado para el chip.");
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _status.Text = "No se pudo compilar: " + ex.Message;
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    private bool Compile()
    {
        if (BlocksMode && !GenerateFromBlocks()) return false;
        try
        {
            _mcu.Load(_editor.Text ?? "");
            _status.Text = $"Código correcto para {_mcu.Board.Name}.";
            _host.SetStatus("Sketch verificado.");
            return true;
        }
        catch (SketchCompileException ex)
        {
            ShowError("Error de compilación", ex.Detail, ex.Line);
            return false;
        }
    }

    private async Task RunSketchAsync()
    {
        if (!await CompileAsync()) return;
        _mcu.ClearSerialOutput();
        _serial.Text = "";
        _mcu.Start();
        _simTime = TimeSpan.Zero;
        _lastWall = _wall.Elapsed;
        _mcu.Advance(_simTime);
        _inputSignature = "";
        UpdateStatus();
    }

    private void ShowError(string title, string message, int line)
    {
        _status.Text = $"{title} en la línea {line}: {message}";
        _host.SetStatus(_status.Text);
        SelectLine(line);
        if (BlocksMode) _blocks.MarkError(BlockAt(line), message);
    }

    private void SelectLine(int line)
    {
        var text = _editor.Text ?? "";
        int start = 0;
        for (int i = 1; i < line && start >= 0; i++)
        {
            start = text.IndexOf('\n', start);
            if (start >= 0) start++;
        }
        if (start < 0) return;
        int end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        _editor.SelectionStart = start;
        _editor.SelectionEnd = end;
    }

    private void ChangeBoard()
    {
        if (_boardBox.SelectedItem is not ArduinoBoard b || b == _mcu.Board) return;
        bool languageChanged = b.Language != _mcu.Board.Language;
        _mcu.Dispose();
        _mcu = new ArduinoMcu("ARD1", b);
        _compiled = null;
        _openedHex = null;
        if (b.IsPython)
        {
            // MicroPython: ni bloques ni chip real (de momento).
            if (BlocksMode) _modeTabs.SelectedIndex = 0;
            _engine.SelectedIndex = 0;
        }
        else _blocks.Board = b;
        _blocksTab.IsEnabled = !b.IsPython;
        _engine.IsEnabled = !b.IsPython;
        _serialTitle.Text = b.IsPython ? "Consola (print e input)" : "Monitor serie";
        BuildBoard();
        // Los ejemplos de MicroPython usan los pines de cada placa; al cambiar de lenguaje cambia la lista entera.
        if (languageChanged || b.IsPython)
        {
            int index = languageChanged ? 0 : Math.Max(0, _examples.SelectedIndex);
            _exampleList = b.IsPython ? PythonExamples.For(b) : ArduinoExamples.All;
            _examples.ItemsSource = _exampleList.Select(e => e.Name).ToList();
            _examples.SelectedIndex = Math.Min(index, _exampleList.Count - 1);
        }
        else GenerateFromBlocks();
        _inputSignature = "";
        UpdateStatus();
    }

    /// <summary>En Python, Tab pone 4 espacios e Intro mantiene la sangría (y la aumenta después de ':').</summary>
    private void EditorKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (!_mcu.Board.IsPython || _editor.IsReadOnly) return;
        string text = _editor.Text ?? "";
        int caret = Math.Clamp(_editor.CaretIndex, 0, text.Length);
        if (e.Key == Avalonia.Input.Key.Tab && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            Insert("    ");
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Enter && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            if (caret == 0) lineStart = 0;
            string line = text[lineStart..caret];
            string indent = new(line.TakeWhile(c => c == ' ').ToArray());
            if (line.TrimEnd().EndsWith(':')) indent += "    ";
            Insert("\n" + indent);
            e.Handled = true;
        }

        void Insert(string s)
        {
            int start = Math.Min(_editor.SelectionStart, _editor.SelectionEnd), end = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
            if (end <= start) start = end = caret;
            _editor.Text = text[..start] + s + text[end..];
            _editor.CaretIndex = start + s.Length;
        }
    }

    private void BuildBoard()
    {
        _board = new ArduinoBoardView(_mcu, TogglePin);
        _boardHost.Content = _board;
        _parts.Attach(_mcu);
        _board.Refresh();
    }

    // ---- Entradas ----

    /// <summary>Activo = pulsador apretado: en un pin con pull-up lo une a GND; en uno sin pull-up le da 5 V.</summary>
    private static void SetPressed(PinPort p, bool pressed)
    {
        if (p.Mode == PinMode.InputPullup)
        {
            if (pressed) p.Value = 0;
            else p.Disconnect();
        }
        else p.Value = pressed ? p.NominalLevel : 0;
    }

    private static bool IsPressed(PinPort p) =>
        p.Mode == PinMode.InputPullup ? p.ExternalVolts is { } v && v < p.NominalLevel * 0.3 : p.Value >= p.NominalLevel * 0.6;

    private void TogglePin(PinPort p)
    {
        SetPressed(p, !IsPressed(p));
        _inputSignature = "";
    }

    /// <summary>Analógicas que aparecen en el código (A0, A1…), para mostrar su regulador.</summary>
    private IEnumerable<PinPort> UsedAnalogPins()
    {
        var board = _mcu.Board;
        if (board.IsPython)
        {
            // ADC(Pin(34)), ADC(26) o, en la Pico, ADC(0) = GP26.
            var numbers = PyAdc().Matches(_editor.Text ?? "").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Select(n => board == ArduinoBoard.Pico && n < board.AnalogCount ? board.FirstAnalog + n : n).ToHashSet();
            return _mcu.Pins.Where(p => p.IsAnalog && numbers.Contains(p.Number));
        }
        var names = AnalogName().Matches(_editor.Text ?? "").Select(m => m.Value).ToHashSet();
        return _mcu.Pins.Where(p => p.IsAnalog && names.Contains(p.Name));
    }

    private void BuildInputs()
    {
        var driven = _parts.DrivenPins;
        var digital = _mcu.Pins.Where(p => !p.IsOutput && p.Configured && !driven.Contains(p)).ToList();
        var analog = UsedAnalogPins().Where(p => !driven.Contains(p)).ToList();
        string signature = string.Join(",", digital.Select(p => p.Name + p.Mode)) + "|" + string.Join(",", analog.Select(p => p.Name));
        if (signature == _inputSignature) return;
        _inputSignature = signature;
        _inputs.Children.Clear();

        foreach (var p in digital)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = p.Name, Width = 44, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center });
            var push = new Button { Content = "Pulsar", Padding = new Thickness(8, 2) };
            push.AddHandler(PointerPressedEvent, (_, _) => SetPressed(p, true), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            push.AddHandler(PointerReleasedEvent, (_, _) => SetPressed(p, false), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            var hold = new ToggleButton { Content = "Fijo", Padding = new Thickness(8, 2), IsChecked = IsPressed(p) };
            hold.IsCheckedChanged += (_, _) => SetPressed(p, hold.IsChecked == true);
            row.Children.Add(push);
            row.Children.Add(hold);
            row.Children.Add(new TextBlock
            {
                Text = p.Mode == PinMode.InputPullup ? (_mcu.Board.IsPython ? "pull-up: pulsar = 0" : "pull-up: pulsar = LOW") : (_mcu.Board.IsPython ? "pulsar = 1" : "pulsar = HIGH"),
                Opacity = 0.6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            });
            _inputs.Children.Add(row);
        }
        foreach (var p in analog)
        {
            var value = new TextBlock { Width = 110, VerticalAlignment = VerticalAlignment.Center, FontFamily = Mono, FontSize = 12 };
            var slider = new Slider { Minimum = 0, Maximum = p.NominalLevel, Value = p.ExternalVolts ?? 0, Width = 200 };
            int full = _mcu.Board.IsPython ? 65535 : 1023;
            void Show() => value.Text = $"{slider.Value:0.00} V = {Math.Round(slider.Value / p.NominalLevel * full)}";
            slider.ValueChanged += (_, _) => { p.Value = slider.Value; Show(); };
            p.Value = slider.Value;
            Show();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = p.Name, Width = 44, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(slider);
            row.Children.Add(value);
            _inputs.Children.Add(row);
        }
        if (_inputs.Children.Count == 0)
            _inputs.Children.Add(new TextBlock { Text = "Ninguna: las entradas que usa el sketch ya tienen su pieza en el montaje.", Opacity = 0.6, FontSize = 12 });
    }

    // ---- Puerto serie ----

    private void SendSerial()
    {
        var text = _serialInput.Text ?? "";
        _mcu.SerialSend(text + "\n");
        _serialInput.Text = "";
    }

    // ---- Simulación ----

    private void OnTimer()
    {
        var now = _wall.Elapsed;
        var dt = now - _lastWall;
        _lastWall = now;
        if (_mcu.State == McuState.Running)
        {
            // Si el PC no da abasto, la simulación va más lenta en lugar de saltar.
            if (dt > TimeSpan.FromMilliseconds(100)) dt = TimeSpan.FromMilliseconds(100);
            _simTime += dt;
            _parts.Apply();
            _mcu.Advance(_simTime);
            _parts.Animate(dt);
        }
        Refresh();
    }

    private void Refresh()
    {
        _board?.Refresh();
        _parts.Refresh();
        BuildInputs();
        var serial = _mcu.SerialOutput;
        if (serial != _serial.Text)
        {
            _serial.Text = serial;
            _serial.CaretIndex = serial.Length;
        }
        var outs = new StringBuilder();
        foreach (var p in _mcu.Pins.Where(p => p.IsOutput))
        {
            outs.Append(p.Name.PadRight(_mcu.Board.IsPython ? 7 : 5));
            string freq = p.PwmHz is { } f ? $" a {f:0} Hz" : "";
            outs.AppendLine(p.ServoAngle is { } a ? $"servo {a:0}°"
                : p.ToneHz is { } hz ? $"tono {hz:0} Hz"
                : p.Duty is { } d ? $"PWM {d * 100:0} %{freq} ({p.OutputVolts:0.00} V)"
                : p.OutputHigh ? $"HIGH ({p.NominalLevel:0.#} V)" : "LOW (0 V)");
        }
        _outputs.Text = outs.Length == 0 ? "Ningún pin configurado como salida." : outs.ToString().TrimEnd();
        _variables.Text = string.Join("\n", _mcu.Variables().Select(v => $"{v.Type} {v.Name} = {v.Value}"));
        if (_variables.Text.Length == 0) _variables.Text = "(sin variables globales)";
        UpdateGutter();
        UpdateStatus();
        if (BlocksMode)
        {
            _blocks.MarkRunning(_mcu.State == McuState.Running ? BlockAt(_mcu.CurrentLine) : null);
            if (_mcu.State == McuState.Fault && _mcu.FaultLine is { } fl) _blocks.MarkError(BlockAt(fl), _mcu.FaultMessage);
        }
    }

    private void UpdateGutter()
    {
        int lines = (_editor.Text ?? "").Count(c => c == '\n') + 1;
        int mark = _mcu.State == McuState.Running ? _mcu.CurrentLine : _mcu.State == McuState.Fault ? _mcu.FaultLine ?? 0 : 0;
        if (lines == _gutterLines && mark == _gutterMark) return;
        _gutterLines = lines;
        _gutterMark = mark;
        var sb = new StringBuilder();
        for (int i = 1; i <= lines; i++)
        {
            if (i > 1) sb.Append('\n');
            sb.Append(i == mark ? (_mcu.State == McuState.Fault ? "✖ " : "▶ ") + i : i.ToString());
        }
        _gutter.Text = sb.ToString();
    }

    private void UpdateStatus()
    {
        string state = _mcu.State switch
        {
            McuState.Running when _mcu.IsChipMode => $"EJECUTANDO en el chip (16 MHz) · {_mcu.Uptime.TotalSeconds:0.0} s",
            McuState.Running => $"EJECUTANDO · {_mcu.Uptime.TotalSeconds:0.0} s · línea {_mcu.CurrentLine}",
            McuState.Stopped when _mcu.ProgramEnded => $"PROGRAMA TERMINADO tras {_mcu.Uptime.TotalSeconds:0.0} s (los pines se quedan como estaban)",
            McuState.Fault when _mcu.FaultLine is null or 0 => $"DETENIDO POR ERROR: {_mcu.FaultMessage}",
            McuState.Fault => $"DETENIDO POR ERROR en la línea {_mcu.FaultLine}: {_mcu.FaultMessage}",
            _ => "DETENIDO",
        };
        var text = $"{_mcu.Board.Name} ({_mcu.Board.Chip}) · {state}";
        if (_mcu.Diagnostics.Count > 0) text += " · Aviso: " + _mcu.Diagnostics[^1];
        if (_busy) return;
        if (_status.Text != text && !(_mcu.State == McuState.Stopped && (_status.Text?.StartsWith("Error", StringComparison.Ordinal) == true
            || _status.Text?.StartsWith("Compilado", StringComparison.Ordinal) == true || _status.Text?.StartsWith("El modo chip", StringComparison.Ordinal) == true
            || _status.Text?.StartsWith("No se pudo", StringComparison.Ordinal) == true || _status.Text?.StartsWith("Programa compilado", StringComparison.Ordinal) == true)))
            _status.Text = text;
    }

    // ---- Archivos .ino ----

    private static readonly FilePickerFileType InoType = new("Sketch de Arduino") { Patterns = ["*.ino", "*.cpp", "*.c"] };
    private static readonly FilePickerFileType PyFileType = new("Programa de MicroPython") { Patterns = ["*.py"] };
    private static readonly FilePickerFileType HexType = new("Programa compilado (chip real)") { Patterns = ["*.hex"] };

    private async Task OpenAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir sketch o programa compilado", FileTypeFilter = _mcu.Board.IsPython ? [PyFileType, InoType, HexType] : [InoType, HexType, PyFileType] });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            _mcu.Stop();
            string text = await reader.ReadToEndAsync();
            if (files[0].Name.EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
            {
                IntelHex.Parse(text); // comprueba que es un .hex válido
                _openedHex = (files[0].Name, text);
                _engine.SelectedIndex = 1;
                if (_modeTabs is not null) _modeTabs.SelectedIndex = 0;
                _examples.SelectedIndex = -1;
                _openedHex = (files[0].Name, text);
                _editor.Text = $"// Programa compilado: {files[0].Name}\n// No hay código fuente: se ejecuta tal cual en el chip emulado. Pulsa ▶ Ejecutar.\n";
                _description.Text = files[0].Name;
                _host.SetStatus($"Abierto {files[0].Name}.");
                return;
            }
            _openedHex = null;
            bool isPy = files[0].Name.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
            if (isPy != _mcu.Board.IsPython)
                _boardBox.SelectedItem = isPy ? ArduinoBoard.Pico : ArduinoBoard.Uno; // cambia a una placa del lenguaje del archivo
            _editor.Text = text.Replace("\r\n", "\n");
            _examples.SelectedIndex = -1;
            _description.Text = files[0].Name;
            _host.SetStatus($"Abierto {files[0].Name}.");
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            _host.SetStatus("No se pudo abrir: " + ex.Message);
        }
    }

    private async Task SaveAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _mcu.Board.IsPython ? "Guardar programa" : "Guardar sketch",
            SuggestedFileName = _mcu.Board.IsPython ? "main.py" : "sketch.ino",
            FileTypeChoices = [_mcu.Board.IsPython ? PyFileType : InoType],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_editor.Text ?? "");
            _host.SetStatus($"Guardado {file.Name}.");
        }
        catch (IOException ex)
        {
            _host.SetStatus("No se pudo guardar: " + ex.Message);
        }
    }

    [GeneratedRegex(@"\bA\d{1,2}\b")]
    private static partial Regex AnalogName();

    [GeneratedRegex(@"ADC\(\s*(?:Pin\(\s*)?(\d+)")]
    private static partial Regex PyAdc();
}
