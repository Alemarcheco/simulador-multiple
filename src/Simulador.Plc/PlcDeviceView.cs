using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Core.Signals;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Plc;

/// <summary>
/// Dibujo del frontal del PLC: bornes de entradas arriba, LEDs de estado de cada entrada y salida, LEDs RUN/STOP/ERROR y
/// bornes de salidas abajo, como en el equipo real. Los LEDs se encienden en vivo; clic en un LED de entrada la conmuta.
/// </summary>
/// <remarks>Es una representación genérica por familia (colores y disposición), no una reproducción de ningún producto.</remarks>
internal sealed class PlcDeviceView : UserControl
{
    private static readonly IBrush LedOff = new SolidColorBrush(Color.Parse("#3A3F44"));
    private static readonly IBrush InputOn = new SolidColorBrush(Color.Parse("#39D353"));
    private static readonly IBrush OutputOn = new SolidColorBrush(Color.Parse("#FFB020"));
    private static readonly IBrush RunOn = new SolidColorBrush(Color.Parse("#39D353"));
    private static readonly IBrush StopOn = new SolidColorBrush(Color.Parse("#FFB020"));
    private static readonly IBrush ErrorOn = new SolidColorBrush(Color.Parse("#FF4D4D"));
    private static readonly IBrush Terminal = new SolidColorBrush(Color.Parse("#B8BCC2"));
    private static readonly IBrush Screw = new SolidColorBrush(Color.Parse("#6B7178"));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly PlcCpu _cpu;
    private readonly double _cell;
    private readonly List<Action> _refresh = [];

    /// <summary>Colores del cuerpo y del frontal por familia.</summary>
    private static (Color Body, Color Panel, Color Text) Style(PlcProfile p) => p switch
    {
        SiemensS71200Profile => (Color.Parse("#2F3B44"), Color.Parse("#1E272E"), Color.Parse("#DCE6EC")),
        AllenBradleyMicroLogixProfile => (Color.Parse("#4A4D52"), Color.Parse("#2C2E31"), Color.Parse("#E8E8E8")),
        SchneiderM221Profile => (Color.Parse("#E9ECEF"), Color.Parse("#C9CED3"), Color.Parse("#1F2D3A")),
        MitsubishiFx5uProfile => (Color.Parse("#D9DBDD"), Color.Parse("#3D4247"), Color.Parse("#1D2125")),
        OmronCp1eProfile => (Color.Parse("#E3E3DE"), Color.Parse("#2E3236"), Color.Parse("#1D2125")),
        DeltaDvpProfile => (Color.Parse("#E6E7E8"), Color.Parse("#34383C"), Color.Parse("#1D2125")),
        _ => (Color.Parse("#39424C"), Color.Parse("#232A31"), Color.Parse("#E6EDF3")),
    };

    public PlcDeviceView(PlcCpu cpu, Action<ISignalPort> toggleInput)
    {
        _cpu = cpu;
        // Ancho de cada borne/LED según la dirección más larga de la marca (%IX0.0 ocupa más que X0).
        _cell = Math.Max(30, cpu.Signals.Max(p => Short(p).Length) * 5.4 + 6);
        var (body, panel, text) = Style(cpu.Profile);
        var textBrush = new SolidColorBrush(text);
        var panelText = new SolidColorBrush(Color.Parse("#E6EDF3"));

        var inputs = cpu.DigitalInputs.Concat(cpu.AnalogInputs).ToList();
        var outputs = cpu.DigitalOutputs.Concat(cpu.AnalogOutputs).ToList();

        var leds = new StackPanel { Spacing = 6 };
        leds.Children.Add(LedRow("IN", cpu.DigitalInputs, InputOn, port => cpu.Image.GetBit(Address(port)), toggleInput, panelText));
        leds.Children.Add(LedRow("OUT", cpu.DigitalOutputs, OutputOn, port => port.Value > 0.5, null, panelText));

        var status = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(StatusLed("RUN", RunOn, () => cpu.State == PlcState.Run, panelText));
        status.Children.Add(StatusLed("STOP", StopOn, () => cpu.State == PlcState.Stop, panelText));
        status.Children.Add(StatusLed("ERROR", ErrorOn, () => cpu.State == PlcState.Fault || cpu.Diagnostics.Count > 0, panelText));

        var front = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8) };
        front.Children.Add(status);
        Grid.SetColumn(leds, 1);
        leds.Margin = new Thickness(14, 0, 0, 0);
        front.Children.Add(leds);

        var faceplate = new Border
        {
            Background = new SolidColorBrush(panel),
            CornerRadius = new CornerRadius(3),
            Child = front,
        };

        var label = new StackPanel
        {
            Margin = new Thickness(10, 6),
            Children =
            {
                new TextBlock { Text = cpu.Profile.Brand.ToUpperInvariant(), FontWeight = FontWeight.Bold, FontSize = 13, Foreground = textBrush },
                new TextBlock { Text = cpu.Profile.Model, FontSize = 11, Foreground = textBrush, Opacity = 0.85 },
                new TextBlock
                {
                    Text = $"{cpu.Profile.DigitalInputs} DI {cpu.Profile.InputNominalVolts:0} V DC · {cpu.Profile.DigitalOutputs} DO " +
                           (cpu.Profile.OutputCommon == CommonType.Relay ? "relé" : "transistor") +
                           (cpu.Profile.AnalogInputs > 0 ? $" · {cpu.Profile.AnalogInputs} AI 0-10 V" : ""),
                    FontSize = 10, Foreground = textBrush, Opacity = 0.7,
                },
            },
        };

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(TerminalStrip(inputs.Select(Short).Prepend("M").Prepend("L+"), textBrush));
        content.Children.Add(faceplate);
        content.Children.Add(label);
        content.Children.Add(TerminalStrip(outputs.Select(Short).Prepend("M").Prepend("L+"), textBrush));

        Content = new Border
        {
            Background = new SolidColorBrush(body),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.Parse("#80000000")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            BoxShadow = BoxShadows.Parse("0 3 8 0 #40000000"),
            Child = content,
        };
    }

    public void Refresh()
    {
        foreach (var r in _refresh) r();
    }

    private string Short(ISignalPort p) => p.Id[(_cpu.Id.Length + 1)..];

    private IoAddress Address(ISignalPort port)
    {
        _cpu.Profile.TryParse(Short(port), out var a);
        return a;
    }

    private Control LedRow(string title, IReadOnlyList<ISignalPort> ports, IBrush on, Func<ISignalPort, bool> isOn,
        Action<ISignalPort>? click, IBrush text)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = title, Width = 34, FontSize = 10, Foreground = text, VerticalAlignment = VerticalAlignment.Center });
        foreach (var port in ports)
        {
            var led = new Rectangle { Width = 10, Height = 6, Fill = LedOff, RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Center };
            var cell = new StackPanel
            {
                Width = _cell,
                Spacing = 2,
                Background = Brushes.Transparent,
                Children =
                {
                    led,
                    new TextBlock { Text = Short(port), FontSize = 8, FontFamily = Mono, Foreground = text, HorizontalAlignment = HorizontalAlignment.Center },
                },
            };
            if (click is not null)
            {
                ToolTip.SetTip(cell, $"{Short(port)}: clic para conmutar la entrada");
                cell.PointerPressed += (_, e) => { click(port); e.Handled = true; };
            }
            _refresh.Add(() => led.Fill = isOn(port) ? on : LedOff);
            row.Children.Add(cell);
        }
        return row;
    }

    private Control StatusLed(string name, IBrush on, Func<bool> isOn, IBrush text)
    {
        var led = new Ellipse { Width = 8, Height = 8, Fill = LedOff, VerticalAlignment = VerticalAlignment.Center };
        _refresh.Add(() => led.Fill = isOn() ? on : LedOff);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children = { led, new TextBlock { Text = name, FontSize = 9, Foreground = text } },
        };
    }

    private Control TerminalStrip(IEnumerable<string> names, IBrush text)
    {
        var strip = new WrapPanel();
        foreach (var n in names)
        {
            strip.Children.Add(new StackPanel
            {
                Width = _cell,
                Spacing = 1,
                Children =
                {
                    new Border
                    {
                        Width = 22, Height = 16, Background = Terminal, CornerRadius = new CornerRadius(2),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new Ellipse { Width = 9, Height = 9, Fill = Screw, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    },
                    new TextBlock { Text = n, FontSize = 8, FontFamily = Mono, Foreground = text, HorizontalAlignment = HorizontalAlignment.Center },
                },
            });
        }
        return strip;
    }
}
