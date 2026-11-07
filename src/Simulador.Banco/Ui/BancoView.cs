using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Simulador.Banco.Scenarios;
using Simulador.Core.CoSim;
using Simulador.Core.Modules;

namespace Simulador.Banco.Ui;

/// <summary>
/// Vista raíz del banco de pruebas: selector de montaje, mandos de simulación (marcha, pausa, paso, velocidad),
/// vista animada, mandos de maniobra y fallos, lista de conexiones, valores y gráfica de tendencias.
/// </summary>
public sealed class BancoView : UserControl
{
    private static readonly (string Label, double Factor)[] Speeds =
        [("×0,1", 0.1), ("×1", 1), ("×10", 10), ("×60", 60), ("Máx.", double.PositiveInfinity)];

    private readonly IModuleHost _host;
    private readonly PlantCanvas _canvas = new();
    private readonly HistoryChart _chart = new() { Height = 200 };
    private readonly ComboBox _scenarios = new() { MinWidth = 330 };
    private readonly ComboBox _speed = new() { ItemsSource = Speeds.Select(s => s.Label).ToArray(), SelectedIndex = 2, MinWidth = 70 };
    private readonly Button _play = new() { Content = "▶ Marcha" };
    private readonly Button _pause = new() { Content = "⏸ Pausa", IsEnabled = false };
    private readonly Button _step = new() { Content = "⏭ Paso 0,1 s" };
    private readonly Button _reset = new() { Content = "⟲ Reiniciar" };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0), FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 6), Opacity = 0.85 };
    private readonly WrapPanel _controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 6) };
    private readonly TextBlock _values = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Margin = new Thickness(8) };
    private readonly TextBlock _wiring = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _program = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private Scenario? _scenario;
    private SimRunner? _runner;
    private DateTime _last = DateTime.UtcNow;

    public BancoView(IModuleHost host)
    {
        _host = host;
        _scenarios.ItemsSource = Scenario.All.Select(s => s.Name).ToArray();
        _scenarios.SelectionChanged += (_, _) => Load(_scenarios.SelectedIndex);

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        toolbar.Children.Add(new TextBlock { Text = "Montaje", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) });
        toolbar.Children.Add(_scenarios);
        foreach (var b in new[] { _play, _pause, _step, _reset }) { b.Margin = new Thickness(4, 0, 0, 0); toolbar.Children.Add(b); }
        toolbar.Children.Add(new TextBlock { Text = "Velocidad", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        toolbar.Children.Add(_speed);
        toolbar.Children.Add(_clock);

        _play.Click += (_, _) => { _runner?.Play(); SetRunning(true); };
        _pause.Click += (_, _) => { _runner?.Pause(); SetRunning(false); };
        _step.Click += (_, _) => _runner?.StepOnce(100);
        _reset.Click += (_, _) => ResetScenario();
        _speed.SelectionChanged += (_, _) => { if (_runner is not null) _runner.Speed = Speeds[Math.Max(0, _speed.SelectedIndex)].Factor; };

        var side = new TabControl
        {
            Width = 380,
            Items =
            {
                Tab("Valores", new ScrollViewer { Content = _values }),
                Tab("Conexiones", new ScrollViewer { Content = _wiring }),
                Tab("Programa PLC", _program),
            },
        };

        var top = new StackPanel { Children = { toolbar, _description, _controls } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var chartBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6, 4, 6, 0) };
        var live = new Button { Content = "● En vivo", Padding = new Thickness(8, 2) };
        var all = new Button { Content = "↔ Ver todo el proceso", Padding = new Thickness(8, 2) };
        live.Click += (_, _) => _chart.GoLive();
        all.Click += (_, _) => _chart.ShowAll();
        chartBar.Children.Add(new TextBlock { Text = "Tendencias", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var popChart = new Button { Content = "⧉ Abrir gráfica en otra ventana", Padding = new Thickness(8, 2) };
        var popPlant = new Button { Content = "⧉ Abrir planta en otra ventana", Padding = new Thickness(8, 2) };
        popChart.Click += (_, _) => OpenChartWindow();
        popPlant.Click += (_, _) => OpenPlantWindow();
        chartBar.Children.Add(live);
        chartBar.Children.Add(all);
        chartBar.Children.Add(popChart);
        chartBar.Children.Add(popPlant);
        var chartPanel = new DockPanel();
        DockPanel.SetDock(chartBar, Dock.Top);
        chartPanel.Children.Add(chartBar);
        chartPanel.Children.Add(_chart);
        var bottom = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Child = chartPanel };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var sideBorder = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1, 0, 0, 0), Child = side };
        DockPanel.SetDock(sideBorder, Dock.Right);
        root.Children.Add(sideBorder);
        root.Children.Add(_canvas);
        Content = root;

        _timer.Tick += (_, _) => Frame();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); _runner?.Pause(); };
        _scenarios.SelectedIndex = 0;
    }

    private static TabItem Tab(string header, Control content) =>
        new() { Header = new TextBlock { Text = header, FontSize = 14 }, Content = content, Padding = new Thickness(8, 4) };

    private HashSet<string> _wired = [];
    private readonly List<HistoryChart> _extraCharts = [];
    private readonly List<PlantCanvas> _extraPlants = [];

    /// <summary>La misma gráfica (mismo historial) en una ventana suelta, para llevarla a otra pantalla.</summary>
    private void OpenChartWindow()
    {
        var chart = new HistoryChart(_chart.History);
        _extraCharts.Add(chart);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6) };
        var live = new Button { Content = "● En vivo" };
        var all = new Button { Content = "↔ Ver todo el proceso" };
        live.Click += (_, _) => chart.GoLive();
        all.Click += (_, _) => chart.ShowAll();
        bar.Children.Add(live);
        bar.Children.Add(all);
        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(chart);
        ShowDetached($"Tendencias · {_scenario?.Name}", panel, 1100, 520, () => _extraCharts.Remove(chart));
    }

    /// <summary>La vista animada de la planta en una ventana suelta; sigue al montaje que esté cargado.</summary>
    private void OpenPlantWindow()
    {
        var plant = new PlantCanvas { Scenario = _scenario };
        if (_runner is not null) plant.Update(_runner.Latest, 0);
        _extraPlants.Add(plant);
        ShowDetached($"Planta · {_scenario?.Name}", plant, 1200, 720, () => _extraPlants.Remove(plant));
    }

    private void ShowDetached(string title, Control content, double w, double h, Action closed)
    {
        var win = new Window { Title = $"Banco de pruebas · {title}", Width = w, Height = h, Content = content, Background = Brushes.White };
        win.Closed += (_, _) => closed();
        if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner); else win.Show();
    }

    private void Load(int index)
    {
        if (index < 0) return;
        _runner?.Dispose();
        _scenario = Scenario.All[index].Create();
        _runner = new SimRunner(_scenario.Scheduler) { Speed = Speeds[Math.Max(0, _speed.SelectedIndex)].Factor };
        var trends = _scenario.Trends;
        _chart.History.Clear(trends.Select(t => t.Name).ToList());
        // Se muestrea en el hilo de la simulación, en tiempo simulado: a ×60 o a velocidad máxima no se salta nada.
        _runner.FramePublished += (_, fr) => _chart.History.Offer(fr.Time.TotalSeconds,
            () => trends.Select(t => Math.Clamp(t.Percent(fr), 0, 110)).ToArray());
        _chart.History.Offer(0, () => trends.Select(t => Math.Clamp(t.Percent(_runner.Latest), 0, 110)).ToArray());
        _chart.GoLive();
        _runner.Faulted += (_, ex) => Dispatcher.UIThread.Post(() => { SetRunning(false); _host.SetStatus($"Simulación detenida: {ex.Message}"); });
        _canvas.Scenario = _scenario;
        foreach (var pl in _extraPlants) { pl.Scenario = _scenario; pl.Update(_runner.Latest, 0); }
        foreach (var ch in _extraCharts) ch.GoLive();
        _description.Text = _scenario.Description;
        _program.Text = _scenario switch
        {
            TankFillScenario => TankFillScenario.Program,
            TankFillElectricScenario => TankFillElectricScenario.Program,
            TurboCompressorScenario => TurboCompressorScenario.Program,
            _ => "(este montaje no lleva PLC: el regulador GOV es un PI genérico)",
        };
        _wiring.Text = string.Join("\n\n", _scenario.Wiring.GroupBy(w => w.Kind).Select(g =>
            $"── {g.Key} ──\n" + string.Join("\n", g.Select(w => $"{w.Terminal,-14} {w.From} → {w.To}\n{"",-14} {w.Detail}"))));
        _wired = _scenario.Wiring.SelectMany(w => new[] { w.From, w.To }).ToHashSet();
        BuildControls();
        SetRunning(false);
        _canvas.Update(_runner.Latest, 0);
        _host.SetStatus($"Banco de pruebas: {_scenario.Name}");
    }

    private void BuildControls()
    {
        _controls.Children.Clear();
        if (_scenario is null) return;
        foreach (var sl in _scenario.Sliders)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), MinWidth = 70 };
            var slider = new Slider { Minimum = sl.Min, Maximum = sl.Max, Value = sl.Initial, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            void Show() => label.Text = $"{slider.Value:0.##} {sl.Unit}";
            Show();
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                Show();
                double v = slider.Value;
                _runner?.Post(() => sl.Apply(v));
            };
            _controls.Children.Add(new TextBlock { Text = sl.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) });
            _controls.Children.Add(slider);
            _controls.Children.Add(label);
        }
        foreach (var a in _scenario.Actions)
        {
            var b = new Button { Content = a.Name, Margin = new Thickness(4, 0) };
            b.Click += (_, _) => _runner?.Post(a.Run);
            _controls.Children.Add(b);
        }
    }

    private void ResetScenario()
    {
        if (_scenario is null || _runner is null) return;
        var sc = _scenario;
        _runner.Pause();
        _runner.Reset(sc.AfterReset);
        _chart.History.Clear();
        _chart.GoLive();
        SetRunning(false);
        BuildControls();
    }

    private void SetRunning(bool running)
    {
        _play.IsEnabled = !running;
        _pause.IsEnabled = running;
        _step.IsEnabled = !running;
    }

    private void Frame()
    {
        var now = DateTime.UtcNow;
        double wall = Math.Min((now - _last).TotalSeconds, 0.2);
        _last = now;
        if (_runner is null || _scenario is null) return;
        var frame = _runner.Latest;
        bool turning = !_runner.IsPaused;
        _canvas.Update(frame, turning ? wall : 0);

        double t = frame.Time.TotalSeconds;
        _chart.Refresh();
        foreach (var ch in _extraCharts) ch.Refresh();
        foreach (var pl in _extraPlants) pl.Update(frame, turning ? wall : 0);
        _clock.Text = $"t = {TimeSpan.FromSeconds(t):hh\\:mm\\:ss\\.f}";
        _values.Text = string.Join("\n", frame.Components.Select(c =>
            $"{c.ComponentId}\n" + string.Join("\n", c.Values.Where(kv => Show(c.ComponentId, kv.Key))
                .Take(24).Select(kv => $"  {kv.Key,-18} {Format(kv.Value)}"))));
    }

    /// <summary>De un PLC solo se listan las E/S cableadas en el montaje, no las 36 de la CPU.</summary>
    private bool Show(string component, string key) =>
        !key.StartsWith(component + ".%", StringComparison.Ordinal) || _wired.Contains(key);

    private static string Format(object v) => v switch
    {
        double d => Math.Abs(d) < 0.0005 ? "0" : Math.Abs(d) >= 1000 ? d.ToString("0") : d.ToString("0.###"),
        bool b => b ? "sí" : "no",
        _ => v.ToString() ?? "",
    };
}
