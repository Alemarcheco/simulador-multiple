using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Simulador.Hidraulica.Neumatica.Ui;

/// <summary>Panel derecho de la pestaña Neumática: propiedades en diseño, valores y mandos en simulación.</summary>
public sealed class PneumaticPropertyPanel : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 6, Margin = new Thickness(12) };
    private PneumaticCircuit _circuit = new();
    private PneumaticSimulation? _sim;
    private readonly List<(TextBlock Text, Func<string> Value)> _live = [];

    public event EventHandler? DesignChanged;
    /// <summary>Hay que girar el componente seleccionado.</summary>
    public event EventHandler? RotateRequested;

    public PneumaticPropertyPanel()
    {
        Content = new ScrollViewer { Content = _root };
        Show(null, new PneumaticCircuit(), null);
    }

    public void Show(object? element, PneumaticCircuit circuit, PneumaticSimulation? sim)
    {
        _circuit = circuit;
        _sim = sim;
        _live.Clear();
        _root.Children.Clear();

        if (element is null)
        {
            Header(circuit.Name);
            if (!string.IsNullOrWhiteSpace(circuit.Description)) Note(circuit.Description);
            if (sim is null)
            {
                TextField("Nombre del circuito", circuit.Name, v => { circuit.Name = v; return true; });
                Note("Elige un componente a la izquierda y haz clic en el lienzo. Con «Manguera», clic en una toma, clic en cada codo y clic en la toma de destino. R gira el componente seleccionado; Supr lo borra.");
            }
            else
            {
                Live("Tiempo", () => $"{sim.Time:0.00} s");
                Live("Aire consumido", () => $"{sim.AirConsumed * 1000:0.0} L normales");
                Note("Mantén el clic sobre un pulsador para accionarlo (Ctrl+clic lo deja enclavado). Un clic cambia una palanca o una bobina; en las de doble bobina, la mitad izquierda es la 14 y la derecha la 12.");
            }
            return;
        }

        if (element is Tube t)
        {
            Header("Manguera");
            Label($"{t.FromComponent}.{t.FromPort} → {t.ToComponent}.{t.ToPort}");
            if (sim is null)
            {
                NumberField("Diámetro interior", "mm", () => t.InnerDiameter * 1000, v => t.InnerDiameter = Math.Max(v, 0.5) / 1000);
                NumberField("Longitud", "m", () => t.Length, v => t.Length = Math.Max(v, 0.01));
                Note("El volumen de las mangueras retrasa el llenado; su pérdida de carga se desprecia frente a la de las válvulas.");
            }
            else
            {
                int index = circuit.Tubes.IndexOf(t);
                Live("Presión", () => $"{sim.TubePressure(index) / 1e5:0.00} bar");
            }
            return;
        }

        var c = (PneumaticComponent)element;
        Header(c.TypeName);
        if (sim is null)
        {
            string id = c.Id;
            TextField("Identificador", id, v =>
            {
                if (!circuit.Rename(id, v.Trim())) return false;
                id = v.Trim();
                return true;
            });
            DesignFields(c);
            var rotate = new Button { Content = "Girar 90° (R)", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            rotate.Click += (_, _) => RotateRequested?.Invoke(this, EventArgs.Empty);
            _root.Children.Add(rotate);
        }
        else
        {
            Label($"Identificador: {c.Id}");
            LiveFields(c);
        }
    }

    private void DesignFields(PneumaticComponent c)
    {
        switch (c)
        {
            case AirSupply s:
                NumberField("Presión", "bar", () => s.Pressure / 1e5, v => s.Pressure = Math.Max(v, 0) * 1e5);
                Note("Red de aire comprimido con regulador: mantiene la presión pase el caudal que pase.");
                break;
            case Compressor k:
                NumberField("Caudal libre", "L/min", () => k.FreeAirDelivery * 60_000, v => k.FreeAirDelivery = Math.Max(v, 0) / 60_000);
                NumberField("Presión máxima", "bar", () => k.MaxPressure / 1e5, v => k.MaxPressure = v * 1e5);
                NumberField("Arranca a", "bar", () => k.CutInPressure / 1e5, v => k.CutInPressure = v * 1e5);
                NumberField("Para a", "bar", () => k.CutOutPressure / 1e5, v => k.CutOutPressure = v * 1e5);
                Note("El caudal baja a medida que sube la presión, hasta cero a la presión máxima. El presostato lo para y lo arranca.");
                break;
            case AirReceiver r:
                NumberField("Volumen", "L", () => r.Volume * 1000, v => r.Volume = Math.Max(v, 0.1) / 1000);
                NumberField("Presión inicial", "bar", () => r.InitialPressure / 1e5, v => r.InitialPressure = Math.Max(v, 0) * 1e5);
                break;
            case DirectionalValve v:
                EnumField("Vías", v.Ways, [(ValveWays.ThreeTwo, "3/2"), (ValveWays.FiveTwo, "5/2")], x => { v.Ways = x; Show(v, _circuit, null); });
                EnumField("Accionamiento", v.Actuation,
                    Enum.GetValues<ValveActuation>().Select(a => (a, Capital(DirectionalValve.ActuationName(a)))).ToList(),
                    x => { v.Actuation = x; Show(v, _circuit, null); });
                if (v.Ways == ValveWays.ThreeTwo) CheckField("Normalmente abierta (1→2 en reposo)", v.NormallyOpen, x => v.NormallyOpen = x);
                if (v.Actuation is ValveActuation.Solenoid or ValveActuation.DoubleSolenoid)
                    TextField("Bobina 14", v.Coil14, x => { v.Coil14 = x.Trim(); return true; });
                if (v.Actuation is ValveActuation.DoubleSolenoid)
                    TextField("Bobina 12", v.Coil12, x => { v.Coil12 = x.Trim(); return true; });
                if (v.Actuation is ValveActuation.Roller)
                {
                    TextField("Cilindro que la acciona", v.RollerCylinder, x =>
                    {
                        if (_circuit.Find(x.Trim()) is not Cylinder) return false;
                        v.RollerCylinder = x.Trim();
                        return true;
                    });
                    NumberField("Posición de la leva", "%", () => v.RollerPosition * 100, x => v.RollerPosition = Math.Clamp(x / 100, 0, 1));
                    Note("0 % = cilindro recogido (inicio de carrera), 100 % = vástago fuera (final de carrera).");
                }
                if (v.Actuation is ValveActuation.Pilot or ValveActuation.DoublePilot)
                    NumberField("Presión de pilotaje", "bar", () => v.PilotPressure / 1e5, x => v.PilotPressure = Math.Max(x, 0.1) * 1e5);
                if (v.IsBistable) CheckField("Empieza accionada", v.InitiallyActuated, x => v.InitiallyActuated = x);
                NumberField("Caudal nominal C", "L/(s·bar)", () => v.Conductance * 1e8, x => v.Conductance = Math.Max(x, 0.01) / 1e8);
                Note(v.Ways == ValveWays.ThreeTwo
                    ? "Tomas ISO: 1 presión, 2 utilización, 3 escape."
                    : "Tomas ISO: 1 presión, 2 y 4 utilización, 3 y 5 escape. En reposo 1→2; accionada 1→4.");
                break;
            case FlowControl f:
                NumberField("Apertura", "%", () => f.Opening * 100, x => f.Opening = Math.Clamp(x / 100, 0, 1));
                NumberField("Caudal máximo C", "L/(s·bar)", () => f.MaxConductance * 1e8, x => f.MaxConductance = Math.Max(x, 0.01) / 1e8);
                CheckField("Con antirretorno (unidireccional)", f.OneWay, x => f.OneWay = x);
                Note("Estrangula el aire que va de 1 a 2; con antirretorno, de 2 a 1 pasa libre. Para regular a la salida, pon la toma 1 hacia el cilindro.");
                break;
            case CheckValve k:
                NumberField("Caudal nominal C", "L/(s·bar)", () => k.Conductance * 1e8, x => k.Conductance = Math.Max(x, 0.01) / 1e8);
                Note("Deja pasar de 1 a 2 y bloquea de 2 a 1.");
                break;
            case LogicValve l:
                EnumField("Función", l.Kind, [(LogicKind.Or, "O (selectora)"), (LogicKind.And, "Y (simultaneidad)")], x => { l.Kind = x; Show(l, _circuit, null); });
                break;
            case Cylinder cy:
                EnumField("Tipo", cy.Kind, [(CylinderKind.SingleActing, "Simple efecto"), (CylinderKind.DoubleActing, "Doble efecto")], x => { cy.Kind = x; Show(cy, _circuit, null); });
                NumberField("Diámetro del émbolo", "mm", () => cy.Bore * 1000, x => cy.Bore = x / 1000);
                NumberField("Diámetro del vástago", "mm", () => cy.RodDiameter * 1000, x => cy.RodDiameter = x / 1000);
                NumberField("Carrera", "mm", () => cy.Stroke * 1000, x => cy.Stroke = x / 1000);
                NumberField("Masa móvil", "kg", () => cy.Mass, x => cy.Mass = x);
                NumberField("Carga (se opone a salir)", "N", () => cy.Load, x => cy.Load = x);
                NumberField("Rozamiento", "N", () => cy.Friction, x => cy.Friction = Math.Max(x, 0));
                if (cy.Kind == CylinderKind.SingleActing)
                {
                    NumberField("Muelle: fuerza inicial", "N", () => cy.SpringPreload, x => cy.SpringPreload = Math.Max(x, 0));
                    NumberField("Muelle: rigidez", "N/m", () => cy.SpringRate, x => cy.SpringRate = Math.Max(x, 0));
                }
                NumberField("Posición inicial", "%", () => cy.InitialPosition * 100, x => cy.InitialPosition = Math.Clamp(x / 100, 0, 1));
                TextField("Detector recogido", cy.SensorRetracted, x => { cy.SensorRetracted = x.Trim(); return true; });
                TextField("Detector fuera", cy.SensorExtended, x => { cy.SensorExtended = x.Trim(); return true; });
                Note($"Fuerza teórica a 6 bar: sale {6e5 * cy.PistonArea:0} N, entra {6e5 * cy.AnnulusArea:0} N. Los detectores son señales para el PLC (vacío = sin detector).");
                break;
            case Gauge:
                Note("Muestra la presión manométrica del punto donde se conecta.");
                break;
        }
    }

    private void LiveFields(PneumaticComponent c)
    {
        var sim = _sim!;
        switch (c)
        {
            case Cylinder cy:
                Live("Posición", () => $"{sim.Position(cy.Id) * 1000:0} mm ({sim.PositionRatio(cy.Id) * 100:0} %)");
                Live("Velocidad", () => $"{sim.Velocity(cy.Id):0.000} m/s");
                Live("Presión A", () => $"{sim.PressureAt(cy.Id, "A") / 1e5:0.00} bar");
                if (cy.Kind == CylinderKind.DoubleActing) Live("Presión B", () => $"{sim.PressureAt(cy.Id, "B") / 1e5:0.00} bar");
                Live("Fuerza de las presiones", () => $"{sim.PressureForce(cy.Id):0} N");
                if (!string.IsNullOrWhiteSpace(cy.SensorRetracted)) Live(cy.SensorRetracted, () => sim.SensorActive(cy.SensorRetracted) ? "activo" : "—");
                if (!string.IsNullOrWhiteSpace(cy.SensorExtended)) Live(cy.SensorExtended, () => sim.SensorActive(cy.SensorExtended) ? "activo" : "—");
                break;
            case DirectionalValve v:
                Live("Posición", () => sim.IsActuated(v.Id) ? "accionada" : "reposo");
                foreach (var p in v.Ports) Live($"Toma {p.Name}", () => $"{sim.PressureAt(v.Id, p.Name) / 1e5:0.00} bar");
                if (v.Actuation is ValveActuation.Solenoid or ValveActuation.DoubleSolenoid)
                    foreach (var coil in new[] { PneumaticSimulation.CoilKey(v, true) }.Concat(v.Actuation == ValveActuation.DoubleSolenoid ? [PneumaticSimulation.CoilKey(v, false)] : []))
                    {
                        var box = new CheckBox { Content = $"Bobina {coil}", IsChecked = sim.IsCoilEnergized(coil) };
                        box.IsCheckedChanged += (_, _) => sim.SetCoil(coil, box.IsChecked == true);
                        _root.Children.Add(box);
                        _live.Add((new TextBlock(), () => { box.IsChecked = sim.IsCoilEnergized(coil); return ""; }));
                    }
                break;
            case FlowControl f:
                Label("Apertura (%)");
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = f.Opening * 100 };
                slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) f.Opening = slider.Value / 100; };
                _root.Children.Add(slider);
                Live("Toma 1", () => $"{sim.PressureAt(f.Id, "1") / 1e5:0.00} bar");
                Live("Toma 2", () => $"{sim.PressureAt(f.Id, "2") / 1e5:0.00} bar");
                break;
            case Compressor k:
                Live("Estado", () => sim.IsCompressorRunning(k.Id) ? "en marcha" : "parado");
                Live("Presión", () => $"{sim.PressureAt(k.Id, "1") / 1e5:0.00} bar");
                break;
            default:
                foreach (var p in c.Ports) Live($"Toma {p.Name}", () => $"{sim.PressureAt(c.Id, p.Name) / 1e5:0.00} bar");
                break;
        }
    }

    public void RefreshLive()
    {
        foreach (var (text, value) in _live)
        {
            try { text.Text = value(); }
            catch (KeyNotFoundException) { text.Text = "—"; }
        }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    private void Header(string text) =>
        _root.Children.Add(new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

    private void Label(string text) => _root.Children.Add(new TextBlock { Text = text, Opacity = 0.85, TextWrapping = TextWrapping.Wrap });

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
        var box = new TextBox { Text = Format(get()), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
        void Commit()
        {
            if (double.TryParse(box.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
            {
                set(v);
                DesignChanged?.Invoke(this, EventArgs.Empty);
            }
            box.Text = Format(get());
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0) });
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        var u = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, Margin = new Thickness(6, 0, 0, 0), MinWidth = 30 };
        Grid.SetColumn(u, 2);
        row.Children.Add(u);
        _root.Children.Add(row);
    }

    private static string Format(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.CurrentCulture);

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

    private void CheckField(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); DesignChanged?.Invoke(this, EventArgs.Empty); };
        _root.Children.Add(box);
    }

    private void EnumField<T>(string label, T value, IReadOnlyList<(T Value, string Text)> options, Action<T> set) where T : struct, Enum
    {
        var combo = new ComboBox
        {
            ItemsSource = options.Select(o => o.Text).ToList(),
            SelectedIndex = options.ToList().FindIndex(o => o.Value.Equals(value)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || options[combo.SelectedIndex].Value.Equals(value)) return;
            DesignChanged?.Invoke(this, EventArgs.Empty);
            set(options[combo.SelectedIndex].Value);
        };
        Label(label);
        _root.Children.Add(combo);
    }
}
