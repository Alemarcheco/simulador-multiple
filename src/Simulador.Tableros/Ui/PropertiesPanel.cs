using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Ui;

/// <summary>Propiedades de lo seleccionado (aparato, carril o canaleta) o, sin selección, de la envolvente.</summary>
internal sealed class PropertiesPanel : ScrollViewer
{
    private readonly StackPanel _stack = new() { Spacing = 6, Margin = new Thickness(10) };
    private bool _loading;

    /// <summary>Algo ha cambiado. El texto describe el cambio (para deshacer).</summary>
    public event Action<string>? Changed;
    /// <summary>Hay que volver a distribuir (cambio de tamaño de la envolvente).</summary>
    public event Action? ArrangeRequested;
    /// <summary>Se ha marcado o quitado el doble frente: hay que elegir armario y repartir de nuevo.</summary>
    public event Action? DoubleFrontChanged;

    public PropertiesPanel()
    {
        Content = _stack;
    }

    public void Show(PanelDocument doc, object? item)
    {
        _loading = true;
        _stack.Children.Clear();
        switch (item)
        {
            case PanelDevice d: ShowDevice(d); break;
            case DinRail r: ShowRail(r); break;
            case Duct k: ShowDuct(k); break;
            default: ShowEnclosure(doc); break;
        }
        _loading = false;
    }

    private void Title(string text, string? sub = null)
    {
        _stack.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.Bold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        if (sub is not null) _stack.Children.Add(new TextBlock { Text = sub, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
    }

    private void Label(string text) => _stack.Children.Add(new TextBlock { Text = text, FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) });

    private void Text(string label, string value, Action<string> set, string? watermark = null)
    {
        Label(label);
        var box = new TextBox { Text = value, Watermark = watermark };
        box.LostFocus += (_, _) =>
        {
            if (_loading || box.Text == value) return;
            value = box.Text ?? "";
            set(value);
            Changed?.Invoke(label);
        };
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus(); };
        _stack.Children.Add(box);
    }

    private void Number(string label, double value, double min, double max, Action<double> set, double step = 1, string format = "0.#")
    {
        Label(label);
        var n = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, FormatString = format };
        n.ValueChanged += (_, e) =>
        {
            if (_loading || e.NewValue is not { } v) return;
            set((double)v);
            Changed?.Invoke(label);
        };
        _stack.Children.Add(n);
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 0) };
        c.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            set(c.IsChecked == true);
            Changed?.Invoke(label);
        };
        _stack.Children.Add(c);
    }

    private void Combo(string label, string[] options, int index, Action<int> set)
    {
        Label(label);
        var c = new ComboBox { ItemsSource = options, SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch };
        c.SelectionChanged += (_, _) =>
        {
            if (_loading || c.SelectedIndex < 0) return;
            set(c.SelectedIndex);
            Changed?.Invoke(label);
        };
        _stack.Children.Add(c);
    }

    private void ShowDevice(PanelDevice d)
    {
        var model = DeviceLibrary.Get(d.Kind);
        Title(d.Tag, model.Name);
        Text("Designación", d.Tag, v => d.Tag = v.Trim());
        Text("Descripción", d.Description, v => d.Description = v);
        Text("Referencia del fabricante", d.Reference, v => d.Reference = v, "Marca y código de catálogo");
        // Librería de fabricante: los modelos del mismo tipo (todos si el aparato es genérico).
        var items = ManufacturerCatalog.All.Where(i => d.Kind == "aparato" || i.Kind == d.Kind).ToList();
        if (items.Count > 0 && d.Location != DeviceLocation.Field)
        {
            var current = ManufacturerCatalog.Find(d.Reference);
            Combo("Modelo de la librería de fabricante", items.Select(i => i.Display).Prepend("— Genérico (medidas típicas) —").ToArray(),
                current is null ? 0 : items.IndexOf(current) + 1,
                i => { if (i > 0) ManufacturerCatalog.Apply(d, items[i - 1]); });
            if (current is not null)
                _stack.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7,
                    Text = current.Verified ? $"Medidas de la ficha del fabricante ({current.Source})." : "Medidas típicas: la ficha del fabricante no las da; compruébalas en su catálogo.",
                });
        }
        Combo("Ubicación", ["Placa de montaje", "Puerta", "Fuera del tablero (campo)"], (int)d.Location, i => d.Location = (DeviceLocation)i);
        if (d.Location == DeviceLocation.Field)
        {
            _stack.Children.Add(new TextBlock { Text = "Va fuera del tablero: se conecta por la regleta y su cable de campo.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });
            return;
        }
        Number("X (mm)", d.X, -2000, 4000, v => d.X = v, 1);
        Number("Y (mm)", d.Y, -2000, 4000, v => d.Y = v, 1);
        Number("Ancho (mm)", d.Width, 5, 2000, v => d.Width = v, 0.5);
        Number("Alto (mm)", d.Height, 5, 2000, v => d.Height = v, 1);
        Number("Fondo (mm)", d.Depth, 5, 1000, v => d.Depth = v, 1);
        Number("Calor disipado (W)", d.HeatW, 0, 5000, v => d.HeatW = v, 0.5);
        if (d.Location == DeviceLocation.Plate) Check("Se monta en carril DIN", d.OnRail, v => d.OnRail = v);
        if (d.Terminals.Count > 0)
        {
            Label("Bornes");
            var top = string.Join("  ", d.Terminals.Where(t => t.Side == TerminalSide.Top).Select(t => t.Label).Distinct());
            var bottom = string.Join("  ", d.Terminals.Where(t => t.Side == TerminalSide.Bottom).Select(t => t.Label).Distinct());
            _stack.Children.Add(new TextBlock { Text = $"Arriba: {top}\nAbajo: {bottom}", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        if (d.Accessories.Count > 0)
        {
            Label("Accesorios");
            _stack.Children.Add(new TextBlock { Text = string.Join("\n", d.Accessories), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
    }

    private void ShowRail(DinRail r)
    {
        Title("Carril DIN 35 mm", "Carril simétrico perforado de 35 × 7,5 mm.");
        Number("X (mm)", r.X, -500, 3000, v => r.X = v, 5);
        Number("Y (mm)", r.Y, -500, 3000, v => r.Y = v, 5);
        Number("Largo (mm)", r.Length, 20, 3000, v => r.Length = v, 5);
    }

    private void ShowDuct(Duct k)
    {
        Title("Canaleta ranurada", k.Vertical ? "Vertical" : "Horizontal");
        Number("X (mm)", k.X, -500, 3000, v => k.X = v, 5);
        Number("Y (mm)", k.Y, -500, 3000, v => k.Y = v, 5);
        Number("Largo (mm)", k.Length, 20, 3000, v => k.Length = v, 5);
        Combo("Ancho × alto", DuctSizes.Select(s => $"{s.W:0} × {s.D:0} mm").ToArray(),
            Math.Max(0, Array.FindIndex(DuctSizes, s => s.W == k.Width && s.D == k.Depth)),
            i => (k.Width, k.Depth) = DuctSizes[i]);
        Check("Vertical", k.Vertical, v => k.Vertical = v);
    }

    public static readonly (double W, double D)[] DuctSizes = [(25, 40), (25, 60), (40, 40), (40, 60), (40, 80), (60, 60), (60, 80), (80, 80), (100, 80)];

    private void ShowEnclosure(PanelDocument doc)
    {
        var e = doc.Enclosure;
        Title("Envolvente", "Nada seleccionado: aquí cambias la caja o el armario. Haz clic en un aparato para ver los suyos.");
        Text("Nombre del tablero", doc.Name, v => doc.Name = v);
        Combo("Tipo", [Enclosure.KindName(EnclosureKind.MuralMetalico), Enclosure.KindName(EnclosureKind.ArmarioDePie), Enclosure.KindName(EnclosureKind.CajaPoliester)],
            (int)e.Kind, i => e.Kind = (EnclosureKind)i);
        var sizes = e.DoubleFront ? AutoLayout.DoubleFrontSizes : AutoLayout.StandardSizes;
        Combo("Medida normalizada", sizes.Select(s => $"{s.W:0} × {s.H:0} × {s.D:0} mm").Prepend("A medida").ToArray(),
            Array.FindIndex(sizes, s => s.W == e.Width && s.H == e.Height && s.D == e.Depth) + 1,
            i =>
            {
                if (i == 0) return;
                (e.Width, e.Height, e.Depth) = sizes[i - 1];
            });
        var df = new CheckBox { Content = "Doble frente (placa y puerta delante y detrás)", IsChecked = e.DoubleFront, Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(df, "Armario de pie con dos placas espalda con espalda, cada una con su puerta. Al marcarlo se elige armario y se reparten los aparatos entre las dos caras.");
        df.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            e.DoubleFront = df.IsChecked == true;
            DoubleFrontChanged?.Invoke();
        };
        _stack.Children.Add(df);
        Number("Ancho (mm)", e.Width, 150, 3000, v => e.Width = v, 50);
        Number("Alto (mm)", e.Height, 150, 2400, v => e.Height = v, 50);
        Number("Fondo (mm)", e.Depth, 80, 800, v => e.Depth = v, 50);
        Text("Grado de protección", e.Ip, v => e.Ip = v);
        var arrange = new Button { Content = "Redistribuir en esta medida", Margin = new Thickness(0, 6, 0, 0) };
        arrange.Click += (_, _) => ArrangeRequested?.Invoke();
        _stack.Children.Add(arrange);

        _stack.Children.Add(new TextBlock { Text = "Temperatura", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 12, 0, 0) });
        Number("Temperatura ambiente (°C)", e.AmbientC, -20, 60, v => e.AmbientC = v);
        Number("Máxima dentro (°C)", e.MaxInsideC, 20, 70, v => e.MaxInsideC = v);
        Number("Ventilador con filtro (m³/h, 0 = sin)", e.FanFlow, 0, 2000, v => e.FanFlow = v, 5);
        Check("Apoyado contra la pared (la espalda no disipa)", e.AgainstWall, v => e.AgainstWall = v);
        var th = PanelChecks.Thermal(doc);
        _stack.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
            Text = $"Pérdidas: {th.PowerW:0} W · superficie eficaz {th.SurfaceM2:0.00} m²\nCalentamiento: +{th.RiseK:0.0} K → {th.InsideC:0.0} °C dentro" +
                   (th.InsideC > e.MaxInsideC && double.IsFinite(th.FanNeededM3h) ? $"\nHace falta ventilación de ≥ {Math.Ceiling(th.FanNeededM3h):0} m³/h." : ""),
        });
    }
}
