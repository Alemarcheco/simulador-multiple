using Simulador.Banco.Scenarios;
using Simulador.Banco.Ui;
using Simulador.Core.CoSim;

namespace Simulador.Banco.Tests;

public class TrendHistoryTests
{
    private static TrendHistory Filled(double seconds, double interval = 0.1, int max = 500_000)
    {
        var h = new TrendHistory(interval, max);
        h.Clear(["a", "b"]);
        for (double t = 0; t <= seconds + 1e-9; t += 0.016) h.Offer(t, () => [t % 100, 50]);
        return h;
    }

    [Fact]
    public void Keeps_the_whole_process_from_the_first_second()
    {
        var h = Filled(3600);
        Assert.NotNull(h.Range);
        Assert.InRange(h.Range!.Value.Start, 0, 0.001);
        Assert.InRange(h.Range!.Value.End, 3599.8, 3600.01);
        Assert.InRange(h.Count, 35_000, 36_100);
    }

    [Fact]
    public void Thins_instead_of_dropping_the_start_when_full()
    {
        var h = Filled(600, interval: 0.1, max: 2000);
        Assert.True(h.Count <= 2000);
        Assert.InRange(h.Range!.Value.Start, 0, 0.001);
        Assert.InRange(h.Range!.Value.End, 599, 600.01);
        Assert.True(h.Interval > 0.1);
    }

    [Fact]
    public void Window_reduces_points_but_keeps_peaks()
    {
        var h = new TrendHistory(0.1);
        h.Clear(["x"]);
        for (int i = 0; i <= 36_000; i++) { double t = i * 0.1; h.Offer(t, () => [i == 18_000 ? 100 : 10]); }
        var pts = h.Window(0, 0, 3600, 300);
        Assert.True(pts.Count <= 700, $"{pts.Count} puntos");
        Assert.Contains(pts, p => p.V == 100);
        var zoomed = h.Window(0, 1000, 1010, 300);
        Assert.InRange(zoomed.Count, 100, 103);
        Assert.True(zoomed[0].T <= 1000 && zoomed[^1].T >= 1010);
    }

    [Fact]
    public void At_reads_the_closest_sample_and_restarts_after_a_reset()
    {
        var h = Filled(100);
        var r = h.At(42.04);
        Assert.NotNull(r);
        Assert.InRange(r!.Value.Time, 41.95, 42.15);
        h.Offer(0, () => [1, 2]);                // el tiempo vuelve atrás: reinicio
        Assert.Equal(1, h.Count);
    }

    [Fact]
    public void Runner_feeds_history_in_simulated_time_even_at_max_speed()
    {
        var sc = new TurboGeneratorScenario();
        var h = new TrendHistory(0.1);
        h.Clear(sc.Trends.Select(t => t.Name).ToList());
        using var runner = new SimRunner(sc.Scheduler) { Speed = double.PositiveInfinity };
        runner.FramePublished += (_, fr) => h.Offer(fr.Time.TotalSeconds, () => sc.Trends.Select(t => t.Percent(fr)).ToArray());
        runner.Play();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (sc.Scheduler.Now < TimeSpan.FromSeconds(30) && DateTime.UtcNow < deadline) Thread.Sleep(20);
        runner.Pause();
        Thread.Sleep(50);
        double end = h.Range!.Value.End;
        Assert.True(end >= 29, $"solo llegó a {end} s");
        Assert.InRange(h.Count, end / 0.1 * 0.95, end / 0.1 + 2);   // sin huecos
    }
}
