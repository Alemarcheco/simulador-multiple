using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.Ui;

/// <summary>Ventana de análisis de golpe de ariete al cerrar una válvula.</summary>
public sealed class WaterHammerWindow : Window
{
    private readonly HydraulicNetwork _net;
    private readonly HydraulicSimulation _sim;
    private readonly string _valveId;
    private readonly TextBox _closing = new() { Width = 90, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly LinePlot _plot = new() { XLabel = "tiempo (s)", YLabel = "presión (bar)", MinHeight = 300 };
    private readonly StackPanel _results = new() { Spacing = 4 };

    /// <param name="sim">Simulación en marcha (se usa su caudal actual) o null para partir del estado inicial del diseño.</param>
    public WaterHammerWindow(HydraulicNetwork net, HydraulicSimulation? sim, string valveId)
    {
        _net = net;
        _valveId = valveId;
        _sim = sim ?? new HydraulicSimulation(net);
        Title = $"Golpe de ariete al cerrar {valveId}";
        Width = 900;
        Height = 640;

        var valve = (Valve)net.FindLink(valveId)!;
        _closing.Text = Math.Max(valve.StrokeTime, 0).ToString("0.###", CultureInfo.CurrentCulture);
        var run = new Button { Content = "Calcular" };
        run.Click += (_, _) => Analyze();
        _closing.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Analyze(); };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(new TextBlock { Text = "Tiempo de cierre de la válvula (s):", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(_closing);
        top.Children.Add(run);

        var side = new ScrollViewer { Content = _results, Width = 300 };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), RowDefinitions = new RowDefinitions("Auto,*") };
        body.Children.Add(top);
        Grid.SetRow(_plot, 1); body.Children.Add(_plot);
        Grid.SetRow(side, 1); Grid.SetColumn(side, 2); body.Children.Add(side);
        _plot.Margin = new Thickness(0, 10, 0, 0);
        side.Margin = new Thickness(0, 10, 0, 0);
        Content = new Border { Padding = new Thickness(14), Child = body };

        Analyze();
    }

    /// <summary>Vuelve a calcular con el tiempo de cierre del cuadro (también lo usan las capturas y pruebas).</summary>
    public WaterHammerResult? Analyze()
    {
        _results.Children.Clear();
        _plot.Clear();
        if (!double.TryParse(_closing.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var tc) || tc < 0)
        {
            Line("Escribe un tiempo de cierre en segundos (0 = instantáneo).", true);
            return null;
        }
        WaterHammerResult r;
        try { r = WaterHammer.Analyze(_net, _sim, _valveId, tc); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Line(ex.Message, true);
            return null;
        }

        _plot.AddSeries($"En la válvula {_valveId}", Color.Parse("#CF222E"), r.Times, [.. r.ValvePressure.Select(p => p / 1e5)]);
        _plot.AddSeries("A media conducción", Color.Parse("#2A78D6"), r.Times, [.. r.MidPressure.Select(p => p / 1e5)]);
        _plot.AddReference("Régimen permanente", Color.Parse("#57606A"), r.SteadyPressure / 1e5);
        _plot.AddReference("Permanente + Joukowsky", Color.Parse("#E8590C"), (r.SteadyPressure + r.JoukowskyRise) / 1e5);
        if (r.MinPressure < -0.9e5) _plot.AddReference("Vapor (≈ −1 bar)", Color.Parse("#8250DF"), -0.98);

        Header("Conducción");
        Row("Tuberías", string.Join(" → ", Enumerable.Reverse(r.PipeIds)));
        Row("Longitud", $"{r.Length:0} m");
        Row("Velocidad de la onda a", $"{r.WaveSpeed:0} m/s");
        Row("Velocidad del agua", $"{r.InitialVelocity:0.00} m/s");
        Row("Tiempo crítico 2L/a", $"{r.CriticalTime:0.00} s");
        Row("Periodo 4L/a", $"{2 * r.CriticalTime:0.00} s");
        Header("Presiones en la válvula");
        Row("Antes del cierre", $"{r.SteadyPressure / 1e5:0.00} bar");
        Row("Joukowsky ρ·a·V", $"+{r.JoukowskyRise / 1e5:0.00} bar");
        Row("Máxima", $"{r.MaxPressure / 1e5:0.00} bar");
        Row("Mínima", $"{r.MinPressure / 1e5:0.00} bar");
        Header("Diagnóstico");
        Line(r.FastClosure
            ? $"Cierre rápido ({r.ClosingTime:0.##} s ≤ 2L/a): la onda vuelve antes de terminar el cierre y la sobrepresión es la completa de Joukowsky."
            : $"Cierre lento ({r.ClosingTime:0.##} s > 2L/a): la onda reflejada en el depósito amortigua parte de la sobrepresión.", false);
        if (r.ColumnSeparationRisk)
            Line("La presión mínima baja a la de vapor: la columna de agua puede romperse y, al volver a juntarse, dar golpes aún mayores. " +
                 "Cierra más despacio, instala un calderín antiariete o una válvula de alivio.", true);
        Line("Para reducir el golpe: cierra más despacio que 2L/a, baja la velocidad del agua (más diámetro) o usa tubería más elástica (PE).", false);
        return r;
    }

    public void SetClosingTime(double seconds) => _closing.Text = seconds.ToString("0.###", CultureInfo.CurrentCulture);

    private void Header(string text) =>
        _results.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 14, Margin = new Thickness(0, 8, 0, 2) });

    private void Row(string label, string value)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = label, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        var v = new TextBlock { Text = value, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        _results.Children.Add(g);
    }

    private void Line(string text, bool warning)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Opacity = warning ? 1 : 0.75 };
        if (warning) t.Foreground = new SolidColorBrush(Color.Parse("#CF222E"));
        _results.Children.Add(t);
    }
}
