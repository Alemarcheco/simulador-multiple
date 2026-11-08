using Simulador.Core.Modules;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Blocks;

namespace Simulador.Plc;

/// <summary>
/// Editor de bloques para Arduino, al estilo de los lenguajes visuales para aprender a programar. A la izquierda la
/// paleta por categorías; a la derecha el programa: «al encender» (setup) y «repetir siempre» (loop). Se hace clic en
/// un bloque o en un hueco para seleccionarlo y luego en la paleta para añadir el bloque nuevo ahí.
/// </summary>
internal sealed class BlockEditor : UserControl, IMenuSource
{
    private static readonly IBrush Selected = Brushes.Black;
    private static readonly IBrush Running = new SolidColorBrush(Color.Parse("#FFD60A"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#FF3B30"));
    private static readonly IBrush SlotSelected = new SolidColorBrush(Color.Parse("#FFD60A"));
    private static readonly IBrush BodyBack = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    private readonly ComboBox _examples;
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 };
    private readonly StackPanel _palette = new() { Spacing = 4, Margin = new Thickness(0, 0, 8, 8) };
    private readonly StackPanel _workspace = new() { Spacing = 10, Margin = new Thickness(4, 0, 8, 8) };
    private readonly Dictionary<Block, Border> _borders = new(ReferenceEqualityComparer.Instance);

    private BlockProgram _program = new();
    private ArduinoBoard _board = ArduinoBoard.Uno;
    private Block? _selected;
    private List<Block>? _selectedList;
    private (Block Parent, string Input)? _selectedSlot;
    private Block? _running;
    private Block? _error;
    private object? _lastClick;

    public BlockEditor()
    {
        _examples = new ComboBox { ItemsSource = BlockExamples.All.Select(e => e.Name).ToList(), MinWidth = 220 };
        _examples.SelectionChanged += (_, _) =>
        {
            if (_examples.SelectedIndex < 0) return;
            var e = BlockExamples.For(_board)[_examples.SelectedIndex];
            _description.Text = e.Description;
            Load(e.Build());
            ExampleChosen?.Invoke(e);
        };

        var up = ToolButton("↑ Subir", "Sube el bloque seleccionado", () => Move(-1));
        var down = ToolButton("↓ Bajar", "Baja el bloque seleccionado", () => Move(1));
        var dup = ToolButton("Duplicar", "Copia el bloque seleccionado debajo", Duplicate);
        var del = ToolButton("Borrar", "Quita el bloque o vacía el hueco seleccionado (tecla Supr)", Delete);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var c in new Control[] { new TextBlock { Text = "Ejemplo:", VerticalAlignment = VerticalAlignment.Center }, _examples, up, down, dup, del })
        {
            c.Margin = new Thickness(0, 0, 6, 4);
            toolbar.Children.Add(c);
        }

        BuildPalette();

        var paletteScroll = new ScrollViewer { Content = _palette, Width = 235 };
        var workScroll = new ScrollViewer { Content = _workspace, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var body = new DockPanel();
        DockPanel.SetDock(paletteScroll, Dock.Left);
        body.Children.Add(paletteScroll);
        body.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Child = workScroll });

        var top = new StackPanel { Spacing = 2 };
        top.Children.Add(toolbar);
        top.Children.Add(_description);
        top.Children.Add(_hint);
        var root = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(top, Dock.Top);
        top.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(top);
        root.Children.Add(body);
        Content = root;
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && e.Source is not TextBox) { Delete(); e.Handled = true; }
        };

        _examples.SelectedIndex = 0;
    }

    /// <summary>El programa cambió (bloques, campos o valores): hay que volver a generar el código.</summary>
    public event Action? Changed;

    /// <summary>Se eligió un ejemplo: trae su montaje de piezas.</summary>
    public event Action<BlockExample>? ExampleChosen;

    public BlockProgram Program => _program;

    public string Description => _description.Text ?? "";

    public BlockExample? CurrentExample => _examples.SelectedIndex >= 0 ? BlockExamples.For(_board)[_examples.SelectedIndex] : null;

    public ArduinoBoard Board
    {
        get => _board;
        set
        {
            bool samePins = value.PinPrefix == _board.PinPrefix;
            _board = value;
            if (!samePins)
            {
                _palette.Children.Clear();
                BuildPalette();
            }
            // Un ejemplo se vuelve a cargar con los pines de la nueva placa (D13 en Arduino, GPIO2 en el ESP32…).
            if (!samePins && CurrentExample is { } e)
            {
                _description.Text = e.Description;
                Load(e.Build());
                ExampleChosen?.Invoke(e);
            }
            else Render();
        }
    }

    public void Load(BlockProgram program)
    {
        _program = program;
        _selected = null;
        _selectedSlot = null;
        _selectedList = program.Loop;
        _error = null;
        _running = null;
        Render();
        Changed?.Invoke();
    }

    /// <summary>Resalta el bloque que se está ejecutando (o ninguno).</summary>
    public void MarkRunning(Block? block)
    {
        if (ReferenceEquals(block, _running)) return;
        var old = _running;
        _running = block;
        Restyle(old);
        Restyle(block);
    }

    /// <summary>Marca en rojo el bloque con el error y explica qué pasa.</summary>
    public void MarkError(Block? block, string? message)
    {
        var old = _error;
        _error = block;
        Restyle(old);
        Restyle(block);
        _hint.Text = message is null ? DefaultHint : "✖ " + message;
        _hint.Foreground = message is null ? null : Error;
    }

    private const string DefaultHint =
        "Haz clic en un bloque del programa (o en un hueco) para seleccionarlo y luego en un bloque de la paleta para añadirlo debajo (o dentro del hueco).";

    private static Button ToolButton(string text, string tip, Action action)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3) };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => action();
        return b;
    }

    // ---- Paleta ----

    private void BuildPalette()
    {
        foreach (var cat in BlockCatalog.Categories)
        {
            _palette.Children.Add(new TextBlock { Text = cat.Name, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 2), Foreground = Brush(cat.Color) });
            foreach (var def in BlockCatalog.In(cat))
            {
                var sample = NewBlock(def.Op);
                var view = def.IsStatement ? StatementView(sample, interactive: false) : ValueView(sample, interactive: false);
                var holder = new Border { Child = view, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
                ToolTip.SetTip(holder, def.Help + (def.IsStatement ? "\nClic: añadir debajo del bloque seleccionado." : "\nClic: poner en el hueco seleccionado."));
                holder.PointerPressed += (_, e) => { Insert(def.Op); e.Handled = true; };
                _palette.Children.Add(holder);
            }
        }
    }

    /// <summary>Bloque nuevo con un pin por defecto que exista en la placa elegida.</summary>
    private Block NewBlock(string op)
    {
        var b = Block.Create(op);
        if (b.Fields.TryGetValue("pin", out var pin)) b.Fields["pin"] = BlockExamples.PinFor(_board, pin);
        return b;
    }

    // ---- Edición ----

    private void Insert(string op)
    {
        var def = BlockCatalog.Get(op);
        var block = NewBlock(op);
        if (def.IsStatement)
        {
            if (_selected is not null && Find(_selected) is var (list, i))
                list.Insert(i + 1, block);
            else
                (_selectedList ?? _program.Loop).Add(block);
            Select(block);
        }
        else
        {
            var slot = _selectedSlot ?? FirstLiteralSlot(_selected);
            if (slot is not { } s)
            {
                _hint.Text = "Primero haz clic en un hueco (los números en blanco) donde poner este bloque.";
                return;
            }
            // Si en el hueco ya había algo, el bloque nuevo lo recoge en su primer hueco cuando cabe (p. ej. «no …»).
            var old = s.Parent.Inputs[s.Input];
            if (old.Op != "num" && block.Inputs.Count > 0) block.Inputs[block.Def.Inputs[0].Name] = old;
            s.Parent.Inputs[s.Input] = block;
            _selectedSlot = s;
            _selected = null;
        }
        Render();
        Changed?.Invoke();
    }

    private static (Block, string)? FirstLiteralSlot(Block? b)
    {
        if (b is null) return null;
        foreach (var i in b.Def.Inputs)
            if (b.Inputs.TryGetValue(i.Name, out var v) && v.Op == "num") return (b, i.Name);
        return null;
    }

    private (List<Block> List, int Index)? Find(Block target)
    {
        (List<Block>, int)? Search(List<Block> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], target)) return (list, i);
                foreach (var body in list[i].Bodies.Values)
                    if (Search(body) is { } r) return r;
            }
            return null;
        }
        return Search(_program.Setup) ?? Search(_program.Loop);
    }

    private void Move(int delta)
    {
        if (_selected is null || Find(_selected) is not var (list, i)) return;
        int j = i + delta;
        if (j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        Render();
        Changed?.Invoke();
    }

    private void Duplicate()
    {
        if (_selected is null || Find(_selected) is not var (list, i)) return;
        var copy = _selected.Clone();
        list.Insert(i + 1, copy);
        Select(copy);
        Render();
        Changed?.Invoke();
    }

    private void Delete()
    {
        if (_selectedSlot is { } s)
        {
            var def = s.Parent.Def.Inputs.First(x => x.Name == s.Input);
            s.Parent.Inputs[s.Input] = Block.Literal(def.Default);
        }
        else if (_selected is not null && Find(_selected) is var (list, i))
        {
            list.RemoveAt(i);
            _selected = list.Count == 0 ? null : list[Math.Min(i, list.Count - 1)];
            _selectedList = list;
        }
        else return;
        Render();
        Changed?.Invoke();
    }

    private void Select(Block b)
    {
        _selected = b;
        _selectedSlot = null;
        _selectedList = Find(b)?.List;
    }

    // ---- Dibujo ----

    private void Render()
    {
        _borders.Clear();
        _workspace.Children.Clear();
        _workspace.Children.Add(Hat("al encender la placa", "setup: se hace una vez", _program.Setup));
        _workspace.Children.Add(Hat("repetir siempre", "loop: se repite sin parar", _program.Loop));
        if (_error is null) _hint.Text = DefaultHint;
    }

    private Control Hat(string title, string sub, List<Block> list)
    {
        var color = Brush(BlockCatalog.Control.Color);
        var head = new Border
        {
            Background = color,
            CornerRadius = new CornerRadius(14, 14, 4, 0),
            Padding = new Thickness(12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(2),
            BorderBrush = ReferenceEquals(_selectedList, list) && _selected is null && _selectedSlot is null ? Selected : Brushes.Transparent,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = sub, Foreground = Brushes.White, FontSize = 11, Opacity = 0.85 },
                },
            },
        };
        head.PointerPressed += (_, e) => { SelectList(list); e.Handled = true; };
        var panel = new StackPanel();
        panel.Children.Add(head);
        panel.Children.Add(BodyView(list, color));
        return panel;
    }

    private void SelectList(List<Block> list)
    {
        _selectedList = list;
        _selected = null;
        _selectedSlot = null;
        Render();
    }

    private Control BodyView(List<Block> list, IBrush color)
    {
        var stack = new StackPanel { Spacing = 2 };
        foreach (var b in list) stack.Children.Add(StatementView(b, interactive: true));
        if (list.Count == 0)
            stack.Children.Add(new TextBlock { Text = "(vacío: selecciona aquí y añade bloques)", FontSize = 11, Opacity = 0.6, Margin = new Thickness(4, 4) });
        var area = new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(6, 0, 0, 6),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Padding = new Thickness(6, 4, 4, 4),
            MinHeight = 30,
            Background = Brushes.Transparent,
            Child = stack,
        };
        area.PointerPressed += (_, e) => { if (!e.Handled) { SelectList(list); e.Handled = true; } };
        return area;
    }

    private Control StatementView(Block b, bool interactive)
    {
        var color = Brush(b.Def.Category.Color);
        var content = new StackPanel();
        content.Children.Add(Line(b, interactive));
        foreach (var body in b.Def.Bodies)
        {
            if (body.Label.Length > 0)
                content.Children.Add(new TextBlock { Text = body.Label, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            if (interactive)
            {
                var inner = BodyView(b.Body(body.Name), Brushes.White);
                inner.Margin = new Thickness(4, 2, 0, 2);
                if (inner is Border ib) { ib.Background = BodyBack; ib.BorderBrush = BodyBack; }
                content.Children.Add(inner);
            }
            else
                content.Children.Add(new Border { Height = 14, Margin = new Thickness(10, 2, 0, 2), Background = BodyBack, CornerRadius = new CornerRadius(3) });
        }
        var border = new Border
        {
            Background = color,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
        if (!interactive) return border;
        _borders[b] = border;
        Style(b, border);
        border.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (ReferenceEquals(_lastClick, e)) return;
            _lastClick = e;
            bool reRender = !ReferenceEquals(_selected, b) || _selectedSlot is not null;
            Select(b);
            if (reRender && e.Source is not TextBox && e.Source is not ComboBox) Render();
            else RestyleAll();
            e.Handled = true;
            Focus();
        }, handledEventsToo: true);
        return border;
    }

    private Control ValueView(Block b, bool interactive)
    {
        if (b.Op == "num")
            return NumberBox(b, interactive);
        var color = Brush(b.Def.Category.Color);
        var border = new Border
        {
            Background = color,
            CornerRadius = b.Def.Shape == BlockShape.Condition ? new CornerRadius(3) : new CornerRadius(12),
            Padding = new Thickness(b.Def.Shape == BlockShape.Condition ? 10 : 8, 2),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Line(b, interactive),
        };
        if (interactive)
        {
            _borders[b] = border;
            Style(b, border);
        }
        return border;
    }

    private static Control NumberBox(Block b, bool interactive)
    {
        var box = new TextBox
        {
            Text = b.Field("n"),
            MinWidth = 40,
            MinHeight = 0,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 1),
            FontSize = 12,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsHitTestVisible = interactive,
        };
        return box;
    }

    /// <summary>La línea de un bloque: texto, campos y huecos según la plantilla.</summary>
    private Control Line(Block b, bool interactive)
    {
        var line = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, isSlot) in b.Def.Parts())
        {
            Control piece;
            if (!isSlot)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                piece = new TextBlock { Text = text.Trim(), Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            }
            else if (b.Def.Field(text) is { } field)
                piece = FieldView(b, field, interactive);
            else
                piece = Slot(b, text, interactive);
            piece.Margin = new Thickness(0, 1, 5, 1);
            line.Children.Add(piece);
        }
        return line;
    }

    private Control Slot(Block parent, string input, bool interactive)
    {
        if (!parent.Inputs.TryGetValue(input, out var value))
            parent.Inputs[input] = value = Block.Literal("0");
        var inner = ValueView(value, interactive);
        if (value.Op == "num" && interactive && inner is TextBox box)
        {
            box.TextChanged += (_, _) =>
            {
                if (value.Fields.GetValueOrDefault("n") == box.Text) return;
                value.Fields["n"] = box.Text ?? "";
                Changed?.Invoke();
            };
        }
        var slot = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            BorderBrush = _selectedSlot is { } s && ReferenceEquals(s.Parent, parent) && s.Input == input ? SlotSelected : Brushes.Transparent,
            Child = inner,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (interactive)
        {
            ToolTip.SetTip(slot, "Hueco: haz clic y luego elige un bloque verde, azul o naranja de la paleta para ponerlo aquí.");
            slot.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (ReferenceEquals(_lastClick, e)) return;
                _lastClick = e;
                bool changed = _selectedSlot is not { } cur || !ReferenceEquals(cur.Parent, parent) || cur.Input != input;
                _selectedSlot = (parent, input);
                _selected = null;
                if (changed && e.Source is not TextBox && e.Source is not ComboBox) Render();
                e.Handled = true;
            }, handledEventsToo: true);
        }
        return slot;
    }

    private Control FieldView(Block b, FieldDef f, bool interactive)
    {
        switch (f.Kind)
        {
            case FieldKind.Number:
                return NumberBox(b, interactive);
            case FieldKind.Text:
            case FieldKind.Variable:
            {
                var box = new TextBox
                {
                    Text = b.Field(f.Name),
                    MinWidth = f.Kind == FieldKind.Variable ? 70 : 90,
                    MinHeight = 0,
                    Height = 26,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(6, 1),
                    FontSize = 12,
                    Background = Brushes.White,
                    Foreground = Brushes.Black,
                    Watermark = f.Kind == FieldKind.Variable ? "nombre" : "texto",
                    IsHitTestVisible = interactive,
                };
                box.TextChanged += (_, _) =>
                {
                    if (b.Fields.GetValueOrDefault(f.Name) == box.Text) return;
                    b.Fields[f.Name] = box.Text ?? "";
                    Changed?.Invoke();
                };
                return box;
            }
            default:
            {
                List<Choice> options = f.Kind switch
                {
                    FieldKind.Pin => PinChoices(analogOnly: false),
                    FieldKind.AnalogPin => PinChoices(analogOnly: true),
                    _ => [.. f.Options!],
                };
                string current = b.Field(f.Name);
                if (!options.Any(o => o.Code == current)) options.Insert(0, new Choice(current + " (no existe)", current));
                var combo = new ComboBox
                {
                    ItemsSource = options.Select(o => o.Label).ToList(),
                    SelectedIndex = options.FindIndex(o => o.Code == current),
                    FontSize = 12,
                    MinHeight = 0,
                    Height = 26,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(6, 0),
                    Background = Brushes.White,
                    Foreground = Brushes.Black,
                    IsHitTestVisible = interactive,
                    MaxDropDownHeight = 320,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex < 0) return;
                    b.Fields[f.Name] = options[combo.SelectedIndex].Code;
                    Changed?.Invoke();
                };
                return combo;
            }
        }
    }

    private List<Choice> PinChoices(bool analogOnly)
    {
        var list = new List<Choice>();
        for (int i = 0; i < _board.PinCount; i++)
        {
            if (analogOnly && !_board.IsAnalog(i)) continue;
            string name = _board.PinName(i);
            string label = !analogOnly && _board.Pwm.Contains(i) ? $"{name} ~" : name;
            list.Add(new Choice(label, name));
        }
        return list;
    }

    private void RestyleAll()
    {
        foreach (var (b, border) in _borders) Style(b, border);
    }

    private void Restyle(Block? b)
    {
        if (b is not null && _borders.TryGetValue(b, out var border)) Style(b, border);
    }

    private void Style(Block b, Border border)
    {
        border.BorderBrush = ReferenceEquals(b, _error) ? Error
            : ReferenceEquals(b, _running) ? Running
            : ReferenceEquals(b, _selected) ? Selected
            : b.Def.IsStatement ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
        border.BorderThickness = new Thickness(ReferenceEquals(b, _error) || ReferenceEquals(b, _running) ? 3 : b.Def.IsStatement ? 2 : 1.5);
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    // ---- Archivos ----

    private static readonly FilePickerFileType JsonType = new("Programa de bloques") { Patterns = ["*.bloques.json", "*.json"] };

    /// <summary>Órdenes de la barra de menús de arriba (sólo en la pestaña Bloques).</summary>
    public IEnumerable<MenuCommand> GetMenuCommands()
    {
        yield return new MenuCommand("Nuevo programa de bloques", () => { _examples.SelectedIndex = -1; _description.Text = ""; Load(new BlockProgram()); })
        {
            SeparatorBefore = true,
        };
        yield return new MenuCommand("Abrir bloques…", () => _ = OpenAsync()) { ToolTip = "Abre un programa de bloques (.bloques.json)" };
        yield return new MenuCommand("Guardar bloques…", () => _ = SaveAsync());
    }

    private async Task OpenAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir programa de bloques", FileTypeFilter = [JsonType] });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var program = BlockProgram.FromJson(await reader.ReadToEndAsync());
            _examples.SelectedIndex = -1;
            _description.Text = files[0].Name;
            Load(program);
        }
        catch (Exception ex) when (ex is IOException or BlockException or System.Text.Json.JsonException)
        {
            MarkError(null, "No se pudo abrir: " + ex.Message);
        }
    }

    private async Task SaveAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar programa de bloques",
            SuggestedFileName = "programa.bloques.json",
            FileTypeChoices = [JsonType],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_program.ToJson());
            _hint.Text = $"Guardado {file.Name}.";
        }
        catch (IOException ex)
        {
            MarkError(null, "No se pudo guardar: " + ex.Message);
        }
    }
}
