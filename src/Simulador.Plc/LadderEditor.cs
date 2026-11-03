using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc;

/// <summary>
/// Editor gráfico de Ladder: paleta de elementos, segmentos con selección por clic, panel de propiedades y coloreado en
/// vivo (verde = contacto cerrado / bobina activa) mientras el PLC está en RUN.
/// </summary>
internal sealed class LadderEditor : UserControl
{
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#2E9E44"));
    private static readonly IBrush ActiveBack = new SolidColorBrush(Color.Parse("#332E9E44"));
    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#2F7FE0"));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly Func<string, object?> _read;
    private readonly Func<string, string> _display;
    private readonly Func<string> _defaultInput;
    private readonly Func<string> _defaultOutput;
    private readonly StackPanel _canvas = new() { Spacing = 14, Margin = new Thickness(12) };
    private readonly ContentControl _properties = new();
    private readonly TextBlock _hint = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly List<(Border Box, object Item, Func<bool> IsActive)> _live = [];
    private LadderDocument _doc = new();
    private object? _selection;

    /// <param name="read">Lee un operando del PLC (null si aún no hay programa cargado).</param>
    /// <param name="display">Convierte un operando a la nomenclatura de la marca.</param>
    /// <param name="defaultInput">Dirección sugerida para contactos nuevos.</param>
    /// <param name="defaultOutput">Dirección sugerida para bobinas nuevas.</param>
    public LadderEditor(Func<string, object?> read, Func<string, string> display, Func<string> defaultInput, Func<string> defaultOutput)
    {
        _read = read;
        _display = display;
        _defaultInput = defaultInput;
        _defaultOutput = defaultOutput;

        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Tool(string text, string tip, Action action)
        {
            var b = new Button { Content = text, Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 4, 4), FontFamily = Mono };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { action(); Rebuild(); };
            palette.Children.Add(b);
        }

        Tool("+ Segmento", "Añade un segmento nuevo debajo del seleccionado", AddRung);
        Tool("┤ ├", "Contacto normalmente abierto", () => AddCondition(new EditContact(ContactKind.NormallyOpen, _defaultInput())));
        Tool("┤/├", "Contacto normalmente cerrado", () => AddCondition(new EditContact(ContactKind.NormallyClosed, _defaultInput())));
        Tool("┤P├", "Flanco ascendente", () => AddCondition(new EditContact(ContactKind.RisingEdge, _defaultInput())));
        Tool("┤N├", "Flanco descendente", () => AddCondition(new EditContact(ContactKind.FallingEdge, _defaultInput())));
        Tool("[CMP]", "Comparación (>=, <, =…)", () => AddCondition(new EditCompare("%MW0", ">=", "0")));
        Tool("⑂ Paralelo", "Pone un contacto nuevo en paralelo con el elemento seleccionado", AddParallel);
        Tool("( )", "Bobina", () => AddOutput(new Coil(CoilKind.Normal, _defaultOutput())));
        Tool("(/)", "Bobina negada", () => AddOutput(new Coil(CoilKind.Negated, _defaultOutput())));
        Tool("(S)", "Activar (enclavar)", () => AddOutput(new Coil(CoilKind.Set, _defaultOutput())));
        Tool("(R)", "Desactivar", () => AddOutput(new Coil(CoilKind.Reset, _defaultOutput())));
        Tool("TON", "Temporizador con retardo a la conexión", () => AddOutput(new TimerBox(TimerKind.TON, NextInstance("T"), TimeSpan.FromSeconds(1))));
        Tool("TOF", "Temporizador con retardo a la desconexión", () => AddOutput(new TimerBox(TimerKind.TOF, NextInstance("T"), TimeSpan.FromSeconds(1))));
        Tool("TP", "Temporizador de impulso", () => AddOutput(new TimerBox(TimerKind.TP, NextInstance("T"), TimeSpan.FromSeconds(1))));
        Tool("CTU", "Contador ascendente", () => AddOutput(new CounterBox(CounterKind.CTU, NextInstance("C"), 10, null)));
        Tool("CTD", "Contador descendente", () => AddOutput(new CounterBox(CounterKind.CTD, NextInstance("C"), 10, null)));
        Tool("MOVE", "Copiar un valor", () => AddOutput(new MoveBox("0", "%MW0")));
        Tool("▲", "Subir segmento", () => MoveRung(-1));
        Tool("▼", "Bajar segmento", () => MoveRung(1));
        Tool("✕ Borrar", "Borra el elemento o segmento seleccionado (tecla Supr)", Delete);

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

    public LadderDocument Document
    {
        get => _doc;
        set
        {
            _doc = value;
            _selection = null;
            Rebuild();
        }
    }

    // ---- Operaciones ----

    private EditRung TargetRung()
    {
        switch (_selection)
        {
            case EditRung r: return r;
            case EditNode n: return LadderDocument.RungOf(n);
            case EditOutput { Rung: { } r }: return r;
        }
        if (_doc.Rungs.Count == 0) _doc.AddRung();
        return _doc.Rungs[^1];
    }

    private void AddRung()
    {
        int index = _selection is null ? _doc.Rungs.Count : _doc.Rungs.IndexOf(TargetRung()) + 1;
        var r = _doc.AddRung(index);
        _doc.Append(r, new EditContact(ContactKind.NormallyOpen, _defaultInput()));
        _doc.AddOutput(r, new Coil(CoilKind.Normal, _defaultOutput()));
        _selection = r;
    }

    private void AddCondition(EditNode node)
    {
        if (_selection is EditNode anchor) _doc.InsertAfter(anchor, node);
        else _doc.Append(TargetRung(), node);
        _selection = node;
    }

    private void AddParallel()
    {
        if (_selection is not EditNode anchor)
        {
            _hint.Text = "Selecciona primero el contacto al que quieres poner otro en paralelo.";
            return;
        }
        var node = new EditContact(ContactKind.NormallyOpen, _defaultInput());
        _doc.WrapInParallel(anchor, node);
        _selection = node;
    }

    private void AddOutput(LadderOutput value) => _selection = _doc.AddOutput(TargetRung(), value);

    private void MoveRung(int delta)
    {
        if (_selection is null) return;
        _doc.MoveRung(TargetRung(), delta);
    }

    private void Delete()
    {
        switch (_selection)
        {
            case EditNode n: _doc.Remove(n); break;
            case EditOutput o: _doc.RemoveOutput(o); break;
            case EditRung r: _doc.RemoveRung(r); break;
        }
        _selection = null;
    }

    private string NextInstance(string prefix)
    {
        var used = _doc.Rungs.SelectMany(r => r.Outputs).Select(o => o.Value switch
        {
            TimerBox t => t.Instance,
            CounterBox c => c.Instance,
            _ => null,
        }).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int i = 1;
        while (used.Contains($"{prefix}{i}")) i++;
        return $"{prefix}{i}";
    }

    // ---- Dibujo ----

    private void Rebuild()
    {
        _live.Clear();
        _canvas.Children.Clear();
        int n = 0;
        foreach (var rung in _doc.Rungs)
        {
            n++;
            var header = new TextBlock
            {
                Text = $"Segmento {n}" + (string.IsNullOrWhiteSpace(rung.Comment) ? "" : $": {rung.Comment}"),
                FontSize = 12,
                Opacity = 0.8,
            };
            var headerBox = new Border { Child = header, Padding = new Thickness(4, 1), CornerRadius = new CornerRadius(3) };
            Selectable(headerBox, rung);
            _live.Add((headerBox, rung, () => false));

            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(Rail());
            if (rung.Condition.Items.Count == 0)
                line.Children.Add(new TextBlock { Text = " (siempre) ", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            else
                line.Children.Add(Series(rung.Condition));
            line.Children.Add(Wire(24));
            var outs = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var o in rung.Outputs) outs.Children.Add(Output(o));
            if (rung.Outputs.Count == 0) outs.Children.Add(new TextBlock { Text = "(sin salida)", Opacity = 0.6 });
            line.Children.Add(outs);
            line.Children.Add(Wire(12));
            line.Children.Add(Rail());

            _canvas.Children.Add(new StackPanel { Spacing = 4, Children = { headerBox, line } });
        }
        if (_doc.Rungs.Count == 0)
            _canvas.Children.Add(new TextBlock { Text = "Programa vacío: pulsa «+ Segmento» para empezar.", Opacity = 0.7 });

        BuildProperties();
        Refresh();
    }

    /// <summary>Actualiza los colores con el estado actual del PLC y la selección.</summary>
    public void Refresh()
    {
        foreach (var (box, item, isActive) in _live)
        {
            bool on;
            try { on = isActive(); }
            catch { on = false; }
            box.BorderBrush = ReferenceEquals(item, _selection) ? Selected : on ? Active : item is EditRung ? Brushes.Transparent : Brushes.Gray;
            box.BorderThickness = new Thickness(ReferenceEquals(item, _selection) ? 2.5 : 1.5);
            box.Background = on ? ActiveBack : Brushes.Transparent;
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

    private static Control Rail() => new Border { Width = 3, MinHeight = 44, Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Stretch };

    private static Control Wire(double width) => new Border { Width = width, Height = 2, Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };

    private Control Series(EditSeries s)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in s.Items)
        {
            row.Children.Add(Wire(10));
            row.Children.Add(Node(item));
        }
        row.Children.Add(Wire(10));
        return row;
    }

    private Control Node(EditNode node)
    {
        switch (node)
        {
            case EditParallel p:
                var col = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                foreach (var b in p.Branches)
                {
                    col.Children.Add(new Border
                    {
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(2, 0, 2, 0),
                        Child = Series(b),
                    });
                }
                return col;
            case EditContact c:
                string sym = c.Kind switch
                {
                    ContactKind.NormallyOpen => "─┤ ├─",
                    ContactKind.NormallyClosed => "─┤/├─",
                    ContactKind.RisingEdge => "─┤P├─",
                    _ => "─┤N├─",
                };
                return Element(c, _display(c.Operand), sym, () =>
                {
                    bool v = AsBool(_read(c.Operand));
                    return c.Kind == ContactKind.NormallyClosed ? !v : v;
                });
            case EditCompare cc:
                return Element(cc, $"{_display(cc.Left)} {cc.Op} {_display(cc.Right)}", "─[CMP]─",
                    () => Compare(_read(cc.Left), cc.Op, _read(cc.Right)));
        }
        return new TextBlock { Text = "?" };
    }

    private Control Output(EditOutput o) => o.Value switch
    {
        Coil c => Element(o, _display(c.Operand), c.Kind switch
        {
            CoilKind.Normal => "─( )─",
            CoilKind.Negated => "─(/)─",
            CoilKind.Set => "─(S)─",
            _ => "─(R)─",
        }, () => AsBool(_read(c.Operand))),
        TimerBox t => Element(o, $"{t.Instance}  PT={FormatTime(t.Preset)}", $"[{t.Kind}]", () => AsBool(_read($"{t.Instance}.Q"))),
        CounterBox k => Element(o, $"{k.Instance}  PV={k.Preset}" + (k.ResetOperand is null ? "" : $"  R={_display(k.ResetOperand)}"),
            $"[{k.Kind}]", () => AsBool(_read($"{k.Instance}.Q"))),
        MoveBox m => Element(o, $"{_display(m.Source)} → {_display(m.Destination)}", "[MOVE]", () => false),
        _ => new TextBlock { Text = "?" },
    };

    private Border Element(object item, string label, string symbol, Func<bool> isActive)
    {
        var box = new Border
        {
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Gray,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = symbol, FontFamily = Mono, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        Selectable(box, item);
        _live.Add((box, item, isActive));
        return box;
    }

    // ---- Propiedades ----

    private void BuildProperties()
    {
        var p = new StackPanel { Spacing = 6 };
        _hint.Text = "Haz clic en un elemento para seleccionarlo. Los contactos nuevos se añaden a continuación del " +
                     "seleccionado; las bobinas y cajas, al final de su segmento. Después pulsa «Compilar y cargar».";
        switch (_selection)
        {
            case EditContact c:
                p.Children.Add(Choice("Tipo", new[] { "NA ┤ ├", "NC ┤/├", "Flanco P", "Flanco N" }, (int)c.Kind, i => c.Kind = (ContactKind)i));
                p.Children.Add(Field("Operando", c.Operand, v => c.Operand = v));
                break;
            case EditCompare cc:
                p.Children.Add(Field("Izquierda", cc.Left, v => cc.Left = v));
                var ops = new[] { "=", "<>", "<", ">", "<=", ">=" };
                p.Children.Add(Choice("Comparación", ops, Array.IndexOf(ops, cc.Op), i => cc.Op = ops[i]));
                p.Children.Add(Field("Derecha", cc.Right, v => cc.Right = v));
                break;
            case EditOutput { Value: Coil coil } o:
                p.Children.Add(Choice("Tipo", new[] { "Bobina ( )", "Negada (/)", "Activar (S)", "Desactivar (R)" }, (int)coil.Kind,
                    i => o.Value = (Coil)o.Value with { Kind = (CoilKind)i }));
                p.Children.Add(Field("Operando", coil.Operand, v => o.Value = (Coil)o.Value with { Operand = v }));
                break;
            case EditOutput { Value: TimerBox t } o:
                p.Children.Add(Choice("Tipo", new[] { "TON", "TOF", "TP" }, (int)t.Kind, i => o.Value = (TimerBox)o.Value with { Kind = (TimerKind)i }));
                p.Children.Add(Field("Instancia", t.Instance, v => o.Value = (TimerBox)o.Value with { Instance = v }));
                p.Children.Add(Field("Tiempo (p. ej. 5s, 500ms, 1m30s)", FormatTime(t.Preset), v =>
                {
                    try { o.Value = (TimerBox)o.Value with { Preset = Lexer.ParseTime(v.StartsWith("T#", StringComparison.OrdinalIgnoreCase) ? v[2..] : v, default) }; }
                    catch (PlcCompileException) { }
                }));
                break;
            case EditOutput { Value: CounterBox k } o:
                p.Children.Add(Choice("Tipo", new[] { "CTU (ascendente)", "CTD (descendente)" }, (int)k.Kind, i => o.Value = (CounterBox)o.Value with { Kind = (CounterKind)i }));
                p.Children.Add(Field("Instancia", k.Instance, v => o.Value = (CounterBox)o.Value with { Instance = v }));
                p.Children.Add(Field("Preselección", k.Preset.ToString(CultureInfo.InvariantCulture), v =>
                {
                    if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) o.Value = (CounterBox)o.Value with { Preset = n };
                }));
                p.Children.Add(Field(k.Kind == CounterKind.CTU ? "Puesta a cero (R)" : "Carga (LD)", k.ResetOperand ?? "",
                    v => o.Value = (CounterBox)o.Value with { ResetOperand = string.IsNullOrWhiteSpace(v) ? null : v }));
                break;
            case EditOutput { Value: MoveBox m } o:
                p.Children.Add(Field("Origen", m.Source, v => o.Value = (MoveBox)o.Value with { Source = v }));
                p.Children.Add(Field("Destino", m.Destination, v => o.Value = (MoveBox)o.Value with { Destination = v }));
                break;
            case EditRung r:
                p.Children.Add(Field("Comentario", r.Comment ?? "", v => r.Comment = v));
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
            // Avalonia avisa también del texto inicial: solo se aplica lo que el usuario cambia.
            if ((box.Text ?? "") == last) return;
            last = box.Text ?? "";
            apply(last);
            _doc.NotifyChanged();
            RebuildCanvasOnly();
        };
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12 }, box } };
    }

    private Control Choice(string label, string[] options, int selected, Action<int> apply)
    {
        var combo = new ComboBox { ItemsSource = options, SelectedIndex = Math.Max(0, selected), HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            apply(combo.SelectedIndex);
            _doc.NotifyChanged();
            RebuildCanvasOnly();
        };
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12 }, combo } };
    }

    /// <summary>Redibuja los segmentos sin recrear el panel de propiedades (para no perder el foco al escribir).</summary>
    private void RebuildCanvasOnly()
    {
        var props = _properties.Content;
        var hint = _hint.Text;
        Rebuild();
        _properties.Content = props;
        _hint.Text = hint;
    }

    private static bool AsBool(object? v) => v is PlcValue p && p.AsBool;

    private static bool Compare(object? a, string op, object? b)
    {
        if (a is not PlcValue x || b is not PlcValue y) return false;
        int c = x.AsReal.CompareTo(y.AsReal);
        return op switch { "=" => c == 0, "<>" => c != 0, "<" => c < 0, ">" => c > 0, "<=" => c <= 0, _ => c >= 0 };
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalMilliseconds % 1000 == 0 ? $"{t.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s" : $"{t.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}ms";
}
