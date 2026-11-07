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
    private readonly ComboBox _examples;
    private readonly TextBox _editor;
    private readonly TextBlock _gutter = new() { FontFamily = Mono, FontSize = EditorFontSize, Opacity = 0.5, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 6, 6, 0) };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl _boardHost = new();
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

        _boardBox = new ComboBox { ItemsSource = ArduinoBoard.All, SelectedIndex = 0, MinWidth = 180 };
        _boardBox.SelectionChanged += (_, _) => ChangeBoard();
        _examples = new ComboBox { ItemsSource = ArduinoExamples.All.Select(e => e.Name).ToList(), SelectedIndex = 0, MinWidth = 260 };
        _examples.SelectionChanged += (_, _) => ShowExample();

        var verify = new Button { Content = "✔ Verificar" };
        ToolTip.SetTip(verify, "Comprueba que el código está bien escrito");
        verify.Click += (_, _) => Compile();
        var run = new Button { Content = "▶ Ejecutar" };
        run.Click += (_, _) => RunSketch();
        var stop = new Button { Content = "■ Detener" };
        stop.Click += (_, _) => { _mcu.Stop(); UpdateStatus(); };
        var open = new Button { Content = "Abrir…" };
        open.Click += async (_, _) => await OpenAsync();
        var save = new Button { Content = "Guardar…" };
        save.Click += async (_, _) => await SaveAsync();

        var toolbar = new WrapPanel { Margin = new Thickness(8, 8, 8, 4) };
        foreach (var c in new Control[] { Label("Placa:"), _boardBox, Label("Ejemplo:"), _examples, verify, run, stop, open, save })
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
        var editorGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        editorGrid.Children.Add(_gutter);
        Grid.SetColumn(_editor, 1);
        editorGrid.Children.Add(_editor);
        var editorScroll = new ScrollViewer { Content = editorGrid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var editorBox = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = editorScroll };
        var left = new DockPanel { Margin = new Thickness(8, 0, 4, 0) };
        DockPanel.SetDock(_description, Dock.Top);
        _description.Margin = new Thickness(0, 0, 0, 6);
        left.Children.Add(_description);
        left.Children.Add(editorBox);

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
            Text = "Rojo: salida en HIGH (más tenue = PWM). Verde: entrada que recibe 5 V. Azul: servo o tono. Clic en un pin de entrada para cambiarlo.",
            FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
        });
        right.Children.Add(Title("Entradas"));
        right.Children.Add(_inputs);
        right.Children.Add(Title("Salidas"));
        right.Children.Add(_outputs);
        right.Children.Add(Title("Monitor serie"));
        right.Children.Add(_serial);
        right.Children.Add(serialSend);
        right.Children.Add(Title("Variables"));
        right.Children.Add(_variables);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,Auto,6*") };
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
        var e = ArduinoExamples.All[_examples.SelectedIndex];
        _mcu.Stop();
        _editor.Text = e.Source.Replace("\r\n", "\n");
        _description.Text = e.Description;
        _mcu.ClearSerialOutput();
        _serial.Text = "";
        UpdateStatus();
    }

    private bool Compile()
    {
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

    private void RunSketch()
    {
        if (!Compile()) return;
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
        _mcu.Dispose();
        _mcu = new ArduinoMcu("ARD1", b);
        BuildBoard();
        _inputSignature = "";
        UpdateStatus();
    }

    private void BuildBoard()
    {
        _board = new ArduinoBoardView(_mcu, TogglePin);
        _boardHost.Content = _board;
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
        var names = AnalogName().Matches(_editor.Text ?? "").Select(m => m.Value).ToHashSet();
        return _mcu.Pins.Where(p => p.IsAnalog && names.Contains(p.Name));
    }

    private void BuildInputs()
    {
        var digital = _mcu.Pins.Where(p => !p.IsOutput && p.Configured).ToList();
        var analog = UsedAnalogPins().ToList();
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
                Text = p.Mode == PinMode.InputPullup ? "pull-up: pulsar = LOW" : "pulsar = HIGH",
                Opacity = 0.6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            });
            _inputs.Children.Add(row);
        }
        foreach (var p in analog)
        {
            var value = new TextBlock { Width = 110, VerticalAlignment = VerticalAlignment.Center, FontFamily = Mono, FontSize = 12 };
            var slider = new Slider { Minimum = 0, Maximum = p.NominalLevel, Value = p.ExternalVolts ?? 0, Width = 200 };
            void Show() => value.Text = $"{slider.Value:0.00} V = {Math.Round(slider.Value / p.NominalLevel * 1023)}";
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
            _inputs.Children.Add(new TextBlock { Text = "El sketch no usa entradas (o aún no se ha ejecutado).", Opacity = 0.6, FontSize = 12 });
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
            _mcu.Advance(_simTime);
        }
        Refresh();
    }

    private void Refresh()
    {
        _board?.Refresh();
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
            outs.Append(p.Name.PadRight(5));
            outs.AppendLine(p.ServoAngle is { } a ? $"servo {a:0}°"
                : p.ToneHz is { } hz ? $"tono {hz:0} Hz"
                : p.Duty is { } d ? $"PWM {d * 100:0} % ({p.OutputVolts:0.00} V)"
                : p.OutputHigh ? "HIGH (5 V)" : "LOW (0 V)");
        }
        _outputs.Text = outs.Length == 0 ? "Ningún pin configurado como salida." : outs.ToString().TrimEnd();
        _variables.Text = string.Join("\n", _mcu.Variables().Select(v => $"{v.Type} {v.Name} = {v.Value}"));
        if (_variables.Text.Length == 0) _variables.Text = "(sin variables globales)";
        UpdateGutter();
        UpdateStatus();
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
            McuState.Running => $"EJECUTANDO · {_mcu.Uptime.TotalSeconds:0.0} s · línea {_mcu.CurrentLine}",
            McuState.Fault => $"DETENIDO POR ERROR en la línea {_mcu.FaultLine}: {_mcu.FaultMessage}",
            _ => "DETENIDO",
        };
        var text = $"{_mcu.Board.Name} ({_mcu.Board.Chip}) · {state}";
        if (_mcu.Diagnostics.Count > 0) text += " · Aviso: " + _mcu.Diagnostics[^1];
        if (_status.Text != text && !(_mcu.State == McuState.Stopped && _status.Text?.StartsWith("Error de compilación", StringComparison.Ordinal) == true))
            _status.Text = text;
    }

    // ---- Archivos .ino ----

    private static readonly FilePickerFileType InoType = new("Sketch de Arduino") { Patterns = ["*.ino", "*.cpp", "*.c"] };

    private async Task OpenAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir sketch", FileTypeFilter = [InoType] });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            _mcu.Stop();
            _editor.Text = (await reader.ReadToEndAsync()).Replace("\r\n", "\n");
            _examples.SelectedIndex = -1;
            _description.Text = files[0].Name;
            _host.SetStatus($"Abierto {files[0].Name}.");
        }
        catch (IOException ex)
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
            Title = "Guardar sketch",
            SuggestedFileName = "sketch.ino",
            FileTypeChoices = [InoType],
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
}
