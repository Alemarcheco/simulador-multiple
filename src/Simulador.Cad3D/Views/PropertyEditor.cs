using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Model;

namespace Simulador.Cad3D.Views;

/// <summary>Panel de propiedades: genera los campos de la pieza u operación seleccionada.</summary>
internal sealed class PropertyEditor : ScrollViewer
{
    private readonly StackPanel _stack = new() { Spacing = 6, Margin = new Thickness(8) };

    /// <summary>Se lanza cuando el usuario cambia un valor.</summary>
    public event Action? Changed;

    /// <summary>Se lanza cuando cambia el nombre (para refrescar las listas).</summary>
    public event Action? Renamed;

    public PropertyEditor()
    {
        Content = _stack;
        ShowNothing();
    }

    public void ShowNothing()
    {
        _stack.Children.Clear();
        _stack.Children.Add(new TextBlock { Text = "Selecciona una pieza u operación.", Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
    }

    public void ShowPart(Part part)
    {
        _stack.Children.Clear();
        Header("Pieza");
        Text("Nombre", part.Name, v => { part.Name = v; Renamed?.Invoke(); });
        Text("Color (#RRGGBB)", part.Color, v => { if (TryColor(v)) { part.Color = v; Changed?.Invoke(); } });
        Check("Visible", part.Visible, v => { part.Visible = v; Changed?.Invoke(); });
        _stack.Children.Add(new TextBlock
        {
            Text = "Cada pieza se exporta como un objeto separado en 3MF; en STL se juntan todas las visibles.",
            Opacity = 0.6, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12,
        });
    }

    public void ShowFeature(Feature f)
    {
        _stack.Children.Clear();
        Header(f.KindName);
        Text("Nombre", f.Name, v => { f.Name = v; Renamed?.Invoke(); });

        var mode = new ComboBox { ItemsSource = new[] { "Sólido (añade)", "Hueco (resta)", "Intersección" }, SelectedIndex = (int)f.Mode, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.SelectionChanged += (_, _) => { f.Mode = (CombineMode)Math.Max(0, mode.SelectedIndex); Renamed?.Invoke(); Changed?.Invoke(); };
        Row("Modo", mode);
        Check("Activa", f.Enabled, v => { f.Enabled = v; Renamed?.Invoke(); Changed?.Invoke(); });

        Header("Medidas (mm)");
        foreach (var prop in f.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<ParamAttribute>() is not { } param || !prop.CanWrite) continue;
            bool isInt = prop.PropertyType == typeof(int);
            double value = Convert.ToDouble(prop.GetValue(f), CultureInfo.InvariantCulture);
            Number(param.Label, value, param.Min, param.Max, isInt ? 1 : param.Step, isInt, v =>
            {
                prop.SetValue(f, isInt ? (object)(int)Math.Round(v) : v);
                Changed?.Invoke();
            });
        }

        if (f is ExtrudeFeature ex)
        {
            var box = new TextBox
            {
                Text = string.Join("; ", ex.Profile.Select(p => FormattableString.Invariant($"{p.X:0.###},{p.Y:0.###}"))),
                AcceptsReturn = false,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            var error = new TextBlock { Foreground = Avalonia.Media.Brushes.OrangeRed, FontSize = 12 };
            box.LostFocus += (_, _) =>
            {
                if (TryParseProfile(box.Text ?? "", out var pts)) { ex.Profile = pts; error.Text = ""; Changed?.Invoke(); }
                else error.Text = "Formato: x,y; x,y; x,y (al menos 3 puntos).";
            };
            Row("Perfil (x,y; x,y; …)", box);
            _stack.Children.Add(error);
        }

        Header("Posición (mm)");
        VecEditor(f.Position, v => { f.Position = v; Changed?.Invoke(); }, 1);
        Header("Rotación (grados)");
        VecEditor(f.Rotation, v => { f.Rotation = v; Changed?.Invoke(); }, 15);
    }

    private void VecEditor(Vec3 initial, Action<Vec3> set, double step)
    {
        var current = initial;
        Number("X", initial.X, double.MinValue, double.MaxValue, step, false, v => { current = current with { X = v }; set(current); });
        Number("Y", initial.Y, double.MinValue, double.MaxValue, step, false, v => { current = current with { Y = v }; set(current); });
        Number("Z", initial.Z, double.MinValue, double.MaxValue, step, false, v => { current = current with { Z = v }; set(current); });
    }

    private void Header(string text) =>
        _stack.Children.Add(new TextBlock { Text = text, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });

    private void Row(string label, Control editor)
    {
        _stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 });
        _stack.Children.Add(editor);
    }

    private void Text(string label, string value, Action<string> set)
    {
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) => set(box.Text ?? "");
        Row(label, box);
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = value };
        cb.IsCheckedChanged += (_, _) => set(cb.IsChecked == true);
        _stack.Children.Add(cb);
    }

    private void Number(string label, double value, double min, double max, double step, bool integer, Action<double> set)
    {
        static decimal Clamp(double v) => (decimal)Math.Clamp(v, -1e9, 1e9);
        var nud = new NumericUpDown
        {
            Value = Clamp(value),
            Minimum = Clamp(min),
            Maximum = Clamp(max),
            Increment = (decimal)step,
            FormatString = integer ? "0" : "0.###",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        nud.ValueChanged += (_, e) => { if (e.NewValue is { } v) set((double)v); };
        Row(label, nud);
    }

    private static bool TryColor(string v) =>
        v.Length == 7 && v[0] == '#' && int.TryParse(v.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    internal static bool TryParseProfile(string text, out List<Vec2> points)
    {
        points = new List<Vec2>();
        foreach (var chunk in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var xy = chunk.Split(',', StringSplitOptions.TrimEntries);
            if (xy.Length != 2
                || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return false;
            points.Add(new Vec2(x, y));
        }
        return points.Count >= 3;
    }
}
