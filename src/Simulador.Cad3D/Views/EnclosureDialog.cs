using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Simulador.Cad3D.Geometry.Templates;

namespace Simulador.Cad3D.Views;

/// <summary>Diálogo para crear una caja con tapa a partir de sus medidas interiores.</summary>
internal sealed class EnclosureDialog : Window
{
    private readonly Dictionary<string, NumericUpDown> _fields = new();

    public EnclosureDialog()
    {
        Title = "Nueva caja con tapa";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var d = new EnclosureParameters();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,140"), Margin = new Thickness(16) };
        void Add(string key, string label, double value, double min, double step)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            var nud = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = 2000, Increment = (decimal)step, FormatString = "0.##", Margin = new Thickness(0, 3) };
            Grid.SetRow(tb, row); Grid.SetRow(nud, row); Grid.SetColumn(nud, 1);
            grid.Children.Add(tb); grid.Children.Add(nud);
            _fields[key] = nud;
        }
        Add("x", "Ancho interior X (mm)", d.InnerX, 5, 1);
        Add("y", "Fondo interior Y (mm)", d.InnerY, 5, 1);
        Add("z", "Alto interior Z (mm)", d.InnerZ, 3, 1);
        Add("wall", "Grosor de pared (mm)", d.Wall, 0.8, 0.2);
        Add("floor", "Grosor del suelo (mm)", d.Floor, 0.6, 0.2);
        Add("lid", "Grosor de la tapa (mm)", d.LidThickness, 0.6, 0.2);
        Add("radius", "Radio de esquinas (mm)", d.CornerRadius, 0, 0.5);
        Add("clearance", "Holgura de encaje (mm)", d.Clearance, 0, 0.05);
        Add("standoffs", "Postes en las esquinas (0 o 4)", 4, 0, 4);
        Add("vents", "Ranuras de ventilación en la tapa", 5, 0, 1);

        var ok = new Button { Content = "Crear", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) => Close(Build());
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Children =
            {
                grid,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16), Children = { cancel, ok } },
            },
        };
    }

    private double V(string key) => (double)(_fields[key].Value ?? 0);

    private EnclosureParameters Build()
    {
        double x = V("x"), y = V("y");
        var standoffs = V("standoffs") >= 4
            ? new[] { new Standoff(5, 5), new Standoff(x - 5, 5), new Standoff(5, y - 5), new Standoff(x - 5, y - 5) }
            : Array.Empty<Standoff>();
        return new EnclosureParameters
        {
            InnerX = x, InnerY = y, InnerZ = V("z"),
            Wall = V("wall"), Floor = V("floor"), LidThickness = V("lid"),
            CornerRadius = V("radius"), Clearance = V("clearance"),
            Standoffs = standoffs,
            VentSlots = (int)V("vents"),
        };
    }
}
