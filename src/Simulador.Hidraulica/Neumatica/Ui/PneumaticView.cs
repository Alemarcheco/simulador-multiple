using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Core.Modules;
using Simulador.Hidraulica.Neumatica.View3D;
using Simulador.Hidraulica.Ui;
using Simulador.Hidraulica.View3D;

namespace Simulador.Hidraulica.Neumatica.Ui;

/// <summary>Vista raíz de la pestaña Neumática: barra, paleta, esquema / vista 3D, propiedades y gráfica de posiciones.</summary>
public sealed class PneumaticView : UserControl
{
    private readonly IModuleHost _host;
    private readonly PneumaticCanvas _canvas = new();
    private readonly Hydraulic3DView _view3d = new();
    private readonly TabControl _views = new() { Padding = new Thickness(0) };
    private readonly PneumaticPropertyPanel _props = new();
    private readonly TrendChart _chart = new() { Height = 160 };
    private readonly TextBlock _messages = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(8, 4) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Button _run = new() { Content = "▶ Simular" };
    private readonly Button _pause = new() { Content = "⏸ Pausa", IsEnabled = false };
    private readonly Button _stop = new() { Content = "⏹ Detener", IsEnabled = false };
    private readonly ComboBox _speed = new() { ItemsSource = new[] { "×0,1", "×0,25", "×0,5", "×1" }, SelectedIndex = 2, MinWidth = 70 };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _palette = new() { Spacing = 2, Margin = new Thickness(6) };
    private readonly Dictionary<PneuTool, ToggleButton> _toolButtons = [];

    private PneumaticCircuit _circuit = PneumaticSamples.SpeedControl();
    private PneumaticSimulation? _sim;
    private DateTime _lastFrame;
    private double _lastSample = double.NegativeInfinity;
    private string? _path;
    private bool _dirty;

    public PneumaticView(IModuleHost host)
    {
        _host = host;
        _canvas.Circuit = _circuit;

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
        toolbar.Children.Add(ToolbarButton("Comprobar circuito", Check));
        toolbar.Children.Add(ToolbarButton("Lista de materiales…", () => _ = ExportBom()));

        BuildPalette();

        _canvas.SelectionChanged += (_, e) =>
        {
            _props.Show(e, _circuit, _sim);
            _view3d.SelectedId = (e as PneumaticComponent)?.Id;
            _view3d.InvalidateVisual();
        };
        _canvas.DesignChanged += (_, _) => { MarkDirty(); _view3d.InvalidateVisual(); _props.Show(_canvas.Selected, _circuit, _sim); };
        _canvas.Hint += (_, text) => _host.SetStatus(text);
        _props.DesignChanged += (_, _) => { MarkDirty(); _canvas.InvalidateVisual(); _view3d.InvalidateVisual(); };
        _props.RotateRequested += (_, _) => _canvas.RotateSelected();
        _props.Show(null, _circuit, null);
        _timer.Tick += (_, _) => Frame();

        _view3d.SceneProvider = (_, selected) => PneumaticSceneBuilder.Build(_circuit, _sim, selected);

        var left = new Border
        {
            Child = new ScrollViewer { Content = _palette },
            BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = Brushes.Gray, Width = 200,
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
        _views.Items.Add(new TabItem { Header = "Esquema", Content = center });
        _views.Items.Add(new TabItem { Header = "Vista 3D", Content = Build3DPanel() });
        root.Children.Add(_views);
        Content = root;
        UpdateClock();
        _messages.Text = "Diseña el circuito con la paleta de la izquierda y pulsa ▶ Simular, o abre otro desde «Ejemplos». " + _circuit.Description;
    }

    // ---------------------------------------------------------------- Paleta

    private void BuildPalette()
    {
        Section("Edición");
        ToolButton(PneuTool.Select, "↖  Seleccionar");
        ToolButton(PneuTool.Delete, "✕  Borrar");
        ToolButton(PneuTool.Tube, "━  Manguera");
        Section("Producción de aire");
        ToolButton(PneuTool.Supply, "⊙  Fuente de aire");
        ToolButton(PneuTool.Compressor, "◉  Compresor");
        ToolButton(PneuTool.Receiver, "▯  Depósito de aire");
        ToolButton(PneuTool.Gauge, "◔  Manómetro");
        Section("Válvulas distribuidoras");
        ToolButton(PneuTool.Valve32Push, "▭▭  3/2 pulsador");
        ToolButton(PneuTool.Valve32Roller, "▭▭  3/2 rodillo");
        ToolButton(PneuTool.Valve52Lever, "▭▭  5/2 palanca");
        ToolButton(PneuTool.Valve52Pilot, "▭▭  5/2 pilotada biestable");
        ToolButton(PneuTool.Valve52Solenoid, "▭▭  5/2 doble bobina");
        Section("Otras válvulas");
        ToolButton(PneuTool.FlowControl, "⌇  Regulador de caudal");
        ToolButton(PneuTool.Check, "◁  Antirretorno");
        ToolButton(PneuTool.OrValve, "≥1  Selectora (O)");
        ToolButton(PneuTool.AndValve, "&  Simultaneidad (Y)");
        Section("Actuadores");
        ToolButton(PneuTool.CylinderSingle, "▭─  Cilindro simple efecto");
        ToolButton(PneuTool.CylinderDouble, "▭═  Cilindro doble efecto");
        SelectTool(PneuTool.Select);
    }

    private void Section(string text) =>
        _palette.Children.Add(new TextBlock { Text = text, Opacity = 0.6, FontSize = 12, Margin = new Thickness(4, 10, 0, 2) });

    private void ToolButton(PneuTool tool, string text)
    {
        var b = new ToggleButton { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => SelectTool(tool);
        _toolButtons[tool] = b;
        _palette.Children.Add(b);
    }

    private void SelectTool(PneuTool tool)
    {
        _canvas.Tool = tool;
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == tool;
        _host.SetStatus(tool switch
        {
            PneuTool.Tube => "Manguera: clic en una toma, clic en cada codo y clic en la toma de destino.",
            PneuTool.Delete => "Clic sobre el componente o la manguera a borrar.",
            PneuTool.Select => "Clic para seleccionar; arrastra para mover; R para girar.",
            _ => "Clic en el lienzo para colocarlo.",
        });
    }

    // ---------------------------------------------------------------- Simulación

    private void Run()
    {
        if (_sim is null)
        {
            var (errors, warnings) = _circuit.Validate();
            if (errors.Count > 0) { ShowReport(errors, warnings); return; }
            try { _sim = new PneumaticSimulation(_circuit); }
            catch (Exception ex) { _messages.Text = "No se pudo simular: " + ex.Message; return; }
            _chart.Clear();
            _lastSample = double.NegativeInfinity;
            SetDesignLocked(true);
        }
        _lastFrame = DateTime.UtcNow;
        _timer.Start();
        _run.IsEnabled = false;
        _pause.IsEnabled = _stop.IsEnabled = true;
        _messages.Text = "Simulando. Acciona pulsadores, palancas y bobinas en el esquema.";
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
        SetDesignLocked(false);
        _run.Content = "▶ Simular";
        _run.IsEnabled = true;
        _pause.IsEnabled = _stop.IsEnabled = false;
        UpdateClock();
        _messages.Text = "Simulación detenida; vuelves al modo diseño.";
    }

    private void SetDesignLocked(bool locked)
    {
        _canvas.DesignLocked = locked;
        _canvas.Simulation = _sim;
        if (locked) SelectTool(PneuTool.Select);
        foreach (var (t, b) in _toolButtons) b.IsEnabled = !locked || t == PneuTool.Select;
        _props.Show(_canvas.Selected, _circuit, _sim);
        _view3d.InvalidateVisual();
    }

    private void Frame()
    {
        if (_sim is null) return;
        var now = DateTime.UtcNow;
        double real = Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;
        double factor = _speed.SelectedIndex switch { 0 => 0.1, 1 => 0.25, 3 => 1, _ => 0.5 };
        _sim.Step(real * factor);

        double t = _sim.Time;
        if (t - _lastSample >= 0.01)
        {
            _lastSample = t;
            foreach (var c in _circuit.Components.OfType<Cylinder>()) _chart.Add($"Posición {c.Id}", t, _sim.PositionRatio(c.Id) * 100);
        }
        _canvas.InvalidateVisual();
        if (_views.SelectedIndex == 1) _view3d.Animate(real);
        _props.RefreshLive();
        UpdateClock();
    }

    private void UpdateClock() => _clock.Text = $"t = {_sim?.Time ?? 0:0.00} s";

    // ---------------------------------------------------------------- Proyecto

    private void New()
    {
        Load(new PneumaticCircuit { Name = "Nuevo circuito" }, null);
        _messages.Text = "Circuito nuevo. Empieza colocando una fuente de aire.";
    }

    private void Load(PneumaticCircuit circuit, string? path)
    {
        if (_sim is not null) Stop();
        _circuit = circuit;
        _path = path;
        _dirty = false;
        _canvas.Circuit = circuit;
        _view3d.SelectedId = null;
        _view3d.ResetFraming();
        _props.Show(null, circuit, null);
        _chart.Clear();
        _host.SetStatus(path is null ? circuit.Name : $"{circuit.Name} — {path}");
    }

    private void MarkDirty()
    {
        _dirty = true;
        _host.SetStatus($"{_circuit.Name}{(_path is null ? "" : " — " + _path)} (sin guardar)");
    }

    private static readonly FilePickerFileType ProjectType = new("Circuito neumático") { Patterns = ["*" + PneumaticFile.Extension] };

    private async Task Open()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Abrir circuito neumático", FileTypeFilter = [ProjectType] });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var reader = new StreamReader(s);
            Load(PneumaticFile.Deserialize(await reader.ReadToEndAsync()), files[0].TryGetLocalPath());
        }
        catch (Exception ex) { _messages.Text = "No se pudo abrir: " + ex.Message; }
    }

    private async Task Save(bool askPath)
    {
        string json = PneumaticFile.Serialize(_circuit);
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
            Title = "Guardar circuito neumático", DefaultExtension = PneumaticFile.Extension.TrimStart('.'),
            SuggestedFileName = _circuit.Name + PneumaticFile.Extension, FileTypeChoices = [ProjectType],
        });
        if (file is null) return;
        await using (var s = await file.OpenWriteAsync())
        await using (var w = new StreamWriter(s))
            await w.WriteAsync(json);
        _path = file.TryGetLocalPath();
        _dirty = false;
        _host.SetStatus($"Guardado en {_path ?? file.Name}");
    }

    private async Task ExportBom()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar lista de materiales", DefaultExtension = "md", SuggestedFileName = _circuit.Name + " - materiales.md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        });
        if (file is null) return;
        await using (var s = await file.OpenWriteAsync())
        await using (var w = new StreamWriter(s))
            await w.WriteAsync(PneumaticFile.BillOfMaterials(_circuit));
        _host.SetStatus($"Lista de materiales guardada: {file.Name}");
    }

    private void Check()
    {
        var (errors, warnings) = _circuit.Validate();
        ShowReport(errors, warnings);
    }

    private void ShowReport(List<string> errors, List<string> warnings)
    {
        if (errors.Count == 0 && warnings.Count == 0) { _messages.Text = "El circuito está listo para simular."; return; }
        _messages.Text = string.Join("   ", errors.Select(e => "✕ " + e).Concat(warnings.Select(w => "⚠ " + w)));
    }

    // ---------------------------------------------------------------- Ejemplos y vista 3D

    private Button ExamplesButton()
    {
        var menu = new MenuFlyout();
        foreach (var sample in PneumaticSamples.All)
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
                var c = sample.Create();
                Load(c, null);
                _messages.Text = c.Description;
            };
            menu.Items.Add(item);
        }
        return new Button { Content = "Ejemplos ▾", Margin = new Thickness(2), Flyout = menu };
    }

    private Control Build3DPanel()
    {
        var bar = new WrapPanel { Margin = new Thickness(6, 4) };
        bar.Children.Add(ToolbarButton("Encuadrar", _view3d.FitAll));
        bar.Children.Add(ToolbarButton("Perspectiva", () => _view3d.SetView(-70, 40)));
        bar.Children.Add(ToolbarButton("Planta", () => _view3d.SetView(-90, 89)));
        var labels = new CheckBox { Content = "Etiquetas", IsChecked = true, Margin = new Thickness(8, 0) };
        labels.IsCheckedChanged += (_, _) => { _view3d.ShowLabels = labels.IsChecked == true; _view3d.InvalidateVisual(); };
        bar.Children.Add(labels);
        bar.Children.Add(new TextBlock
        {
            Text = "Maqueta del circuito montado en un tablero de prácticas.",
            VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, Margin = new Thickness(8, 0),
        });
        _view3d.SetView(-70, 40);
        var panel = new DockPanel();
        panel.Children.Add(Dock(bar, Avalonia.Controls.Dock.Top));
        panel.Children.Add(_view3d);
        return panel;
    }

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

    public bool HasUnsavedChanges => _dirty;
}
