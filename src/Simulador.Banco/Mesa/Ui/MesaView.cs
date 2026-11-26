using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Core.CoSim;
using Simulador.Core.Modules;
using Simulador.Core.Signals;

using Simulador.Banco.Mesa.View3D;

namespace Simulador.Banco.Mesa.Ui;

/// <summary>
/// Mesa libre del banco de pruebas: lista de piezas de todos los módulos a la izquierda, la mesa en el centro, la
/// ficha de la pieza a la derecha y, abajo, los avisos explicados y las pantallas de los instrumentos.
/// </summary>
public sealed class MesaView : UserControl, IMenuSource
{
    private static readonly (string Label, double Factor)[] Speeds =
        [("×0,1", 0.1), ("×0,5", 0.5), ("×1", 1), ("×10", 10), ("×60", 60), ("Máx.", double.PositiveInfinity)];

    private static readonly FilePickerFileType FileType = new("Montaje del banco de pruebas") { Patterns = ["*" + BenchProject.Extension] };
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, monospace");
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#5F6B78"));

    private readonly IModuleHost _host;
    private readonly BenchCanvas _canvas = new();
    private readonly Bench3DView _view3d = new();
    private readonly TabControl _views = new();
    private readonly StackPanel _palette = new() { Spacing = 2, Margin = new Thickness(6) };
    private readonly TextBox _search = new() { Watermark = "Buscar pieza…", Margin = new Thickness(6, 6, 6, 0) };
    private readonly StackPanel _inspector = new() { Spacing = 6, Margin = new Thickness(10) };
    private readonly StackPanel _issues = new() { Spacing = 4, Margin = new Thickness(8) };
    private readonly WrapPanel _instruments = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
    private readonly TabControl _bottom = new();
    private readonly TabItem _issuesTab;
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0), FontWeight = FontWeight.SemiBold, MinWidth = 110 };
    private readonly Button _play = new() { Content = "▶ Simular" };
    private readonly Button _pause = new() { Content = "⏸ Pausa" };
    private readonly Button _step = new() { Content = "⏭ Paso" };
    private readonly Button _reset = new() { Content = "⟲ Reiniciar" };
    private readonly Button _stop = new() { Content = "⏹ Detener" };
    private readonly ComboBox _speed = new() { ItemsSource = Speeds.Select(s => s.Label).ToArray(), SelectedIndex = 2, MinWidth = 70 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly List<(InstrumentView View, BenchPart Part)> _instrumentViews = [];
    private readonly Dictionary<string, Func<bool>> _wireActivity = [];

    private BenchProject _project = new();
    private string? _path;
    private bool _dirty;
    private bool _fitBlocks = true;
    private List<BenchIssue> _designIssues = [];
    private BuiltBench? _built;
    private SimRunner? _runner;
    private BenchMonitor? _monitor;
    private TextBlock? _liveText;
    private DateTime _last = DateTime.UtcNow;

    public MesaView(IModuleHost host)
    {
        _host = host;

        // ---- barra de herramientas
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        void Add(Control c, double left = 4) { c.Margin = new Thickness(left, 2, 0, 2); bar.Children.Add(c); }
        var newBtn = new Button { Content = "Nuevo" };
        var openBtn = new Button { Content = "Abrir…" };
        var saveBtn = new Button { Content = "Guardar" };
        var examples = new Button { Content = "📚 Ejemplos ▾" };
        var flyout = new MenuFlyout();
        foreach (var ex in BenchExamples.All)
        {
            var item = new MenuItem { Header = $"{ex.Name}  ·  {ex.Level}" };
            item.Click += (_, _) => OpenExample(ex);
            flyout.Items.Add(item);
        }
        examples.Flyout = flyout;
        newBtn.Click += (_, _) => New();
        openBtn.Click += (_, _) => _ = Open();
        saveBtn.Click += (_, _) => _ = Save(false);
        Add(newBtn, 0); Add(openBtn); Add(saveBtn); Add(examples, 10);
        Add(_play, 18); Add(_pause); Add(_step); Add(_reset); Add(_stop);
        Add(new TextBlock { Text = "Velocidad", VerticalAlignment = VerticalAlignment.Center }, 14);
        Add(_speed);
        Add(_clock);
        var fit = new Button { Content = "⤢ Ver todo" };
        var del = new Button { Content = "🗑 Quitar" };
        ToolTip.SetTip(fit, "Encaja todo el montaje en la pantalla (rueda del ratón: zoom; arrastrar el fondo: mover)");
        ToolTip.SetTip(del, "Quita la pieza o la conexión seleccionada (tecla Supr)");
        fit.Click += (_, _) => FitView();
        del.Click += (_, _) => _canvas.DeleteSelection();
        Add(fit, 10); Add(del);

        _play.Click += (_, _) => Run();
        _pause.Click += (_, _) => { _runner?.Pause(); UpdateButtons(); };
        _step.Click += (_, _) => { if (EnsureBuilt()) { _runner!.StepOnce(100); UpdateButtons(); } };
        _reset.Click += (_, _) => ResetSim();
        _stop.Click += (_, _) => StopSim();
        _speed.SelectionChanged += (_, _) => { if (_runner is not null) _runner.Speed = Speeds[Math.Max(0, _speed.SelectedIndex)].Factor; };

        // ---- lista de piezas
        _search.TextChanged += (_, _) => BuildPalette();
        BuildPalette();
        var paletteHeader = new TextBlock { Text = "Piezas", FontWeight = FontWeight.SemiBold, FontSize = 15, Margin = new Thickness(8, 8, 8, 0) };
        var paletteHelp = new TextBlock
        {
            Text = "Elige una pieza y haz clic en la mesa. Arrastra de un borne a otro para unirlos.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = MutedBrush, Margin = new Thickness(8, 2, 8, 0),
        };
        var left = new DockPanel { Width = 240 };
        var leftTop = new StackPanel { Children = { paletteHeader, paletteHelp, _search } };
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        left.Children.Add(new ScrollViewer { Content = _palette });

        // ---- ficha
        var right = new ScrollViewer { Width = 340, Content = _inspector };

        // ---- abajo: avisos e instrumentos
        _issuesTab = new TabItem { Header = Header("Avisos"), Content = new ScrollViewer { Content = _issues } };
        _bottom.Items.Add(_issuesTab);
        _bottom.Items.Add(new TabItem { Header = Header("Instrumentos"), Content = new ScrollViewer { Content = _instruments, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var bottom = new Border { Height = 260, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Child = _bottom };

        var top = new StackPanel { Children = { bar, new Border { Padding = new Thickness(10, 0, 10, 4), Child = _title } } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        var leftBorder = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
        var rightBorder = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1, 0, 0, 0), Child = right };
        DockPanel.SetDock(leftBorder, Dock.Left);
        DockPanel.SetDock(rightBorder, Dock.Right);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(leftBorder);
        root.Children.Add(rightBorder);
        var tab3d = new TabItem { Header = Header("🧊 Vista 3D"), Content = _view3d };
        var tab2d = new TabItem { Header = Header("🔗 Diagrama de bloques"), Content = _canvas };
        ToolTip.SetTip(tab3d, "Los equipos sobre la mesa, en 3D. Gira con el ratón, mueve piezas y une bornes.");
        ToolTip.SetTip(tab2d, "Lo que hay detrás: cada pieza como un bloque con sus bornes y cómo se conecta con las demás.");
        _views.Items.Add(tab3d);
        _views.Items.Add(tab2d);
        root.Children.Add(_views);
        Content = root;

        // ---- vista 3D (la misma mesa; la selección y la pieza por poner se comparten con el diagrama)
        _view3d.SelectedPart = () => _canvas.Selected;
        _view3d.GetPending = () => _canvas.Pending;
        _view3d.ClearPending = () => _canvas.Pending = null;
        _view3d.IssueOf = WorstIssue;
        _view3d.PartSelected += (_, p) => { _canvas.Select(p); _view3d.InvalidateVisual(); };
        _view3d.ProjectChanged += (_, _) => { MarkDirty(); BuildInspector(); Redraw(); };
        _view3d.Message += (_, m) => _host.SetStatus(m);
        _view3d.PlacementDone += (_, _) => BuildPalette();
        _view3d.PartPressed += (_, e) => PartPressed(e.PartId, e.Down);
        _view3d.DeleteRequested += (_, _) => _canvas.DeleteSelection();
        _views.SelectionChanged += (_, _) =>
        {
            // El diagrama se encuadra la primera vez que se ve (oculto no tiene tamaño).
            if (_views.SelectedIndex == 1 && _fitBlocks) { _fitBlocks = false; Dispatcher.UIThread.Post(_canvas.ZoomToFit, DispatcherPriority.Background); }
            Redraw();
        };

        // ---- mesa
        _canvas.SelectionChanged += (_, _) => { BuildInspector(); _view3d.InvalidateVisual(); };
        _canvas.ProjectChanged += (_, _) => { MarkDirty(); BuildInspector(); };
        _canvas.Message += (_, m) => _host.SetStatus(m);
        _canvas.PlacementDone += (_, _) => BuildPalette();
        _canvas.PartPressed += (_, e) => PartPressed(e.PartId, e.Down);
        _canvas.IssueOf = WorstIssue;

        _checkTimer.Tick += (_, _) => { _checkTimer.Stop(); CheckDesign(); };
        _timer.Tick += (_, _) => Frame();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); _runner?.Pause(); };

        Load(BenchExamples.MotorStartStop(), null);
    }

    /// <summary>Redibuja las dos vistas de la mesa (bloques y 3D).</summary>
    private void Redraw()
    {
        _canvas.InvalidateVisual();
        _view3d.InvalidateVisual();
    }

    private void FitView()
    {
        if (_views.SelectedIndex == 0) _view3d.FitAll();
        else _canvas.ZoomToFit();
    }

    private static TextBlock Header(string s) => new() { Text = s, FontSize = 14 };

    // ================================================================ archivo

    public IEnumerable<MenuCommand> GetMenuCommands()
    {
        yield return new MenuCommand("Nuevo montaje", New);
        yield return new MenuCommand("Abrir montaje…", () => _ = Open());
        yield return new MenuCommand("Guardar", () => _ = Save(false)) { Shortcut = "Ctrl+S" };
        yield return new MenuCommand("Guardar como…", () => _ = Save(true));
        yield return MenuCommand.Submenu("Ejemplos de la mesa", BenchExamples.All.Select(ex => new MenuCommand($"{ex.Name} ({ex.Level})", () => OpenExample(ex))));
        yield return new MenuCommand("Quitar la selección", () => _canvas.DeleteSelection()) { Menu = MenuNames.Edit, Shortcut = "Supr" };
        yield return new MenuCommand("Ver todo el montaje", FitView) { Menu = MenuNames.View };
        yield return new MenuCommand("Comprobar montaje", () => { CheckDesign(); _bottom.SelectedItem = _issuesTab; }) { Menu = MenuNames.Tools };
    }

    private void New()
    {
        Load(new BenchProject(), null);
        _host.SetStatus("Mesa vacía: elige una pieza en la lista de la izquierda.");
    }

    private void OpenExample(BenchExamples.Example ex)
    {
        Load(ex.Create(), null);
        _host.SetStatus($"Ejemplo abierto: {ex.Name}. Pulsa ▶ Simular.");
    }

    private void Load(BenchProject project, string? path)
    {
        StopSim();
        _project = project;
        _path = path;
        _dirty = false;
        _canvas.Project = project;
        _view3d.Project = project;
        UpdateTitle();
        BuildInspector();
        CheckDesign();
        _fitBlocks = _views.SelectedIndex != 1;
        _view3d.ResetFraming();
        Dispatcher.UIThread.Post(_canvas.ZoomToFit, DispatcherPriority.Background);
    }

    private async Task Open()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir montaje del banco de pruebas", AllowMultiple = false, FileTypeFilter = [FileType] });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var reader = new StreamReader(s);
            Load(BenchProject.Deserialize(await reader.ReadToEndAsync()), files[0].TryGetLocalPath());
            _host.SetStatus($"Abierto {files[0].Name}");
        }
        catch (Exception ex) { _host.SetStatus("No se pudo abrir el montaje: " + ex.Message); }
    }

    private async Task Save(bool askPath)
    {
        string json = BenchProject.Serialize(_project);
        if (!askPath && _path is not null)
        {
            await File.WriteAllTextAsync(_path, json);
            _dirty = false; UpdateTitle();
            _host.SetStatus($"Guardado en {_path}");
            return;
        }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar montaje del banco de pruebas", DefaultExtension = BenchProject.Extension.TrimStart('.'),
            SuggestedFileName = _project.Name + BenchProject.Extension, FileTypeChoices = [FileType],
        });
        if (file is null) return;
        await using (var s = await file.OpenWriteAsync())
        await using (var w = new StreamWriter(s))
            await w.WriteAsync(json);
        _path = file.TryGetLocalPath();
        _dirty = false; UpdateTitle();
        _host.SetStatus($"Guardado en {_path ?? file.Name}");
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    private void UpdateTitle() => _title.Text = (_dirty ? "● " : "") + _project.Name + (_path is null ? "" : $"   ·   {Path.GetFileName(_path)}");

    // ================================================================ lista de piezas

    private void BuildPalette()
    {
        _palette.Children.Clear();
        string q = (_search.Text ?? "").Trim();
        foreach (var cat in BenchCatalog.Categories)
        {
            var types = BenchCatalog.All.Where(t => t.Category == cat &&
                (q.Length == 0 || t.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || t.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (types.Count == 0) continue;
            var color = BenchCanvas.CategoryColor(cat);
            _palette.Children.Add(new TextBlock { Text = cat, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(color), Margin = new Thickness(2, 10, 0, 2) });
            foreach (var t in types)
            {
                bool pending = _canvas.Pending == t;
                var b = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4), Background = pending ? new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)) : Brushes.Transparent,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = t.Icon, Width = 22, Foreground = new SolidColorBrush(color), FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = t.Name, TextWrapping = TextWrapping.Wrap, MaxWidth = 170 },
                        },
                    },
                };
                ToolTip.SetTip(b, t.Description);
                b.Click += (_, _) =>
                {
                    if (_runner is not null) { _host.SetStatus("Detén la simulación (⏹) para añadir piezas."); return; }
                    _canvas.Pending = _canvas.Pending == t ? null : t;
                    Redraw();
                    _canvas.Focus();
                    BuildPalette();
                    if (_canvas.Pending is not null) _host.SetStatus($"Haz clic en la mesa para poner: {t.Name}. Con Mayús pones varias.");
                };
                _palette.Children.Add(b);
            }
        }
    }

    // ================================================================ comprobación y avisos

    private void CheckDesign()
    {
        if (_runner is not null) return;
        BuiltBench? b = null;
        try
        {
            b = BenchBuilder.Build(_project.Clone());
            _designIssues = b.Issues;
        }
        catch (Exception ex) { _designIssues = [new(IssueLevel.Error, null, "No se pudo comprobar el montaje: " + ex.Message)]; }
        finally { if (b is not null) DisposeParts(b); }
        RefreshIssues();
        Redraw();
    }

    private IssueLevel? WorstIssue(string partId)
    {
        IssueLevel? worst = _monitor?.WorstFor(partId);
        foreach (var i in _designIssues)
            if (i.PartId == partId && i.Level != IssueLevel.Info && (worst is null || i.Level < worst)) worst = i.Level;
        return worst;
    }

    private void RefreshIssues()
    {
        _issues.Children.Clear();
        int errors = _designIssues.Count(i => i.Level == IssueLevel.Error) + (_monitor?.Active.Count(a => a.Issue.Level == IssueLevel.Error) ?? 0);
        int warnings = _designIssues.Count(i => i.Level == IssueLevel.Warning) + (_monitor?.Active.Count(a => a.Issue.Level == IssueLevel.Warning) ?? 0);
        _issuesTab.Header = Header(errors + warnings == 0 ? "Avisos ✓" : $"Avisos ({(errors > 0 ? $"{errors} ⛔ " : "")}{(warnings > 0 ? $"{warnings} ⚠" : "")})".Replace(" )", ")"));

        if (_monitor is not null)
        {
            Section(_monitor.Active.Count > 0 ? "Ahora mismo" : "Ahora mismo: todo en orden ✓");
            foreach (var a in _monitor.Active) IssueRow(a.Issue, a.Time);
            var past = _monitor.Log.Where(l => !_monitor.Active.Any(a => a.Issue.PartId == l.Issue.PartId && a.Time == l.Time)).ToList();
            if (past.Count > 0)
            {
                Section("Lo que ha pasado");
                foreach (var l in past.AsEnumerable().Reverse().Take(30)) IssueRow(l.Issue, l.Time, faded: true);
            }
        }
        var design = _designIssues.OrderBy(i => i.Level).ToList();
        if (design.Count == 0 && _monitor is null)
            Section("El montaje está bien conectado ✓  Pulsa ▶ Simular.");
        else if (design.Count > 0)
        {
            Section(_monitor is null ? "Revisión del montaje" : "Del montaje");
            foreach (var i in design) IssueRow(i, null);
        }
    }

    private void Section(string text) =>
        _issues.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 8, 0, 2) });

    private void IssueRow(BenchIssue issue, double? time, bool faded = false)
    {
        var color = issue.Level switch { IssueLevel.Error => Color.Parse("#D32F2F"), IssueLevel.Warning => Color.Parse("#B7791F"), _ => Color.Parse("#4A6B8A") };
        var body = new StackPanel { Spacing = 1 };
        body.Children.Add(new TextBlock
        {
            Text = $"{issue.Icon} {(time is { } t ? $"[{TimeSpan.FromSeconds(t):mm\\:ss\\.f}] " : "")}{issue.Message}",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(color),
        });
        if (issue.Hint is not null)
            body.Children.Add(new TextBlock { Text = "→ " + issue.Hint, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = MutedBrush, Margin = new Thickness(20, 0, 0, 0) });
        var row = new Border
        {
            Child = body, Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(6), Opacity = faded ? 0.65 : 1,
            Background = new SolidColorBrush(Color.FromArgb(18, color.R, color.G, color.B)), Cursor = issue.PartId is null ? Cursor.Default : new Cursor(StandardCursorType.Hand),
        };
        if (issue.PartId is { } id)
            row.PointerPressed += (_, _) => { if (_project.Part(id) is { } p) _canvas.Select(p); };
        _issues.Children.Add(row);
    }

    // ================================================================ simulación

    private bool EnsureBuilt()
    {
        if (_runner is not null) return true;
        BuiltBench b;
        try { b = BenchBuilder.Build(_project.Clone()); }
        catch (Exception ex) { _host.SetStatus("No se pudo preparar la simulación: " + ex.Message); return false; }
        _designIssues = b.Issues;
        if (!b.CanRun)
        {
            DisposeParts(b);
            RefreshIssues();
            _bottom.SelectedItem = _issuesTab;
            int n = b.Issues.Count(i => i.Level == IssueLevel.Error);
            _host.SetStatus(b.Parts.Count == 0 && n == 0 ? "La mesa está vacía." : $"No se puede simular todavía: hay {n} error{(n == 1 ? "" : "es")} en el montaje (mira «Avisos»).");
            Redraw();
            return false;
        }
        _built = b;
        _monitor = new BenchMonitor(b);
        _monitor.Changed += (_, _) => RefreshIssues();
        _runner = new SimRunner(b.Scheduler) { Speed = Speeds[Math.Max(0, _speed.SelectedIndex)].Factor };
        _runner.Faulted += (_, ex) => Dispatcher.UIThread.Post(() => { _host.SetStatus($"Simulación detenida por un error: {ex.Message}"); UpdateButtons(); });
        _canvas.Pending = null;
        _canvas.Running = true;
        _canvas.LookOf = LookOf;
        _canvas.WireActive = w => _wireActivity.TryGetValue(WireKey(w), out var f) && f();
        _view3d.Running = true;
        _view3d.LookOf = _canvas.LookOf;
        _view3d.WireActive = _canvas.WireActive;
        PrepareWireActivity(b);
        BuildInstrumentViews(b);
        BuildInspector();
        RefreshIssues();
        BuildPalette();
        return true;
    }

    private void Run()
    {
        if (!EnsureBuilt()) return;
        _runner!.Play();
        if (_instrumentViews.Count > 0 && _bottom.SelectedIndex == 0 && _designIssues.All(i => i.Level != IssueLevel.Warning)) _bottom.SelectedIndex = 1;
        _host.SetStatus($"Simulando «{_project.Name}». Pulsa los pulsadores en la mesa; sus mandos están en la ficha de la derecha.");
        UpdateButtons();
    }

    private void ResetSim()
    {
        if (_runner is null || _built is null) return;
        var b = _built;
        _runner.Pause();
        _runner.Reset(b.AfterReset);
        _monitor?.Clear();
        BuildInspector();
        UpdateButtons();
    }

    private void StopSim()
    {
        if (_runner is null) { UpdateButtons(); return; }
        _runner.Dispose();
        if (_built is not null) DisposeParts(_built);
        _runner = null;
        _built = null;
        _monitor = null;
        _wireActivity.Clear();
        _canvas.Running = false;
        _canvas.LookOf = null;
        _canvas.WireActive = null;
        _view3d.Running = false;
        _view3d.LookOf = null;
        _view3d.WireActive = null;
        _instrumentViews.Clear();
        _instruments.Children.Clear();
        _clock.Text = "";
        BuildInspector();
        CheckDesign();
        BuildPalette();
        UpdateButtons();
        Redraw();
    }

    private static void DisposeParts(BuiltBench b)
    {
        foreach (var c in b.Scheduler.Components.OfType<IDisposable>())
            try { c.Dispose(); } catch { /* una placa que no se para bien no debe romper la mesa */ }
    }

    private void UpdateButtons()
    {
        bool running = _runner is not null && !_runner.IsPaused;
        _play.IsEnabled = !running;
        _play.Content = _runner is null ? "▶ Simular" : "▶ Seguir";
        _pause.IsEnabled = running;
        _step.IsEnabled = !running;
        _reset.IsEnabled = _runner is not null;
        _stop.IsEnabled = _runner is not null;
    }

    private PartLook? LookOf(string id)
    {
        if (_built is null || _runner is null || !_built.Parts.TryGetValue(id, out var bp) || bp.Look is null) return null;
        try { return bp.Look(_built.Reader(_runner.Latest, id)); }
        catch { return null; }
    }

    private void PartPressed(string id, bool down)
    {
        if (_runner is null || _built is null || !_built.Parts.TryGetValue(id, out var bp)) return;
        if (bp.Controls.FirstOrDefault(c => c.Kind == LiveKind.Momentary) is { } m)
        {
            double v = down ? 1 : 0;
            _runner.Post(() => m.Set(v));
        }
        else if (down && bp.Type.TypeId == "selector" && bp.Controls.FirstOrDefault(c => c.Kind == LiveKind.Toggle) is { } t)
        {
            double v = (t.Get?.Invoke() ?? 0) > 0.5 ? 0 : 1;
            _runner.Post(() => t.Set(v));
            Dispatcher.UIThread.Post(BuildInspector, DispatcherPriority.Background);
        }
    }

    private static string WireKey(BenchWire w) => $"{w.FromPart}.{w.FromPort}|{w.ToPart}.{w.ToPort}";

    /// <summary>Para cada cable, cómo saber si ahora lleva algo (para iluminarlo en la mesa).</summary>
    private void PrepareWireActivity(BuiltBench b)
    {
        _wireActivity.Clear();
        foreach (var w in _project.Wires)
        {
            if (!b.Parts.TryGetValue(w.FromPart, out var pa) || !b.Parts.TryGetValue(w.ToPart, out var pb)) continue;
            var da = pa.Type.Port(pa.Part, w.FromPort);
            var db = pb.Type.Port(pb.Part, w.ToPort);
            if (da is null || db is null) continue;
            Func<bool>? f = null;
            if (da.Kind == PortKind.SignalOut || db.Kind == PortKind.SignalOut)
            {
                var s = da.Kind == PortKind.SignalOut ? pa.Signal(w.FromPort) : pb.Signal(w.ToPort);
                if (s is not null) f = () => Math.Abs(s.Value) > (s.Kind == SignalKind.AO ? 1e-6 : 0.5);
            }
            else if (da.Kind == PortKind.Power && db.Kind == PortKind.Power)
            {
                var p = pa.Power(w.FromPort);
                if (p is not null) f = () => Math.Abs(p.Flow) > 1e-3 && Math.Abs(p.Effort) > 1e-3 || (p.Domain == PowerDomain.Electrical3Ph && Math.Abs(p.Effort) > 1 && Math.Abs(p.Flow) > 0.01);
            }
            else if (da.Kind == PortKind.Pipe && db.Kind == PortKind.Pipe)
            {
                var link = new[] { pa, pb }.FirstOrDefault(x => x.Model is Hidraulica.CoSim.HydraulicPart { Element: Hidraulica.Model.HydraulicLink });
                if (link is not null)
                {
                    string id = link.Part.Id;
                    f = () => _runner is not null && Math.Abs(b.Reader(_runner.Latest, id).D("flow")) > 1e-5;
                }
            }
            if (f is not null) _wireActivity[WireKey(w)] = f;
        }
    }

    private void BuildInstrumentViews(BuiltBench b)
    {
        _instrumentViews.Clear();
        _instruments.Children.Clear();
        foreach (var inst in b.Instruments)
        {
            var part = b.Parts[inst.Id].Part;
            var type = b.Parts[inst.Id].Type;
            var targets = type.PortsFor(part).Select(d => _project.WiresAt(part.Id, d.Name).FirstOrDefault() is { } w
                ? $"{w.Other(part.Id, d.Name).Part}.{w.Other(part.Id, d.Name).Port}" : null).ToList();
            var view = new InstrumentView(inst, targets, part.Number("ventana", 5)) { Margin = new Thickness(0, 0, 10, 10) };
            _instrumentViews.Add((view, part));
            _instruments.Children.Add(view);
        }
        if (_instrumentViews.Count == 0)
            _instruments.Children.Add(new TextBlock
            {
                Text = "No hay instrumentos en la mesa. Pon un Multímetro, un Osciloscopio o una Línea de tiempo (grupo «Instrumentos») y une sus puntas a los bornes que quieras medir.",
                TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush, MaxWidth = 700,
            });
    }

    private void Frame()
    {
        var now = DateTime.UtcNow;
        double wall = Math.Min((now - _last).TotalSeconds, 0.2);
        _last = now;
        if (_runner is null || _built is null) return;
        var frame = _runner.Latest;
        _monitor?.Update(frame);
        _canvas.Animate(_runner.IsPaused ? 0 : wall);
        _view3d.Animate(_runner.IsPaused ? 0 : wall);
        foreach (var (v, _) in _instrumentViews) v.InvalidateVisual();
        _clock.Text = $"t = {frame.Time:hh\\:mm\\:ss\\.f}";
        if (_liveText is not null && _canvas.Selected is { } sel) _liveText.Text = LiveText(sel.Id, frame);
        UpdateButtons();
    }

    private string LiveText(string id, SimFrame frame)
    {
        if (_built is null || !_built.Parts.TryGetValue(id, out var bp)) return "";
        var lines = new List<string>();
        if (LookOf(id) is { } look) lines.AddRange(look.Lines.Where(l => l.Length > 0));
        var r = _built.Reader(frame, id);
        if (bp.Model is Hidraulica.CoSim.HydraulicPart hp)
        {
            foreach (var (k, v) in hp.Snapshot().Values) lines.Add($"{KeyLabel(k),-16} {Fmt(k, v)}");
        }
        else if (bp.SnapshotSource is { } src)
        {
            var st = frame.Components.FirstOrDefault(c => c.ComponentId == src.Id || c.ComponentId == src.Id + ".MCU");
            if (st is not null)
                foreach (var (k, v) in st.Values.Take(40)) lines.Add($"{KeyLabel(k.StartsWith(id + ".", StringComparison.Ordinal) ? k[(id.Length + 1)..] : k),-16} {Fmt(k, v)}");
        }
        _ = r;
        return string.Join("\n", lines);
    }

    private static readonly Dictionary<string, string> KeyNames = new()
    {
        ["voltage"] = "tensión (V)", ["current"] = "corriente (A)", ["frequency"] = "frecuencia (Hz)", ["closed"] = "cerrado",
        ["coilOn"] = "bobina", ["tripped"] = "disparado", ["speed"] = "velocidad (rpm)", ["rpm"] = "rpm", ["torque"] = "par (N·m)",
        ["loadTorque"] = "par carga (N·m)", ["power"] = "potencia (kW)", ["admission"] = "admisión (%)", ["pressure"] = "presión (bar)",
        ["flow"] = "caudal (L/s)", ["output"] = "salida", ["input"] = "entrada", ["relief"] = "seguridad abierta", ["actuated"] = "accionado",
        ["lit"] = "encendido", ["level"] = "nivel (m)", ["levelRatio"] = "nivel (%)", ["volume"] = "volumen (m³)", ["overflow"] = "rebose (L/s)",
        ["head"] = "altura (m)", ["velocity"] = "velocidad (m/s)", ["opening"] = "apertura (%)", ["running"] = "en marcha", ["value"] = "medida",
        ["plc.state"] = "estado", ["plc.scanCount"] = "ciclos", ["plc.fault"] = "fallo", ["mcu.state"] = "estado", ["mcu.line"] = "línea", ["mcu.fault"] = "fallo",
    };

    private static string KeyLabel(string key) => KeyNames.GetValueOrDefault(key, key);

    /// <summary>Valor en la unidad de su etiqueta (rpm, kW, bar, L/s, %).</summary>
    private static string Fmt(string key, object v) => v switch
    {
        double d => Reading.Format(key switch
        {
            "speed" => d * 60 / (2 * Math.PI),
            "power" => d / 1000,
            "pressure" => d / 1e5,
            "flow" or "overflow" => d * 1000,
            "admission" or "levelRatio" or "opening" => d * 100,
            _ => d,
        }),
        bool b => b ? "sí" : "no",
        _ => v.ToString() ?? "",
    };

    // ================================================================ ficha

    private void BuildInspector()
    {
        _inspector.Children.Clear();
        _liveText = null;
        var part = _canvas.Selected;
        if (part is null && _canvas.SelectedWire is { } w) { WireInspector(w); return; }
        if (part is null) { ProjectInspector(); return; }
        var type = BenchCatalog.Get(part.Type);
        if (type is null) { _inspector.Children.Add(new TextBlock { Text = $"{part.Id}: tipo desconocido «{part.Type}»." }); return; }

        var color = BenchCanvas.CategoryColor(type.Category);
        _inspector.Children.Add(new TextBlock { Text = $"{type.Icon}  {part.Id}", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(color) });
        _inspector.Children.Add(new TextBlock { Text = $"{type.Name} · {type.Category}", FontWeight = FontWeight.SemiBold });
        _inspector.Children.Add(new TextBlock { Text = type.Description, TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush, FontSize = 12.5 });

        if (_runner is not null) { LiveInspector(part); return; }

        // nombre
        var nameBox = new TextBox { Text = part.Id };
        var nameErr = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        void Rename()
        {
            var why = _project.Rename(part, nameBox.Text ?? "");
            nameErr.Text = why ?? "";
            if (why is null && nameBox.Text?.Trim() != null) { MarkDirty(); Redraw(); }
        }
        nameBox.LostFocus += (_, _) => Rename();
        nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); };
        _inspector.Children.Add(Labeled("Nombre", nameBox));
        _inspector.Children.Add(nameErr);

        // parámetros
        foreach (var d in type.Params)
        {
            Control editor;
            switch (d.Kind)
            {
                case ParamKind.Choice:
                    var combo = new ComboBox { ItemsSource = d.Options, SelectedItem = part.Text(d.Key, d.Default), HorizontalAlignment = HorizontalAlignment.Stretch };
                    combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s && part[d.Key] != s) { part[d.Key] = s; MarkDirty(); Redraw(); } };
                    editor = combo;
                    break;
                case ParamKind.Program:
                    var code = new TextBox
                    {
                        Text = part.Text(d.Key, d.Default), AcceptsReturn = true, AcceptsTab = true, FontFamily = MonoFont, FontSize = 12,
                        MinHeight = 240, MaxHeight = 460, TextWrapping = TextWrapping.NoWrap,
                    };
                    if (string.IsNullOrWhiteSpace(code.Text) && part.Type == "arduino-io") code.Watermark = BenchCatalog.ArduinoTemplate(part.Text("placa", "Arduino Uno"));
                    code.TextChanged += (_, _) => { if (part[d.Key] == (code.Text ?? "")) return; part[d.Key] = code.Text ?? ""; MarkDirty(); };
                    var sample = new Button { Content = "Cargar el programa de ejemplo", FontSize = 12 };
                    sample.Click += (_, _) => code.Text = part.Type == "plc" ? BenchCatalog.DefaultPlcProgram : BenchCatalog.ArduinoTemplate(part.Text("placa", "Arduino Uno"));
                    editor = new StackPanel { Spacing = 4, Children = { code, sample } };
                    break;
                default:
                    var box = new TextBox { Text = part.Text(d.Key, d.Default) };
                    box.TextChanged += (_, _) =>
                    {
                        string t = (box.Text ?? "").Trim().Replace(',', '.');
                        bool ok = double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                        box.BorderBrush = ok ? null : Brushes.Firebrick;
                        if (ok && part[d.Key] != t) { part[d.Key] = t; MarkDirty(); Redraw(); }
                    };
                    editor = d.Unit.Length > 0
                        ? new DockPanel { Children = { new TextBlock { Text = d.Unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), [DockPanel.DockProperty] = Dock.Right }, box } }
                        : box;
                    break;
            }
            _inspector.Children.Add(Labeled(d.Label, editor, d.Help));
        }

        // bornes
        _inspector.Children.Add(new TextBlock { Text = "Bornes", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var d in type.PortsFor(part))
        {
            var links = _project.WiresAt(part.Id, d.Name).Select(x => x.Other(part.Id, d.Name)).Select(o => $"{o.Part}.{o.Port}").ToList();
            var c = BenchCanvas.PortColor(d);
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Inlines = [] };
            tb.Inlines!.Add(new Avalonia.Controls.Documents.Run("● ") { Foreground = new SolidColorBrush(c) });
            tb.Inlines.Add(new Avalonia.Controls.Documents.Run(d.Text) { FontWeight = FontWeight.SemiBold });
            tb.Inlines.Add(new Avalonia.Controls.Documents.Run($"  {BenchRules.KindName(d)}  ") { Foreground = MutedBrush });
            tb.Inlines.Add(new Avalonia.Controls.Documents.Run(links.Count > 0 ? "→ " + string.Join(", ", links) : "libre"));
            _inspector.Children.Add(tb);
        }
        foreach (var i in _designIssues.Where(i => i.PartId == part.Id))
            _inspector.Children.Add(new TextBlock { Text = $"{i.Icon} {i.Message}", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = i.Level == IssueLevel.Error ? Brushes.Firebrick : MutedBrush });

        var remove = new Button { Content = "🗑 Quitar pieza", Margin = new Thickness(0, 10, 0, 0) };
        remove.Click += (_, _) => _canvas.DeleteSelection();
        _inspector.Children.Add(remove);
    }

    private void LiveInspector(BenchPart part)
    {
        if (_built is null || !_built.Parts.TryGetValue(part.Id, out var bp)) return;
        if (bp.Controls.Count > 0)
            _inspector.Children.Add(new TextBlock { Text = "Mandos", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var c in bp.Controls)
        {
            switch (c.Kind)
            {
                case LiveKind.Momentary:
                    var hold = new Button { Content = $"{c.Name} (mantén pulsado)", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
                    hold.AddHandler(PointerPressedEvent, (_, _) => _runner?.Post(() => c.Set(1)), RoutingStrategies.Tunnel);
                    hold.AddHandler(PointerReleasedEvent, (_, _) => _runner?.Post(() => c.Set(0)), RoutingStrategies.Tunnel);
                    _inspector.Children.Add(hold);
                    break;
                case LiveKind.Toggle:
                    var tg = new ToggleSwitch { Content = c.Name, IsChecked = (c.Get?.Invoke() ?? 0) > 0.5 };
                    tg.IsCheckedChanged += (_, _) => { double v = tg.IsChecked == true ? 1 : 0; _runner?.Post(() => c.Set(v)); };
                    _inspector.Children.Add(tg);
                    break;
                case LiveKind.Slider:
                    var val = new TextBlock { MinWidth = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                    var sl = new Slider { Minimum = c.Min, Maximum = c.Max, Value = Math.Clamp(c.Get?.Invoke() ?? c.Min, c.Min, c.Max) };
                    void Show() => val.Text = $"{Reading.Format(sl.Value)} {c.Unit}";
                    Show();
                    sl.PropertyChanged += (_, e) =>
                    {
                        if (e.Property != RangeBase.ValueProperty) return;
                        Show();
                        double v = sl.Value;
                        _runner?.Post(() => c.Set(v));
                    };
                    var row = new DockPanel();
                    DockPanel.SetDock(val, Dock.Right);
                    row.Children.Add(val);
                    row.Children.Add(sl);
                    _inspector.Children.Add(Labeled(c.Name, row));
                    break;
                default:
                    var btn = new Button { Content = c.Name };
                    btn.Click += (_, _) => _runner?.Post(() => c.Set(1));
                    _inspector.Children.Add(btn);
                    break;
            }
        }
        if (bp.Controls.Any(c => c.Kind == LiveKind.Momentary) || part.Type == "selector")
            _inspector.Children.Add(new TextBlock { Text = "También puedes pulsarlo con el ratón directamente en la mesa.", FontSize = 12, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap });

        _inspector.Children.Add(new TextBlock { Text = "Valores", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        _liveText = new TextBlock { FontFamily = MonoFont, FontSize = 12 };
        if (_runner is not null) _liveText.Text = LiveText(part.Id, _runner.Latest);
        _inspector.Children.Add(_liveText);
    }

    private void WireInspector(BenchWire w)
    {
        _inspector.Children.Add(new TextBlock { Text = "Conexión", FontSize = 18, FontWeight = FontWeight.SemiBold });
        var a = BenchRules.PortOf(_project, w.FromPart, w.FromPort);
        var b = BenchRules.PortOf(_project, w.ToPart, w.ToPort);
        _inspector.Children.Add(new TextBlock { Text = $"{w.FromPart}.{w.FromPort}\n↔ {w.ToPart}.{w.ToPort}", FontFamily = MonoFont, TextWrapping = TextWrapping.Wrap });
        if (a is not null && b is not null)
            _inspector.Children.Add(new TextBlock { Text = $"Une {BenchRules.KindName(a)} con {BenchRules.KindName(b)}.", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush });
        if (_runner is null)
        {
            var remove = new Button { Content = "🗑 Quitar conexión" };
            remove.Click += (_, _) => _canvas.DeleteSelection();
            _inspector.Children.Add(remove);
        }
    }

    private void ProjectInspector()
    {
        _inspector.Children.Add(new TextBlock { Text = "Montaje", FontSize = 18, FontWeight = FontWeight.SemiBold });
        var name = new TextBox { Text = _project.Name };
        name.TextChanged += (_, _) => { if (_project.Name == (name.Text ?? "")) return; _project.Name = name.Text ?? ""; MarkDirty(); };
        var desc = new TextBox { Text = _project.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
        desc.TextChanged += (_, _) => { if (_project.Description == (desc.Text ?? "")) return; _project.Description = desc.Text ?? ""; MarkDirty(); };
        name.IsEnabled = desc.IsEnabled = _runner is null;
        _inspector.Children.Add(Labeled("Nombre", name));
        _inspector.Children.Add(Labeled("Qué hace este montaje", desc));
        _inspector.Children.Add(new TextBlock { Text = "Cómo se usa", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        foreach (var tip in new[]
        {
            "1. Elige una pieza en la lista y haz clic en la mesa (con Mayús pones varias).",
            "2. Arrastra desde un borne hasta otro para unirlos. Los colores dicen qué es cada borne: verde señal digital, naranja analógica, rojo potencia eléctrica, gris eje, azul agua, turquesa aire, morado puntas de medida.",
            "3. Si algo no encaja, la mesa no lo une y te explica por qué abajo en la barra de estado.",
            "4. Pulsa ▶ Simular. Pulsa los pulsadores en la propia mesa y mueve los mandos desde la ficha.",
            "5. Mira «Avisos» para saber qué pasa y cómo arreglarlo, e «Instrumentos» para medir.",
        })
            _inspector.Children.Add(new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
    }

    private static Control Labeled(string label, Control editor, string? help = null)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = MutedBrush });
        panel.Children.Add(editor);
        if (help is not null) panel.Children.Add(new TextBlock { Text = help, FontSize = 11.5, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}
