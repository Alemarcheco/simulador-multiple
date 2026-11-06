using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Civil.Model;
using Simulador.Core.Building;
using Simulador.Core.Modules;

namespace Simulador.Civil.Views;

/// <summary>Vista raíz de la pestaña Edificación.</summary>
public sealed class EdificacionView : DockPanel
{
    private readonly IModuleHost _host;
    private readonly BuildingDocument _doc;
    private readonly PlanCanvas _plan;
    private readonly Building3DView _view3d;
    private readonly ComboBox _levelBox = new() { MinWidth = 160 };
    private readonly StackPanel _props = new() { Spacing = 6 };
    private readonly StackPanel _rooms = new() { Spacing = 2 };
    private readonly StackPanel _checks = new() { Spacing = 4 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<ToggleButton> _toolButtons = [];
    private string? _filePath;
    private bool _syncingLevels;

    public EdificacionView(IModuleHost host, BuildingDocument doc)
    {
        _host = host;
        _doc = doc;
        _plan = new PlanCanvas(doc);
        _view3d = new Building3DView(doc);

        _plan.Status += (_, s) => _host.SetStatus(s);
        _plan.SelectionChanged += (_, _) => RefreshProps();
        _plan.AskSpaceName = AskSpaceNameAsync;
        _doc.Changed += (_, _) => RefreshAll();

        var toolbar = BuildToolbar();
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);

        var palette = new ScrollViewer { Content = BuildPalette(), Width = 210 };
        SetDock(palette, Dock.Left);
        Children.Add(palette);

        var side = new ScrollViewer { Content = BuildSidePanel(), Width = 300 };
        SetDock(side, Dock.Right);
        Children.Add(side);

        var view3dPanel = new DockPanel();
        var v3bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6) };
        var roof = new CheckBox { Content = "Mostrar cubierta", IsChecked = true };
        roof.IsCheckedChanged += (_, _) => { _view3d.ShowRoof = roof.IsChecked == true; _view3d.InvalidateVisual(); };
        var upTo = new CheckBox { Content = "Solo hasta el nivel actual" };
        upTo.IsCheckedChanged += (_, _) => { _view3d.UpToLevelId = upTo.IsChecked == true ? _plan.LevelId : null; _view3d.InvalidateVisual(); };
        _levelBox.SelectionChanged += (_, _) => { if (upTo.IsChecked == true) { _view3d.UpToLevelId = _plan.LevelId; _view3d.InvalidateVisual(); } };
        var reset = new Button { Content = "Vista inicial" };
        reset.Click += (_, _) => _view3d.ResetView();
        v3bar.Children.Add(roof);
        v3bar.Children.Add(upTo);
        v3bar.Children.Add(reset);
        v3bar.Children.Add(new TextBlock { Text = "Arrastra para girar · botón derecho para mover · rueda para zoom", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        SetDock(v3bar, Dock.Top);
        view3dPanel.Children.Add(v3bar);
        view3dPanel.Children.Add(_view3d);

        var center = new TabControl();
        center.Items.Add(new TabItem { Header = "Planta 2D", Content = _plan });
        center.Items.Add(new TabItem { Header = "Vista 3D", Content = view3dPanel });
        center.Items.Add(new TabItem
        {
            Header = "Asistente",
            Content = new AdvisorView(doc, id => { center.SelectedIndex = 0; GoTo(id); }),
        });
        Children.Add(center);

        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(_plan.ZoomToFit, Avalonia.Threading.DispatcherPriority.Background);
        KeyDown += OnKeyDown;
        RefreshAll();
        SetTool(PlanTool.Select);
    }

    // ------------------------------------------------------------ barra superior

    private Control BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(6), Orientation = Orientation.Horizontal };

        var templates = new Button { Content = "Plantillas ▾" };
        var menu = new MenuFlyout();
        foreach (var (name, create) in BuildingTemplates.All)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => { _doc.LoadFrom(create()); _filePath = null; AfterLoad(); };
            menu.Items.Add(item);
        }
        templates.Flyout = menu;

        bar.Children.Add(Btn("Nuevo", () => { _doc.LoadFrom(new BuildingDocument()); _filePath = null; AfterLoad(); }));
        bar.Children.Add(templates);
        bar.Children.Add(Btn("Abrir…", () => _ = OpenAsync()));
        bar.Children.Add(Btn("Guardar", () => _ = SaveAsync(false)));
        bar.Children.Add(Btn("Guardar como…", () => _ = SaveAsync(true)));
        bar.Children.Add(Sep());
        bar.Children.Add(Btn("↶ Deshacer", _doc.Undo));
        bar.Children.Add(Btn("↷ Rehacer", _doc.Redo));
        bar.Children.Add(Sep());
        bar.Children.Add(new TextBlock { Text = "Nivel:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        _levelBox.SelectionChanged += (_, _) =>
        {
            if (_syncingLevels || _levelBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            _plan.LevelId = id;
            _plan.CancelCurrent();
            _plan.Select(null);
            _plan.InvalidateVisual();
        };
        bar.Children.Add(_levelBox);
        bar.Children.Add(Btn("+ Nivel vacío", () =>
        {
            var top = _doc.Levels.MaxBy(l => l.Elevation)!;
            Level? added = null;
            _doc.Edit(e => added = e.AddLevel($"Planta {_doc.Levels.Count}", top.Elevation + top.Height, top.Height));
            SelectLevel(added!.Id);
        }));
        bar.Children.Add(Btn("Copiar planta encima", () =>
        {
            Level? added = null;
            _doc.Edit(e => added = e.DuplicateLevel(_plan.LevelId));
            SelectLevel(added!.Id);
        }));
        bar.Children.Add(Btn("Encuadrar", _plan.ZoomToFit));
        return bar;
    }

    private static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Margin = new Thickness(2) };
        b.Click += (_, _) => click();
        return b;
    }

    private static Control Sep() => new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(6, 4), Opacity = 0.4 };

    // ------------------------------------------------------------ paleta de herramientas

    private Control BuildPalette()
    {
        var p = new StackPanel { Margin = new Thickness(8), Spacing = 4 };
        p.Children.Add(Header("Dibujar"));
        p.Children.Add(ToolButton("↖ Seleccionar", PlanTool.Select));
        p.Children.Add(ToolButton("▭ Habitación rectangular", PlanTool.Room));
        p.Children.Add(ToolButton("╱ Muro", PlanTool.Wall));
        p.Children.Add(ToolButton("🚪 Puerta", PlanTool.Door));
        p.Children.Add(ToolButton("🪟 Ventana", PlanTool.Window));
        p.Children.Add(ToolButton("▤ Escalera", PlanTool.Stair));
        p.Children.Add(ToolButton("🏷 Nombrar habitación", PlanTool.Rename));

        p.Children.Add(Header("Medidas al dibujar"));
        p.Children.Add(Num("Espesor de muro (m)", 0.15, 0.05, 1, v => _plan.WallThickness = v));
        p.Children.Add(Num("Altura de muro (m)", 2.8, 1, 15, v => _plan.WallHeight = v));
        p.Children.Add(Num("Ancho de puerta (m)", 0.8, 0.5, 6, v => _plan.DoorWidth = v));
        p.Children.Add(Num("Ancho de ventana (m)", 1.2, 0.3, 6, v => _plan.WindowWidth = v));
        p.Children.Add(Num("Ancho de escalera (m)", 1.0, 0.6, 3, v => _plan.StairWidth = v));
        p.Children.Add(Choice("Forma de escalera", StairForms.Select(f => f.Label).ToArray(), 0, i =>
        {
            (_, _plan.StairShape, _plan.StairTurnLeft) = StairForms[i];
            SetTool(PlanTool.Stair);
        }));

        p.Children.Add(Header("Instalaciones"));
        var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var t in FixtureCatalog.All) kind.Items.Add(new ComboBoxItem { Content = $"{t.Icon} {t.Name}", Tag = t });
        kind.SelectedIndex = 0;
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is ComboBoxItem { Tag: FixtureType t }) { _plan.FixtureType = t; SetTool(PlanTool.Fixture); }
        };
        p.Children.Add(kind);
        p.Children.Add(ToolButton("＋ Colocar elemento", PlanTool.Fixture));
        p.Children.Add(ToolButton("⤳ Trazar ruta (tubo/tubería)", PlanTool.Route));
        p.Children.Add(new TextBlock
        {
            Text = "Los elementos quedan enlazados con el esquema del módulo eléctrico o hidráulico por su designación (X1, PA1…).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11,
        });
        return p;
    }

    private ToggleButton ToolButton(string text, PlanTool tool)
    {
        var b = new ToggleButton { Content = text, Tag = tool, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => SetTool(tool);
        _toolButtons.Add(b);
        return b;
    }

    private void SetTool(PlanTool tool)
    {
        _plan.Tool = tool;
        _plan.CancelCurrent();
        foreach (var b in _toolButtons) b.IsChecked = (PlanTool)b.Tag! == tool;
        _plan.Focus();
    }

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2) };

    private static Control Num(string label, double value, double min, double max, Action<double> set, double step = 0.05)
    {
        var n = new NumericUpDown
        {
            Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step,
            FormatString = "0.00", HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        n.ValueChanged += (_, e) => { if (e.NewValue is { } v) set((double)v); };
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 }, n } };
    }

    private static Control Choice(string label, IReadOnlyList<string> options, int selected, Action<int> set)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = options, SelectedIndex = selected };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) set(c.SelectedIndex); };
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 }, c } };
    }

    /// <summary>Formas de escalera que se ofrecen, con el sentido de giro del segundo tramo.</summary>
    private static readonly (string Label, StairShape Shape, bool TurnLeft)[] StairForms =
    [
        ("Recta", StairShape.Straight, true),
        ("En L, gira a la izquierda", StairShape.L, true),
        ("En L, gira a la derecha", StairShape.L, false),
        ("En U, vuelve por la izquierda", StairShape.U, true),
        ("En U, vuelve por la derecha", StairShape.U, false),
    ];

    private static int StairFormIndex(StairShape shape, bool turnLeft) =>
        Math.Max(0, Array.FindIndex(StairForms, f => f.Shape == shape && (shape == StairShape.Straight || f.TurnLeft == turnLeft)));

    // ------------------------------------------------------------ panel derecho

    private Control BuildSidePanel()
    {
        var p = new StackPanel { Margin = new Thickness(8), Spacing = 4 };
        p.Children.Add(Header("Selección"));
        p.Children.Add(_props);
        p.Children.Add(Header("Habitaciones"));
        p.Children.Add(_rooms);
        p.Children.Add(Header("Comprobaciones"));
        p.Children.Add(_checks);
        p.Children.Add(Header("Resumen"));
        p.Children.Add(_summary);
        return p;
    }

    private void RefreshAll()
    {
        RefreshLevels();
        RefreshProps();
        RefreshRooms();
        RefreshChecks();
        RefreshSummary();
        _plan.InvalidateVisual();
        _view3d.InvalidateVisual();
    }

    private void RefreshLevels()
    {
        _syncingLevels = true;
        _levelBox.Items.Clear();
        foreach (var l in _doc.Levels.OrderBy(l => l.Elevation))
            _levelBox.Items.Add(new ComboBoxItem { Content = $"{l.Name} (+{l.Elevation:0.00} m)", Tag = l.Id });
        if (_doc.Levels.All(l => l.Id != _plan.LevelId)) _plan.LevelId = _doc.Levels.OrderBy(l => l.Elevation).First().Id;
        _levelBox.SelectedItem = _levelBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag! == _plan.LevelId);
        _syncingLevels = false;
    }

    private void SelectLevel(string id)
    {
        _plan.LevelId = id;
        RefreshLevels();
        _plan.Select(null);
        _plan.InvalidateVisual();
    }

    private void AfterLoad()
    {
        _plan.LevelId = _doc.Levels.OrderBy(l => l.Elevation).First().Id;
        RefreshAll();
        _plan.Select(null);
        _plan.ZoomToFit();
        _view3d.ResetView();
    }

    private void RefreshRooms()
    {
        _rooms.Children.Clear();
        var list = _doc.Spaces.Where(s => s.LevelId == _plan.LevelId).OrderBy(s => s.Name, StringComparer.CurrentCulture).ToList();
        if (list.Count == 0)
            _rooms.Children.Add(new TextBlock { Text = "Cierra muros para formar habitaciones.", Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        foreach (var s in list)
        {
            var b = new Button
            {
                Content = $"{s.Name}  ·  {s.Area.ToString("0.0", CultureInfo.CurrentCulture)} m²",
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, Padding = new Thickness(4, 2),
            };
            b.Click += (_, _) => _plan.Select(s.Id);
            _rooms.Children.Add(b);
        }
    }

    private void RefreshChecks()
    {
        _checks.Children.Clear();
        var results = BuildingChecks.Run(_doc);
        if (results.Count == 0)
        {
            _checks.Children.Add(new TextBlock { Text = "✔ Sin avisos.", Foreground = Brushes.SeaGreen });
            return;
        }
        foreach (var r in results.OrderByDescending(r => r.Severity).Take(40))
        {
            var b = new Button
            {
                Content = new TextBlock { Text = (r.Severity == CheckSeverity.Error ? "⛔ " : "⚠ ") + r.Message, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, Padding = new Thickness(4, 2),
                Foreground = r.Severity == CheckSeverity.Error ? Brushes.Firebrick : Brushes.DarkGoldenrod,
            };
            b.Click += (_, _) => GoTo(r.ElementId);
            _checks.Children.Add(b);
        }
    }

    private void GoTo(string elementId)
    {
        string? level =
            _doc.Walls.FirstOrDefault(w => w.Id == elementId)?.LevelId ??
            _doc.Openings.Where(o => o.Id == elementId).Select(o => _doc.Walls.First(w => w.Id == o.HostWallId).LevelId).FirstOrDefault() ??
            _doc.Fixtures.FirstOrDefault(f => f.Id == elementId)?.LevelId ??
            _doc.Stairs.FirstOrDefault(s => s.Id == elementId)?.LevelId ??
            _doc.Spaces.FirstOrDefault(s => s.Id == elementId)?.LevelId;
        if (level is not null && level != _plan.LevelId) SelectLevel(level);
        _plan.Select(elementId);
    }

    private void RefreshSummary()
    {
        double built = _doc.Spaces.Sum(s => s.Area);
        double wallLen = _doc.Walls.Sum(w => w.Length);
        double wallArea = _doc.Walls.Sum(w => w.Length * w.Height) - _doc.Openings.Sum(o => o.Width * o.Height);
        int doors = _doc.Openings.Count(o => o.Kind == OpeningKind.Door);
        int windows = _doc.Openings.Count - doors;
        double elec = _doc.Routes.Where(r => r.ModuleId == "electrico").Sum(r => r.Length);
        double hydro = _doc.Routes.Where(r => r.ModuleId == "hidraulica").Sum(r => r.Length);
        _summary.Text =
            $"{_doc.Name} · {_doc.Levels.Count} nivel(es)\n" +
            $"Superficie de habitaciones (a ejes): {built:0.0} m²\n" +
            $"Muros: {wallLen:0.0} m lineales, {wallArea:0.0} m² de paño (sin huecos)\n" +
            $"Puertas: {doors} · Ventanas: {windows} · Escaleras: {_doc.Stairs.Count}\n" +
            $"Elementos de instalación: {_doc.Fixtures.Count}\n" +
            $"Canalización eléctrica: {elec:0.0} m · Tubería: {hydro:0.0} m";
    }

    // ------------------------------------------------------------ propiedades de la selección

    private void RefreshProps()
    {
        _props.Children.Clear();
        var id = _plan.SelectedId;
        if (id is null)
        {
            _props.Children.Add(new TextBlock { Text = "Nada seleccionado.", Opacity = 0.6 });
            AddLevelProps();
            return;
        }

        if (_doc.Walls.FirstOrDefault(w => w.Id == id) is { } wall)
        {
            Title($"Muro {wall.Id} · {wall.Length:0.00} m");
            _props.Children.Add(Num("Espesor (m)", wall.Thickness, 0.05, 1, v => Apply(() => _doc.Walls.First(w => w.Id == id), (e, w) => e.Replace(w, w with { Thickness = v }))));
            _props.Children.Add(Num("Altura (m)", wall.Height, 0.5, 20, v => Apply(() => _doc.Walls.First(w => w.Id == id), (e, w) => e.Replace(w, w with { Height = v }))));
            var mat = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var m in new[] { "bloque", "ladrillo", "hormigón", "drywall", "madera", "chapa" }) mat.Items.Add(m);
            mat.SelectedItem = wall.Material;
            mat.SelectionChanged += (_, _) => { if (mat.SelectedItem is string m && m != _doc.Walls.First(w => w.Id == id).Material) Apply(() => _doc.Walls.First(w => w.Id == id), (e, w) => e.Replace(w, w with { Material = m })); };
            _props.Children.Add(mat);
        }
        else if (_doc.Openings.FirstOrDefault(o => o.Id == id) is { } op)
        {
            Title(op.Kind == OpeningKind.Door ? $"Puerta {op.Id}" : $"Ventana {op.Id}");
            _props.Children.Add(Num("Posición desde el inicio del muro (m)", op.Offset, 0, 100, v => Apply(() => _doc.Openings.First(o => o.Id == id), (e, o) => e.Replace(o, o with { Offset = v }))));
            _props.Children.Add(Num("Ancho (m)", op.Width, 0.3, 10, v => Apply(() => _doc.Openings.First(o => o.Id == id), (e, o) => e.Replace(o, o with { Width = v }))));
            _props.Children.Add(Num("Alto (m)", op.Height, 0.3, 10, v => Apply(() => _doc.Openings.First(o => o.Id == id), (e, o) => e.Replace(o, o with { Height = v }))));
            if (op.Kind == OpeningKind.Window)
                _props.Children.Add(Num("Antepecho (m)", op.SillHeight, 0, 5, v => Apply(() => _doc.Openings.First(o => o.Id == id), (e, o) => e.Replace(o, o with { SillHeight = v }))));
            else
            {
                _props.Children.Add(Btn("Cambiar lado de apertura", () => Apply(() => _doc.Openings.First(o => o.Id == id), (e, o) => e.Replace(o, o with { SwingLeft = !o.SwingLeft }))));
                _props.Children.Add(Btn("Cambiar lado de bisagras", () => Apply(() => _doc.Openings.First(o => o.Id == id), (e, o) => e.Replace(o, o with { HingeAtStart = !o.HingeAtStart }))));
            }
        }
        else if (_doc.Stairs.FirstOrDefault(s => s.Id == id) is { } st)
        {
            var d = st.Design;
            Title($"Escalera {st.Id}");
            var (len, breadth) = st.Footprint;
            string flights = st.Shape == StairShape.Straight ? "" : $"\nTramos de {st.FirstFlightRisers} y {d.Risers - st.FirstFlightRisers} escalones, descanso a {st.FirstFlightRisers * d.Riser:0.00} m";
            _props.Children.Add(Info($"{d.Risers} escalones de {d.Riser * 100:0.0} cm\nHuella {d.Tread * 100:0.0} cm · 2C+H = {d.Blondel * 100:0.0} cm{flights}\nCaja de escalera {len:0.00} × {breadth:0.00} m"));
            foreach (var w in d.Warnings) _props.Children.Add(Info("⚠ " + w));
            _props.Children.Add(Num("Altura a salvar (m)", st.Rise, 0.3, 10, v => Apply(() => _doc.Stairs.First(s => s.Id == id), (e, s) => e.Replace(s, s with { Rise = v }))));
            _props.Children.Add(Num("Ancho (m)", st.Width, 0.5, 4, v => Apply(() => _doc.Stairs.First(s => s.Id == id), (e, s) => e.Replace(s, s with { Width = v }))));
            _props.Children.Add(Choice("Forma", StairForms.Select(f => f.Label).ToArray(), StairFormIndex(st.Shape, st.TurnLeft), i =>
            {
                var (_, shape, left) = StairForms[i];
                var cur = _doc.Stairs.First(s => s.Id == id);
                if (cur.Shape != shape || (shape != StairShape.Straight && cur.TurnLeft != left))
                    Apply(() => _doc.Stairs.First(s => s.Id == id), (e, s) => e.Replace(s, s with { Shape = shape, TurnLeft = left }));
            }));
        }
        else if (_doc.Fixtures.FirstOrDefault(f => f.Id == id) is { } fx)
        {
            var t = FixtureCatalog.ByKind(fx.Kind);
            var lvl = _doc.GetLevel(fx.LevelId);
            Title($"{t?.Name ?? fx.Kind} {fx.ComponentId}");
            _props.Children.Add(Info($"Módulo: {fx.ModuleId}\nAltura sobre el piso: {fx.Position.Z - lvl.Elevation:0.00} m\n{(fx.HostId is null ? "En el recinto" : "En el muro " + fx.HostId)}\nEspacio: {_doc.SpaceAt(fx.LevelId, fx.Position.Xy)?.Name ?? "—"}"));
            foreach (var r in _doc.Routes.Where(r => r.FromFixtureId == id || r.ToFixtureId == id))
                _props.Children.Add(Info($"Ruta {r.Id}: {r.Length:0.00} m"));
        }
        else if (_doc.Routes.FirstOrDefault(r => r.Id == id) is { } route)
        {
            Title($"Ruta {route.Id} ({route.Kind.Replace('_', ' ')})");
            var from = _doc.Fixtures.FirstOrDefault(f => f.Id == route.FromFixtureId);
            var to = _doc.Fixtures.FirstOrDefault(f => f.Id == route.ToFixtureId);
            _props.Children.Add(Info($"{from?.ComponentId} → {to?.ComponentId}\nLongitud real: {route.Length:0.00} m\nDesnivel: {route.DeltaZ:+0.00;-0.00} m\nTramos: {route.Segments.Count}"));
            _props.Children.Add(Info("Estos valores se pasan al cable o tubería del banco de pruebas (caída de tensión, pérdidas de carga)."));
        }
        else if (_doc.Spaces.FirstOrDefault(s => s.Id == id) is { } sp)
        {
            Title(sp.Name);
            _props.Children.Add(Info($"Uso: {sp.Use.Replace('_', ' ')}\nÁrea (a ejes): {sp.Area:0.00} m²\nPerímetro: {sp.Perimeter:0.00} m"));
            _props.Children.Add(Btn("Cambiar nombre y uso…", async () =>
            {
                var anchor = Geo.Contains(sp.Outline, Geo.Centroid(sp.Outline)) ? Geo.Centroid(sp.Outline) : sp.Outline[0];
                if (await AskSpaceNameAsync(anchor) is { } n) _doc.Edit(e => e.RenameSpaceAt(sp.LevelId, anchor, n.Name, n.Use));
            }));
            return;
        }
        else
        {
            _props.Children.Add(new TextBlock { Text = "Nada seleccionado.", Opacity = 0.6 });
            return;
        }
        _props.Children.Add(Btn("🗑 Borrar", () => { _doc.Edit(e => e.Delete(id)); _plan.Select(null); }));
    }

    private void AddLevelProps()
    {
        var lvl = _doc.GetLevel(_plan.LevelId);
        Title($"Nivel: {lvl.Name}");
        var name = new TextBox { Text = lvl.Name };
        name.LostFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text) && name.Text != _doc.GetLevel(_plan.LevelId).Name)
                _doc.Edit(e => ReplaceLevel(e, l => l with { Name = name.Text! }));
        };
        _props.Children.Add(name);
        _props.Children.Add(Num("Cota del piso (m)", lvl.Elevation, -10, 200, v => _doc.Edit(e => ReplaceLevel(e, l => l with { Elevation = v }))));
        _props.Children.Add(Num("Altura de piso a piso (m)", lvl.Height, 2, 20, v => _doc.Edit(e => ReplaceLevel(e, l => l with { Height = v }))));
        if (_doc.Levels.Count > 1)
            _props.Children.Add(Btn("🗑 Borrar nivel y su contenido", () =>
            {
                var id = _plan.LevelId;
                _doc.Edit(e =>
                {
                    foreach (var w in _doc.Walls.Where(w => w.LevelId == id).ToList()) e.Delete(w.Id);
                    foreach (var s in _doc.Stairs.Where(s => s.LevelId == id).ToList()) e.Delete(s.Id);
                    foreach (var s in _doc.Slabs.Where(s => s.LevelId == id).ToList()) e.Delete(s.Id);
                    foreach (var f in _doc.Fixtures.Where(f => f.LevelId == id).ToList()) e.Delete(f.Id);
                    e.RemoveLevel(id);
                });
            }));
    }

    private void ReplaceLevel(BuildingDocument.Editor e, Func<Level, Level> change) => e.ReplaceLevel(_plan.LevelId, change);

    private void Title(string text) => _props.Children.Add(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold });

    private static TextBlock Info(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };

    /// <summary>Aplica un cambio al elemento actual (se vuelve a buscar por si cambió tras deshacer).</summary>
    private void Apply<T>(Func<T> current, Action<BuildingDocument.Editor, T> change) where T : class
    {
        var item = current();
        _doc.Edit(e => change(e, item));
    }

    // ------------------------------------------------------------ diálogos y archivo

    private static readonly (string Label, string Use)[] Uses =
    [
        ("Sala / estar", "estar"), ("Dormitorio", "dormitorio"), ("Cocina", "cocina"), ("Baño", "baño"),
        ("Pasillo / circulación", "circulacion"), ("Habitación de hotel", "habitacion_hotel"), ("Oficina", "oficina"),
        ("Nave / producción", "nave"), ("Almacén", "almacen"), ("Cuarto técnico", "tecnico"), ("Exterior / patio", "exterior"),
    ];

    private async Task<(string Name, string Use)?> AskSpaceNameAsync(Point2 at)
    {
        var current = _doc.SpaceAt(_plan.LevelId, at);
        var name = new TextBox { Text = current?.Name ?? "", Watermark = "Nombre" };
        var use = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, u) in Uses) use.Items.Add(new ComboBoxItem { Content = label, Tag = u });
        use.SelectedItem = use.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == current?.Use) ?? use.Items[0];

        (string, string)? result = null;
        var dlg = new Window
        {
            Title = "Habitación", Width = 340, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Aceptar", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text) && use.SelectedItem is ComboBoxItem { Tag: string u }) result = (name.Text.Trim(), u);
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Nombre" }, name, new TextBlock { Text = "Uso" }, use,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        if (TopLevel.GetTopLevel(this) is Window owner) await dlg.ShowDialog(owner);
        return result;
    }

    private static readonly FilePickerFileType FileType = new("Edificio (Simulador múltiple)") { Patterns = ["*" + BuildingDocument.FileExtension] };

    private async Task OpenAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir edificio", AllowMultiple = false, FileTypeFilter = [FileType, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var reader = new StreamReader(s);
            _doc.LoadFrom(BuildingDocument.FromJson(await reader.ReadToEndAsync()));
            _filePath = files[0].TryGetLocalPath();
            AfterLoad();
            _host.SetStatus($"Abierto: {files[0].Name}");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo abrir el archivo: {ex.Message}");
        }
    }

    private async Task SaveAsync(bool askPath)
    {
        try
        {
            if (!askPath && _filePath is not null)
            {
                await File.WriteAllTextAsync(_filePath, _doc.ToJson());
                _host.SetStatus($"Guardado: {_filePath}");
                return;
            }
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar edificio", SuggestedFileName = _doc.Name + BuildingDocument.FileExtension,
                DefaultExtension = BuildingDocument.FileExtension, FileTypeChoices = [FileType],
            });
            if (file is null) return;
            await using (var s = await file.OpenWriteAsync())
            await using (var w = new StreamWriter(s))
                await w.WriteAsync(_doc.ToJson());
            _filePath = file.TryGetLocalPath();
            _host.SetStatus($"Guardado: {file.Name}");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo guardar: {ex.Message}");
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.Z) { _doc.Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { _doc.Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { _ = SaveAsync(false); e.Handled = true; }
    }
}
