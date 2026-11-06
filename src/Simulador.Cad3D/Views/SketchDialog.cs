using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Cad3D.Geometry;

namespace Simulador.Cad3D.Views;

/// <summary>
/// Editor de bocetos 2D para el perfil de una extrusión o revolución: dibujo con el ratón sobre rejilla
/// y tabla de puntos con cotas editables. Devuelve el perfil nuevo o null si se cancela.
/// </summary>
internal sealed class SketchDialog : Window
{
    private const int MaxRows = 300;

    private readonly SketchCanvas _canvas;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _ok;
    private readonly bool _revolve;

    public SketchDialog(IEnumerable<Vec2> profile, bool revolve)
    {
        _revolve = revolve;
        Title = revolve ? "Boceto del perfil de revolución (X = radio, Y = altura)" : "Boceto del perfil de extrusión";
        Width = 1100;
        Height = 720;
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _canvas = new SketchCanvas { Revolve = revolve };
        _canvas.Points.AddRange(profile);
        _canvas.Edited += UpdateStatus;
        _canvas.Committed += RebuildRows;

        _ok = new Button { Content = "Aceptar", IsDefault = true, MinWidth = 90 };
        _ok.Click += (_, _) => Close(Polygon2D.Normalize(_canvas.Points));
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);

        var side = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        side.Children.Add(new TextBlock
        {
            Text = "Clic: añadir punto · Arrastrar: mover · Clic derecho o Supr: borrar · Rueda: zoom · Botón central o Mayús+arrastrar: desplazar · F: encuadrar",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8,
        });
        side.Children.Add(SnapRow());
        side.Children.Add(ShapesPanel());
        side.Children.Add(new TextBlock { Text = "Puntos y cotas (mm)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        side.Children.Add(new TextBlock { Text = "Tramo: distancia al punto siguiente. Al cambiarla se mueve el punto siguiente en la misma dirección.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7 });
        side.Children.Add(Header());

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var right = new DockPanel { Width = 360 };
        DockPanel.SetDock(side, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var statusBox = new Border { Child = _status, Margin = new Thickness(12, 0) };
        DockPanel.SetDock(statusBox, Dock.Bottom);
        right.Children.Add(side);
        right.Children.Add(buttons);
        right.Children.Add(statusBox);
        right.Children.Add(new ScrollViewer { Content = _rows, Margin = new Thickness(12, 0) });

        var root = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(new Border { Child = _canvas, Margin = new Thickness(8) });
        Content = root;

        RebuildRows();
    }

    private Control SnapRow()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Sin ajuste", "0.1 mm", "0.5 mm", "1 mm", "2 mm", "5 mm", "10 mm" }, SelectedIndex = 3, Width = 130 };
        double[] values = { 0, 0.1, 0.5, 1, 2, 5, 10 };
        combo.SelectionChanged += (_, _) => _canvas.Snap = values[Math.Max(0, combo.SelectedIndex)];
        var fit = new Button { Content = "Encuadrar" };
        fit.Click += (_, _) => _canvas.FitView();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = "Ajustar a rejilla", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(combo);
        row.Children.Add(fit);
        return row;
    }

    private Control ShapesPanel()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "Formas (reemplazan el boceto)", FontWeight = FontWeight.SemiBold });

        var w = Num(40); var h = Num(20);
        var rect = new Button { Content = "Rectángulo" };
        rect.Click += (_, _) => Replace(Sketch2D.Rectangle(Value(w, 40), Value(h, 20)));
        panel.Children.Add(Line(rect, "ancho", w, "alto", h));

        var rad = Num(15); var sides = Num(6);
        var poly = new Button { Content = "Polígono" };
        poly.Click += (_, _) => Replace(Sketch2D.RegularPolygon(Center(Value(rad, 15)), Value(rad, 15), (int)Math.Clamp(Value(sides, 6), 3, 64)));
        var circle = new Button { Content = "Círculo" };
        circle.Click += (_, _) => Replace(Sketch2D.RegularPolygon(Center(Value(rad, 15)), Value(rad, 15), 48));
        panel.Children.Add(Line(poly, "radio", rad, "lados", sides));
        panel.Children.Add(circle);
        return panel;
    }

    // En una revolución las formas se colocan a la derecha del eje para que el radio no sea negativo.
    private Vec2 Center(double radius) => _revolve ? new Vec2(radius + 5, radius) : new Vec2(0, 0);

    private static Control Line(Button b, string l1, TextBox t1, string l2, TextBox t2)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        b.MinWidth = 96;
        row.Children.Add(b);
        row.Children.Add(new TextBlock { Text = l1, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(t1);
        row.Children.Add(new TextBlock { Text = l2, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(t2);
        return row;
    }

    private static TextBox Num(double v) => new() { Text = SketchCanvas.Fmt(v), Width = 64 };

    private static double Value(TextBox t, double fallback) =>
        TryNumber(t.Text, out var v) && v > 0 ? v : fallback;

    private static bool TryNumber(string? s, out double v) =>
        double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void Replace(List<Vec2> pts)
    {
        _canvas.Points.Clear();
        _canvas.Points.AddRange(pts);
        _canvas.Select(-1);
        _canvas.FitView();
        RebuildRows();
    }

    private static Grid Header()
    {
        var g = RowGrid();
        Cell(g, 0, new TextBlock { Text = "#", Opacity = 0.7 });
        Cell(g, 1, new TextBlock { Text = "X", Opacity = 0.7 });
        Cell(g, 2, new TextBlock { Text = "Y", Opacity = 0.7 });
        Cell(g, 3, new TextBlock { Text = "Tramo", Opacity = 0.7 });
        return g;
    }

    private static Grid RowGrid() => new() { ColumnDefinitions = new ColumnDefinitions("32,*,*,*") };

    private static void Cell(Grid g, int col, Control c)
    {
        Grid.SetColumn(c, col);
        c.Margin = new Thickness(0, 0, 4, 0);
        if (c is TextBlock tb) tb.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(c);
    }

    private bool _rebuilding;

    private void RebuildRows()
    {
        if (_rebuilding) return;
        _rebuilding = true;
        try { FillRows(); }
        finally { _rebuilding = false; }
        UpdateStatus();
    }

    private void FillRows()
    {
        _rows.Children.Clear();
        var pts = _canvas.Points;
        for (int i = 0; i < Math.Min(pts.Count, MaxRows); i++)
        {
            int idx = i;
            var g = RowGrid();
            Cell(g, 0, new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture) });
            Cell(g, 1, Field(pts[i].X, v => { pts[idx] = pts[idx] with { X = v }; return true; }));
            Cell(g, 2, Field(pts[i].Y, v => { pts[idx] = pts[idx] with { Y = v }; return true; }));
            Cell(g, 3, Field(Sketch2D.SegmentLength(pts, i), v => Sketch2D.SetSegmentLength(pts, idx, v), rebuild: true));
            g.PointerPressed += (_, _) => _canvas.Select(idx);
            _rows.Children.Add(g);
        }
        if (pts.Count > MaxRows)
            _rows.Children.Add(new TextBlock { Text = $"… y {pts.Count - MaxRows} puntos más (edítalos en el dibujo).", FontSize = 12, Opacity = 0.7 });
    }

    private TextBox Field(double value, Func<double, bool> apply, bool rebuild = false)
    {
        var box = new TextBox { Text = SketchCanvas.Fmt(value), MinWidth = 0, Padding = new Thickness(4, 2) };
        void Commit()
        {
            // Al regenerar la tabla los cuadros pierden el foco: no hay que volver a aplicar nada.
            if (_rebuilding || box.Parent is null) return;
            if (TryNumber(box.Text, out var same) && Math.Abs(same - value) < 1e-9) return;
            if (TryNumber(box.Text, out var v) && apply(v))
            {
                _canvas.InvalidateVisual();
                if (rebuild) RebuildRows(); else UpdateStatus();
            }
            else box.Text = SketchCanvas.Fmt(value);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return box;
    }

    private void UpdateStatus()
    {
        var pts = _canvas.Points;
        var problem = Sketch2D.Validate(pts, _revolve);
        _ok.IsEnabled = problem is null;
        if (problem is not null)
        {
            _status.Text = problem;
            _status.Foreground = Brushes.OrangeRed;
            return;
        }
        double area = Math.Abs(Polygon2D.SignedArea(pts));
        double perimeter = Enumerable.Range(0, pts.Count).Sum(i => Sketch2D.SegmentLength(pts, i));
        double w = pts.Max(p => p.X) - pts.Min(p => p.X), h = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        _status.Text = $"{pts.Count} puntos · {SketchCanvas.Fmt(w)} × {SketchCanvas.Fmt(h)} mm · área {SketchCanvas.Fmt(area)} mm² · perímetro {SketchCanvas.Fmt(perimeter)} mm";
        _status.Foreground = Brushes.Gray;
    }
}
