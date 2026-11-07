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
/// y tabla de puntos con cotas editables, tramos rectos o en arco. Devuelve el perfil nuevo o null si se cancela.
/// </summary>
internal sealed record SketchResult(List<Vec2> Points, List<double> Bulges, List<SketchConstraint> Constraints);

internal sealed class SketchDialog : Window
{
    private const int MaxRows = 300;

    private readonly SketchCanvas _canvas;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly StackPanel _ruleList = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _ok;
    private readonly bool _revolve;

    public SketchDialog(IEnumerable<Vec2> profile, IEnumerable<double>? bulges, bool revolve, IEnumerable<SketchConstraint>? constraints = null)
    {
        _revolve = revolve;
        Title = revolve ? "Boceto del perfil de revolución (X = radio, Y = altura)" : "Boceto del perfil de extrusión";
        Width = 1100;
        Height = 720;
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _canvas = new SketchCanvas { Revolve = revolve };
        _canvas.SetProfile(profile, bulges);
        _canvas.SetConstraints(constraints);
        _canvas.Edited += UpdateStatus;
        _canvas.Committed += RebuildRows;

        _ok = new Button { Content = "Aceptar", IsDefault = true, MinWidth = 90 };
        _ok.Click += (_, _) =>
        {
            int before = _canvas.Points.Count;
            bool reversed = Polygon2D.SignedArea(Sketch2D.Tessellate(_canvas.Points, _canvas.Bulges)) < 0;
            var (pts, bl) = Sketch2D.Normalize(_canvas.Points, _canvas.Bulges);
            // Si al limpiar el perfil se juntaron puntos repetidos, los números de tramo ya no cuadran.
            var rules = pts.Count != before ? new List<SketchConstraint>()
                : reversed ? SketchSolver.AfterReverse(_canvas.Constraints, pts.Count)
                : _canvas.Constraints.ToList();
            Close(new SketchResult(pts, bl, rules));
        };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);

        var side = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        side.Children.Add(new TextBlock
        {
            Text = "Clic: añadir punto · Arrastrar: mover · Clic derecho o Supr: borrar · Arrastrar el cuadradito del centro de un tramo: convertirlo en arco (doble clic: recto) · Rueda: zoom · Botón central o Mayús+arrastrar: desplazar · F: encuadrar",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8,
        });
        side.Children.Add(SnapRow());
        side.Children.Add(ShapesPanel());
        side.Children.Add(FilletRow());
        side.Children.Add(RulesPanel());
        side.Children.Add(new TextBlock { Text = "Puntos y cotas (mm)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        side.Children.Add(new TextBlock { Text = "Tramo: distancia en línea recta al punto siguiente (al cambiarla se mueve el punto siguiente en la misma dirección). Radio: vacío = tramo recto; escribe un radio para hacerlo arco.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7 });
        side.Children.Add(Header());

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var right = new DockPanel { Width = 360 };
        // Todo el lateral se desplaza junto: formas, reglas y tabla de puntos.
        side.Children.Add(_rows);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var statusBox = new Border { Child = _status, Margin = new Thickness(12, 0) };
        DockPanel.SetDock(statusBox, Dock.Bottom);
        right.Children.Add(buttons);
        right.Children.Add(statusBox);
        right.Children.Add(new ScrollViewer { Content = side });

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

        var w = Num(40); var h = Num(20); var cr = Num(0);
        var rect = new Button { Content = "Rectángulo" };
        rect.Click += (_, _) =>
        {
            double radius = TryNumber(cr.Text, out var r) ? Math.Max(0, r) : 0;
            var (pts, bl) = Sketch2D.RoundedRectangle(Value(w, 40), Value(h, 20), radius);
            Replace(pts, bl);
            // Un rectángulo sin redondeos sigue siendo rectángulo al mover sus esquinas.
            if (pts.Count == 4)
            {
                var rules = Enumerable.Range(0, 4).Select(i =>
                    new SketchConstraint(Math.Abs(pts[(i + 1) % 4].Y - pts[i].Y) < 1e-9 ? ConstraintKind.Horizontal : ConstraintKind.Vertical, i));
                _canvas.SetConstraints(rules);
                RebuildRows();
            }
        };
        panel.Children.Add(Line(rect, "ancho", w, "alto", h));
        var crRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(102, 0, 0, 0) };
        crRow.Children.Add(new TextBlock { Text = "radio de esquinas", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        crRow.Children.Add(cr);
        panel.Children.Add(crRow);

        var rad = Num(15); var sides = Num(6);
        var poly = new Button { Content = "Polígono" };
        poly.Click += (_, _) => Replace(Sketch2D.RegularPolygon(Center(Value(rad, 15)), Value(rad, 15), (int)Math.Clamp(Value(sides, 6), 3, 64)));
        var circle = new Button { Content = "Círculo" };
        circle.Click += (_, _) =>
        {
            var (pts, bl) = Sketch2D.CircleArcs(Center(Value(rad, 15)), Value(rad, 15));
            Replace(pts, bl);
        };
        panel.Children.Add(Line(poly, "radio", rad, "lados", sides));
        panel.Children.Add(circle);
        return panel;
    }

    private Control FilletRow()
    {
        var r = Num(3);
        var b = new Button { Content = "Redondear esquina", MinWidth = 96 };
        b.Click += (_, _) =>
        {
            if (_canvas.Selected < 0) { ShowProblem("Primero haz clic en el punto de la esquina que quieres redondear."); return; }
            int corner = _canvas.Selected, before = _canvas.Points.Count;
            var problem = Sketch2D.FilletCorner(_canvas.Points, _canvas.Bulges, corner, Value(r, 3));
            if (problem is not null) { ShowProblem(problem); return; }
            _canvas.SetConstraints(_canvas.Points.Count == before + 1 ? SketchSolver.AfterFillet(_canvas.Constraints, corner, before) : null);
            _canvas.Select(-1);
            RebuildRows();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(b);
        row.Children.Add(new TextBlock { Text = "radio", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(r);
        ToolTip.SetTip(b, "Redondea la esquina del punto seleccionado (haz clic antes en el punto).");
        return row;
    }

    private Control RulesPanel()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "Reglas", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "Elige tramos con un clic en su cuadradito central (dos para paralelo, perpendicular e iguales; Esc los suelta). Para fijar un punto, haz clic en él.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7,
        });
        var wrap = new WrapPanel();
        void Btn(string text, string tip, Action action)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 2) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => action();
            wrap.Children.Add(b);
        }
        Btn("Horizontal", "El tramo elegido queda horizontal.", () => OneSegment(ConstraintKind.Horizontal));
        Btn("Vertical", "El tramo elegido queda vertical.", () => OneSegment(ConstraintKind.Vertical));
        Btn("Paralelo", "Los dos tramos elegidos quedan paralelos.", () => TwoSegments(ConstraintKind.Paralelo));
        Btn("Perpendicular", "Los dos tramos elegidos forman 90°.", () => TwoSegments(ConstraintKind.Perpendicular));
        Btn("Iguales", "Los dos tramos elegidos miden lo mismo.", () => TwoSegments(ConstraintKind.IgualLongitud));
        Btn("Fijar cota", "La longitud actual del tramo elegido no cambia al mover otros puntos.", () => OneSegment(ConstraintKind.Longitud));
        Btn("Fijar punto", "El punto seleccionado no se mueve.", FixPoint);
        panel.Children.Add(wrap);
        panel.Children.Add(_ruleList);
        return panel;
    }

    private void OneSegment(ConstraintKind kind)
    {
        if (_canvas.SelectedSegments.Count == 0) { ShowProblem("Primero haz clic en el cuadradito central del tramo."); return; }
        int seg = _canvas.SelectedSegments[^1];
        double value = kind == ConstraintKind.Longitud ? Sketch2D.SegmentLength(_canvas.Points, seg) : 0;
        if (kind == ConstraintKind.Longitud && value < 1e-6) { ShowProblem("El tramo mide 0."); return; }
        AddRule(new SketchConstraint(kind, seg, Value: value));
    }

    private void TwoSegments(ConstraintKind kind)
    {
        if (_canvas.SelectedSegments.Count < 2) { ShowProblem("Elige dos tramos: clic en el cuadradito central de cada uno."); return; }
        AddRule(new SketchConstraint(kind, _canvas.SelectedSegments[0], _canvas.SelectedSegments[1]));
    }

    private void FixPoint()
    {
        if (_canvas.Selected < 0) { ShowProblem("Primero haz clic en el punto que quieres fijar."); return; }
        AddRule(new SketchConstraint(ConstraintKind.Fijo, _canvas.Selected));
    }

    private void AddRule(SketchConstraint rule)
    {
        var rules = _canvas.Constraints;
        if (rules.Any(r => SameRule(r, rule))) { ShowProblem("Esa regla ya está puesta."); return; }
        var saved = _canvas.Points.ToList();
        rules.Add(rule);
        if (_canvas.ApplyRules() > 1e-3)
        {
            rules.RemoveAt(rules.Count - 1);
            _canvas.Points.Clear();
            _canvas.Points.AddRange(saved);
            _canvas.ApplyRules();
            RebuildRows();
            ShowProblem($"No se puede: «{rule.Describe()}» choca con las otras reglas o con los puntos fijos.");
            return;
        }
        _canvas.ClearSegmentSelection();
        RebuildRows();
    }

    private static bool SameRule(SketchConstraint a, SketchConstraint b) =>
        a.Kind == b.Kind && (a.A == b.A && a.B == b.B || a.TwoSegments && a.A == b.B && a.B == b.A);

    private void FillRuleList()
    {
        _ruleList.Children.Clear();
        var rules = _canvas.Constraints;
        for (int k = 0; k < rules.Count; k++)
        {
            var rule = rules[k];
            bool ok = SketchSolver.Error(_canvas.Points, rule) < 1e-3;
            var row = new DockPanel();
            var del = new Button { Content = "✕", Padding = new Thickness(6, 0), FontSize = 11 };
            ToolTip.SetTip(del, "Quitar esta regla");
            del.Click += (_, _) => { rules.Remove(rule); _canvas.ApplyRules(); RebuildRows(); };
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            var label = new TextBlock { Text = $"{k + 1}. {rule.Describe()}", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            if (!ok) label.Foreground = Brushes.OrangeRed;
            row.Children.Add(label);
            _ruleList.Children.Add(row);
        }
    }

    private void ShowProblem(string text)
    {
        _status.Text = text;
        _status.Foreground = Brushes.OrangeRed;
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

    private void Replace(List<Vec2> pts, List<double>? bulges = null)
    {
        _canvas.SetProfile(pts, bulges);
        _canvas.SetConstraints(null);
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
        Cell(g, 4, new TextBlock { Text = "Radio", Opacity = 0.7 });
        return g;
    }

    private static Grid RowGrid() => new() { ColumnDefinitions = new ColumnDefinitions("28,*,*,*,*") };

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
        try { FillRows(); FillRuleList(); }
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
            Cell(g, 1, Field(pts[i].X, v => { pts[idx] = pts[idx] with { X = v }; _canvas.ApplyRules(idx); return true; }, rebuild: _canvas.Constraints.Count > 0));
            Cell(g, 2, Field(pts[i].Y, v => { pts[idx] = pts[idx] with { Y = v }; _canvas.ApplyRules(idx); return true; }, rebuild: _canvas.Constraints.Count > 0));
            Cell(g, 3, Field(Sketch2D.SegmentLength(pts, i), v => SetLength(idx, v), rebuild: true));
            Cell(g, 4, RadiusField(idx));
            g.PointerPressed += (_, _) => _canvas.Select(idx);
            _rows.Children.Add(g);
        }
        if (pts.Count > MaxRows)
            _rows.Children.Add(new TextBlock { Text = $"… y {pts.Count - MaxRows} puntos más (edítalos en el dibujo).", FontSize = 12, Opacity = 0.7 });
    }

    /// <summary>Cambia la longitud del tramo; si tenía la cota fija, la cota pasa a ser la nueva.</summary>
    private bool SetLength(int seg, double value)
    {
        var rules = _canvas.Constraints;
        int k = rules.FindIndex(r => r.Kind == ConstraintKind.Longitud && r.A == seg);
        if (k >= 0)
        {
            if (value <= 0) return false;
            rules[k] = rules[k] with { Value = value };
            _canvas.ApplyRules();
            return true;
        }
        if (!Sketch2D.SetSegmentLength(_canvas.Points, seg, value)) return false;
        _canvas.ApplyRules((seg + 1) % _canvas.Points.Count);
        return true;
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

    private TextBox RadiusField(int i)
    {
        var pts = _canvas.Points;
        var bl = _canvas.Bulges;
        Vec2 A() => pts[i];
        Vec2 B() => pts[(i + 1) % pts.Count];
        string Current() => Sketch2D.ArcGeometry(A(), B(), Sketch2D.Bulge(bl, i)) is { } g ? SketchCanvas.Fmt(g.Radius) : "";
        var box = new TextBox { Text = Current(), MinWidth = 0, Padding = new Thickness(4, 2), Watermark = "recto" };
        void Commit()
        {
            if (_rebuilding || box.Parent is null || i >= bl.Count) return;
            var text = (box.Text ?? "").Trim();
            if (text == Current()) return;
            if (text.Length == 0 || (TryNumber(text, out var z) && z == 0)) bl[i] = 0;
            else if (TryNumber(text, out var r) && Sketch2D.BulgeFromRadius(A(), B(), r, bl[i] == 0 ? 1 : bl[i]) is { } bulge) bl[i] = bulge;
            else
            {
                ShowProblem($"El radio del tramo {i + 1} tiene que ser al menos la mitad del tramo ({SketchCanvas.Fmt(Sketch2D.SegmentLength(pts, i) / 2)} mm).");
                box.Text = Current();
                return;
            }
            _canvas.InvalidateVisual();
            RebuildRows();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return box;
    }

    private void UpdateStatus()
    {
        var pts = _canvas.Points;
        var bl = _canvas.Bulges;
        var problem = Sketch2D.Validate(pts, bl, _revolve);
        _ok.IsEnabled = problem is null;
        if (problem is not null)
        {
            _status.Text = problem;
            _status.Foreground = Brushes.OrangeRed;
            return;
        }
        var outline = Sketch2D.Tessellate(pts, bl);
        double area = Math.Abs(Polygon2D.SignedArea(outline));
        double perimeter = Enumerable.Range(0, pts.Count).Sum(i => Sketch2D.PathLength(pts, bl, i));
        double w = outline.Max(p => p.X) - outline.Min(p => p.X), h = outline.Max(p => p.Y) - outline.Min(p => p.Y);
        int arcs = bl.Count(b => Math.Abs(b) > 1e-9);
        if (_canvas.Constraints.Count > 0 && _canvas.RuleError > 1e-3)
        {
            _status.Text = "Hay reglas que no se cumplen (en rojo): quita alguna o mueve los puntos.";
            _status.Foreground = Brushes.OrangeRed;
            return;
        }
        _status.Text = $"{pts.Count} puntos · {arcs} arco(s) · {SketchCanvas.Fmt(w)} × {SketchCanvas.Fmt(h)} mm · área {SketchCanvas.Fmt(area)} mm² · perímetro {SketchCanvas.Fmt(perimeter)} mm";
        _status.Foreground = Brushes.Gray;
    }
}
