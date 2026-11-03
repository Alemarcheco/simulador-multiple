using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Modules;

namespace Simulador.Circuitos.Simulation;

/// <summary>Pestaña "Simulación": elige el análisis, simula con ngspice y muestra voltímetro u osciloscopio.</summary>
public sealed class SimulationView : DockPanel
{
    private readonly IModuleHost _host;
    private readonly ComponentCatalog _catalog;
    private readonly SchematicCanvas _schematic;

    private readonly RadioButton _op = new() { Content = "Punto de trabajo (voltímetro)", GroupName = "an", IsChecked = true };
    private readonly RadioButton _tran = new() { Content = "Transitorio (osciloscopio)", GroupName = "an" };
    private readonly RadioButton _ac = new() { Content = "Respuesta en frecuencia (Bode)", GroupName = "an" };
    private readonly TextBox _stop = new() { Text = "5m" };
    private readonly TextBox _step = new() { Text = "5u" };
    private readonly TextBox _fStart = new() { Text = "10" };
    private readonly TextBox _fStop = new() { Text = "1meg" };
    private readonly StackPanel _tranFields = new() { Spacing = 4 };
    private readonly StackPanel _acFields = new() { Spacing = 4 };
    private readonly Button _run = new() { Content = "▶  Simular", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 8) };
    private readonly StackPanel _signals = new() { Spacing = 2 };
    private readonly ContentControl _output = new();
    private readonly PlotControl _plot = new();
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, Height = 110, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 11 };

    private SimulationResult? _last;
    private AnalysisKind _lastKind;
    private Dictionary<string, string> _netByVector = new(StringComparer.OrdinalIgnoreCase);

    public SimulationView(IModuleHost host, ComponentCatalog catalog, SchematicCanvas schematic)
    {
        _host = host;
        _catalog = catalog;
        _schematic = schematic;

        _tranFields.Children.Add(Label("Tiempo total"));
        _tranFields.Children.Add(_stop);
        _tranFields.Children.Add(Label("Paso máximo"));
        _tranFields.Children.Add(_step);
        _acFields.Children.Add(Label("Frecuencia inicial (Hz)"));
        _acFields.Children.Add(_fStart);
        _acFields.Children.Add(Label("Frecuencia final (Hz)"));
        _acFields.Children.Add(_fStop);
        _acFields.Children.Add(new TextBlock { Text = "Necesita una fuente con AC (el generador senoidal la trae).", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });

        foreach (var rb in new[] { _op, _tran, _ac }) rb.IsCheckedChanged += (_, _) => UpdateFields();
        _run.Click += async (_, _) => await RunAsync();

        var left = new StackPanel
        {
            Width = 250,
            Spacing = 6,
            Margin = new Thickness(0, 0, 10, 0),
            Children =
            {
                new TextBlock { Text = "Análisis", FontSize = 16, FontWeight = FontWeight.SemiBold },
                _op, _tran, _ac,
                _tranFields, _acFields,
                _run,
                new TextBlock { Text = "Señales", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
                new ScrollViewer { Content = _signals, MaxHeight = 320 },
            },
        };
        SetDock(left, Dock.Left);
        Children.Add(left);

        var logBox = new Expander { Header = "Mensajes de ngspice", Content = _log, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
        SetDock(logBox, Dock.Bottom);
        Children.Add(logBox);
        Children.Add(_output);

        _output.Content = Hint("Dibuja un circuito en la pestaña Esquemático (con masa GND) y pulsa Simular.");
        UpdateFields();
        _schematic.DocumentChanged += (_, _) => _schematic.SetOperatingPoint(null);
    }

    private static TextBlock Label(string t) => new() { Text = t, FontSize = 12, Opacity = 0.85 };

    private static Control Hint(string text) => new TextBlock
    {
        Text = text, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
    };

    private void UpdateFields()
    {
        _tranFields.IsVisible = _tran.IsChecked == true;
        _acFields.IsVisible = _ac.IsChecked == true;
    }

    private AnalysisSettings Settings() => new()
    {
        Kind = _tran.IsChecked == true ? AnalysisKind.Transient : _ac.IsChecked == true ? AnalysisKind.Ac : AnalysisKind.OperatingPoint,
        StopTime = _stop.Text?.Trim() ?? "5m",
        Step = _step.Text?.Trim() ?? "5u",
        StartFrequency = _fStart.Text?.Trim() ?? "10",
        StopFrequency = _fStop.Text?.Trim() ?? "1meg",
    };

    private async Task RunAsync()
    {
        var settings = Settings();
        var netlist = SpiceNetlister.Build(_schematic.Document, _catalog, settings.ToSpice());
        _netByVector = netlist.Connectivity.Nets
            .Where(n => n.Name != Connectivity.GroundNet)
            .GroupBy(n => n.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        _run.IsEnabled = false;
        _host.SetStatus("Simulando…");
        try
        {
            var result = await Task.Run(() => NgSpice.Run(netlist.Text));
            _last = result;
            _lastKind = settings.Kind;
            var log = new List<string>();
            if (netlist.Warnings.Count > 0) log.Add("Avisos del esquema:\n  " + string.Join("\n  ", netlist.Warnings));
            log.Add("Netlist:\n" + netlist.Text);
            log.AddRange(result.Log);
            _log.Text = string.Join("\n", log);

            if (!result.Ok)
            {
                _output.Content = Hint("La simulación falló:\n\n" + string.Join("\n", result.Errors.Take(8)) +
                    (netlist.Warnings.Count > 0 ? "\n\nRevisa también: " + string.Join("; ", netlist.Warnings) : ""));
                _host.SetStatus("La simulación falló. Mira los mensajes de ngspice.");
                return;
            }
            ShowResult();
            _host.SetStatus(netlist.Warnings.Count > 0 ? "Simulado con avisos: " + netlist.Warnings[0] : "Simulación terminada.");
        }
        catch (NgSpiceNotFoundException ex)
        {
            _output.Content = Hint(ex.Message);
            _host.SetStatus("No se encontró ngspice.");
        }
        finally
        {
            _run.IsEnabled = true;
        }
    }

    private void ShowResult()
    {
        if (_last is null) return;
        _signals.Children.Clear();

        if (_lastKind == AnalysisKind.OperatingPoint)
        {
            var volts = _netByVector
                .Where(kv => _last.Real.TryGetValue(kv.Key, out var v) && v.Length > 0)
                .ToDictionary(kv => kv.Value, kv => _last.Real[kv.Key][0]);
            _schematic.SetOperatingPoint(volts);

            var rows = new StackPanel { Spacing = 2, Margin = new Thickness(10) };
            rows.Children.Add(new TextBlock { Text = "Tensiones respecto a masa", FontSize = 16, FontWeight = FontWeight.SemiBold });
            foreach (var (net, v) in volts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                rows.Children.Add(Row(net, EngineeringFormat.Format(v, "V", 4)));
            var currents = _last.Real.Where(kv => kv.Key.EndsWith("#branch", StringComparison.OrdinalIgnoreCase)).ToList();
            if (currents.Count > 0)
            {
                rows.Children.Add(new TextBlock { Text = "Corrientes en fuentes", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
                foreach (var (name, v) in currents.OrderBy(k => k.Key))
                    rows.Children.Add(Row(name[..name.IndexOf('#')].ToUpperInvariant(), EngineeringFormat.Format(-v[0], "A", 4) + "  (sale por +)"));
            }
            rows.Children.Add(new TextBlock { Text = "Las tensiones también aparecen sobre el esquema.", Opacity = 0.7, Margin = new Thickness(0, 12, 0, 0) });
            _output.Content = new ScrollViewer { Content = rows };
            return;
        }

        bool ac = _lastKind == AnalysisKind.Ac;
        var x = ac ? _last.Complex.GetValueOrDefault("frequency")?.Select(c => c.Real).ToArray() : _last.Real.GetValueOrDefault("time");
        if (x is null) { _output.Content = Hint("ngspice no devolvió el eje X."); return; }

        var available = _netByVector.Keys.Where(k => ac ? _last.Complex.ContainsKey(k) : _last.Real.ContainsKey(k))
            .OrderBy(k => _netByVector[k], StringComparer.OrdinalIgnoreCase).ToList();
        int color = 0;
        var checks = new List<(CheckBox Box, string Key, Color Color)>();
        foreach (var key in available)
        {
            var c = PlotControl.Palette[color++ % PlotControl.Palette.Length];
            var box = new CheckBox
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { new Border { Width = 12, Height = 4, Background = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = _netByVector[key] } },
                },
                IsChecked = checks.Count < 4,
            };
            checks.Add((box, key, c));
            box.IsCheckedChanged += (_, _) => Replot();
            _signals.Children.Add(box);
        }

        void Replot()
        {
            _plot.LogX = ac;
            _plot.XUnit = ac ? "Hz" : "s";
            _plot.YUnit = ac ? "dB" : "V";
            _plot.Title = ac ? "Magnitud (dB) frente a frecuencia" : "Tensión frente a tiempo";
            _plot.SetTraces(checks.Where(c => c.Box.IsChecked == true).Select(c => new PlotTrace(
                _netByVector[c.Key], x,
                ac ? _last!.Complex[c.Key].Select(v => 20 * Math.Log10(Math.Max(v.Magnitude, 1e-20))).ToArray() : _last!.Real[c.Key],
                c.Color)));
        }
        Replot();
        _output.Content = _plot;
    }

    private static Control Row(string name, string value) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("180,*"),
        Children =
        {
            new TextBlock { Text = name },
            new TextBlock { Text = value, FontWeight = FontWeight.SemiBold, [Grid.ColumnProperty] = 1 },
        },
    };
}
