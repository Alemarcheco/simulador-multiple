using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Editor;

/// <summary>Propiedades del elemento seleccionado: designación, parámetros, bornes y aparato de catálogo.</summary>
internal sealed class PropertiesPanel : ScrollViewer
{
    private readonly ElementCatalog _catalog;
    private readonly Func<ManufacturerCatalog> _presets;
    private readonly StackPanel _stack = new() { Spacing = 6, Margin = new Thickness(10) };

    public event Action? Changed;
    /// <summary>Cambio de carga de motor durante la simulación (id, %).</summary>
    public event Action<Guid, double>? MotorLoadChanged;

    public PropertiesPanel(ElementCatalog catalog, Func<ManufacturerCatalog> presets)
    {
        _catalog = catalog;
        _presets = presets;
        Content = _stack;
        Show(null, false);
    }

    public void Show(PlacedElement? e, bool simulating)
    {
        _stack.Children.Clear();
        if (e is null || !_catalog.TryGet(e.Type, out var t))
        {
            _stack.Children.Add(Title("Propiedades"));
            _stack.Children.Add(Note(simulating
                ? "Simulando. Haz clic en pulsadores, interruptores, setas, finales de carrera y protecciones para accionarlos. Mayús+clic en un diferencial pulsa su botón de prueba."
                : "Elige un componente de la librería y haz clic en la hoja para colocarlo. Para cablear, haz clic en un borne y luego en otro (con Mayús sigues cableando). R gira, Supr borra."));
            return;
        }

        _stack.Children.Add(Title(t.Name));
        _stack.Children.Add(Note(t.Description));

        if (simulating)
        {
            _stack.Children.Add(Note($"Designación: {e.Tag}"));
            if (t.Behavior is Behavior.Motor3 or Behavior.Motor6)
            {
                _stack.Children.Add(Caption("Carga del motor (% del par asignado)"));
                var slider = new Slider { Minimum = 0, Maximum = 200, Value = e.Param(t, "carga"), TickFrequency = 10 };
                var value = new TextBlock { Text = $"{slider.Value:0} %" };
                slider.PropertyChanged += (_, a) =>
                {
                    if (a.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
                    value.Text = $"{slider.Value:0} %";
                    MotorLoadChanged?.Invoke(e.Id, slider.Value);
                };
                _stack.Children.Add(slider);
                _stack.Children.Add(value);
            }
            return;
        }

        var tag = new TextBox { Text = e.Tag };
        tag.TextChanged += (_, _) => { e.Tag = tag.Text?.Trim() ?? ""; Changed?.Invoke(); };
        _stack.Children.Add(Caption("Designación (los elementos con la misma forman un aparato)"));
        _stack.Children.Add(tag);

        var presets = _presets().ForType(t.Id).ToList();
        if (presets.Count > 0)
        {
            _stack.Children.Add(Caption("Aparato de catálogo"));
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Elegir referencia…" };
            foreach (var p in presets) combo.Items.Add(new ComboBoxItem { Content = $"{p.Display} — {p.Description}", Tag = p });
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: ManufacturerPreset p })
                {
                    p.ApplyTo(e);
                    Changed?.Invoke();
                    Show(e, false);
                }
            };
            _stack.Children.Add(combo);
        }

        foreach (var p in t.Params)
        {
            _stack.Children.Add(Caption(string.IsNullOrEmpty(p.Unit) ? p.Name : $"{p.Name} ({p.Unit})"));
            var current = e.Param(t, p.Key);
            if (p.Options is not null)
            {
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var o in p.Options) combo.Items.Add(o);
                combo.SelectedIndex = (int)Math.Clamp(Math.Round(current), 0, p.Options.Length - 1);
                combo.SelectionChanged += (_, _) => { e.Params[p.Key] = combo.SelectedIndex; Changed?.Invoke(); };
                _stack.Children.Add(combo);
            }
            else
            {
                var box = new TextBox { Text = current.ToString("0.###", CultureInfo.CurrentCulture) };
                box.LostFocus += (_, _) => Commit(box, e, p);
                box.KeyDown += (_, k) => { if (k.Key == Avalonia.Input.Key.Enter) Commit(box, e, p); };
                _stack.Children.Add(box);
            }
        }

        if (t.Terminals.Count == 2 && !t.IsSource)
        {
            _stack.Children.Add(Caption("Numeración de bornes (p. ej. 13-14, 95-96)"));
            var bornes = new TextBox { Text = e.Text("Bornes", t.DefaultTerminalText), Watermark = "13-14" };
            bornes.TextChanged += (_, _) => { e.Texts["Bornes"] = bornes.Text ?? ""; Changed?.Invoke(); };
            _stack.Children.Add(bornes);
        }
        if (t.Behavior is Behavior.SensorNO or Behavior.SensorNC)
        {
            _stack.Children.Add(Caption("Rótulo del captador (flotador, presostato, B1…)"));
            var label = new TextBox { Text = e.Text("Rótulo"), Watermark = "Flotador" };
            label.TextChanged += (_, _) => { e.Texts["Rótulo"] = label.Text ?? ""; Changed?.Invoke(); };
            _stack.Children.Add(label);
        }
        _stack.Children.Add(Caption("Referencia de fabricante"));
        var reference = new TextBox { Text = e.Text("Referencia"), Watermark = "Opcional" };
        reference.TextChanged += (_, _) => { e.Texts["Referencia"] = reference.Text ?? ""; Changed?.Invoke(); };
        _stack.Children.Add(reference);
    }

    private void Commit(TextBox box, PlacedElement e, ParamDef p)
    {
        var text = (box.Text ?? "").Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            v = Math.Clamp(v, p.Min, p.Max);
            e.Params[p.Key] = v;
            box.Text = v.ToString("0.###", CultureInfo.CurrentCulture);
            Changed?.Invoke();
        }
        else box.Text = e.Params.TryGetValue(p.Key, out var old) ? old.ToString("0.###", CultureInfo.CurrentCulture) : p.Default.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static TextBlock Title(string s) => new() { Text = s, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Caption(string s) => new() { Text = s, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private static TextBlock Note(string s) => new() { Text = s, FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
}
