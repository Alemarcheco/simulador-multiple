using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Simulador.Cad3D.Geometry.Export;

namespace Simulador.Cad3D.Views;

/// <summary>Datos para guardar una pieza como aparato de la librería de Tableros eléctricos.</summary>
internal sealed record SaveDeviceRequest(EdzWriter.Options Options);

/// <summary>Diálogo «Guardar como aparato de tablero»: marca, referencia, tipo, dónde va y qué cara se apoya.</summary>
internal sealed class SaveDeviceDialog : Window
{
    /// <summary>Tipos de aparato del módulo Tableros (clave y nombre).</summary>
    private static readonly (string Kind, string Name, bool Door, bool Rail)[] Kinds =
    [
        ("aparato", "Aparato genérico", false, true),
        ("contactor", "Contactor", false, true),
        ("magnetotermico", "Interruptor automático (magnetotérmico)", false, true),
        ("diferencial", "Interruptor diferencial", false, true),
        ("guardamotor", "Guardamotor", false, true),
        ("rele_termico", "Relé térmico", false, true),
        ("rele", "Relé de interfaz", false, true),
        ("temporizador", "Temporizador", false, true),
        ("fusible", "Portafusibles", false, true),
        ("fuente", "Fuente de alimentación", false, true),
        ("plc", "Autómata (PLC) o módulo de E/S", false, true),
        ("mcu", "Microcontrolador (Arduino, ESP32…)", false, true),
        ("variador", "Variador de frecuencia", false, false),
        ("trafo_mando", "Transformador de mando", false, false),
        ("regleta", "Borne / regleta", false, true),
        ("barra_pe", "Barra de tierra", false, false),
        ("pulsador", "Pulsador (puerta)", true, false),
        ("seta", "Seta de emergencia (puerta)", true, false),
        ("selector", "Selector (puerta)", true, false),
        ("piloto", "Piloto de señalización (puerta)", true, false),
        ("instrumento", "Instrumento de panel (puerta)", true, false),
        ("controlador", "Pantalla o controlador de panel (puerta)", true, false),
    ];

    private readonly TextBox _brand = new() { Text = "Mis piezas" };
    private readonly TextBox _reference = new();
    private readonly TextBox _description = new();
    private readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _where = new() { ItemsSource = new[] { "Placa de montaje", "Puerta" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _rail = new() { Content = "Va en carril DIN", IsChecked = true };
    private readonly ComboBox _face = new()
    {
        ItemsSource = new[] { "La de abajo (la que tocaba la mesa): se verá como en la vista «Arriba»", "La de atrás: se verá como en la vista «Frente»" },
        SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly NumericUpDown _protrusion = new() { Value = 20, Minimum = 0, Maximum = 500, Increment = 1, FormatString = "0.#" };
    private readonly NumericUpDown _heat = new() { Value = 0, Minimum = 0, Maximum = 5000, Increment = 0.5m, FormatString = "0.##" };
    private readonly NumericUpDown _tris = new() { Value = 8000, Minimum = 200, Maximum = 200000, Increment = 1000, FormatString = "0" };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.OrangeRed, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public SaveDeviceDialog(string partName, string size)
    {
        Title = "Guardar como aparato de tablero";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _reference.Text = partName;
        _description.Text = partName;
        _kind.ItemsSource = Kinds.Select(k => k.Name).ToList();
        _kind.SelectedIndex = 0;
        _kind.SelectionChanged += (_, _) =>
        {
            if (_kind.SelectedIndex < 0) return;
            var k = Kinds[_kind.SelectedIndex];
            _where.SelectedIndex = k.Door ? 1 : 0;
            _rail.IsChecked = k.Rail;
        };
        _where.SelectionChanged += (_, _) => _protrusion.IsEnabled = _where.SelectedIndex == 1;
        _protrusion.IsEnabled = false;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new Thickness(16, 16, 16, 4) };
        void Row(string label, Control c)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            c.Margin = new Thickness(0, 4);
            Grid.SetRow(tb, r); Grid.SetRow(c, r); Grid.SetColumn(c, 1);
            grid.Children.Add(tb); grid.Children.Add(c);
        }
        Row("Marca (librería)", _brand);
        Row("Referencia", _reference);
        Row("Descripción", _description);
        Row("Tipo de aparato", _kind);
        Row("Dónde va", _where);
        Row("", _rail);
        Row("Cara que se apoya", _face);
        Row("Sobresale de la puerta (mm)", _protrusion);
        Row("Calor que disipa (W)", _heat);
        Row("Triángulos como mucho", _tris);

        var ok = new Button { Content = "Guardar en la librería", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_reference.Text)) { _error.Text = "Pon una referencia (por ejemplo, el modelo que lleva impreso el aparato)."; return; }
            var k = Kinds[Math.Max(0, _kind.SelectedIndex)];
            Close(new SaveDeviceRequest(new EdzWriter.Options(
                _brand.Text ?? "", _reference.Text!.Trim(), string.IsNullOrWhiteSpace(_description.Text) ? k.Name : _description.Text!.Trim(), k.Kind,
                OnDoor: _where.SelectedIndex == 1, OnRail: _rail.IsChecked == true,
                Face: _face.SelectedIndex == 1 ? MountFace.Atras : MountFace.Abajo,
                HeatW: (double)(_heat.Value ?? 0), MaxTriangles: (int)(_tris.Value ?? 8000),
                DoorProtrusion: (double)(_protrusion.Value ?? 20))));
        };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = $"La pieza ({size}) se guarda en tu librería y aparece en Tableros eléctricos › Añadir aparato › Librería «marca». " +
                           "Colócala antes con la cara que se apoya en la placa hacia abajo (o hacia atrás).",
                    Margin = new Thickness(16, 16, 16, 0), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8, FontSize = 12,
                },
                grid,
                new Border { Child = _error, Margin = new Thickness(16, 0) },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 16), Children = { cancel, ok } },
            },
        };
    }
}
