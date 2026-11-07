using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Civil.Model;

namespace Simulador.Civil.Views;

/// <summary>
/// Pestaña "Asistente": sugerencias con botón Aplicar, cálculo de agua, materiales y presupuesto con precios editables,
/// y las reglas (editables y guardadas con el proyecto).
/// </summary>
public sealed class AdvisorView : ScrollViewer
{
    private readonly BuildingDocument _doc;
    private readonly Action<string> _goTo;
    private readonly StackPanel _suggestions = new() { Spacing = 4 };
    private readonly TextBlock _water = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid _budget = new();
    private readonly TextBlock _budgetTotal = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _rules = new() { Spacing = 6 };
    private bool _dirty = true;
    private AdvisorSettings? _rulesFor;

    public AdvisorView(BuildingDocument doc, Action<string> goTo)
    {
        _doc = doc;
        _goTo = goTo;
        doc.Changed += (_, _) => { _dirty = true; if (IsEffectivelyVisible) Refresh(); };
        AttachedToVisualTree += (_, _) => { if (_dirty) Refresh(); };

        var copy = new Button { Content = "Copiar presupuesto (para Excel)" };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(BudgetCsv());
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                H1("Sugerencias"),
                new TextBlock { Text = "Revisión con reglas orientativas que puedes ajustar abajo a la norma de tu país. «Aplicar» coloca lo que falta; se puede deshacer con Ctrl+Z.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                _suggestions,
                H1("Agua"),
                _water,
                H1("Materiales y presupuesto"),
                _budget,
                _budgetTotal,
                copy,
                new Expander { Header = "Reglas del asistente", Content = _rules, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) },
            },
        };
        BuildRules();
    }

    private static TextBlock H1(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 2) };

    public void Refresh()
    {
        _dirty = false;
        if (!ReferenceEquals(_rulesFor, _doc.Advisor)) BuildRules();
        RefreshSuggestions();
        RefreshWater();
        RefreshBudget();
    }

    private void RefreshSuggestions()
    {
        _suggestions.Children.Clear();
        var list = BuildingAdvisor.Suggest(_doc).OrderByDescending(s => s.Severity).ThenBy(s => s.Kind).ToList();
        var applicable = list.Where(s => s.Apply is not null).ToList();
        if (applicable.Count > 0)
        {
            var all = new Button { Content = $"✨ Aplicar las {applicable.Count} sugerencias", Margin = new Thickness(0, 0, 0, 4) };
            all.Click += (_, _) => _doc.Edit(e => { foreach (var s in applicable) s.Apply!(e); });
            _suggestions.Children.Add(all);
        }
        if (list.Count == 0)
        {
            _suggestions.Children.Add(new TextBlock { Text = "✔ Nada que sugerir con las reglas actuales.", Foreground = Brushes.SeaGreen });
            return;
        }
        foreach (var s in list)
        {
            string icon = s.Severity switch { CheckSeverity.Error => "⛔", CheckSeverity.Warning => "⚠", _ => "💡" };
            string kind = s.Kind switch
            {
                SuggestionKind.Electricidad => "Electricidad",
                SuggestionKind.Ventilacion => "Ventilación",
                SuggestionKind.Agua => "Agua",
                SuggestionKind.Evacuacion => "Evacuación",
                _ => s.Kind.ToString(),
            };
            var row = new DockPanel { LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
            if (!string.IsNullOrEmpty(s.ElementId))
            {
                var see = new Button { Content = "Ver" };
                see.Click += (_, _) => _goTo(s.ElementId);
                buttons.Children.Add(see);
            }
            if (s.Apply is { } apply)
            {
                var ap = new Button { Content = "Aplicar" };
                ap.Click += (_, _) => _doc.Edit(apply);
                buttons.Children.Add(ap);
            }
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            var text = new TextBlock { Text = $"{icon} {kind} · {s.Message}", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            if (s.Severity == CheckSeverity.Error) text.Foreground = Brushes.Firebrick;
            row.Children.Add(text);
            _suggestions.Children.Add(new Border { Child = row, Padding = new Thickness(6, 4), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1) });
        }
    }

    private void RefreshWater()
    {
        var w = BuildingAdvisor.Water(_doc);
        _water.Text =
            $"Personas estimadas: {w.Persons} (2 por dormitorio o habitación de hotel, 1 por cada 10 m² de oficina)\n" +
            $"Consumo diario: {w.DailyLiters:0} L ({_doc.Advisor.WaterPerPersonDay:0} L por persona y día)\n" +
            $"Tanque elevado necesario: {w.TankLitersNeeded:0} L · instalado: {w.TankLitersInstalled:0} L\n" +
            $"Grifo más alto: {w.HighestTapElevation:0.00} m · base del tanque: {(w.TankBaseElevation is { } b ? $"{b:0.00} m" : "sin tanque")} · mínima para {_doc.Advisor.MinPressureHead:0.0} m.c.a.: {w.MinTankBaseElevation:0.00} m";
    }

    private void RefreshBudget()
    {
        _budget.Children.Clear();
        _budget.RowDefinitions.Clear();
        _budget.ColumnDefinitions = new ColumnDefinitions("120,*,90,50,110,110");
        string[] heads = ["Capítulo", "Partida", "Cantidad", "Ud.", $"Precio ({_doc.Advisor.Currency})", "Total"];
        _budget.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < heads.Length; c++) Cell(heads[c], 0, c, bold: true);

        var lines = BuildingAdvisor.Budget(_doc);
        int r = 1;
        foreach (var l in lines)
        {
            _budget.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(l.Group, r, 0);
            Cell(l.Item, r, 1);
            Cell(l.Quantity.ToString(l.Quantity >= 100 ? "0" : "0.##", CultureInfo.CurrentCulture), r, 2, right: true);
            Cell(l.Unit, r, 3);
            var price = new NumericUpDown
            {
                Value = (decimal)l.UnitPrice, Minimum = 0, Maximum = 1_000_000, Increment = 0.1m, FormatString = "0.00",
                Margin = new Thickness(2), MinWidth = 100,
            };
            var key = l.PriceKey;
            price.ValueChanged += (_, e) =>
            {
                if (e.NewValue is not { } v) return;
                _doc.Advisor.UnitPrices[key] = (double)v;
                UpdateBudgetTotals();
            };
            Grid.SetRow(price, r);
            Grid.SetColumn(price, 4);
            _budget.Children.Add(price);
            Cell(l.Total.ToString("N2", CultureInfo.CurrentCulture), r, 5, right: true, tag: key);
            r++;
        }
        UpdateBudgetTotals();
    }

    private void UpdateBudgetTotals()
    {
        var lines = BuildingAdvisor.Budget(_doc);
        foreach (var tb in _budget.Children.OfType<TextBlock>().Where(t => t.Tag is string))
        {
            var key = (string)tb.Tag!;
            var row = Grid.GetRow(tb);
            if (row - 1 < lines.Count && lines[row - 1].PriceKey == key)
                tb.Text = lines[row - 1].Total.ToString("N2", CultureInfo.CurrentCulture);
        }
        _budgetTotal.Text = $"Total orientativo: {lines.Sum(l => l.Total).ToString("N2", CultureInfo.CurrentCulture)} {_doc.Advisor.Currency} " +
                            "(sin mano de obra; precios editables, se guardan con el proyecto)";
    }

    private void Cell(string text, int row, int col, bool bold = false, bool right = false, string? tag = null)
    {
        var tb = new TextBlock
        {
            Text = text, Margin = new Thickness(4, 3), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextAlignment = right ? TextAlignment.Right : TextAlignment.Left, Tag = tag,
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        _budget.Children.Add(tb);
    }

    private string BudgetCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Capítulo;Partida;Cantidad;Unidad;Precio unitario;Total");
        foreach (var l in BuildingAdvisor.Budget(_doc))
            sb.AppendLine(string.Join(";", l.Group, l.Item, l.Quantity.ToString("0.##", CultureInfo.CurrentCulture), l.Unit,
                l.UnitPrice.ToString("0.00", CultureInfo.CurrentCulture), l.Total.ToString("0.00", CultureInfo.CurrentCulture)));
        return sb.ToString();
    }

    private void BuildRules()
    {
        var s = _doc.Advisor;
        _rulesFor = s;
        _rules.Children.Clear();
        void Num(string label, Func<double> get, Action<double> set, double min, double max, double step)
        {
            var n = new NumericUpDown { Value = (decimal)get(), Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, FormatString = "0.##", Width = 140 };
            n.ValueChanged += (_, e) => { if (e.NewValue is { } v) { set((double)v); Refresh(); } };
            _rules.Children.Add(new DockPanel
            {
                Children = { new TextBlock { Text = label, Width = 420, VerticalAlignment = VerticalAlignment.Center }, n },
            });
        }
        // Las reglas leen siempre _doc.Advisor (puede cambiar al abrir otro proyecto).
        Num("Un tomacorriente por cada … m de perímetro", () => _doc.Advisor.OutletEveryMeters, v => _doc.Advisor.OutletEveryMeters = v, 1, 20, 0.5);
        Num("Tomacorrientes mínimos en dormitorio", () => _doc.Advisor.MinOutletsBedroom, v => _doc.Advisor.MinOutletsBedroom = (int)v, 0, 20, 1);
        Num("Tomacorrientes mínimos en cocina", () => _doc.Advisor.MinOutletsKitchen, v => _doc.Advisor.MinOutletsKitchen = (int)v, 0, 20, 1);
        Num("Tomacorrientes mínimos en sala / oficina", () => _doc.Advisor.MinOutletsLiving, v => _doc.Advisor.MinOutletsLiving = (int)v, 0, 20, 1);
        Num("Tomacorrientes mínimos en baño", () => _doc.Advisor.MinOutletsBath, v => _doc.Advisor.MinOutletsBath = (int)v, 0, 10, 1);
        Num("Ventana mínima (% del área del piso)", () => _doc.Advisor.MinWindowRatio * 100, v => _doc.Advisor.MinWindowRatio = v / 100, 0, 50, 0.5);
        Num("Agua por persona y día (L)", () => _doc.Advisor.WaterPerPersonDay, v => _doc.Advisor.WaterPerPersonDay = v, 20, 1000, 10);
        Num("Tanque elevado (% del consumo diario)", () => _doc.Advisor.ElevatedTankFraction * 100, v => _doc.Advisor.ElevatedTankFraction = v / 100, 5, 300, 5);
        Num("Presión mínima en el grifo más alto (m.c.a.)", () => _doc.Advisor.MinPressureHead, v => _doc.Advisor.MinPressureHead = v, 0, 30, 0.5);
        Num("Distancia máxima de evacuación (m)", () => _doc.Advisor.MaxEvacuationDistance, v => _doc.Advisor.MaxEvacuationDistance = v, 5, 200, 1);
        var currency = new TextBox { Text = s.Currency, Width = 140 };
        currency.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(currency.Text)) { _doc.Advisor.Currency = currency.Text.Trim(); Refresh(); } };
        _rules.Children.Add(new DockPanel { Children = { new TextBlock { Text = "Moneda", Width = 420, VerticalAlignment = VerticalAlignment.Center }, currency } });
    }
}
