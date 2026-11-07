using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Arduino.Runtime;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Editor;

/// <summary>Propiedades del elemento seleccionado: designación, parámetros, bornes y aparato de catálogo.</summary>
internal sealed class PropertiesPanel : ScrollViewer
{
    private readonly ElementCatalog _catalog;
    private readonly Func<ManufacturerCatalog> _presets;
    private readonly StackPanel _stack = new() { Spacing = 6, Margin = new Thickness(10) };

    public event Action? Changed;
    /// <summary>Cambio de carga de motor durante la simulación (id, %).</summary>
    public event Action<Guid, double>? MotorLoadChanged;
    public event Action<Guid, double>? TransmitterChanged;
    /// <summary>Cambio de consigna de un variador durante la simulación (id, Hz).</summary>
    public event Action<Guid, double>? VfdReferenceChanged;
    /// <summary>Texto de estado del PLC durante la simulación (lo da la vista, que es quien tiene la CPU).</summary>
    public Func<PlacedElement, string>? PlcStatus { get; set; }
    /// <summary>Valor actual de un transmisor en la simulación (% del rango).</summary>
    public Func<Guid, double>? TransmitterValue { get; set; }

    public PropertiesPanel(ElementCatalog catalog, Func<ManufacturerCatalog> presets)
    {
        _catalog = catalog;
        _presets = presets;
        Content = _stack;
        Show(null, false);
    }

    public void Show(PlacedElement? e, bool simulating)
    {
        _stack.Children.Clear();
        if (e is null || !_catalog.TryGet(e.Type, out var t))
        {
            _stack.Children.Add(Title("Propiedades"));
            _stack.Children.Add(Note(simulating
                ? "Simulando. Haz clic en pulsadores, interruptores, setas, finales de carrera y protecciones para accionarlos. Mayús+clic en un diferencial pulsa su botón de prueba."
                : "Elige un componente de la librería y haz clic en la hoja para colocarlo. Para cablear, haz clic en un borne y luego en otro (con Mayús sigues cableando). R gira, Supr borra."));
            return;
        }

        _stack.Children.Add(Title(t.Name));
        _stack.Children.Add(Note(t.Description));

        if (simulating)
        {
            _stack.Children.Add(Note($"Designación: {e.Tag}"));
            if (t.Behavior == Behavior.Vfd)
            {
                _stack.Children.Add(Caption("Consigna de frecuencia (Hz)"));
                var slider = new Slider { Minimum = 0, Maximum = Math.Max(60, e.Param(t, "fn") * 1.2), Value = e.Param(t, "fref"), TickFrequency = 5 };
                var value = new TextBlock { Text = $"{slider.Value:0.#} Hz" };
                slider.PropertyChanged += (_, a) =>
                {
                    if (a.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
                    value.Text = $"{slider.Value:0.#} Hz";
                    VfdReferenceChanged?.Invoke(e.Id, slider.Value);
                };
                _stack.Children.Add(slider);
                _stack.Children.Add(value);
                _stack.Children.Add(Note("Si el variador está en fallo, haz clic sobre él para rearmarlo."));
            }
            if (t.Behavior == Behavior.Transmitter)
            {
                var kind = ElementCatalog.TransmitterKinds[(int)Math.Clamp(e.Param(t, "magnitud"), 0, ElementCatalog.TransmitterKinds.Length - 1)];
                _stack.Children.Add(Caption($"{kind.Name}: mueve el valor (salida de 0 a 10 V)"));
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = TransmitterValue?.Invoke(e.Id) ?? e.Param(t, "valor"), TickFrequency = 5 };
                var value = new TextBlock();
                void ShowValue() => value.Text = $"{slider.Value / 100 * kind.Full:0.#} {kind.Unit}  →  {slider.Value / 10:0.00} V";
                ShowValue();
                slider.PropertyChanged += (_, a) =>
                {
                    if (a.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
                    ShowValue();
                    TransmitterChanged?.Invoke(e.Id, slider.Value);
                };
                _stack.Children.Add(slider);
                _stack.Children.Add(value);
            }
            if (t.Behavior == Behavior.Plc && PlcStatus is not null)
                _stack.Children.Add(Note(PlcStatus(e)));
            if (t.IsMotor)
            {
                _stack.Children.Add(Caption("Carga del motor (% del par asignado)"));
                var slider = new Slider { Minimum = 0, Maximum = 200, Value = e.Param(t, "carga"), TickFrequency = 10 };
                var value = new TextBlock { Text = $"{slider.Value:0} %" };
                slider.PropertyChanged += (_, a) =>
                {
                    if (a.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
                    value.Text = $"{slider.Value:0} %";
                    MotorLoadChanged?.Invoke(e.Id, slider.Value);
                };
                _stack.Children.Add(slider);
                _stack.Children.Add(value);
            }
            return;
        }

        var tag = new TextBox { Text = e.Tag };
        tag.TextChanged += (_, _) => { e.Tag = tag.Text?.Trim() ?? ""; Changed?.Invoke(); };
        _stack.Children.Add(Caption("Designación (los elementos con la misma forman un aparato)"));
        _stack.Children.Add(tag);

        var presets = _presets().ForType(t.Id).ToList();
        if (presets.Count > 0)
        {
            _stack.Children.Add(Caption("Aparato de catálogo"));
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Elegir referencia…" };
            foreach (var p in presets) combo.Items.Add(new ComboBoxItem { Content = $"{p.Display} — {p.Description}", Tag = p });
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: ManufacturerPreset p })
                {
                    p.ApplyTo(e);
                    Changed?.Invoke();
                    Show(e, false);
                }
            };
            _stack.Children.Add(combo);
        }

        foreach (var p in t.Params)
        {
            _stack.Children.Add(Caption(string.IsNullOrEmpty(p.Unit) ? p.Name : $"{p.Name} ({p.Unit})"));
            var current = e.Param(t, p.Key);
            if (p.Options is not null)
            {
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var o in p.Options) combo.Items.Add(o);
                combo.SelectedIndex = (int)Math.Clamp(Math.Round(current), 0, p.Options.Length - 1);
                combo.SelectionChanged += (_, _) =>
                {
                    var before = e.Param(t, p.Key);
                    e.Params[p.Key] = combo.SelectedIndex;
                    Changed?.Invoke();
                    // Al cambiar de placa, si el programa es el de ejemplo, se pone el de la placa nueva.
                    if (t.Id == ElementCatalog.McuTypeId && p.Key == "placa" && (int)before != combo.SelectedIndex)
                    {
                        if (e.Text(McuProgramKeys.Code).Length == 0 || e.Text(McuProgramKeys.Code) == McuIoBoard.Template(McuProgramKeys.Board((int)before)))
                            e.Texts.Remove(McuProgramKeys.Code);
                        Show(e, false);
                    }
                };
                _stack.Children.Add(combo);
            }
            else
            {
                var box = new TextBox { Text = current.ToString("0.###", CultureInfo.CurrentCulture) };
                box.LostFocus += (_, _) => Commit(box, e, p);
                box.KeyDown += (_, k) => { if (k.Key == Avalonia.Input.Key.Enter) Commit(box, e, p); };
                _stack.Children.Add(box);
            }
        }

        if (t.Terminals.Count == 2 && !t.IsSource)
        {
            _stack.Children.Add(Caption("Numeración de bornes (p. ej. 13-14, 95-96)"));
            var bornes = new TextBox { Text = e.Text("Bornes", t.DefaultTerminalText), Watermark = "13-14" };
            bornes.TextChanged += (_, _) => { e.Texts["Bornes"] = bornes.Text ?? ""; Changed?.Invoke(); };
            _stack.Children.Add(bornes);
        }
        if (t.Behavior is Behavior.SensorNO or Behavior.SensorNC)
        {
            _stack.Children.Add(Caption("Rótulo del captador (flotador, presostato, B1…)"));
            var label = new TextBox { Text = e.Text("Rótulo"), Watermark = "Flotador" };
            label.TextChanged += (_, _) => { e.Texts["Rótulo"] = label.Text ?? ""; Changed?.Invoke(); };
            _stack.Children.Add(label);
        }
        if (t.Id == ElementCatalog.McuTypeId) AddMcuProgram(e, t);
        else if (t.Behavior == Behavior.Plc) AddPlcProgram(e);
        _stack.Children.Add(Caption("Referencia de fabricante"));
        var reference = new TextBox { Text = e.Text("Referencia"), Watermark = "Opcional" };
        reference.TextChanged += (_, _) => { e.Texts["Referencia"] = reference.Text ?? ""; Changed?.Invoke(); };
        _stack.Children.Add(reference);
    }

    /// <summary>Programa del microcontrolador: C++ de Arduino o MicroPython, según la placa elegida.</summary>
    private void AddMcuProgram(PlacedElement e, ElementType t)
    {
        var board = McuProgramKeys.Board((int)e.Param(t, "placa"));
        _stack.Children.Add(Caption(board.IsPython ? $"Programa en MicroPython para {board.Name}" : $"Sketch de Arduino (C++) para {board.Name}"));
        _stack.Children.Add(Note(McuIoBoard.MappingText(board)));
        var program = new TextBox
        {
            Text = e.Text(McuProgramKeys.Code, McuIoBoard.Template(board)),
            AcceptsReturn = true,
            AcceptsTab = !board.IsPython,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 11,
            MinHeight = 220,
            MaxHeight = 420,
        };
        program.TextChanged += (_, _) => { e.Texts[McuProgramKeys.Code] = program.Text ?? ""; Changed?.Invoke(); };
        _stack.Children.Add(program);
        var load = new Button { Content = board.IsPython ? "Cargar archivo .py…" : "Cargar sketch .ino…" };
        ToolTip.SetTip(load, "Un programa guardado desde la pestaña Arduino del módulo PLC");
        load.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
            var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Cargar programa del microcontrolador",
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType(board.IsPython ? "MicroPython" : "Sketch de Arduino") { Patterns = board.IsPython ? ["*.py"] : ["*.ino", "*.cpp"] },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All,
                ],
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            program.Text = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
        };
        _stack.Children.Add(load);
        _stack.Children.Add(Note("Los cambios se aplican al pulsar Simular. Mientras simula, el estado de la placa y la línea que se ejecuta aparecen arriba."));
    }

    /// <summary>Programa del PLC: texto estructurado escrito aquí, o un Ladder guardado desde el módulo PLC.</summary>
    private void AddPlcProgram(PlacedElement e)
    {
        var hasLadder = e.Text(PlcProgramKeys.Ladder).Length > 0;
        _stack.Children.Add(Caption(hasLadder
            ? $"Programa: Ladder «{e.Text(PlcProgramKeys.LadderName, "programa")}» (cargado del módulo PLC)"
            : "Programa en texto estructurado (IEC 61131-3). Direcciones %IX0.0-%IX0.7 y %QX0.0-%QX0.7"));
        if (hasLadder)
        {
            var remove = new Button { Content = "Quitar el Ladder y usar texto estructurado" };
            remove.Click += (_, _) => { e.Texts.Remove(PlcProgramKeys.Ladder); e.Texts.Remove(PlcProgramKeys.LadderName); Changed?.Invoke(); Show(e, false); };
            _stack.Children.Add(remove);
        }
        else
        {
            var program = new TextBox
            {
                Text = e.Text(PlcProgramKeys.StructuredText, PlcProgramKeys.Template),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 11,
                MinHeight = 180,
                MaxHeight = 360,
            };
            program.TextChanged += (_, _) => { e.Texts[PlcProgramKeys.StructuredText] = program.Text ?? ""; Changed?.Invoke(); };
            _stack.Children.Add(program);
        }
        var load = new Button { Content = "Cargar programa del módulo PLC…" };
        ToolTip.SetTip(load, "Ladder (.ladder.json) o texto estructurado (.st, .txt) guardado desde la pestaña PLC");
        load.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
            var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Cargar programa de PLC",
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Programa de PLC") { Patterns = ["*.ladder.json", "*.st", "*.txt"] },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All,
                ],
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var text = await File.ReadAllTextAsync(path);
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                e.Texts[PlcProgramKeys.Ladder] = text;
                e.Texts[PlcProgramKeys.LadderName] = Path.GetFileName(path);
            }
            else
            {
                e.Texts.Remove(PlcProgramKeys.Ladder);
                e.Texts[PlcProgramKeys.StructuredText] = text;
            }
            Changed?.Invoke();
            Show(e, false);
        };
        _stack.Children.Add(load);
    }

    private void Commit(TextBox box, PlacedElement e, ParamDef p)
    {
        var text = (box.Text ?? "").Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            v = Math.Clamp(v, p.Min, p.Max);
            e.Params[p.Key] = v;
            box.Text = v.ToString("0.###", CultureInfo.CurrentCulture);
            Changed?.Invoke();
        }
        else box.Text = e.Params.TryGetValue(p.Key, out var old) ? old.ToString("0.###", CultureInfo.CurrentCulture) : p.Default.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static TextBlock Title(string s) => new() { Text = s, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Caption(string s) => new() { Text = s, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private static TextBlock Note(string s) => new() { Text = s, FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
}

/// <summary>Dónde guarda su programa el microcontrolador del esquema.</summary>
internal static class McuProgramKeys
{
    public const string Code = "codigo";

    /// <summary>Placa según el índice del parámetro «placa» (mismo orden que <see cref="ElementCatalog.McuBoards"/>).</summary>
    public static ArduinoBoard Board(int index) => ArduinoBoard.Everything[Math.Clamp(index, 0, ArduinoBoard.Everything.Count - 1)];
}

/// <summary>Claves de <see cref="PlacedElement.Texts"/> donde el PLC del esquema guarda su programa.</summary>
internal static class PlcProgramKeys
{
    public const string StructuredText = "programa";
    public const string Ladder = "ladder";
    public const string LadderName = "ladderNombre";

    public const string Template = """
        PROGRAM Principal
        VAR
            Marcha AT %IX0.0 : BOOL;
            Paro   AT %IX0.1 : BOOL;   (* pulsador NC *)
            Motor  AT %QX0.0 : BOOL;
        END_VAR

        Motor := (Marcha OR Motor) AND Paro;
        END_PROGRAM
        """;
}
