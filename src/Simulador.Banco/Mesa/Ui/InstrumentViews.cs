using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Simulador.Banco.Mesa.Ui;

/// <summary>Pantalla de un instrumento de la mesa: multímetro, osciloscopio o línea de tiempo de señales.</summary>
public sealed class InstrumentView : Control
{
    private static readonly Color[] Traces = [Color.Parse("#FFD54F"), Color.Parse("#4DD0E1"), Color.Parse("#F06292"), Color.Parse("#81C784"),
        Color.Parse("#FFB74D"), Color.Parse("#BA68C8"), Color.Parse("#90CAF9"), Color.Parse("#E0E0E0")];
    private static readonly Typeface Mono = new("Cascadia Mono, Consolas, monospace");
    private static readonly Typeface Face = new("Inter, Segoe UI, sans-serif");
    private static readonly IBrush Screen = new SolidColorBrush(Color.Parse("#10181F"));
    private static readonly IPen Div = new Pen(new SolidColorBrush(Color.Parse("#26343F")), 1);
    private static readonly IBrush Label = new SolidColorBrush(Color.Parse("#B8C7D3"));

    public InstrumentView(InstrumentComponent instrument, IReadOnlyList<string?> targets, double window)
    {
        Instrument = instrument;
        Targets = targets;
        Window = Math.Max(0.05, window);
        (Width, Height) = instrument.Kind switch
        {
            InstrumentKind.Multimeter => (300.0, 150.0),
            InstrumentKind.Oscilloscope => (620.0, 205.0),
            _ => (620.0, 205.0),
        };
    }

    public InstrumentComponent Instrument { get; }
    /// <summary>Lo que mide cada canal ("M1.Bornes"), o null si está libre.</summary>
    public IReadOnlyList<string?> Targets { get; }
    public double Window { get; }

    private static void Text(DrawingContext ctx, string s, Point at, double size, IBrush brush, Typeface face, bool right = false)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush);
        ctx.DrawText(ft, right ? new Point(at.X - ft.Width, at.Y) : at);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#2B3740")), null, r, 8, 8);
        Text(ctx, $"{Instrument.Id} · {Instrument.Kind switch { InstrumentKind.Multimeter => "multímetro", InstrumentKind.Oscilloscope => "osciloscopio", _ => "línea de tiempo" }}",
            new Point(10, 6), 12, Brushes.White, Face);
        var scr = new Rect(8, 26, r.Width - 16, r.Height - 34);
        ctx.DrawRectangle(Screen, null, scr, 4, 4);
        switch (Instrument.Kind)
        {
            case InstrumentKind.Multimeter: DrawMeter(ctx, scr); break;
            case InstrumentKind.Oscilloscope: DrawScope(ctx, scr); break;
            default: DrawLogic(ctx, scr); break;
        }
    }

    private void DrawMeter(DrawingContext ctx, Rect scr)
    {
        var last = Instrument.LastReadings();
        var rd = last.Length > 0 ? last[0] : [];
        if (rd.Length == 0) { Text(ctx, "Une la punta a un borne", new Point(scr.X + 12, scr.Y + 40), 14, Label, Face); return; }
        Text(ctx, Targets[0] ?? "", new Point(scr.X + 10, scr.Y + 6), 11, Label, Mono);
        Text(ctx, $"{Reading.Format(rd[0].Value)} {rd[0].Unit}", new Point(scr.Right - 12, scr.Y + 22), 30, new SolidColorBrush(Traces[0]), Mono, right: true);
        Text(ctx, rd[0].Name, new Point(scr.X + 10, scr.Y + 40), 12, Label, Face);
        for (int i = 1; i < Math.Min(rd.Length, 3); i++)
            Text(ctx, $"{rd[i].Name} {Reading.Format(rd[i].Value)} {rd[i].Unit}", new Point(scr.X + 10, scr.Y + 64 + (i - 1) * 18), 12, Label, Mono);
    }

    private void DrawScope(DrawingContext ctx, Rect scr)
    {
        var plot = new Rect(scr.X + 6, scr.Y + 6, scr.Width - 170, scr.Height - 24);
        for (int i = 0; i <= 10; i++) ctx.DrawLine(Div, new Point(plot.X + plot.Width * i / 10, plot.Y), new Point(plot.X + plot.Width * i / 10, plot.Bottom));
        for (int i = 0; i <= 8; i++) ctx.DrawLine(Div, new Point(plot.X, plot.Y + plot.Height * i / 8), new Point(plot.Right, plot.Y + plot.Height * i / 8));
        double t1 = Instrument.Time, t0 = Math.Max(0, t1 - Window);
        if (t1 - t0 < Window) t1 = t0 + Window;
        var samples = Instrument.Samples(t0);
        var last = Instrument.LastReadings();
        Text(ctx, $"{t0:0.##} s", new Point(plot.X, plot.Bottom + 3), 10, Label, Mono);
        Text(ctx, $"{t1:0.##} s", new Point(plot.Right, plot.Bottom + 3), 10, Label, Mono, right: true);
        double ly = scr.Y + 6;
        for (int ch = 0; ch < Targets.Count; ch++)
        {
            var color = Traces[ch % Traces.Length];
            var brush = new SolidColorBrush(color);
            if (Targets[ch] is null) { Text(ctx, $"CH{ch + 1} libre", new Point(plot.Right + 10, ly), 11, Label, Mono); ly += 44; continue; }
            double min = double.MaxValue, max = double.MinValue;
            foreach (var s in samples) { var v = s.Values[ch]; if (double.IsNaN(v)) continue; min = Math.Min(min, v); max = Math.Max(max, v); }
            if (min > max) { min = 0; max = 1; }
            if (max - min < 1e-6) { max += 0.5; min -= 0.5; }
            if (min > 0 && min < 0.3 * max) min = 0;
            double pad = (max - min) * 0.08;
            min -= pad; max += pad;
            var unit = last.Length > ch && last[ch].Length > 0 ? last[ch][0] : default;
            Text(ctx, $"CH{ch + 1} {Targets[ch]}", new Point(plot.Right + 10, ly), 11, brush, Mono);
            Text(ctx, $"{unit.Name} {Reading.Format(unit.Value)} {unit.Unit}", new Point(plot.Right + 10, ly + 14), 11, Label, Mono);
            Text(ctx, $"escala {Reading.Format(min + pad)}…{Reading.Format(max - pad)}", new Point(plot.Right + 10, ly + 27), 10, Label, Mono);
            ly += 46;
            if (samples.Count < 2) continue;
            int stride = Math.Max(1, samples.Count / (int)Math.Max(50, plot.Width * 1.5));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < samples.Count; i += stride)
                {
                    var s = samples[i];
                    double v = s.Values[ch];
                    if (double.IsNaN(v)) continue;
                    var p = new Point(plot.X + (s.Time - t0) / (t1 - t0) * plot.Width, plot.Bottom - (v - min) / (max - min) * plot.Height);
                    if (!started) { g.BeginFigure(p, false); started = true; } else g.LineTo(p);
                }
            }
            ctx.DrawGeometry(null, new Pen(brush, 1.6), geo);
        }
    }

    private void DrawLogic(DrawingContext ctx, Rect scr)
    {
        double t1 = Instrument.Time, t0 = Math.Max(0, t1 - Window);
        if (t1 - t0 < Window) t1 = t0 + Window;
        var samples = Instrument.Samples(t0);
        int lanes = Targets.Count;
        double labelW = 130, laneH = (scr.Height - 20) / lanes;
        var plot = new Rect(scr.X + labelW, scr.Y + 4, scr.Width - labelW - 8, laneH * lanes);
        for (int i = 0; i <= 10; i++) ctx.DrawLine(Div, new Point(plot.X + plot.Width * i / 10, plot.Y), new Point(plot.X + plot.Width * i / 10, plot.Bottom));
        Text(ctx, $"{t0:0.##} s", new Point(plot.X, plot.Bottom + 2), 10, Label, Mono);
        Text(ctx, $"{t1:0.##} s", new Point(plot.Right, plot.Bottom + 2), 10, Label, Mono, right: true);
        for (int ch = 0; ch < lanes; ch++)
        {
            double y0 = plot.Y + ch * laneH;
            var brush = new SolidColorBrush(Traces[ch % Traces.Length]);
            Text(ctx, $"D{ch + 1} {Targets[ch] ?? "—"}", new Point(scr.X + 6, y0 + laneH / 2 - 7), 10.5, Targets[ch] is null ? Label : brush, Mono);
            ctx.DrawLine(Div, new Point(plot.X, y0 + laneH), new Point(plot.Right, y0 + laneH));
            if (Targets[ch] is null || samples.Count < 2) continue;
            double hi = y0 + laneH * 0.2, lo = y0 + laneH * 0.8;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool? prev = null;
                foreach (var s in samples)
                {
                    bool on = s.Values[ch] > 0.5;
                    double x = plot.X + (s.Time - t0) / (t1 - t0) * plot.Width;
                    if (prev is null) g.BeginFigure(new Point(x, on ? hi : lo), false);
                    else if (prev != on) { g.LineTo(new Point(x, prev.Value ? hi : lo)); g.LineTo(new Point(x, on ? hi : lo)); }
                    prev = on;
                }
                if (prev is not null) g.LineTo(new Point(plot.X + (samples[^1].Time - t0) / (t1 - t0) * plot.Width, prev.Value ? hi : lo));
            }
            ctx.DrawGeometry(null, new Pen(brush, 1.8), geo);
        }
    }
}
