using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Core.Modules;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Persistence;
using Simulador.Hidraulica.Solver;
using Simulador.Hidraulica.View3D;

namespace Simulador.Hidraulica.Ui;

/// <summary>Vista raíz de la pestaña Hidráulica: barra de herramientas, paleta, lienzo, propiedades y tendencias.</summary>
public sealed class HydraulicView : UserControl
{
    private readonly IModuleHost _host;
    private readonly NetworkCanvas _canvas = new();
    private readonly Hydraulic3DView _view3d = new();
    private readonly TabControl _views = new() { Padding = new Thickness(0) };
    private readonly PropertyPanel _props = new();
    private readonly TrendChart _chart = new() { Height = 170 };
    private readonly TextBlock _messages = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(8, 4) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Button _run = new() { Content = "▶ Simular" };
    private readonly Button _pause = new() { Content = "⏸ Pausa", IsEnabled = false };
    private readonly Button _stop = new() { Content = "⏹ Detener", IsEnabled = false };
    private readonly ComboBox _speed = new() { ItemsSource = new[] { "×1", "×5", "×20", "×60" }, SelectedIndex = 1, MinWidth = 70 };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _palette = new() { Spacing = 2, Margin = new Thickness(6) };
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = [];

    private HydraulicNetwork _net = Samples.PumpingToTank();
    private HydraulicSimulation? _sim;
    private DateTime _lastFrame;
    private double _lastSample = double.NegativeInfinity;
    private string? _path;
    private bool _dirty;

    public HydraulicView(IModuleHost host)
    {
        _host = host;
        _canvas.Network = _net;

        var toolbar = new WrapPanel { Margin = new Thickness(6), Orientation = Orientation.Horizontal };
        toolbar.Children.Add(ToolbarButton("Nuevo", New));
        toolbar.Children.Add(ToolbarButton("Abrir…", () => _ = Open()));
        toolbar.Children.Add(ToolbarButton("Guardar", () => _ = Save(false)));
        toolbar.Children.Add(ToolbarButton("Guardar como…", () => _ = Save(true)));
        toolbar.Children.Add(ExamplesButton());
        toolbar.Children.Add(Separator());
        _run.Click += (_, _) => Run();
        _pause.Click += (_, _) => Pause();
        _stop.Click += (_, _) => Stop();
        foreach (var b in new[] { _run, _pause, _stop }) { b.Margin = new Thickness(2); toolbar.Children.Add(b); }
        toolbar.Children.Add(new TextBlock { Text = "Velocidad", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) });
        toolbar.Children.Add(_speed);
        toolbar.Children.Add(_clock);
        toolbar.Children.Add(Separator());
        toolbar.Children.Add(ToolbarButton("Comprobar diseño", Check));
        toolbar.Children.Add(ToolbarButton("Informe…", () => _ = ExportReport()));

        BuildPalette();

        _canvas.SelectionChanged += (_, e) =>
        {
            _props.Show(e, _net, _sim);
            _view3d.SelectedId = e switch { HydraulicNode n => n.Id, HydraulicLink l => l.Id, Sensor x => x.Id, _ => null };
            _view3d.InvalidateVisual();
        };
        _canvas.DesignChanged += (_, _) => { MarkDirty(); _view3d.InvalidateVisual(); };
        _canvas.Hint += (_, text) => _host.SetStatus(text);
        _props.DesignChanged += (_, _) => { MarkDirty(); _canvas.InvalidateVisual(); _view3d.InvalidateVisual(); };
        _props.Show(null, _net, null);
        _timer.Tick += (_, _) => Frame();

        var left = new Border
        {
            Child = new ScrollViewer { Content = _palette },
            BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = Brushes.Gray, Width = 190,
        };
        var right = new Border { Child = _props, BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = Brushes.Gray, Width = 300 };
        var bottom = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brushes.Gray,
            Child = new DockPanel { Children = { Dock(_messages, Avalonia.Controls.Dock.Bottom), _chart } },
        };
        var center = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var root = new DockPanel();
        root.Children.Add(Dock(new Border { Child = toolbar, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brushes.Gray }, Avalonia.Controls.Dock.Top));
        root.Children.Add(Dock(bottom, Avalonia.Controls.Dock.Bottom));
        root.Children.Add(Dock(left, Avalonia.Controls.Dock.Left));
        root.Children.Add(Dock(right, Avalonia.Controls.Dock.Right));
        _view3d.Network = _net;
        _views.Items.Add(new TabItem { Header = "Esquema", Content = center });
        _views.Items.Add(new TabItem { Header = "Vista 3D", Content = Build3DPanel() });
        root.Children.Add(_views);
        Content = root;
        UpdateClock();
        _messages.Text = "Diseña la red con la paleta de la izquierda y pulsa ▶ Simular, o abre otro diseño desde «Ejemplos». " + _net.Description;
    }

    // ---------------------------------------------------------------- Paleta

    private void BuildPalette()
    {
        Section("Edición");
        ToolButton(Tool.Select, "↖  Seleccionar");
        ToolButton(Tool.Delete, "✕  Borrar");
        Section("Depósitos");
        ToolButton(Tool.Reservoir, "🌊  Depósito fijo");
        ToolButton(Tool.Tank, "🛢  Tanque");
        ToolButton(Tool.Junction, "•  Unión / consumo");
        Section("Conducciones");
        ToolButton(Tool.Pipe, "━  Tubería");
        ToolButton(Tool.Pump, "⟳  Bomba centrífuga");
        Section("Válvulas");
        ToolButton(Tool.ManualValve, "⋈  Manual");
        ToolButton(Tool.ControlValve, "⋈  De control");
        ToolButton(Tool.SolenoidValve, "⋈  Electroválvula");
        ToolButton(Tool.CheckValve, "▷  Antirretorno");
        Section("Instrumentos");
        ToolButton(Tool.PressureSensor, "PT  Presión");
        ToolButton(Tool.LevelSensor, "LT  Nivel");
        ToolButton(Tool.FlowSensor, "FT  Caudal");
        SelectTool(Tool.Select);
    }

    private void Section(string text) =>
        _palette.Children.Add(new TextBlock { Text = text, Opacity = 0.6, FontSize = 12, Margin = new Thickness(4, 10, 0, 2) });

    private void ToolButton(Tool tool, string text)
    {
        var b = new ToggleButton { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => SelectTool(tool);
        _toolButtons[tool] = b;
        _palette.Children.Add(b);
    }

    private void SelectTool(Tool tool)
    {
        _canvas.Tool = tool;
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == tool;
        _host.SetStatus(tool switch
        {
            Tool.Pipe => "Tubería: clic en el origen y en cada punto siguiente; clic derecho o Esc para terminar.",
            Tool.Pump or Tool.ManualValve or Tool.ControlValve or Tool.SolenoidValve or Tool.CheckValve =>
                "Clic en el nudo de entrada y luego en el de salida (sentido del caudal).",
            Tool.PressureSensor or Tool.LevelSensor or Tool.FlowSensor => "Clic sobre el elemento a medir.",
            Tool.Delete => "Clic sobre el elemento a borrar.",
            Tool.Select => "Clic para seleccionar; arrastra para mover.",
            _ => "Clic en el lienzo para colocarlo.",
        });
    }

    // ---------------------------------------------------------------- Simulación

    private void Run()
    {
        if (_sim is null)
        {
            var report = _net.Validate();
            if (!report.IsValid) { ShowReport(report); return; }
            try { _sim = new HydraulicSimulation(_net); }
            catch (Exception ex) { _messages.Text = "No se pudo simular: " + ex.Message; return; }
            _chart.Clear();
            _lastSample = double.NegativeInfinity;
            SetDesignLocked(true);
        }
        _lastFrame = DateTime.UtcNow;
        _timer.Start();
        _run.IsEnabled = false;
        _pause.IsEnabled = _stop.IsEnabled = true;
        _messages.Text = "Simulando. Selecciona bombas y válvulas para manejarlas.";
    }

    private void Pause()
    {
        _timer.Stop();
        _run.IsEnabled = true;
        _run.Content = "▶ Continuar";
        _pause.IsEnabled = false;
    }

    private void Stop()
    {
        _timer.Stop();
        _sim = null;
        _canvas.Simulation = null;
        _view3d.Simulation = null;
        _view3d.InvalidateVisual();
        SetDesignLocked(false);
        _run.Content = "▶ Simular";
        _run.IsEnabled = true;
        _pause.IsEnabled = _stop.IsEnabled = false;
        _props.Show(_canvas.Selected, _net, null);
        UpdateClock();
        _messages.Text = "Simulación detenida; vuelves al modo diseño con los valores iniciales.";
    }

    private void SetDesignLocked(bool locked)
    {
        _canvas.DesignLocked = locked;
        _canvas.Simulation = _sim;
        _view3d.Simulation = _sim;
        if (locked) SelectTool(Tool.Select);
        foreach (var (t, b) in _toolButtons) b.IsEnabled = !locked || t == Tool.Select;
        _props.Show(_canvas.Selected, _net, _sim);
    }

    private void Frame()
    {
        if (_sim is null) return;
        var now = DateTime.UtcNow;
        double real = Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;
        double factor = _speed.SelectedIndex switch { 0 => 1, 2 => 20, 3 => 60, _ => 5 };
        _sim.Step(TimeSpan.FromSeconds(real * factor));

        double t = _sim.Time.TotalSeconds;
        if (t - _lastSample >= Math.Max(0.25, factor / 10))
        {
            _lastSample = t;
            foreach (var tank in _net.Nodes.OfType<Tank>())
                _chart.Add($"Nivel {tank.Id}", t, tank.Level / tank.MaxLevel * 100);
            foreach (var p in _net.Links.OfType<Pump>())
                _chart.Add($"Caudal {p.Id}", t, _sim.FlowIn(p.Id) / p.RatedFlow * 100);
            foreach (var v in _net.Links.OfType<Valve>().Where(v => v.Kind == ValveKind.Control))
                _chart.Add($"Apertura {v.Id}", t, v.Opening * 100);
        }

        _canvas.Animate(real);
        if (_views.SelectedIndex == 1) _view3d.Animate(real);
        _props.RefreshLive();
        UpdateClock();
        if (_sim.Warnings.Count > 0) _messages.Text = "Avisos: " + string.Join(" · ", _sim.Warnings.TakeLast(3));
    }

    private void UpdateClock()
    {
        var t = _sim?.Time ?? TimeSpan.Zero;
        _clock.Text = $"t = {(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    // ---------------------------------------------------------------- Proyecto

    private void New()
    {
        Load(new HydraulicNetwork { Name = "Nuevo proceso" }, null);
        _messages.Text = "Proyecto nuevo. Empieza colocando un depósito o un tanque.";
    }

    private void Load(HydraulicNetwork net, string? path)
    {
        if (_sim is not null) Stop();
        _net = net;
        _path = path;
        _dirty = false;
        _canvas.Network = net;
        _view3d.Network = net;
        _view3d.SelectedId = null;
        _view3d.ResetFraming();
        _canvas.InvalidateMeasure();
        _props.Show(null, net, null);
        _chart.Clear();
        _host.SetStatus(path is null ? net.Name : $"{net.Name} — {path}");
    }

    private void MarkDirty()
    {
        _dirty = true;
        _host.SetStatus($"{_net.Name}{(_path is null ? "" : " — " + _path)} (sin guardar)");
    }

    private static readonly FilePickerFileType ProjectType = new("Proyecto hidráulico") { Patterns = ["*" + ProjectFile.Extension] };

    private async Task Open()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir proyecto hidráulico", AllowMultiple = false, FileTypeFilter = [ProjectType],
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var reader = new StreamReader(s);
            Load(ProjectFile.Deserialize(await reader.ReadToEndAsync()), files[0].TryGetLocalPath());
        }
        catch (Exception ex) { _messages.Text = "No se pudo abrir: " + ex.Message; }
    }

    private async Task Save(bool askPath)
    {
        string json = ProjectFile.Serialize(_net);
        if (!askPath && _path is not null)
        {
            await File.WriteAllTextAsync(_path, json);
            _dirty = false;
            _host.SetStatus($"Guardado en {_path}");
            return;
        }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar proyecto hidráulico", DefaultExtension = ProjectFile.Extension.TrimStart('.'),
            SuggestedFileName = _net.Name + ProjectFile.Extension, FileTypeChoices = [ProjectType],
        });
        if (file is null) return;
        await using (var s = await file.OpenWriteAsync())
        await using (var w = new StreamWriter(s))
            await w.WriteAsync(json);
        _path = file.TryGetLocalPath();
        _dirty = false;
        _host.SetStatus($"Guardado en {_path ?? file.Name}");
    }

    private async Task ExportReport()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar informe", DefaultExtension = "md", SuggestedFileName = _net.Name + " - informe.md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        });
        if (file is null) return;
        HydraulicSimulation? sim = _sim;
        if (sim is null && _net.Validate().IsValid) sim = new HydraulicSimulation(_net); // estado inicial
        await using (var s = await file.OpenWriteAsync())
        await using (var w = new StreamWriter(s))
            await w.WriteAsync(Report.ToMarkdown(_net, sim));
        _host.SetStatus($"Informe guardado: {file.Name}");
    }

    private void Check() => ShowReport(_net.Validate());

    private void ShowReport(ValidationReport r)
    {
        if (r.IsValid && r.Warnings.Count == 0) { _messages.Text = "El diseño está listo para simular."; return; }
        var parts = r.Errors.Select(e => "✕ " + e).Concat(r.Warnings.Select(w => "⚠ " + w));
        _messages.Text = string.Join("   ", parts);
    }

    // ---------------------------------------------------------------- Ejemplos y vista 3D

    private Button ExamplesButton()
    {
        var menu = new MenuFlyout();
        foreach (var sample in Samples.All)
        {
            var item = new MenuItem
            {
                Header = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = sample.Name, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = sample.Summary, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                    },
                },
            };
            item.Click += (_, _) =>
            {
                var net = sample.Create();
                Load(net, null);
                _messages.Text = net.Description;
            };
            menu.Items.Add(item);
        }
        return new Button { Content = "Ejemplos ▾", Margin = new Thickness(2), Flyout = menu };
    }

    private Control Build3DPanel()
    {
        var bar = new WrapPanel { Margin = new Thickness(6, 4) };
        bar.Children.Add(ToolbarButton("Encuadrar", _view3d.FitAll));
        bar.Children.Add(ToolbarButton("Perspectiva", () => _view3d.SetView(-60, 28)));
        bar.Children.Add(ToolbarButton("Frente", () => _view3d.SetView(-90, 5)));
        bar.Children.Add(ToolbarButton("Lateral", () => _view3d.SetView(0, 5)));
        bar.Children.Add(ToolbarButton("Planta", () => _view3d.SetView(-90, 89)));
        var labels = new CheckBox { Content = "Etiquetas", IsChecked = true, Margin = new Thickness(8, 0) };
        labels.IsCheckedChanged += (_, _) => { _view3d.ShowLabels = labels.IsChecked == true; _view3d.InvalidateVisual(); };
        bar.Children.Add(labels);
        bar.Children.Add(new TextBlock
        {
            Text = "Las alturas (cotas) son las reales; la planta sigue la disposición del esquema.",
            VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, Margin = new Thickness(8, 0),
        });
        var panel = new DockPanel();
        panel.Children.Add(Dock(bar, Avalonia.Controls.Dock.Top));
        panel.Children.Add(_view3d);
        return panel;
    }

    // ---------------------------------------------------------------- Utilidades

    private static Button ToolbarButton(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(2) };
        b.Click += (_, _) => action();
        return b;
    }

    private static Control Separator() => new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(8, 4), Opacity = 0.5 };

    private static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>Indica si hay cambios sin guardar (la aplicación puede preguntar al cerrar).</summary>
    public bool HasUnsavedChanges => _dirty;
}
