using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Arduino.Runtime;

namespace Simulador.Plc;

/// <summary>
/// Dibujo de la placa: tarjeta, conector USB, chip, LEDs ON/L/TX/RX y las tiras de pines. Cada pin se ilumina en vivo
/// (rojo = salida en HIGH, con el brillo del PWM; verde = entrada que recibe HIGH; azul = servo o tono). Clic en un pin
/// de entrada lo conmuta, como si se pulsara un botón conectado a él.
/// </summary>
/// <remarks>Dibujo genérico inspirado en la forma de las placas; no reproduce logotipos ni marcas.</remarks>
internal sealed class ArduinoBoardView : UserControl
{
    private static readonly IBrush Pcb = new SolidColorBrush(Color.Parse("#0E7C86"));
    private static readonly IBrush PcbEdge = new SolidColorBrush(Color.Parse("#0A5C63"));
    private static readonly IBrush Silk = new SolidColorBrush(Color.Parse("#E8F6F7"));
    private static readonly IBrush Socket = new SolidColorBrush(Color.Parse("#151515"));
    private static readonly IBrush HoleOff = new SolidColorBrush(Color.Parse("#3A3A3A"));
    private static readonly IBrush LedOff = new SolidColorBrush(Color.Parse("#4D5A5C"));
    private static readonly Color OutColor = Color.Parse("#FF4D3D");
    private static readonly IBrush InHigh = new SolidColorBrush(Color.Parse("#39D353"));
    private static readonly IBrush Special = new SolidColorBrush(Color.Parse("#4DA3FF"));
    private static readonly IBrush PullupRing = new SolidColorBrush(Color.Parse("#7739D353"));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly ArduinoMcu _mcu;
    private readonly List<Action> _refresh = [];

    private sealed record HeaderPin(string Label, int Pin);

    public ArduinoBoardView(ArduinoMcu mcu, Action<PinPort> toggle)
    {
        _mcu = mcu;
        var b = mcu.Board;
        var canvas = new Canvas();
        double w, h;

        int A(int i) => b.FirstAnalog + i;
        IEnumerable<HeaderPin> Digital(params int[] pins) => pins.Select(p => new HeaderPin((b.Pwm.Contains(p) ? "~" : "") + p, p));
        HeaderPin Fixed(string s) => new(s, -1);
        IEnumerable<HeaderPin> Gpio(params int[] pins) => pins.Select(p => new HeaderPin(b == ArduinoBoard.Pico ? "GP" + p : p.ToString(), p));

        string led = b.PinName(b.LedBuiltin);
        if (b == ArduinoBoard.Esp32)
        {
            // ESP32 DevKit V1 de 30 pines, con el USB a la izquierda.
            w = 440; h = 170;
            Board(canvas, w, h, "#1E1E22");
            Rect(canvas, -14, 62, 40, 46, new SolidColorBrush(Color.Parse("#B0B4B8")), 3, "USB");
            // Módulo ESP-WROOM-32 con su blindaje metálico y la antena.
            Rect(canvas, 300, 34, 128, 102, new SolidColorBrush(Color.Parse("#2A2A2A")), 3, "");
            canvas.Children.Add(Place(new Border
            {
                Width = 92, Height = 86, Background = new SolidColorBrush(Color.Parse("#C4C8CC")), CornerRadius = new CornerRadius(2),
                Child = new TextBlock { Text = "ESP-WROOM-32", FontSize = 9, FontFamily = Mono, Foreground = new SolidColorBrush(Color.Parse("#444")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            }, 306, 42));
            for (int i = 0; i < 4; i++)
                canvas.Children.Add(Place(new Border { Width = 4, Height = 70, Background = new SolidColorBrush(Color.Parse("#C9A227")), Opacity = 0.8 }, 404 + i * 6, 50));
            Chip(canvas, 150, 70, 46, 30, "CP2102");
            var top = new List<HeaderPin> { Fixed("VIN"), Fixed("GND") };
            top.AddRange(Gpio(13, 12, 14, 27, 26, 25, 33, 32, 35, 34, 39, 36));
            top.Add(Fixed("EN"));
            Header(canvas, 50, 6, top, toggle, labelsBelow: true);
            var bottom = new List<HeaderPin> { Fixed("3V3"), Fixed("GND") };
            bottom.AddRange(Gpio(15, 2, 4, 16, 17, 5, 18, 19, 21, 3, 1, 22, 23));
            Header(canvas, 50, h - 22, bottom, toggle, labelsBelow: false);
            Led(canvas, 100, 62, "ON", () => true, Color.Parse("#FF3B30"));
            Led(canvas, 100, 100, "IO2", () => _mcu.Pin(led).IsOutput && _mcu.Pin(led).OutputVolts > 0.3, Color.Parse("#2F81F7"), () => _mcu.Pin(led).OutputVolts / b.Vcc);
            Button(canvas, 36, 58, "EN");
            Button(canvas, 36, 96, "BOOT");
            BoardLabel(canvas, 214, 112, "ESP32");
        }
        else if (b == ArduinoBoard.Pico)
        {
            // Raspberry Pi Pico, con el micro-USB a la izquierda: pines 1-20 arriba y 40-21 abajo.
            w = 420; h = 150;
            Board(canvas, w, h, "#1F7A3A");
            Rect(canvas, -12, 56, 34, 38, new SolidColorBrush(Color.Parse("#B0B4B8")), 3, "USB");
            Chip(canvas, 190, 50, 50, 50, b.Chip);
            Chip(canvas, 116, 60, 46, 30, "W25Q16");
            var top = Gpio(0, 1).ToList();
            top.Add(Fixed("GND"));
            top.AddRange(Gpio(2, 3, 4, 5));
            top.Add(Fixed("GND"));
            top.AddRange(Gpio(6, 7, 8, 9));
            top.Add(Fixed("GND"));
            top.AddRange(Gpio(10, 11, 12, 13));
            top.Add(Fixed("GND"));
            top.AddRange(Gpio(14, 15));
            Header(canvas, 50, 6, top, toggle, labelsBelow: true);
            var bottom = new List<HeaderPin> { Fixed("VBUS"), Fixed("VSYS"), Fixed("GND"), Fixed("3V3E"), Fixed("3V3"), Fixed("VREF") };
            bottom.AddRange(Gpio(28));
            bottom.Add(Fixed("AGND"));
            bottom.AddRange(Gpio(27, 26));
            bottom.Add(Fixed("RUN"));
            bottom.AddRange(Gpio(22));
            bottom.Add(Fixed("GND"));
            bottom.AddRange(Gpio(21, 20, 19, 18));
            bottom.Add(Fixed("GND"));
            bottom.AddRange(Gpio(17, 16));
            Header(canvas, 50, h - 22, bottom, toggle, labelsBelow: false);
            Led(canvas, 36, 42, "LED", () => _mcu.Pin(led).IsOutput && _mcu.Pin(led).OutputVolts > 0.3, Color.Parse("#39D353"), () => _mcu.Pin(led).OutputVolts / b.Vcc);
            Button(canvas, 60, 92, "BOOTSEL");
            BoardLabel(canvas, 262, 62, "Raspberry Pi Pico");
        }
        else if (b.Id == "arduino-nano")
        {
            w = 380; h = 130;
            Board(canvas, w, h);
            Rect(canvas, -14, 45, 40, 40, new SolidColorBrush(Color.Parse("#B0B4B8")), 3, "USB");
            Chip(canvas, 170, 48, 70, 34, b.Chip);
            var top = new List<HeaderPin> { Fixed("TX1"), Fixed("RX0"), Fixed("RST"), Fixed("GND") };
            top.AddRange(Digital(2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));
            Header(canvas, 40, 6, top, toggle, labelsBelow: true);
            var bottom = new List<HeaderPin> { new("13", 13), Fixed("3V3"), Fixed("REF") };
            bottom.AddRange(Enumerable.Range(0, 8).Select(i => new HeaderPin($"A{i}", A(i))));
            bottom.AddRange([Fixed("5V"), Fixed("RST"), Fixed("GND"), Fixed("VIN")]);
            Header(canvas, 40, h - 22, bottom, toggle, labelsBelow: false);
            Led(canvas, 120, 50, "L", () => _mcu.Pin("D13").IsOutput && _mcu.Pin("D13").OutputHigh, Color.Parse("#FFB020"));
            Led(canvas, 120, 66, "TX", () => _mcu.SerialActive, Color.Parse("#FFB020"));
            Led(canvas, 120, 82, "ON", () => true, Color.Parse("#39D353"));
            BoardLabel(canvas, 236, 52, b.Name.ToUpperInvariant());
        }
        else if (b.Id == "arduino-mega")
        {
            w = 640; h = 300;
            Board(canvas, w, h);
            Rect(canvas, -12, 30, 70, 60, new SolidColorBrush(Color.Parse("#B0B4B8")), 4, "USB");
            Rect(canvas, -8, 220, 60, 50, new SolidColorBrush(Color.Parse("#222")), 4, "");
            Chip(canvas, 250, 120, 110, 110, b.Chip);
            var top = new List<HeaderPin> { Fixed("AREF"), Fixed("GND") };
            top.AddRange(Digital(13, 12, 11, 10, 9, 8));
            top.Add(Fixed(""));
            top.AddRange(Digital(7, 6, 5, 4, 3, 2, 1, 0));
            top.Add(Fixed(""));
            top.AddRange(Digital(14, 15, 16, 17, 18, 19, 20, 21));
            Header(canvas, 110, 8, top, toggle, labelsBelow: true);
            // Doble tira del extremo derecho: 22-53 en dos columnas.
            for (int i = 0; i < 16; i++)
            {
                PinCell(canvas, w - 52, 50 + i * 15, new HeaderPin((22 + 2 * i).ToString(), 22 + 2 * i), toggle, LabelSide.Left);
                PinCell(canvas, w - 34, 50 + i * 15, new HeaderPin((23 + 2 * i).ToString(), 23 + 2 * i), toggle, LabelSide.Right);
            }
            var bottom = new List<HeaderPin> { Fixed("IOREF"), Fixed("RST"), Fixed("3V3"), Fixed("5V"), Fixed("GND"), Fixed("GND"), Fixed("VIN"), Fixed("") };
            bottom.AddRange(Enumerable.Range(0, 16).Select(i => new HeaderPin($"A{i}", A(i))));
            Header(canvas, 110, h - 22, bottom, toggle, labelsBelow: false);
            Led(canvas, 80, 60, "L", () => _mcu.Pin("D13").IsOutput && _mcu.Pin("D13").OutputHigh, Color.Parse("#FFB020"));
            Led(canvas, 80, 80, "TX", () => _mcu.SerialActive, Color.Parse("#FFB020"));
            Led(canvas, 80, 100, "RX", () => false, Color.Parse("#FFB020"));
            Led(canvas, 440, 60, "ON", () => true, Color.Parse("#39D353"));
            BoardLabel(canvas, 400, 140, b.Name.ToUpperInvariant());
        }
        else
        {
            w = 430; h = 300;
            Board(canvas, w, h);
            Rect(canvas, -12, 30, 70, 60, new SolidColorBrush(Color.Parse("#B0B4B8")), 4, "USB");
            Rect(canvas, -8, 220, 60, 50, new SolidColorBrush(Color.Parse("#222")), 4, "");
            Chip(canvas, 200, 180, 200, 40, b.Chip);
            var top = new List<HeaderPin> { Fixed("AREF"), Fixed("GND") };
            top.AddRange(Digital(13, 12, 11, 10, 9, 8));
            top.Add(Fixed(""));
            top.AddRange(Digital(7, 6, 5, 4, 3, 2, 1, 0));
            Header(canvas, 128, 8, top, toggle, labelsBelow: true);
            var bottom = new List<HeaderPin> { Fixed("IOREF"), Fixed("RST"), Fixed("3V3"), Fixed("5V"), Fixed("GND"), Fixed("GND"), Fixed("VIN"), Fixed("") };
            bottom.AddRange(Enumerable.Range(0, 6).Select(i => new HeaderPin($"A{i}", A(i))));
            Header(canvas, 128, h - 22, bottom, toggle, labelsBelow: false);
            Led(canvas, 110, 70, "L", () => _mcu.Pin("D13").IsOutput && _mcu.Pin("D13").OutputHigh, Color.Parse("#FFB020"));
            Led(canvas, 110, 90, "TX", () => _mcu.SerialActive, Color.Parse("#FFB020"));
            Led(canvas, 110, 110, "RX", () => false, Color.Parse("#FFB020"));
            Led(canvas, 340, 70, "ON", () => true, Color.Parse("#39D353"));
            BoardLabel(canvas, 220, 110, b.Name.ToUpperInvariant());
            // Botón de reset.
            Rect(canvas, 60, 12, 26, 26, new SolidColorBrush(Color.Parse("#C9CDD1")), 3, "");
            canvas.Children.Add(Place(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.Parse("#B03030")) }, 66, 18));
        }

        canvas.Width = w;
        canvas.Height = h;
        Content = new Border { Padding = new Thickness(16, 6, 6, 6), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left };
    }

    public void Refresh()
    {
        foreach (var r in _refresh) r();
    }

    private static Control Place(Control c, double x, double y)
    {
        Canvas.SetLeft(c, x);
        Canvas.SetTop(c, y);
        return c;
    }

    private static void Board(Canvas canvas, double w, double h, string? color = null) => canvas.Children.Add(new Border
    {
        Width = w, Height = h, Background = color is null ? Pcb : new SolidColorBrush(Color.Parse(color)), BorderBrush = color is null ? PcbEdge : new SolidColorBrush(Color.Parse(color), 0.7), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10),
        BoxShadow = BoxShadows.Parse("0 3 10 0 #50000000"),
    });

    private static void Rect(Canvas canvas, double x, double y, double w, double h, IBrush fill, double r, string text) =>
        canvas.Children.Add(Place(new Border
        {
            Width = w, Height = h, Background = fill, CornerRadius = new CornerRadius(r),
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = Brushes.Black, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        }, x, y));

    private static void Chip(Canvas canvas, double x, double y, double w, double h, string name)
    {
        canvas.Children.Add(Place(new Border
        {
            Width = w, Height = h, Background = new SolidColorBrush(Color.Parse("#1B1B1B")), CornerRadius = new CornerRadius(2),
            Child = new TextBlock { Text = name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")), FontFamily = Mono, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        }, x, y));
    }

    private static void BoardLabel(Canvas canvas, double x, double y, string text) =>
        canvas.Children.Add(Place(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Silk, Opacity = 0.9 }, x, y));

    /// <summary>Botón de la placa (dibujo): EN, BOOT, BOOTSEL…</summary>
    private static void Button(Canvas canvas, double x, double y, string label)
    {
        Rect(canvas, x, y, 16, 16, new SolidColorBrush(Color.Parse("#C9CDD1")), 2, "");
        canvas.Children.Add(Place(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Color.Parse("#333")) }, x + 3.5, y + 3.5));
        canvas.Children.Add(Place(new TextBlock { Text = label, FontSize = 8, Foreground = Silk }, x + 20, y + 3));
    }

    private void Led(Canvas canvas, double x, double y, string label, Func<bool> isOn, Color color, Func<double>? brightness = null)
    {
        var led = new Border { Width = 12, Height = 7, Background = LedOff, CornerRadius = new CornerRadius(1) };
        var on = new SolidColorBrush(color);
        _refresh.Add(() =>
        {
            // MicroPython deja los pines como estaban al acabar el programa: el LED sigue encendido.
            bool v = isOn() && (_mcu.State == McuState.Running || _mcu.ProgramEnded);
            led.Background = v ? (brightness is null ? on : new SolidColorBrush(color, 0.3 + 0.7 * Math.Clamp(brightness(), 0, 1))) : LedOff;
            led.BoxShadow = v ? BoxShadows.Parse($"0 0 6 1 {color}") : default;
        });
        canvas.Children.Add(Place(led, x, y));
        canvas.Children.Add(Place(new TextBlock { Text = label, FontSize = 9, Foreground = Silk }, x + 16, y - 3));
    }

    private enum LabelSide { Above, Below, Left, Right }

    private void Header(Canvas canvas, double x, double y, List<HeaderPin> pins, Action<PinPort> toggle, bool labelsBelow)
    {
        const double step = 16;
        var strip = new Border { Width = pins.Count * step + 2, Height = 16, Background = Socket, CornerRadius = new CornerRadius(1) };
        canvas.Children.Add(Place(strip, x - 1, y - 1));
        for (int i = 0; i < pins.Count; i++)
        {
            if (pins[i].Label.Length == 0) continue; // hueco entre tiras
            PinCell(canvas, x + i * step, y, pins[i], toggle, labelsBelow ? LabelSide.Below : LabelSide.Above);
        }
    }

    private void PinCell(Canvas canvas, double x, double y, HeaderPin hp, Action<PinPort> toggle, LabelSide side)
    {
        var hole = new Border { Width = 8, Height = 8, Background = HoleOff, CornerRadius = new CornerRadius(1) };
        var cell = new Border
        {
            Width = 14, Height = 14, Background = Socket, BorderThickness = new Thickness(1.5), BorderBrush = Socket,
            Child = new Border { Child = hole, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        canvas.Children.Add(Place(cell, x, y));
        var label = new TextBlock { Text = hp.Label, FontSize = 7.5, FontFamily = Mono, Foreground = Silk };
        if (side is LabelSide.Above or LabelSide.Below)
        {
            var lt = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = label };
            canvas.Children.Add(Place(lt, x + 3, side == LabelSide.Below ? y + 17 : y - 4 - hp.Label.Length * 5.2));
        }
        else
            canvas.Children.Add(Place(label, side == LabelSide.Left ? x - 4 - hp.Label.Length * 5 : x + 17, y + 2));

        if (hp.Pin < 0 || hp.Pin >= _mcu.Pins.Count) return;
        var port = _mcu.Pins[hp.Pin];
        ToolTip.SetTip(cell, port.Name);
        cell.PointerPressed += (_, e) =>
        {
            if (!port.IsOutput) toggle(port);
            e.Handled = true;
        };
        _refresh.Add(() =>
        {
            IBrush fill = HoleOff;
            IBrush ring = Socket;
            string tip = $"{port.Name}: ";
            if (port.ServoAngle is { } ang) { fill = Special; tip += $"servo a {ang:0}°"; }
            else if (port.ToneHz is { } hz) { fill = Special; tip += $"tono de {hz:0} Hz"; }
            else if (port.IsOutput)
            {
                double level = port.Duty ?? (port.OutputHigh ? 1 : 0);
                if (level > 0) fill = new SolidColorBrush(OutColor, 0.25 + 0.75 * level);
                string pwmHz = port.PwmHz is { } f ? $" a {f:0} Hz" : "";
                tip += port.Duty is { } d ? $"salida PWM {d * 100:0} %{pwmHz}" : port.OutputHigh ? $"salida HIGH ({port.NominalLevel:0.#} V)" : "salida LOW (0 V)";
            }
            else
            {
                double v = port.Value;
                if (port.IsAnalog && port.ExternalVolts is { } ev && ev > 0.05 && ev < port.NominalLevel - 0.05)
                    fill = new SolidColorBrush(Color.Parse("#39D353"), 0.25 + 0.75 * ev / port.NominalLevel);
                else if (v >= port.NominalLevel * 0.6) fill = InHigh;
                if (port.Mode == PinMode.InputPullup) ring = PullupRing;
                tip += (port.Mode == PinMode.InputPullup ? "entrada con pull-up, " : "entrada, ") + $"{v:0.##} V · clic para cambiar";
            }
            hole.Background = fill;
            cell.BorderBrush = ring;
            ToolTip.SetTip(cell, tip);
        });
    }
}
