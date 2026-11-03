using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Sfc;

namespace Simulador.Plc;

/// <summary>
/// Dibujo de un SFC/GRAFCET al estilo de la norma: etapas (doble recuadro la inicial), transiciones como trazo
/// horizontal con su receptividad, acciones a la derecha y saltos indicados con flecha. Las etapas activas se ven en
/// verde con el tiempo que llevan activas.
/// </summary>
internal sealed class SfcView : UserControl
{
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#2E9E44"));
    private static readonly IBrush ActiveBack = new SolidColorBrush(Color.Parse("#332E9E44"));
    private static readonly IBrush Line = Brushes.Gray;
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly Func<string, object?> _read;
    private readonly List<Action> _refresh = [];

    public SfcView(SfcChart chart, Func<string, object?> read, Func<string, string> display)
    {
        _read = read;
        var col = new StackPanel { Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < chart.Steps.Count; i++)
        {
            var step = chart.Steps[i];
            col.Children.Add(StepRow(step, display));
            var outgoing = chart.Transitions.Where(t => string.Equals(t.From[0], step.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            string? next = i + 1 < chart.Steps.Count ? chart.Steps[i + 1].Name : null;
            if (outgoing.Count == 0)
            {
                col.Children.Add(new TextBlock { Text = "   (sin transición de salida)", Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 4) });
                continue;
            }
            foreach (var tr in outgoing) col.Children.Add(TransitionRow(tr, next, display));
        }
        Content = new ScrollViewer { Content = col, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    public void Refresh()
    {
        foreach (var r in _refresh) r();
    }

    private bool IsActive(string step) => _read($"{step}.X") is PlcValue v && v.AsBool;

    private Control StepRow(SfcStep step, Func<string, string> display)
    {
        var name = new TextBlock { Text = step.Name, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var time = new TextBlock { FontSize = 10, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center };
        var inner = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(step.Initial ? 1.5 : 0),
            Margin = new Thickness(step.Initial ? 3 : 0),
            Padding = new Thickness(6, 4),
            Child = new StackPanel { Children = { name, time } },
        };
        var box = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(2),
            MinWidth = 110,
            Child = inner,
        };
        _refresh.Add(() =>
        {
            bool on = IsActive(step.Name);
            box.BorderBrush = on ? Active : Line;
            box.Background = on ? ActiveBack : Brushes.Transparent;
            time.Text = on && _read($"{step.Name}.T") is PlcValue t ? t.ToString() : "";
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        if (step.Actions.Count > 0)
        {
            row.Children.Add(new Border { Width = 18, Height = 2, Background = Line, VerticalAlignment = VerticalAlignment.Center });
            var actions = new StackPanel();
            foreach (var a in step.Actions)
            {
                string q = a.Time is { } t ? $"{a.Qualifier} {PlcValue.FromTime(t)}" : a.Qualifier;
                actions.Children.Add(new Border
                {
                    BorderBrush = Line,
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, 0, -1.5),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new Border
                            {
                                BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1.5, 0), Padding = new Thickness(6, 3), MinWidth = 34,
                                Child = new TextBlock { Text = q, FontFamily = Mono, FontSize = 12 },
                            },
                            new TextBlock { Text = display(a.Action), Margin = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                });
            }
            row.Children.Add(actions);
        }
        return row;
    }

    private Control TransitionRow(SfcTransition tr, string? next, Func<string, string> display)
    {
        bool simultaneousIn = tr.From.Count > 1, simultaneousOut = tr.To.Count > 1;
        bool jump = tr.To.Count == 1 && !string.Equals(tr.To[0], next, StringComparison.OrdinalIgnoreCase);

        var bar = new Border { Width = 34, Height = 3, Background = Line, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(38, 0, 0, 0) };
        var cond = new TextBlock { Text = DisplayCondition(tr.ConditionText, display), FontFamily = Mono, FontSize = 12, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
        string extra = "";
        if (simultaneousIn) extra += $"  ⇇ espera a ({string.Join(", ", tr.From)})";
        if (simultaneousOut) extra += $"  ⇉ activa a la vez ({string.Join(", ", tr.To)})";
        else if (jump) extra += $"  → {tr.To[0]}";
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2) };
        row.Children.Add(bar);
        row.Children.Add(cond);
        if (extra.Length > 0) row.Children.Add(new TextBlock { Text = extra, Opacity = 0.75, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        _refresh.Add(() =>
        {
            bool armed = tr.From.All(IsActive);
            bar.Background = armed ? Active : Line;
        });

        return new StackPanel
        {
            Children =
            {
                new Border { Width = 2, Height = 10, Background = Line, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(54, 0, 0, 0) },
                row,
                new Border { Width = 2, Height = 10, Background = Line, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(54, 0, 0, 0) },
            },
        };
    }

    private static string DisplayCondition(string text, Func<string, string> display) =>
        string.Join(" ", text.Replace(" . ", ".").Split(' ').Select(display));
}
