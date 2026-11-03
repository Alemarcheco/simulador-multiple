using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Plc.Runtime.Ladder;

namespace Simulador.Plc;

/// <summary>
/// Dibuja un programa Ladder y colorea en verde los contactos cerrados y las bobinas activas mientras el PLC está en RUN.
/// </summary>
internal sealed class LadderView : UserControl
{
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#2E9E44"));
    private static readonly IBrush ActiveBack = new SolidColorBrush(Color.Parse("#332E9E44"));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly List<(Border Box, Func<bool> IsActive)> _live = [];
    private readonly Func<string, string> _display;

    /// <param name="program">Programa a dibujar.</param>
    /// <param name="read">Lee un operando (null si no se puede leer todavía).</param>
    /// <param name="display">Convierte un operando a la nomenclatura de la marca para mostrarlo.</param>
    public LadderView(LadderProgram program, Func<string, object?> read, Func<string, string> display)
    {
        _display = display;
        var rungs = new StackPanel { Spacing = 14, Margin = new Thickness(12) };
        int n = 0;
        foreach (var rung in program.Rungs)
        {
            n++;
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(Rail());
            if (rung.Condition is not null) line.Children.Add(Node(rung.Condition, read));
            line.Children.Add(Wire(24));
            var outs = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var o in rung.Outputs) outs.Children.Add(Output(o, read));
            line.Children.Add(outs);
            line.Children.Add(Wire(12));
            line.Children.Add(Rail());

            var block = new StackPanel { Spacing = 4 };
            block.Children.Add(new TextBlock
            {
                Text = $"Segmento {n}" + (rung.Comment is null ? "" : $": {rung.Comment}"),
                Opacity = 0.7,
                FontSize = 12,
            });
            block.Children.Add(line);
            rungs.Children.Add(block);
        }
        Content = new ScrollViewer { Content = rungs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    /// <summary>Actualiza los colores con el estado actual del PLC.</summary>
    public void Refresh()
    {
        foreach (var (box, isActive) in _live)
        {
            bool on;
            try { on = isActive(); }
            catch { on = false; }
            box.BorderBrush = on ? Active : Brushes.Gray;
            box.Background = on ? ActiveBack : Brushes.Transparent;
        }
    }

    private static Control Rail() => new Border { Width = 3, MinHeight = 44, Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Stretch };

    private static Control Wire(double width) => new Border { Width = width, Height = 2, Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };

    private Control Node(LadderNode node, Func<string, object?> read)
    {
        switch (node)
        {
            case Series s:
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var item in s.Items)
                {
                    row.Children.Add(Wire(10));
                    row.Children.Add(Node(item, read));
                }
                return row;
            case ParallelBranch p:
                var col = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                foreach (var b in p.Branches)
                {
                    var branch = new StackPanel { Orientation = Orientation.Horizontal };
                    branch.Children.Add(new Border { Width = 2, Background = Brushes.Gray });
                    branch.Children.Add(Node(b, read));
                    branch.Children.Add(new Border { Width = 2, Background = Brushes.Gray });
                    col.Children.Add(branch);
                }
                return col;
            case Contact c:
                string sym = c.Kind switch
                {
                    ContactKind.NormallyOpen => "─┤ ├─",
                    ContactKind.NormallyClosed => "─┤/├─",
                    ContactKind.RisingEdge => "─┤P├─",
                    _ => "─┤N├─",
                };
                return Element(_display(c.Operand), sym, () =>
                {
                    bool v = AsBool(read(c.Operand));
                    return c.Kind == ContactKind.NormallyClosed ? !v : v;
                });
            case CompareContact cc:
                return Element($"{_display(cc.Left)} {cc.Op} {_display(cc.Right)}", "─[CMP]─", () => Compare(read(cc.Left), cc.Op, read(cc.Right)));
        }
        return new TextBlock { Text = "?" };
    }

    private Control Output(LadderOutput o, Func<string, object?> read) => o switch
    {
        Coil c => Element(_display(c.Operand), c.Kind switch
        {
            CoilKind.Normal => "─( )─",
            CoilKind.Negated => "─(/)─",
            CoilKind.Set => "─(S)─",
            _ => "─(R)─",
        }, () => AsBool(read(c.Operand))),
        TimerBox t => Element($"{t.Instance}  PT={FormatTime(t.Preset)}", $"[{t.Kind}]", () => AsBool(read($"{t.Instance}.Q"))),
        CounterBox k => Element($"{k.Instance}  PV={k.Preset}" + (k.ResetOperand is null ? "" : $"  R={_display(k.ResetOperand)}"),
            $"[{k.Kind}]", () => AsBool(read($"{k.Instance}.Q"))),
        MoveBox m => Element($"{_display(m.Source)} → {_display(m.Destination)}", "[MOVE]", () => false),
        _ => new TextBlock { Text = "?" },
    };

    private Border Element(string label, string symbol, Func<bool> isActive)
    {
        var box = new Border
        {
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Gray,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = symbol, FontFamily = Mono, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        _live.Add((box, isActive));
        return box;
    }

    private static bool AsBool(object? v) => v is Runtime.PlcValue p && p.AsBool;

    private static bool Compare(object? a, string op, object? b)
    {
        if (a is not Runtime.PlcValue x || b is not Runtime.PlcValue y) return false;
        int c = x.AsReal.CompareTo(y.AsReal);
        return op switch { "=" => c == 0, "<>" => c != 0, "<" => c < 0, ">" => c > 0, "<=" => c <= 0, _ => c >= 0 };
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalMilliseconds % 1000 == 0 ? $"{t.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s" : $"{t.TotalMilliseconds}ms";
}
