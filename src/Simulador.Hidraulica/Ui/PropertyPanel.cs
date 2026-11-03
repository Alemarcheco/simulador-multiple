using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.Ui;

/// <summary>
/// Panel derecho. En diseño edita las propiedades del elemento seleccionado (en unidades cómodas: mm, L/s, %, rpm).
/// Durante la simulación muestra sus valores en vivo y los mandos (marcha de bomba, apertura de válvula).
/// </summary>
public sealed class PropertyPanel : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 6, Margin = new Thickness(12) };
    private HydraulicNetwork _net = new();
    private HydraulicSimulation? _sim;
    private object? _element;
    private readonly List<(TextBlock Text, Func<string> Value)> _live = [];

    /// <summary>Se editó una propiedad del diseño.</summary>
    public event EventHandler? DesignChanged;

    public PropertyPanel()
    {
        Content = new ScrollViewer { Content = _root };
        Show(null, new HydraulicNetwork(), null);
    }

    public void Show(object? element, HydraulicNetwork net, HydraulicSimulation? sim)
    {
        _element = element;
        _net = net;
        _sim = sim;
        _live.Clear();
        _root.Children.Clear();

        if (element is null)
        {
            Header(net.Name);
            if (sim is null)
            {
                TextField("Nombre del proyecto", net.Name, v => net.Name = v);
                NumberField("Densidad del fluido", "kg/m³", () => net.Fluid.Density, v => net.Fluid = net.Fluid with { Density = v });
                NumberField("Viscosidad cinemática", "mm²/s", () => net.Fluid.KinematicViscosity * 1e6, v => net.Fluid = net.Fluid with { KinematicViscosity = v / 1e6 });
                Note("Elige una herramienta a la izquierda y haz clic en el lienzo. Tubería, bomba y válvulas se trazan con dos clics: origen y destino (el sentido positivo del caudal). Clic derecho vuelve a seleccionar.");
            }
            else
            {
                Live("Tiempo simulado", () => $"{sim.Time.TotalSeconds:0.0} s");
                Note("Selecciona una bomba o válvula para manejarla, o cualquier elemento para ver sus valores.");
            }
            return;
        }

        string id = element switch { HydraulicNode n => n.Id, HydraulicLink l => l.Id, Sensor s => s.Id, _ => "" };
        string type = element switch { HydraulicNode n => n.TypeName, HydraulicLink l => l.TypeName, Sensor s => s.TypeName, _ => "" };
        Header($"{type}");
        if (sim is null) TextField("Identificador", id, v =>
        {
            if (!net.Rename(id, v.Trim())) return false;
            id = v.Trim();
            return true;
        });
        else Label($"Identificador: {id}");

        if (sim is null) DesignFields(element);
        else LiveFields(element);
    }

    private void DesignFields(object element)
    {
        switch (element)
        {
            case Reservoir r:
                NumberField("Cota", "m", () => r.Elevation, v => r.Elevation = v);
                NumberField("Nivel de agua", "m", () => r.Level, v => r.Level = v);
                break;
            case Tank t:
                NumberField("Cota del fondo", "m", () => t.Elevation, v => t.Elevation = v);
                NumberField("Diámetro", "m", () => t.Diameter, v => t.Diameter = v);
                NumberField("Altura máxima", "m", () => t.MaxLevel, v => t.MaxLevel = v);
                NumberField("Nivel inicial", "m", () => t.InitialLevel, v => t.InitialLevel = v);
                Note($"Capacidad: {t.Capacity * 1000:0} L");
                break;
            case Junction j:
                NumberField("Cota", "m", () => j.Elevation, v => j.Elevation = v);
                NumberField("Consumo", "L/s", () => j.Demand * 1000, v => j.Demand = v / 1000);
                break;
            case Pipe p:
                NumberField("Longitud", "m", () => p.Length, v => p.Length = v);
                NumberField("Diámetro interior", "mm", () => p.Diameter * 1000, v => p.Diameter = v / 1000);
                NumberField("Rugosidad", "mm", () => p.Roughness, v => p.Roughness = v);
                NumberField("Pérdidas locales (ΣK)", "", () => p.MinorLoss, v => p.MinorLoss = v);
                TextField("Material", p.Material, v => { p.Material = v; return true; });
                Note("Rugosidad típica: PVC/PE 0,0015 · cobre 0,0015 · acero 0,045 · galvanizado 0,15 · fundición 0,26 mm.");
                break;
            case Pump b:
                NumberField("Altura a caudal cero", "m", () => b.ShutoffHead, v => b.ShutoffHead = v);
                NumberField("Caudal nominal", "L/s", () => b.RatedFlow * 1000, v => b.RatedFlow = v / 1000);
                NumberField("Altura nominal", "m", () => b.RatedHead, v => b.RatedHead = v);
                NumberField("Velocidad nominal", "rpm", () => b.RatedSpeed * 60 / (2 * Math.PI), v => b.RatedSpeed = v * 2 * Math.PI / 60);
                NumberField("Rendimiento", "%", () => b.Efficiency * 100, v => b.Efficiency = v / 100);
                NumberField("Inercia del rodete", "kg·m²", () => b.Inertia, v => b.Inertia = v);
                CheckField("Encendida al empezar", b.InitiallyOn, v => b.InitiallyOn = v);
                Note("Si la conectas a un motor en el banco combinado, el motor impone la velocidad y la bomba le devuelve el par.");
                break;
            case Valve v:
                NumberField("Diámetro", "mm", () => v.Diameter * 1000, x => v.Diameter = x / 1000);
                NumberField("K abierta", "", () => v.OpenLoss, x => v.OpenLoss = x);
                if (v.Kind != ValveKind.Check)
                    NumberField("Apertura inicial", "%", () => v.InitialOpening * 100, x => v.InitialOpening = Math.Clamp(x / 100, 0, 1));
                if (v.Kind is ValveKind.Control or ValveKind.Solenoid)
                    NumberField("Tiempo de recorrido", "s", () => v.StrokeTime, x => v.StrokeTime = Math.Max(x, 0));
                break;
            case Sensor s:
                Label($"Mide: {s.Target}");
                break;
        }
    }

    private void LiveFields(object element)
    {
        var sim = _sim!;
        switch (element)
        {
            case Tank t:
                Live("Nivel", () => $"{t.Level:0.000} m ({t.Level / t.MaxLevel * 100:0} %)");
                Live("Volumen", () => $"{t.Volume * 1000:0} L");
                Live("Caudal neto", () => $"{sim.NetInflow(sim.NodeIndex(t.Id)) * 1000:0.00} L/s");
                Live("Rebose", () => $"{sim.Overflow[sim.NodeIndex(t.Id)] * 1000:0.00} L/s");
                break;
            case HydraulicNode n:
                Live("Altura piezométrica", () => $"{sim.HeadAt(n.Id):0.00} m");
                Live("Presión", () => $"{sim.PressureAt(n.Id) / 1e5:0.000} bar");
                break;
            case Pump b:
                var toggle = new ToggleSwitch { OnContent = "En marcha", OffContent = "Parada", IsChecked = b.RelativeSpeed > 0.01 };
                toggle.IsCheckedChanged += (_, _) => { sim.SetPumpRunning(b.Id, toggle.IsChecked == true); sim.Refresh(); };
                _root.Children.Add(toggle);
                var speed = new Slider { Minimum = 0, Maximum = 120, Value = b.RelativeSpeed * 100 };
                speed.PropertyChanged += (_, e) =>
                {
                    if (e.Property != RangeBase.ValueProperty) return;
                    sim.SetPumpShaftSpeed(b.Id, speed.Value / 100 * b.RatedSpeed);
                    sim.Refresh();
                };
                Label("Velocidad (variador, % de la nominal)");
                _root.Children.Add(speed);
                Live("Velocidad", () => $"{b.RelativeSpeed * b.RatedSpeed * 60 / (2 * Math.PI):0} rpm");
                Live("Caudal", () => $"{sim.FlowIn(b.Id) * 1000:0.00} L/s ({sim.FlowIn(b.Id) * 3600:0.00} m³/h)");
                Live("Altura", () => $"{sim.PumpHead(b.Id):0.00} m");
                Live("Potencia en el eje", () => $"{sim.PumpShaftPower(b.Id) / 1000:0.000} kW");
                Live("Par", () => $"{sim.PumpTorque(b.Id):0.00} N·m");
                break;
            case Valve v:
                if (v.Kind != ValveKind.Check)
                {
                    Label("Apertura pedida (%)");
                    var slider = new Slider { Minimum = 0, Maximum = 100, Value = v.OpeningSetpoint * 100 };
                    slider.PropertyChanged += (_, e) =>
                    {
                        if (e.Property != RangeBase.ValueProperty) return;
                        sim.SetValveOpening(v.Id, slider.Value / 100);
                        sim.Refresh();
                    };
                    _root.Children.Add(slider);
                }
                Live("Apertura real", () => $"{v.Opening * 100:0} %");
                Live("Caudal", () => $"{sim.FlowIn(v.Id) * 1000:0.00} L/s");
                Live("Pérdida de carga", () => $"{LossOf(v):0.00} m");
                break;
            case Pipe p:
                Live("Caudal", () => $"{sim.FlowIn(p.Id) * 1000:0.00} L/s");
                Live("Velocidad", () => $"{sim.VelocityIn(p.Id):0.00} m/s");
                Live("Pérdida de carga", () => $"{LossOf(p):0.00} m");
                Note("Velocidades recomendadas en impulsión: 0,5–2 m/s.");
                break;
            case Sensor s:
                Live("Medida", () => s.Kind switch
                {
                    SensorKind.Pressure => $"{sim.SensorValue(s) / 1e5:0.000} bar",
                    SensorKind.Level => $"{sim.SensorValue(s):0.000} m",
                    _ => $"{sim.SensorValue(s) * 1000:0.00} L/s",
                });
                break;
        }
    }

    private double LossOf(HydraulicLink l)
    {
        var sim = _sim!;
        return sim.Heads[sim.NodeIndex(l.From)] - sim.Heads[sim.NodeIndex(l.To)];
    }

    /// <summary>Refresca los valores en vivo (llamar en cada fotograma de la simulación).</summary>
    public void RefreshLive()
    {
        foreach (var (text, value) in _live)
        {
            try { text.Text = value(); }
            catch (KeyNotFoundException) { text.Text = "—"; }
        }
    }

    // ---------------------------------------------------------------- Campos

    private void Header(string text) =>
        _root.Children.Add(new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

    private void Label(string text) => _root.Children.Add(new TextBlock { Text = text, Opacity = 0.85 });

    private void Note(string text) =>
        _root.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });

    private void Live(string label, Func<string> value)
    {
        var v = new TextBlock { FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock { Text = label, Opacity = 0.85 });
        Grid.SetColumn(v, 1);
        row.Children.Add(v);
        _root.Children.Add(row);
        _live.Add((v, value));
        try { v.Text = value(); } catch (KeyNotFoundException) { v.Text = "—"; }
    }

    private void NumberField(string label, string unit, Func<double> get, Action<double> set)
    {
        var box = new TextBox { Text = Format(get()), MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Right };
        void Commit()
        {
            if (TryParse(box.Text, out var v) && double.IsFinite(v))
            {
                set(v);
                DesignChanged?.Invoke(this, EventArgs.Empty);
            }
            box.Text = Format(get());
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };
        AddRow(label, unit, box);
    }

    private void TextField(string label, string value, Func<string, bool> set)
    {
        var box = new TextBox { Text = value };
        void Commit()
        {
            if (box.Text is { } t && t != value && set(t)) { value = t; DesignChanged?.Invoke(this, EventArgs.Empty); }
            else box.Text = value;
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };
        Label(label);
        _root.Children.Add(box);
    }

    private void TextField(string label, string value, Action<string> set) => TextField(label, value, v => { set(v); return true; });

    private void CheckField(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); DesignChanged?.Invoke(this, EventArgs.Empty); };
        _root.Children.Add(box);
    }

    private void AddRow(string label, string unit, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,44"), Margin = new Thickness(0, 2) };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        var u = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.7 };
        Grid.SetColumn(u, 2);
        grid.Children.Add(u);
        _root.Children.Add(grid);
    }

    private static string Format(double v) => v.ToString("0.######", CultureInfo.CurrentCulture);

    /// <summary>Acepta coma o punto decimal.</summary>
    public static bool TryParse(string? text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
