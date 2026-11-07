using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Arduino.Runtime;

namespace Simulador.Plc;

/// <summary>
/// Montaje: las piezas conectadas a la placa, cada una dibujada con su control (pulsador que se aprieta, mando del
/// potenciómetro, brazo del servo…) y su estado. Se pueden añadir y quitar piezas.
/// </summary>
internal sealed class PartsPanel : UserControl
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#40808080"));

    private readonly WrapPanel _cards = new();
    private readonly ComboBox _kind;
    private readonly ComboBox _pin = new() { MinWidth = 80 };
    private readonly List<PartSpec> _specs = [];
    private readonly List<(Part Part, Action Refresh)> _parts = [];
    private ArduinoMcu? _mcu;

    public PartsPanel()
    {
        var kinds = Enum.GetValues<PartKind>();
        _kind = new ComboBox { ItemsSource = kinds.Select(Part.DefaultLabel).ToList(), SelectedIndex = 0, MinWidth = 200 };
        _kind.SelectionChanged += (_, _) => FillPins();
        var add = new Button { Content = "+ Añadir pieza" };
        add.Click += (_, _) => Add();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(_kind);
        bar.Children.Add(new TextBlock { Text = "en el pin", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_pin);
        bar.Children.Add(add);
        Content = new StackPanel { Children = { bar, _cards } };
    }

    public IReadOnlyList<Part> Parts => _parts.Select(p => p.Part).ToList();

    /// <summary>Pines que tienen una pieza de entrada (los controles genéricos no se muestran para ellos).</summary>
    public IReadOnlySet<PinPort> DrivenPins => _parts.Where(p => p.Part.IsInput).Select(p => p.Part.Pin).ToHashSet();

    /// <summary>Monta las piezas indicadas (por ejemplo, las de un ejemplo).</summary>
    public void SetSpecs(IEnumerable<PartSpec> specs)
    {
        _specs.Clear();
        _specs.AddRange(specs);
        if (_mcu is not null) Attach(_mcu);
    }

    /// <summary>Vuelve a crear las piezas sobre otra placa (los pines se buscan por nombre).</summary>
    public void Attach(ArduinoMcu mcu)
    {
        foreach (var (p, _) in _parts) p.Detach();
        _mcu = mcu;
        _parts.Clear();
        _cards.Children.Clear();
        foreach (var spec in _specs.ToList())
        {
            if (!mcu.Pins.Any(p => p.Name == spec.Pin)) { _specs.Remove(spec); continue; }
            AddCard(spec, Part.Create(spec, mcu));
        }
        if (_parts.Count == 0) _cards.Children.Add(new TextBlock { Text = "Sin piezas. Añade una con el botón de arriba.", Opacity = 0.6, FontSize = 12 });
        FillPins();
    }

    public void Apply()
    {
        foreach (var (p, _) in _parts) p.Apply();
    }

    public void Animate(TimeSpan dt)
    {
        foreach (var s in _parts.Select(p => p.Part).OfType<ServoPart>()) s.Animate(dt);
    }

    public void Refresh()
    {
        foreach (var (_, r) in _parts) r();
    }

    private void FillPins()
    {
        if (_mcu is null) return;
        var kind = Enum.GetValues<PartKind>()[Math.Max(0, _kind.SelectedIndex)];
        var pins = _mcu.Pins.Where(p => Part.Fits(kind, p)).Select(p => p.Name).ToList();
        _pin.ItemsSource = pins;
        _pin.SelectedIndex = pins.Count > 0 ? 0 : -1;
    }

    private void Add()
    {
        if (_mcu is null || _pin.SelectedItem is not string pin) return;
        var kind = Enum.GetValues<PartKind>()[_kind.SelectedIndex];
        string? color = kind == PartKind.Led ? new[] { "#FF3B30", "#39D353", "#FFB020", "#4DA3FF", "#FFE14D" }[_specs.Count(s => s.Kind == PartKind.Led) % 5] : null;
        var spec = new PartSpec(kind, pin, color);
        _specs.Add(spec);
        if (_parts.Count == 0) _cards.Children.Clear();
        AddCard(spec, Part.Create(spec, _mcu));
    }

    private void Remove(PartSpec spec, Part part, Control card)
    {
        part.Detach();
        _specs.Remove(spec);
        _parts.RemoveAll(p => p.Part == part);
        _cards.Children.Remove(card);
    }

    private void AddCard(PartSpec spec, Part part)
    {
        var state = new TextBlock { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, MaxWidth = 170 };
        var (drawing, refresh) = Drawing(part);
        var remove = new Button { Content = "✕", Padding = new Thickness(5, 0), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(remove, "Quitar la pieza");
        var header = new DockPanel();
        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(remove);
        header.Children.Add(new TextBlock { Text = $"{part.Label} → {part.Pin.Name}", FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var card = new Border
        {
            BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 8, 8), Width = 190,
            Child = new StackPanel { Spacing = 6, Children = { header, drawing, state } },
        };
        remove.Click += (_, _) => Remove(spec, part, card);
        _parts.Add((part, () => { refresh(); state.Text = part.Describe(); }));
        _cards.Children.Add(card);
        refresh();
        state.Text = part.Describe();
    }

    /// <summary>Dibujo y controles de cada tipo de pieza.</summary>
    private static (Control Drawing, Action Refresh) Drawing(Part part)
    {
        switch (part)
        {
            case LedPart led:
            {
                var color = Color.Parse(led.Color);
                var bulb = new Ellipse { Width = 30, Height = 30, Stroke = new SolidColorBrush(color), StrokeThickness = 2 };
                var glow = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = bulb, HorizontalAlignment = HorizontalAlignment.Center };
                var legs = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { new Rectangle { Width = 2, Height = 14, Fill = Brushes.Gray }, new Rectangle { Width = 2, Height = 18, Fill = Brushes.Gray } },
                };
                return (new StackPanel { Children = { glow, legs } }, () =>
                {
                    double b = led.Brightness;
                    bulb.Fill = new SolidColorBrush(color, 0.15 + 0.85 * b);
                    glow.BoxShadow = b > 0.02 ? BoxShadows.Parse($"0 0 {6 + 14 * b:0} {2 + 4 * b:0} #{(byte)(180 * b):X2}{color.R:X2}{color.G:X2}{color.B:X2}") : default;
                });
            }
            case ButtonPart button:
            {
                var cap = new Ellipse { Width = 34, Height = 34, Fill = new SolidColorBrush(Color.Parse("#2F2F2F")), Stroke = Brushes.Gray, StrokeThickness = 3 };
                var body = new Border
                {
                    Width = 50, Height = 50, Background = new SolidColorBrush(Color.Parse("#1E1E1E")), CornerRadius = new CornerRadius(4),
                    Child = cap, HorizontalAlignment = HorizontalAlignment.Center,
                };
                ToolTip.SetTip(body, "Mantén pulsado con el ratón");
                body.PointerPressed += (_, e) => { button.Pressed = true; e.Handled = true; };
                body.PointerReleased += (_, _) => button.Pressed = false;
                body.PointerCaptureLost += (_, _) => button.Pressed = false;
                var hold = new ToggleButton { Content = "Fijo", Padding = new Thickness(8, 1), FontSize = 11 };
                hold.IsCheckedChanged += (_, _) => button.Pressed = hold.IsChecked == true;
                string vcc = $"{button.Pin.NominalLevel:0.#} V";
                var to5v = new CheckBox { Content = $"Conectado a {vcc}", FontSize = 11, IsChecked = !button.ToGround };
                ToolTip.SetTip(to5v, $"Sin marcar: el pulsador une el pin con GND (usa la resistencia de pull-up). Marcado: lo une con {vcc} y lleva resistencia a GND.");
                to5v.IsCheckedChanged += (_, _) => button.ToGround = to5v.IsChecked != true;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { body, new StackPanel { Spacing = 4, Children = { hold, to5v } } } };
                return (row, () => cap.Fill = new SolidColorBrush(Color.Parse(button.Pressed ? "#C62828" : "#2F2F2F")));
            }
            case PotentiometerPart pot:
            {
                var pointer = new Rectangle { Width = 3, Height = 14, Fill = Brushes.White, RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative) };
                var knob = new Grid
                {
                    Width = 44, Height = 44,
                    Children =
                    {
                        new Ellipse { Fill = new SolidColorBrush(Color.Parse("#3478C6")), Stroke = new SolidColorBrush(Color.Parse("#1F4E86")), StrokeThickness = 2 },
                        new Border { Child = pointer, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) },
                    },
                };
                var slider = new Slider { Minimum = 0, Maximum = 1, Value = pot.Position, Width = 120, VerticalAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (_, _) => pot.Position = slider.Value;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { knob, slider } };
                return (row, () => pointer.RenderTransform = new RotateTransform(-135 + 270 * pot.Position));
            }
            case BuzzerPart buzzer:
            {
                var waves = new TextBlock { FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.Parse("#4DA3FF")) };
                var body = new Grid
                {
                    Width = 40, Height = 40,
                    Children =
                    {
                        new Ellipse { Fill = new SolidColorBrush(Color.Parse("#1B1B1B")) },
                        new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.Parse("#444")) },
                    },
                };
                int frame = 0;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { body, waves } };
                return (row, () => waves.Text = buzzer.Sounding ? new[] { "♪", "♪ ♫", "♪ ♫ ♪" }[frame++ / 4 % 3] : "");
            }
            case ServoPart servo:
            {
                var arm = new Rectangle
                {
                    Width = 6, Height = 34, Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1, RadiusX = 3, RadiusY = 3,
                    RenderTransformOrigin = new RelativePoint(0.5, 0.9, RelativeUnit.Relative),
                };
                var canvas = new Canvas { Width = 90, Height = 56 };
                var body = new Border { Width = 70, Height = 26, Background = new SolidColorBrush(Color.Parse("#2B5FA8")), CornerRadius = new CornerRadius(3) };
                Canvas.SetLeft(body, 10);
                Canvas.SetTop(body, 28);
                Canvas.SetLeft(arm, 42);
                Canvas.SetTop(arm, 7);
                var hub = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White };
                Canvas.SetLeft(hub, 39);
                Canvas.SetTop(hub, 32);
                canvas.Children.Add(body);
                canvas.Children.Add(arm);
                canvas.Children.Add(hub);
                // 0° = brazo a la izquierda, 90° = arriba, 180° = a la derecha.
                return (canvas, () => arm.RenderTransform = new RotateTransform(servo.Angle - 90));
            }
            case LdrPart ldr:
            {
                var sun = new TextBlock { Text = "☀", FontSize = 26, VerticalAlignment = VerticalAlignment.Center };
                // Escala logarítmica: 1 a 10 000 lux.
                var slider = new Slider { Minimum = 0, Maximum = 4, Value = Math.Log10(Math.Max(1, ldr.Lux)), Width = 120, VerticalAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (_, _) => ldr.Lux = Math.Pow(10, slider.Value);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sun, slider } };
                return (row, () => sun.Opacity = 0.2 + 0.8 * Math.Log10(Math.Max(1, ldr.Lux)) / 4);
            }
            case Lm35Part lm:
            {
                var thermo = new TextBlock { Text = "🌡", FontSize = 24, VerticalAlignment = VerticalAlignment.Center };
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = lm.Celsius, Width = 120, VerticalAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (_, _) => lm.Celsius = slider.Value;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { thermo, slider } };
                return (row, () => thermo.Foreground = new SolidColorBrush(lm.Celsius > 30 ? Color.Parse("#E5533D") : Color.Parse("#4DA3FF")));
            }
        }
        return (new TextBlock { Text = "?" }, () => { });
    }
}
