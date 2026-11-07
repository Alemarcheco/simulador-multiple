using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Civil.Export;
using Simulador.Civil.Import;
using Simulador.Civil.Model;
using Simulador.Core.Building;
using Simulador.Core.Modules;

namespace Simulador.Civil.Views;

/// <summary>Vista raíz de la pestaña Edificación.</summary>
public sealed class EdificacionView : DockPanel, IMenuSource
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
    private readonly StackPanel _calcos = new() { Spacing = 6 };
    private bool _editingUnderlay;
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
        _plan.AskRealDistance = AskRealDistanceAsync;
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
        var v3bar = new WrapPanel { Margin = new Thickness(6) }; // pasa a otra fila si no cabe
        var roof = new CheckBox { Content = "Mostrar cubierta", IsChecked = true };
        roof.IsCheckedChanged += (_, _) => { _view3d.ShowRoof = roof.IsChecked == true; _view3d.InvalidateVisual(); };
        var upTo = new CheckBox { Content = "Solo hasta el nivel actual" };
        upTo.IsCheckedChanged += (_, _) => { _view3d.UpToLevelId = upTo.IsChecked == true ? _plan.LevelId : null; _view3d.InvalidateVisual(); };
        _levelBox.SelectionChanged += (_, _) => { if (upTo.IsChecked == true) { _view3d.UpToLevelId = _plan.LevelId; _view3d.InvalidateVisual(); } };
        var persp = new CheckBox { Content = "Perspectiva", IsChecked = _view3d.Perspective };
        ToolTip.SetTip(persp, "Con perspectiva lo lejano se ve más pequeño, como lo ve el ojo, y el edificio se ve proporcionado. Sin ella (vista paralela) todas las medidas se dibujan iguales, pero el fondo parece más ancho que el frente.");
        persp.IsCheckedChanged += (_, _) => _view3d.Perspective = persp.IsChecked == true;
        var reset = new Button { Content = "Vista inicial" };
        reset.Click += (_, _) => _view3d.ResetView();
        var walk = new ToggleButton { Content = "🚶 Recorrer por dentro" };
        ToolTip.SetTip(walk, "Recorrido en primera persona: empieza delante de la puerta de entrada. W A S D o flechas para andar, arrastra para mirar, Esc para salir.");
        walk.IsCheckedChanged += (_, _) =>
        {
            _view3d.WalkMode = walk.IsChecked == true;
            roof.IsEnabled = upTo.IsEnabled = persp.IsEnabled = reset.IsEnabled = walk.IsChecked != true;
        };
        _view3d.WalkModeExited += (_, _) => walk.IsChecked = false;
        var sunToggle = new ToggleButton { Content = "☀ Sol y sombras" };
        ToolTip.SetTip(sunToggle, "Estudio de soleamiento: sombras según el lugar, la orientación, la fecha y la hora.");
        var sunBar = BuildSunBar();
        sunBar.IsVisible = false;
        sunToggle.IsCheckedChanged += (_, _) =>
        {
            sunBar.IsVisible = sunToggle.IsChecked == true;
            _view3d.SunStudy = sunToggle.IsChecked == true;
        };
        v3bar.Children.Add(walk);
        v3bar.Children.Add(sunToggle);
        v3bar.Children.Add(roof);
        v3bar.Children.Add(upTo);
        v3bar.Children.Add(persp);
        v3bar.Children.Add(reset);
        v3bar.Children.Add(new TextBlock { Text = "Arrastra para girar (también por debajo) · botón derecho para mover · rueda para zoom", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        foreach (var c in v3bar.Children) c.Margin = new Thickness(0, 2, 8, 2);
        SetDock(v3bar, Dock.Top);
        view3dPanel.Children.Add(v3bar);
        SetDock(sunBar, Dock.Top);
        view3dPanel.Children.Add(sunBar);
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

        bar.Children.Add(new TextBlock { Text = "Nivel:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        _levelBox.SelectionChanged += (_, _) =>
        {
            if (_syncingLevels || _levelBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            _plan.LevelId = id;
            _plan.CancelCurrent();
            _plan.Select(null);
            RefreshCalcos();
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
        return bar;
    }

    /// <summary>Órdenes de la barra de menús de arriba.</summary>
    public IEnumerable<MenuCommand> GetMenuCommands()
    {
        yield return new MenuCommand("Nuevo", () => { _doc.LoadFrom(new BuildingDocument()); _filePath = null; AfterLoad(); });
        yield return MenuCommand.Submenu("Plantillas", BuildingTemplates.All.Select(t =>
            new MenuCommand(t.Item1, () => { _doc.LoadFrom(t.Item2()); _filePath = null; AfterLoad(); })));
        yield return new MenuCommand("Abrir…", () => _ = OpenAsync()) { SeparatorBefore = true };
        yield return new MenuCommand("Guardar", () => _ = SaveAsync(false)) { Shortcut = "Ctrl+S" };
        yield return new MenuCommand("Guardar como…", () => _ = SaveAsync(true));
        yield return new MenuCommand("Exportar (PDF, DXF, IFC)…", () => _ = ExportPlanAsync()) { SeparatorBefore = true };
        yield return new MenuCommand("Deshacer", _doc.Undo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Z" };
        yield return new MenuCommand("Rehacer", _doc.Redo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Y" };
        yield return new MenuCommand("Encuadrar el plano", _plan.ZoomToFit) { Menu = MenuNames.View };
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

        p.Children.Add(Header("Calco (plano de referencia)"));
        p.Children.Add(Btn("📐 Importar DXF…", () => _ = ImportDxfAsync()));
        p.Children.Add(Btn("🖼 Importar plano escaneado…", () => _ = ImportImageAsync()));
        p.Children.Add(ToolButton("↔ Escalar con una medida", PlanTool.CalibrateUnderlay));
        p.Children.Add(ToolButton("✥ Mover calco", PlanTool.MoveUnderlay));
        p.Children.Add(ToolButton("⟲ Girar calco", PlanTool.RotateUnderlay));
        p.Children.Add(Btn("🧱 Crear muros desde el DXF", ConvertUnderlayToWalls));
        p.Children.Add(_calcos);
        p.Children.Add(new TextBlock
        {
            Text = "Al dibujar muros, el cursor se pega a los extremos de las líneas del calco.",
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
        if (!_editingUnderlay) RefreshCalcos();
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
        RefreshCalcos();
        _plan.Select(null);
        _plan.InvalidateVisual();
    }

    private Action? _refreshSun;

    /// <summary>
    /// Barra del soleamiento: lugar (o coordenadas a mano), norte de la planta, fecha, hora con su deslizador y un botón
    /// para ver pasar el día; debajo, la altura y el acimut del sol, la salida, la puesta y las horas de sol.
    /// </summary>
    private Control BuildSunBar()
    {
        var places = SiteLocation.Presets.Select(p => p.Place).Append("Personalizado").ToArray();
        var place = new ComboBox { ItemsSource = places, MinWidth = 170 };
        NumericUpDown Num(double min, double max, double inc, string fmt, double width) => new()
        {
            Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)inc, FormatString = fmt, Width = width,
        };
        var lat = Num(-90, 90, 0.1, "0.0000", 150);
        var lon = Num(-180, 180, 0.1, "0.0000", 160);
        var utc = Num(-12, 14, 0.5, "+0.#;-0.#;0", 120);
        var north = Num(-180, 180, 5, "0", 120);
        var date = new DatePicker { SelectedDate = DateTimeOffset.Now.Date };
        var hour = new Slider { Minimum = 0, Maximum = 24, SmallChange = 0.25, LargeChange = 1, Value = 12, Width = 260 };
        var hourText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 44 };
        var play = new ToggleButton { Content = "▶ Ver pasar el día" };
        var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
        bool syncing = false;

        void ShowSite()
        {
            syncing = true;
            var site = _doc.Site;
            int idx = Array.FindIndex(SiteLocation.Presets, p => p.Place == site.Place);
            place.SelectedIndex = idx >= 0 ? idx : places.Length - 1;
            lat.Value = (decimal)site.Latitude;
            lon.Value = (decimal)site.Longitude;
            utc.Value = (decimal)site.UtcOffsetHours;
            north.Value = (decimal)site.NorthAngleDeg;
            syncing = false;
        }

        void Update()
        {
            var day = date.SelectedDate?.Date ?? DateTime.Today;
            var t = day.AddHours(hour.Value);
            hourText.Text = t.ToString("HH:mm", CultureInfo.InvariantCulture);
            _view3d.SunTime = t;
            var sun = Solar.Position(_doc.Site, t);
            var (rise, set) = Solar.RiseSet(_doc.Site, day);
            var len = Solar.DayLength(_doc.Site, day);
            string R(DateTime? d) => d?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
            info.Text = (sun.IsUp
                    ? $"Sol a {sun.AltitudeDeg:0}° de altura, hacia el {sun.Cardinal} (acimut {sun.AzimuthDeg:0}°)"
                    : "De noche: el sol está bajo el horizonte")
                + $" · sale {R(rise)} · se pone {R(set)} · {(int)len.TotalHours} h {len.Minutes:00} min de sol";
        }

        void SetSite(SiteLocation site)
        {
            _doc.Site = site;
            ShowSite();
            Update();
            _view3d.InvalidateVisual();
        }

        place.SelectionChanged += (_, _) =>
        {
            if (syncing || place.SelectedIndex < 0 || place.SelectedIndex >= SiteLocation.Presets.Length) return;
            SetSite(SiteLocation.Presets[place.SelectedIndex] with { NorthAngleDeg = _doc.Site.NorthAngleDeg });
        };
        void Custom(object? _, NumericUpDownValueChangedEventArgs __)
        {
            if (syncing || lat.Value is null || lon.Value is null || utc.Value is null) return;
            SetSite(new SiteLocation("Personalizado", (double)lat.Value, (double)lon.Value, (double)utc.Value, _doc.Site.NorthAngleDeg));
        }
        lat.ValueChanged += Custom;
        lon.ValueChanged += Custom;
        utc.ValueChanged += Custom;
        north.ValueChanged += (_, _) =>
        {
            if (syncing || north.Value is null) return;
            SetSite(_doc.Site with { NorthAngleDeg = (double)north.Value });
        };
        date.SelectedDateChanged += (_, _) => Update();
        hour.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Update(); };

        // Ver pasar el día: avanza la hora un cuarto cada décima de segundo, de la salida a la puesta del sol.
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var (rise, set) = Solar.RiseSet(_doc.Site, date.SelectedDate?.Date ?? DateTime.Today);
            double from = rise?.TimeOfDay.TotalHours ?? 0, to = set?.TimeOfDay.TotalHours ?? 24;
            double next = hour.Value + 0.25;
            hour.Value = next > to || next < from ? from : next;
        };
        play.IsCheckedChanged += (_, _) => { if (play.IsChecked == true) timer.Start(); else timer.Stop(); };
        DetachedFromVisualTree += (_, _) => timer.Stop();

        _refreshSun = () => { ShowSite(); Update(); };
        ShowSite();
        Update();

        TextBlock L(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var row1 = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { L("Lugar"), place, L("Lat."), lat, L("Lon."), lon, L("UTC"), utc, L("Norte de la planta (°)"), north },
        };
        var row2 = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { L("Fecha"), date, L("Hora"), hour, hourText, play },
        };
        return new StackPanel
        {
            Margin = new Thickness(6, 0, 6, 6), Spacing = 4,
            Children = { new WrapPanel { Children = { row1 } }, new WrapPanel { Children = { row2 } }, info },
        };
    }

    private void AfterLoad()
    {
        _refreshSun?.Invoke();
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

    // ------------------------------------------------------------ calcos

    private void RefreshCalcos()
    {
        _calcos.Children.Clear();
        var list = _doc.Underlays.Where(u => u.LevelId == _plan.LevelId).ToList();
        if (list.Count == 0)
        {
            _calcos.Children.Add(new TextBlock { Text = "Este nivel no tiene calco.", Opacity = 0.6, FontSize = 12 });
            return;
        }
        var active = list.FirstOrDefault(u => u.Id == _plan.ActiveUnderlayId) ?? list[0];
        _plan.ActiveUnderlayId = active.Id;
        foreach (var u in list)
        {
            string id = u.Id;
            var box = new StackPanel { Spacing = 3 };
            var pick = new RadioButton { GroupName = "calco", IsChecked = u.Id == active.Id, Content = u.Name, FontWeight = FontWeight.SemiBold };
            pick.IsCheckedChanged += (_, _) => { if (pick.IsChecked == true) _plan.ActiveUnderlayId = id; };
            box.Children.Add(pick);
            string info = u.Kind == UnderlayKind.Dxf
                ? $"DXF · {u.Segments.Count} líneas · escala ×{u.Scale:0.####}"
                : $"Imagen {u.PixelWidth}×{u.PixelHeight} px · {u.Scale * 1000:0.###} mm/px";
            box.Children.Add(new TextBlock { Text = info, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            var visible = new CheckBox { Content = "Visible", IsChecked = u.Visible };
            visible.IsCheckedChanged += (_, _) => EditUnderlay(id, x => x with { Visible = visible.IsChecked == true });
            var del = new Button { Content = "Quitar", Padding = new Thickness(6, 2) };
            del.Click += (_, _) => _doc.RemoveUnderlay(id);
            box.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { visible, del } });

            var opacity = new Slider { Minimum = 0.1, Maximum = 1, Value = u.Opacity, HorizontalAlignment = HorizontalAlignment.Stretch };
            opacity.ValueChanged += (_, e) => EditUnderlay(id, x => x with { Opacity = e.NewValue });
            box.Children.Add(new TextBlock { Text = "Opacidad", FontSize = 11, Opacity = 0.7 });
            box.Children.Add(opacity);

            if (u.Kind == UnderlayKind.Dxf)
            {
                var layers = new StackPanel { Spacing = 1 };
                foreach (var layer in u.Segments.Select(x => x.Layer).Distinct().Order())
                {
                    int count = u.Segments.Count(x => x.Layer == layer);
                    var cb = new CheckBox { Content = $"{layer} ({count})", IsChecked = !u.HiddenLayers.Contains(layer), FontSize = 11 };
                    cb.IsCheckedChanged += (_, _) => EditUnderlay(id, x =>
                    {
                        var hidden = new List<string>(x.HiddenLayers);
                        hidden.Remove(layer);
                        if (cb.IsChecked != true) hidden.Add(layer);
                        return x with { HiddenLayers = hidden };
                    });
                    layers.Children.Add(cb);
                }
                box.Children.Add(new Expander { Header = "Capas", Content = layers, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
            _calcos.Children.Add(new Border
            {
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Child = box,
            });
        }
    }

    /// <summary>Cambia un calco sin reconstruir el panel (para no cortar el arrastre del deslizador).</summary>
    private void EditUnderlay(string id, Func<Underlay, Underlay> change)
    {
        var u = _doc.Underlays.FirstOrDefault(x => x.Id == id);
        if (u is null) return;
        _editingUnderlay = true;
        try { _doc.ReplaceUnderlay(change(u)); }
        finally { _editingUnderlay = false; }
    }

    private static readonly FilePickerFileType DxfType = new("Plano DXF") { Patterns = ["*.dxf", "*.DXF"] };
    private static readonly FilePickerFileType ImageType = new("Imagen") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.PNG", "*.JPG", "*.JPEG"] };

    private async Task<IStorageFile?> PickAsync(string title, FilePickerFileType type)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, FileTypeFilter = [type, FilePickerFileTypes.All],
        });
        return files.Count == 0 ? null : files[0];
    }

    private async Task ImportDxfAsync()
    {
        try
        {
            var file = await PickAsync("Importar plano DXF", DxfType);
            if (file is null) return;
            string text;
            await using (var s = await file.OpenReadAsync())
            using (var r = new StreamReader(s))
                text = await r.ReadToEndAsync();
            var dxf = DxfReader.Parse(text);
            if (dxf.Segments.Count == 0) { _host.SetStatus("El DXF no tiene líneas que se puedan calcar."); return; }
            var u = _doc.AddUnderlay(Underlay.FromDxf("", _plan.LevelId, Path.GetFileNameWithoutExtension(file.Name), dxf));
            _plan.ActiveUnderlayId = u.Id;
            RefreshCalcos();
            _plan.ZoomToFit();
            var (min, max) = dxf.Extents;
            string skipped = dxf.Skipped.Count == 0 ? "" : " · sin dibujar: " + string.Join(", ", dxf.Skipped.Select(kv => $"{kv.Key} {kv.Value}"));
            _host.SetStatus($"DXF importado: {dxf.Segments.Count} líneas, {max.X - min.X:0.00} × {max.Y - min.Y:0.00} m (unidad {dxf.UnitSource}){skipped}. Si la medida no cuadra, usa «Escalar con una medida».");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo leer el DXF: {ex.Message}");
        }
    }

    private async Task ImportImageAsync()
    {
        try
        {
            var file = await PickAsync("Importar plano escaneado", ImageType);
            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (path is null) { _host.SetStatus("La imagen tiene que estar en una carpeta del equipo."); return; }
            int w, h;
            using (var bmp = new Avalonia.Media.Imaging.Bitmap(path)) { w = bmp.PixelSize.Width; h = bmp.PixelSize.Height; }
            var u = _doc.AddUnderlay(Underlay.FromImage("", _plan.LevelId, Path.GetFileNameWithoutExtension(file.Name), path, w, h));
            _plan.ActiveUnderlayId = u.Id;
            RefreshCalcos();
            _plan.ZoomToFit();
            SetTool(PlanTool.CalibrateUnderlay);
            _host.SetStatus("Plano importado con una escala provisional (20 m de ancho). Marca los extremos de una medida conocida para escalarlo.");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo abrir la imagen: {ex.Message}");
        }
    }

    private void ConvertUnderlayToWalls()
    {
        var u = _doc.Underlays.FirstOrDefault(x => x.Id == _plan.ActiveUnderlayId && x.LevelId == _plan.LevelId)
            ?? _doc.Underlays.FirstOrDefault(x => x.LevelId == _plan.LevelId && x.Kind == UnderlayKind.Dxf);
        if (u is null || u.Kind != UnderlayKind.Dxf)
        {
            _host.SetStatus("Hace falta un calco DXF en este nivel (en un plano escaneado los muros se dibujan a mano encima).");
            return;
        }
        var found = WallDetector.Detect(u.PlanSegments());
        if (found.Count == 0)
        {
            _host.SetStatus("No se encontraron muros (parejas de líneas paralelas de 7 a 45 cm). Oculta las capas que no sean muros o dibújalos a mano.");
            return;
        }
        double height = _doc.GetLevel(_plan.LevelId).Height - 0.2;
        _doc.Edit(e =>
        {
            foreach (var w in found) e.AddWall(_plan.LevelId, w.A, w.B, w.Thickness, Math.Max(2.2, height));
        });
        _host.SetStatus($"Creados {found.Count} muros desde el DXF. Revísalos: se puede deshacer con Ctrl+Z.");
    }

    private async Task<double?> AskRealDistanceAsync(double measured)
    {
        var input = new NumericUpDown
        {
            Value = (decimal)Math.Round(measured, 3), Minimum = 0.01m, Maximum = 10000, Increment = 0.05m,
            FormatString = "0.000", HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        double? result = null;
        var dlg = new Window
        {
            Title = "Escalar calco", Width = 340, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Aceptar", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) => { if (input.Value is { } v && v > 0) result = (double)v; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = $"En el calco ese tramo mide {measured:0.000} m. ¿Cuánto mide en realidad (m)?", TextWrapping = TextWrapping.Wrap },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        if (TopLevel.GetTopLevel(this) is Window owner) await dlg.ShowDialog(owner);
        return result;
    }

    /// <summary>Diálogo de exportación: formato, papel, escala, niveles y qué se dibuja; luego pide dónde guardar.</summary>
    private async Task ExportPlanAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var format = new ComboBox { ItemsSource = new[] { "PDF (para imprimir, a escala)", "DXF (para AutoCAD y otros CAD)", "IFC (modelo BIM 3D: Revit, ArchiCAD, BIMvision)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var paper = new ComboBox { ItemsSource = Paper.All.Select(p => p.Name).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scale = new ComboBox
        {
            ItemsSource = new[] { "Automática (la mayor que quepa)" }.Concat(PlanExport.StandardScales.Select(s => $"1:{s}")).ToArray(),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var levels = new ComboBox { ItemsSource = new[] { "Solo el nivel actual", "Todos los niveles" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var dims = new CheckBox { Content = "Cotas", IsChecked = true };
        var rooms = new CheckBox { Content = "Nombres y superficies", IsChecked = true };
        var inst = new CheckBox { Content = "Instalaciones", IsChecked = true };
        format.SelectionChanged += (_, _) =>
        {
            paper.IsEnabled = scale.IsEnabled = format.SelectedIndex == 0;
            // IFC lleva el edificio entero en 3D: no hay papel, escala ni capas que elegir.
            levels.IsEnabled = dims.IsEnabled = rooms.IsEnabled = inst.IsEnabled = format.SelectedIndex != 2;
        };

        bool accepted = false;
        var dlg = new Window
        {
            Title = "Exportar", Width = 380, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Exportar…", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) => { accepted = true; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        TextBlock L(string t) => new() { Text = t, Margin = new Thickness(0, 4, 0, 0) };
        dlg.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 4,
            Children =
            {
                L("Formato"), format, L("Papel (apaisado)"), paper, L("Escala"), scale, L("Niveles"), levels,
                L("Dibujar"), dims, rooms, inst,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        await dlg.ShowDialog(owner);
        if (!accepted) return;

        if (format.SelectedIndex == 2) { await ExportIfcAsync(owner); return; }
        bool pdf = format.SelectedIndex == 0;
        var options = new PlanOptions(dims.IsChecked == true, rooms.IsChecked == true, inst.IsChecked == true);
        var ids = levels.SelectedIndex == 1 ? _doc.Levels.OrderBy(l => l.Elevation).Select(l => l.Id).ToList() : [_plan.LevelId];
        var drawings = ids.Select(id => PlanDrawing.Build(_doc, id, options)).Where(d => d.Items.Count > 0).ToList();
        if (drawings.Count == 0) { _host.SetStatus("No hay nada dibujado en ese nivel para exportar."); return; }

        var ext = pdf ? ".pdf" : ".dxf";
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar plano", SuggestedFileName = _doc.Name + ext, DefaultExtension = ext,
            FileTypeChoices = [pdf ? new FilePickerFileType("PDF") { Patterns = ["*.pdf"] } : new FilePickerFileType("DXF") { Patterns = ["*.dxf"] }],
        });
        if (file is null) return;
        try
        {
            var sheet = Paper.All[Math.Max(0, paper.SelectedIndex)];
            int? fixedScale = scale.SelectedIndex > 0 ? PlanExport.StandardScales[scale.SelectedIndex - 1] : null;
            byte[] bytes = pdf ? PlanExport.ToPdf(drawings, sheet, fixedScale) : PlanExport.ToDxfBytes(drawings);
            await using (var s = await file.OpenWriteAsync()) await s.WriteAsync(bytes);
            string detail = pdf
                ? $"{drawings.Count} hoja(s) {sheet.Name}, escala 1:{fixedScale ?? PlanExport.AutoScale(drawings[0], sheet)}"
                : $"{drawings.Count} nivel(es), en metros";
            _host.SetStatus($"Plano exportado: {file.Name} ({detail}).");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo exportar: {ex.Message}");
        }
    }

    private async Task ExportIfcAsync(Window owner)
    {
        if (_doc.Walls.Count == 0) { _host.SetStatus("No hay muros que exportar a IFC."); return; }
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar modelo IFC", SuggestedFileName = _doc.Name + ".ifc", DefaultExtension = ".ifc",
            FileTypeChoices = [new FilePickerFileType("IFC4") { Patterns = ["*.ifc"] }],
        });
        if (file is null) return;
        try
        {
            var bytes = IfcExport.ToIfcBytes(_doc);
            await using (var s = await file.OpenWriteAsync()) await s.WriteAsync(bytes);
            _host.SetStatus($"Modelo IFC exportado: {file.Name} ({_doc.Levels.Count} planta(s), {_doc.Walls.Count} muros, {_doc.Openings.Count} huecos).");
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo exportar: {ex.Message}");
        }
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
