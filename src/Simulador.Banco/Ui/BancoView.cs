using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Simulador.Banco.Scenarios;
using Simulador.Core.CoSim;
using Simulador.Core.Modules;
using Simulador.Hidraulica.Ui;

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
    private readonly TrendChart _chart = new() { Height = 170 };
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
    private double _lastSample = double.NegativeInfinity;

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
        var bottom = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Child = _chart };
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

    private void Load(int index)
    {
        if (index < 0) return;
        _runner?.Dispose();
        _scenario = Scenario.All[index].Create();
        _runner = new SimRunner(_scenario.Scheduler) { Speed = Speeds[Math.Max(0, _speed.SelectedIndex)].Factor };
        _runner.Faulted += (_, ex) => Dispatcher.UIThread.Post(() => { SetRunning(false); _host.SetStatus($"Simulación detenida: {ex.Message}"); });
        _canvas.Scenario = _scenario;
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
        _chart.Clear();
        _lastSample = double.NegativeInfinity;
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
        _chart.Clear();
        _lastSample = double.NegativeInfinity;
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
        if (t < _lastSample) { _chart.Clear(); _lastSample = double.NegativeInfinity; }
        if (t - _lastSample >= 0.5)
        {
            _lastSample = t;
            foreach (var tr in _scenario.Trends) _chart.Add(tr.Name, t, Math.Clamp(tr.Percent(frame), 0, 110));
        }
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
