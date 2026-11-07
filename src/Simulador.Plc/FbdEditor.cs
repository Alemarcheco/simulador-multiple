using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Fbd;

namespace Simulador.Plc;

/// <summary>
/// Editor gráfico de diagramas de bloques de función (FBD/FUP). Cada red se dibuja de izquierda a derecha: operandos,
/// bloques encadenados y, a la derecha, las asignaciones (=, S, R). Con el PLC en RUN las señales activas se ven en
/// verde y los bloques numéricos muestran su valor.
/// </summary>
internal sealed class FbdEditor : UserControl
{
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#2E9E44"));
    private static readonly IBrush ActiveBack = new SolidColorBrush(Color.Parse("#332E9E44"));
    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#2F7FE0"));
    private static readonly IBrush BlockBack = new SolidColorBrush(Color.Parse("#14808080"));
    private static readonly IBrush Missing = new SolidColorBrush(Color.Parse("#D9534F"));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly Func<string, object?> _read;
    private readonly Func<string, string> _display;
    private readonly Func<string> _defaultInput;
    private readonly Func<string> _defaultOutput;
    private readonly StackPanel _canvas = new() { Spacing = 16, Margin = new Thickness(12) };
    private readonly ContentControl _properties = new();
    private readonly TextBlock _hint = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly List<(Border Box, object Item, Func<bool> IsActive)> _live = [];
    private readonly List<(TextBlock Text, Func<string> Value)> _values = [];
    private FbdProgram _program = new();
    private object? _selection;

    public FbdEditor(Func<string, object?> read, Func<string, string> display, Func<string> defaultInput, Func<string> defaultOutput)
    {
        _read = read;
        _display = display;
        _defaultInput = defaultInput;
        _defaultOutput = defaultOutput;

        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Tool(string text, string tip, Action action)
        {
            var b = new Button { Content = text.Replace("_", "__"), Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 4, 4), FontFamily = Mono };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { action(); Rebuild(); };
            palette.Children.Add(b);
        }
        void Gate(GateKind k, string tip) => Tool(FbdGate.Symbol(k), tip, () => Insert(NewGate(k)));
        void Block(string type, string tip) => Tool(type, tip, () => Insert(NewInstance(type)));

        Tool("+ Red", "Añade una red nueva debajo de la seleccionada", AddNetwork);
        Gate(GateKind.And, "Y (AND): la salida se activa si todas las entradas están activas");
        Gate(GateKind.Or, "O (OR): la salida se activa si alguna entrada está activa");
        Gate(GateKind.Xor, "O exclusiva (XOR)");
        Gate(GateKind.Not, "Negación (NOT)");
        Tool("○ Negar", "Pone o quita el círculo de negación en la entrada o salida seleccionada", Negate);
        Tool("+ Entrada", "Añade una entrada al bloque seleccionado (&, ≥1, ADD…)", AddInput);
        Block("TON", "Temporizador con retardo a la conexión");
        Block("TOF", "Temporizador con retardo a la desconexión");
        Block("TP", "Temporizador de impulso");
        Block("CTU", "Contador ascendente");
        Block("CTD", "Contador descendente");
        Block("SR", "Biestable con prioridad a activar");
        Block("RS", "Biestable con prioridad a desactivar");
        Block("R_TRIG", "Flanco ascendente");
        Block("F_TRIG", "Flanco descendente");
        Gate(GateKind.Gt, "Comparación: mayor que");
        Gate(GateKind.Ge, "Comparación: mayor o igual");
        Gate(GateKind.Eq, "Comparación: igual");
        Gate(GateKind.Lt, "Comparación: menor que");
        Gate(GateKind.Add, "Suma");
        Gate(GateKind.Sub, "Resta");
        Gate(GateKind.Mul, "Multiplicación");
        Gate(GateKind.Div, "División");
        Gate(GateKind.Sel, "Selección: G=0 → IN0, G=1 → IN1");
        Gate(GateKind.Move, "Copiar un valor");
        Tool("+ Salida", "Añade otra asignación (=, S o R) a la red", AddOutput);
        Tool("▲", "Subir red", () => MoveNetwork(-1));
        Tool("▼", "Bajar red", () => MoveNetwork(1));
        Tool("✕ Borrar", "Borra el elemento seleccionado (tecla Supr). Un bloque se sustituye por su primera entrada.", Delete);

        var propsBox = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Width = 240,
            Margin = new Thickness(8, 0, 0, 0),
            Child = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Propiedades", FontWeight = FontWeight.SemiBold }, _properties, _hint } },
        };

        var body = new DockPanel();
        DockPanel.SetDock(propsBox, Dock.Right);
        body.Children.Add(propsBox);
        body.Children.Add(new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });

        var root = new DockPanel();
        DockPanel.SetDock(palette, Dock.Top);
        root.Children.Add(palette);
        root.Children.Add(body);
        Content = root;
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && e.Source is not TextBox)
            {
                Delete();
                Rebuild();
            }
        };
        Rebuild();
    }

    public FbdProgram Program
    {
        get => _program;
        set
        {
            _program = value;
            _selection = null;
            Rebuild();
        }
    }

    // ---- Búsqueda en el árbol ----

    /// <summary>Red y forma de sustituir un nodo en su sitio (en la raíz, en una puerta o en un pin de bloque).</summary>
    private (FbdNetwork Net, Action<FbdNode?> Replace, FbdGate? Parent)? Locate(FbdNode target)
    {
        foreach (var net in _program.Networks)
        {
            if (ReferenceEquals(net.Root, target)) return (net, n => net.Root = n, null);
            if (net.Root is not null && Find(net.Root, target) is { } hit) return (net, hit.Replace, hit.Parent);
        }
        return null;
    }

    private static (Action<FbdNode?> Replace, FbdGate? Parent)? Find(FbdNode node, FbdNode target)
    {
        switch (node)
        {
            case FbdGate g:
                for (int i = 0; i < g.Inputs.Count; i++)
                {
                    int index = i;
                    if (ReferenceEquals(g.Inputs[i], target))
                        return (n => { if (n is null) g.Inputs.RemoveAt(index); else g.Inputs[index] = n; }, g);
                    if (Find(g.Inputs[i], target) is { } hit) return hit;
                }
                break;
            case FbdInstance fb:
                foreach (var (pin, child) in fb.Inputs.ToList())
                {
                    if (ReferenceEquals(child, target))
                        return (n => { if (n is null) fb.Inputs.Remove(pin); else fb.Inputs[pin] = n; }, null);
                    if (Find(child, target) is { } hit) return hit;
                }
                break;
        }
        return null;
    }

    private FbdNetwork? TargetNetwork() => _selection switch
    {
        FbdNetwork n => n,
        FbdNode node => Locate(node)?.Net,
        FbdAssignment a => _program.Networks.FirstOrDefault(n => n.Outputs.Contains(a)),
        _ => _program.Networks.LastOrDefault(),
    };

    // ---- Operaciones ----

    private void AddNetwork()
    {
        var net = new FbdNetwork
        {
            Root = new FbdGate(GateKind.And, new FbdOperand(_defaultInput()), new FbdOperand(_defaultInput())),
            Outputs = { new FbdAssignment(_defaultOutput()) },
        };
        int index = TargetNetwork() is { } t ? _program.Networks.IndexOf(t) + 1 : _program.Networks.Count;
        _program.Networks.Insert(index, net);
        _selection = net;
    }

    private FbdGate NewGate(GateKind k)
    {
        int count = FbdGate.FixedInputs(k) ?? 2;
        bool boolean = k is GateKind.And or GateKind.Or or GateKind.Xor or GateKind.Not;
        var g = new FbdGate { Kind = k };
        for (int i = 0; i < count; i++)
            g.Inputs.Add(new FbdOperand(boolean || (k == GateKind.Sel && i == 0) ? _defaultInput() : i == count - 1 && k == GateKind.Sel ? "1" : "0"));
        return g;
    }

    private FbdInstance NewInstance(string type)
    {
        string prefix = type switch { "TON" or "TOF" or "TP" => "T", "CTU" or "CTD" => "C", "SR" or "RS" => "B", _ => "P" };
        var used = _program.Nodes().OfType<FbdInstance>().Select(i => i.Instance).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = 1;
        while (used.Contains($"{prefix}{n}")) n++;
        var fb = new FbdInstance(type, $"{prefix}{n}", FbdProgram.OutputPins(type)[0]);
        FillPins(fb, null);
        return fb;
    }

    /// <summary>Rellena los pines con valores típicos; el primero recibe <paramref name="first"/> si se da.</summary>
    private void FillPins(FbdInstance fb, FbdNode? first)
    {
        var pins = FbdProgram.InputPins(fb.Type);
        fb.Inputs.Clear();
        for (int i = 0; i < pins.Count; i++)
        {
            fb.Inputs[pins[i]] = i == 0 && first is not null ? first : pins[i] switch
            {
                "PT" => new FbdOperand("T#1s"),
                "PV" => new FbdOperand("10"),
                "R" or "LD" or "R1" when i > 0 => new FbdOperand("FALSE"),
                _ => new FbdOperand(_defaultInput()),
            };
        }
    }

    /// <summary>
    /// Inserta un bloque nuevo. Si hay un elemento seleccionado, el bloque se pone en su lugar y el elemento pasa a ser
    /// su primera entrada; si no, el bloque abre una red nueva.
    /// </summary>
    private void Insert(FbdNode block)
    {
        if (_selection is FbdNode node && Locate(node) is { } at)
        {
            switch (block)
            {
                case FbdGate g: g.Inputs[0] = node; break;
                case FbdInstance fb: FillPins(fb, node); break;
            }
            at.Replace(block);
        }
        else if (_selection is FbdNetwork { Root: null } empty)
        {
            empty.Root = block;
        }
        else
        {
            var net = new FbdNetwork { Root = block };
            net.Outputs.Add(new FbdAssignment(block is FbdGate g && !FbdGate.IsBoolean(g.Kind) ? "%MW0" : _defaultOutput()));
            int index = TargetNetwork() is { } t ? _program.Networks.IndexOf(t) + 1 : _program.Networks.Count;
            _program.Networks.Insert(index, net);
        }
        _selection = block;
    }

    private void Negate()
    {
        switch (_selection)
        {
            case FbdOperand o: o.Negated = !o.Negated; break;
            case FbdGate g: g.Negated = !g.Negated; break;
            default: _hint.Text = "Selecciona primero un operando o un bloque lógico."; break;
        }
    }

    private void AddInput()
    {
        var gate = _selection as FbdGate ?? (_selection is FbdNode n ? Locate(n)?.Parent : null);
        if (gate is null || FbdGate.FixedInputs(gate.Kind) is not null)
        {
            _hint.Text = "Solo los bloques &, ≥1, =1, ADD, MUL, MAX y MIN admiten más entradas.";
            return;
        }
        var input = new FbdOperand(FbdGate.IsBoolean(gate.Kind) ? _defaultInput() : "0");
        gate.Inputs.Add(input);
        _selection = input;
    }

    private void AddOutput()
    {
        if (TargetNetwork() is not { } net) return;
        var a = new FbdAssignment(_defaultOutput());
        net.Outputs.Add(a);
        _selection = a;
    }

    private void MoveNetwork(int delta)
    {
        if (TargetNetwork() is not { } net) return;
        int i = _program.Networks.IndexOf(net), j = i + delta;
        if (j < 0 || j >= _program.Networks.Count) return;
        _program.Networks.RemoveAt(i);
        _program.Networks.Insert(j, net);
    }

    private void Delete()
    {
        switch (_selection)
        {
            case FbdNetwork net:
                _program.Networks.Remove(net);
                break;
            case FbdAssignment a:
                foreach (var n in _program.Networks) n.Outputs.Remove(a);
                break;
            case FbdNode node when Locate(node) is { } at:
                var first = node switch
                {
                    FbdGate g => g.Inputs.FirstOrDefault(),
                    FbdInstance fb => FbdProgram.InputPins(fb.Type).Select(p => fb.Inputs.GetValueOrDefault(p)).FirstOrDefault(x => x is not null),
                    _ => null,
                };
                if (first is not null) at.Replace(first);
                else if (at.Parent is { } parent && FbdGate.FixedInputs(parent.Kind) is null && parent.Inputs.Count > 2) at.Replace(null);
                else if (at.Parent is null && ReferenceEquals(at.Net.Root, node)) at.Net.Root = null;
                else
                {
                    _hint.Text = "Esta entrada es obligatoria: cambia su operando en Propiedades o borra el bloque entero.";
                    return;
                }
                break;
        }
        _selection = null;
    }

    // ---- Dibujo ----

    private void Rebuild()
    {
        _live.Clear();
        _values.Clear();
        _canvas.Children.Clear();
        int n = 0;
        foreach (var net in _program.Networks)
        {
            n++;
            var header = new Border
            {
                Padding = new Thickness(4, 1),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = $"Red {n}" + (string.IsNullOrWhiteSpace(net.Comment) ? "" : $": {DisplayText(net.Comment)}"),
                    FontSize = 12,
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            Selectable(header, net);
            _live.Add((header, net, () => false));

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (net.Root is null)
            {
                row.Children.Add(new TextBlock { Text = "(red vacía: selecciónala y elige un bloque de la paleta)", Opacity = 0.6 });
            }
            else
            {
                row.Children.Add(Render(net.Root));
                row.Children.Add(Wire(22));
                var outs = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                foreach (var o in net.Outputs) outs.Children.Add(Assignment(o));
                if (net.Outputs.Count == 0) outs.Children.Add(new TextBlock { Text = "(sin salida)", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(outs);
            }
            _canvas.Children.Add(new StackPanel { Spacing = 6, Children = { header, row } });
        }
        if (_program.Networks.Count == 0)
            _canvas.Children.Add(new TextBlock { Text = "Programa vacío: pulsa «+ Red» para empezar.", Opacity = 0.7 });

        BuildProperties();
        Refresh();
    }

    public void Refresh()
    {
        foreach (var (box, item, isActive) in _live)
        {
            bool on;
            try { on = isActive(); }
            catch { on = false; }
            bool sel = ReferenceEquals(item, _selection);
            box.BorderBrush = sel ? Selected : on ? Active : item is FbdNetwork ? Brushes.Transparent : Brushes.Gray;
            box.BorderThickness = new Thickness(sel ? 2.5 : 1.5);
            if (item is not FbdInstance && item is not FbdGate) box.Background = on ? ActiveBack : Brushes.Transparent;
            else box.Background = on ? ActiveBack : BlockBack;
        }
        foreach (var (text, value) in _values)
        {
            try { text.Text = value(); }
            catch { text.Text = ""; }
            text.IsVisible = !string.IsNullOrEmpty(text.Text);
        }
    }

    private void Selectable(Border box, object item)
    {
        box.PointerPressed += (_, e) =>
        {
            _selection = item;
            e.Handled = true;
            Focus();
            BuildProperties();
            Refresh();
        };
    }

    private static Control Wire(double width) => new Border { Width = width, Height = 2, Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };

    private static Control NegationCircle() => new Ellipse
    {
        Width = 8, Height = 8, Stroke = Brushes.Gray, StrokeThickness = 1.5, Fill = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
    };

    private Control Render(FbdNode node) => node switch
    {
        FbdOperand o => Operand(o),
        FbdGate g => Block(g, FbdGate.Symbol(g.Kind), null, g.Inputs.Select(i => ((string?)null, (FbdNode?)i)).ToList(), g.Negated,
            () => g.RuntimeName is { } r && AsBool(_read(r)), FbdGate.IsBoolean(g.Kind) || g.Negated ? null : () => g.RuntimeName),
        FbdInstance fb => Block(fb, fb.Type.ToUpperInvariant(), fb.Instance,
            FbdProgram.InputPins(fb.Type).Select(p => ((string?)p, fb.Inputs.GetValueOrDefault(p))).ToList(), false,
            () => fb.RuntimeName is { } r && AsBool(_read(r)), fb.Output is "Q" or "Q1" ? null : () => fb.RuntimeName, fb.Output),
        _ => new TextBlock { Text = "?" },
    };

    private Control Operand(FbdOperand o)
    {
        bool empty = string.IsNullOrWhiteSpace(o.Text);
        var value = new TextBlock { FontSize = 9, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center };
        var box = new Border
        {
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Gray,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Children =
                {
                    empty
                        ? new TextBlock { Text = "???", FontFamily = Mono, FontSize = 12, Foreground = Missing, HorizontalAlignment = HorizontalAlignment.Center }
                        : new TextBlock { Text = _display(o.Text), FontFamily = Mono, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
                    value,
                },
            },
        };
        Selectable(box, o);
        _live.Add((box, o, () =>
        {
            if (empty || _read(o.Text) is not PlcValue v || v.Type != PlcType.Bool) return false;
            return v.AsBool;
        }));
        _values.Add((value, () => !empty && _read(o.Text) is PlcValue v && v.Type != PlcType.Bool && !IsLiteral(o.Text) ? v.ToString() : ""));

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(box);
        if (o.Negated) row.Children.Add(NegationCircle());
        return row;
    }

    /// <summary>
    /// Bloque: a la izquierda sus entradas (cada una con su subárbol), el cuerpo con el símbolo y, si es un bloque con
    /// memoria, el nombre de instancia encima y los nombres de pin dentro.
    /// </summary>
    private Control Block(FbdNode node, string symbol, string? instance, List<(string? Pin, FbdNode? Input)> inputs, bool negated,
        Func<bool> isActive, Func<string?>? valueName, string? outputPin = null)
    {
        int rows = Math.Max(1, inputs.Count);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var body = new Border
        {
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Gray,
            Background = BlockBack,
            CornerRadius = new CornerRadius(2),
            MinWidth = instance is null ? 44 : 70,
            MinHeight = 34,
            Margin = new Thickness(0, 2),
        };
        Grid.SetColumn(body, 2);
        Grid.SetColumnSpan(body, 2);
        Grid.SetRowSpan(body, rows);
        grid.Children.Add(body);
        Selectable(body, node);
        _live.Add((body, node, isActive));

        for (int i = 0; i < inputs.Count; i++)
        {
            var (pin, input) = inputs[i];
            Control left;
            if (input is null)
            {
                // Pin sin conectar (por ejemplo un archivo antiguo): clic para crear un operando.
                var ph = new Border
                {
                    BorderBrush = Missing, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "—", FontFamily = Mono, Foreground = Missing },
                };
                var fb = (FbdInstance)node;
                string p = pin!;
                ph.PointerPressed += (_, e) =>
                {
                    var op = new FbdOperand(_defaultInput());
                    fb.Inputs[p] = op;
                    _selection = op;
                    e.Handled = true;
                    Rebuild();
                };
                left = ph;
            }
            else left = Render(input);

            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3) };
            line.Children.Add(left);
            Grid.SetRow(line, i);
            grid.Children.Add(line);

            var wire = Wire(14);
            wire.Margin = new Thickness(0, 3);
            Grid.SetRow(wire, i);
            Grid.SetColumn(wire, 1);
            grid.Children.Add(wire);

            if (pin is not null)
            {
                var label = new TextBlock { Text = pin, FontSize = 10, FontFamily = Mono, Opacity = 0.8, Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 2);
                grid.Children.Add(label);
            }
        }

        var title = new StackPanel { Margin = new Thickness(6, 4), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        if (instance is not null) title.Children.Add(new TextBlock { Text = instance, FontSize = 10, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center });
        title.Children.Add(new TextBlock { Text = symbol, FontFamily = Mono, FontWeight = FontWeight.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
        if (outputPin is not null && outputPin is not "Q")
            title.Children.Add(new TextBlock { Text = "→ " + outputPin, FontSize = 10, FontFamily = Mono, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center });
        if (valueName is not null)
        {
            var value = new TextBlock { FontSize = 10, FontFamily = Mono, Foreground = Active, HorizontalAlignment = HorizontalAlignment.Center };
            _values.Add((value, () => valueName() is { } name && _read(name) is PlcValue v ? v.ToString() : ""));
            title.Children.Add(value);
        }
        Grid.SetColumn(title, 3);
        Grid.SetRowSpan(title, rows);
        grid.Children.Add(title);

        var outer = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        outer.Children.Add(grid);
        if (negated) outer.Children.Add(NegationCircle());
        return outer;
    }

    private Control Assignment(FbdAssignment a)
    {
        string sym = a.Kind switch { FbdAssignKind.Set => "S", FbdAssignKind.Reset => "R", _ => "=" };
        var box = new Border
        {
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Gray,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Border
                    {
                        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1.5, 0), Padding = new Thickness(6, 3), MinWidth = 22,
                        Child = new TextBlock { Text = sym, FontFamily = Mono, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                    new TextBlock { Text = _display(a.Target), FontFamily = Mono, FontSize = 12, Margin = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        Selectable(box, a);
        _live.Add((box, a, () => AsBool(_read(a.Target))));
        return box;
    }

    // ---- Propiedades ----

    private void BuildProperties()
    {
        var p = new StackPanel { Spacing = 6 };
        _hint.Text = "Haz clic en un elemento para seleccionarlo. Un bloque de la paleta se coloca en lugar del elemento " +
                     "seleccionado y este pasa a ser su primera entrada. Después pulsa «Compilar y cargar».";
        switch (_selection)
        {
            case FbdOperand o:
                p.Children.Add(Field("Operando (dirección, variable, T1.Q, 5, T#2s…)", o.Text, v => o.Text = v));
                p.Children.Add(Check("Negado ○", o.Negated, v => o.Negated = v));
                break;
            case FbdGate g:
            {
                var kinds = Enum.GetValues<GateKind>();
                p.Children.Add(Choice("Bloque", kinds.Select(k => $"{FbdGate.Symbol(k)}").ToArray(), Array.IndexOf(kinds, g.Kind), i =>
                {
                    g.Kind = kinds[i];
                    if (FbdGate.FixedInputs(g.Kind) is { } c)
                    {
                        while (g.Inputs.Count > c) g.Inputs.RemoveAt(g.Inputs.Count - 1);
                        while (g.Inputs.Count < c) g.Inputs.Add(new FbdOperand("0"));
                    }
                }));
                p.Children.Add(Check("Salida negada ○", g.Negated, v => g.Negated = v));
                p.Children.Add(new TextBlock { Text = $"{g.Inputs.Count} entrada(s)", FontSize = 12, Opacity = 0.7 });
                break;
            }
            case FbdInstance fb:
            {
                var types = FbdProgram.BlockTypes;
                p.Children.Add(Choice("Bloque", types, Array.IndexOf(types, fb.Type.ToUpperInvariant()), i =>
                {
                    if (string.Equals(types[i], fb.Type, StringComparison.OrdinalIgnoreCase)) return;
                    var first = FbdProgram.InputPins(fb.Type).Select(pin => fb.Inputs.GetValueOrDefault(pin)).FirstOrDefault();
                    fb.Type = types[i];
                    fb.Output = FbdProgram.OutputPins(fb.Type)[0];
                    FillPins(fb, first);
                    BuildProperties();
                }));
                p.Children.Add(Field("Instancia", fb.Instance, v => fb.Instance = v));
                var outs = FbdProgram.OutputPins(fb.Type).ToArray();
                if (outs.Length > 1)
                    p.Children.Add(Choice("Salida que sigue la red", outs, Array.IndexOf(outs, fb.Output), i => fb.Output = outs[i]));
                p.Children.Add(new TextBlock
                {
                    Text = $"Las demás salidas se usan como operando: {string.Join(", ", outs.Select(o => $"{fb.Instance}.{o}"))}.",
                    FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                });
                break;
            }
            case FbdAssignment a:
                p.Children.Add(Choice("Tipo", ["= Asignar", "S Activar (enclavar)", "R Desactivar"], (int)a.Kind, i => a.Kind = (FbdAssignKind)i));
                p.Children.Add(Field("Destino", a.Target, v => a.Target = v));
                break;
            case FbdNetwork net:
                p.Children.Add(Field("Comentario", net.Comment ?? "", v => net.Comment = v));
                break;
            default:
                p.Children.Add(new TextBlock { Text = "Nada seleccionado.", Opacity = 0.7 });
                break;
        }
        _properties.Content = p;
    }

    private Control Field(string label, string value, Action<string> apply)
    {
        var box = new TextBox { Text = value, FontFamily = Mono };
        string last = value;
        box.TextChanged += (_, _) =>
        {
            if ((box.Text ?? "") == last) return;
            last = box.Text ?? "";
            apply(last);
            RebuildCanvasOnly();
        };
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap }, box } };
    }

    private Control Check(string label, bool value, Action<bool> apply)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) =>
        {
            apply(box.IsChecked == true);
            RebuildCanvasOnly();
        };
        return box;
    }

    private Control Choice(string label, string[] options, int selected, Action<int> apply)
    {
        var combo = new ComboBox { ItemsSource = options, SelectedIndex = Math.Max(0, selected), HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            apply(combo.SelectedIndex);
            RebuildCanvasOnly();
        };
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12 }, combo } };
    }

    private void RebuildCanvasOnly()
    {
        var props = _properties.Content;
        var hint = _hint.Text;
        Rebuild();
        _properties.Content = props;
        _hint.Text = hint;
    }

    /// <summary>Muestra las direcciones IEC de un comentario (%IX0.0) con la nomenclatura de la marca.</summary>
    private string DisplayText(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"%[A-Za-z]+[0-9.]+[0-9]", m => _display(m.Value));

    private static bool AsBool(object? v) => v is PlcValue p && p.AsBool;

    private static bool IsLiteral(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || text.StartsWith("T#", StringComparison.OrdinalIgnoreCase);
}
