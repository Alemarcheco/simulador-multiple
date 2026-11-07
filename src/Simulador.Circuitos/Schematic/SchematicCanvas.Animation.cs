using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Simulador.Circuitos.Simulation;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Schematic;

// Animación de la corriente: puntos amarillos que avanzan por los cables a una velocidad que crece con la corriente,
// cables coloreados según su tensión (verde positiva, rojo negativa) y LEDs que se encienden.
public sealed partial class SchematicCanvas
{
    public const double DotSpacing = 2.54;
    public const double MaxDotSpeed = 14;   // mm/s con la corriente máxima

    private FlowAnimation? _anim;
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();
    private double _lastTick;
    private double[] _dotOffset = [];
    private int _frame;

    public FlowAnimation? Animation => _anim;
    /// <summary>Segundos reales que dura una vuelta completa del transitorio.</summary>
    public double PlaybackSeconds { get; private set; }
    public event EventHandler? AnimationChanged;

    public void SetAnimation(FlowAnimation? anim)
    {
        _anim = anim;
        _dotOffset = new double[anim?.Segments.Count ?? 0];
        _frame = 0;
        if (anim is null)
        {
            _timer?.Stop();
            _clock.Reset();
        }
        else
        {
            double total = anim.Times[^1];
            // Si el circuito es lento (más de 1 s) se ve en tiempo real; si no, a cámara lenta en unos 8 s.
            PlaybackSeconds = anim.IsStatic ? 0 : total >= 1 ? total : 8;
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
            _clock.Restart();
            _lastTick = 0;
            _timer.Start();
        }
        AnimationChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void Tick()
    {
        if (_anim is null) return;
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.1, now - _lastTick);
        _lastTick = now;
        if (!_anim.IsStatic)
        {
            double t = (now % PlaybackSeconds) / PlaybackSeconds * _anim.Times[^1];
            _frame = _anim.IndexAt(t);
        }
        var currents = _anim.Currents[_frame];
        for (int i = 0; i < _dotOffset.Length; i++)
        {
            double v = DotSpeed(currents[i]);
            _dotOffset[i] = ((_dotOffset[i] + v * dt) % DotSpacing + DotSpacing) % DotSpacing;
        }
        InvalidateVisual();
    }

    private double DotSpeed(double current)
    {
        if (_anim is null || _anim.MaxCurrent < 1e-12) return 0;
        double r = Math.Abs(current) / _anim.MaxCurrent;
        if (r < 1e-3) return 0;
        return Math.Sign(current) * MaxDotSpeed * Math.Sqrt(r);
    }

    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#F5C400"));
    private static readonly IPen DotPen = new Pen(new SolidColorBrush(Color.Parse("#7A6200")), 0.08);

    /// <summary>Color del cable según su tensión: gris a 0 V, verde positiva y rojo negativa.</summary>
    internal static Color VoltageColor(double v, double max)
    {
        double r = max > 1e-9 ? Math.Clamp(Math.Abs(v) / max, 0, 1) : 0;
        var gray = Color.Parse("#8A8F98");
        var target = v >= 0 ? Color.Parse("#16A34A") : Color.Parse("#DC2626");
        byte L(byte a, byte b) => (byte)(a + (b - a) * r);
        return Color.FromRgb(L(gray.R, target.R), L(gray.G, target.G), L(gray.B, target.B));
    }

    private void DrawAnimationUnder(DrawingContext ctx, double minPen)
    {
        if (_anim is null) return;
        // Tensión: se repinta cada tramo con el color de su red.
        foreach (var s in _anim.Segments)
        {
            if (!_anim.Voltages.TryGetValue(s.Net, out var v)) continue;
            var pen = new Pen(new SolidColorBrush(VoltageColor(v[_frame], _anim.MaxVoltage)), Math.Max(0.4, minPen * 2.5), lineCap: PenLineCap.Round);
            ctx.DrawLine(pen, P(s.A), P(s.B));
        }
        // LEDs encendidos: halo rojo proporcional a la corriente (20 mA = brillo máximo).
        foreach (var (reference, i) in _anim.LedCurrents)
        {
            var sym = _doc.Symbols.FirstOrDefault(s => s.Reference == reference);
            if (sym is null) continue;
            double b = Math.Clamp(i[_frame] / 0.02, 0, 1);
            if (b < 0.02) continue;
            var glow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(230 * b), 255, 40, 30), 0),
                    new GradientStop(Color.FromArgb(0, 255, 40, 30), 1),
                },
            };
            ctx.DrawEllipse(glow, null, P(sym.Position), 5, 5);
        }
    }

    private void DrawAnimationOver(DrawingContext ctx)
    {
        if (_anim is null) return;
        var currents = _anim.Currents[_frame];
        for (int k = 0; k < _anim.Segments.Count; k++)
        {
            double speed = DotSpeed(currents[k]);
            if (speed == 0) continue;
            var s = _anim.Segments[k];
            double dx = s.B.X - s.A.X, dy = s.B.Y - s.A.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) continue;
            // Los puntos avanzan de A a B si la corriente es positiva.
            for (double d = _dotOffset[k]; d < len; d += DotSpacing)
            {
                double along = speed > 0 ? d : len - d;
                var p = new Vec2(s.A.X + dx * along / len, s.A.Y + dy * along / len);
                ctx.DrawEllipse(DotBrush, DotPen, P(p), 0.4, 0.4);
            }
        }
    }

    private void DrawAnimationOverlay(DrawingContext ctx)
    {
        if (_anim is null) return;
        string text = _anim.IsStatic
            ? "⚡ Corriente en reposo (punto de trabajo)"
            : $"⚡ t = {EngineeringFormat.Format(_anim.Times[_frame], "s", 3)} de {EngineeringFormat.Format(_anim.Times[^1], "s", 3)}" +
              (PlaybackSeconds > _anim.Times[^1] * 1.01 ? $"  (cámara lenta ×{PlaybackSeconds / _anim.Times[^1]:0.#E+0})" : "");
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Black);
        var rect = new Rect(8, 8, ft.Width + 16, ft.Height + 8);
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#FFF8D6")), new Pen(new SolidColorBrush(Color.Parse("#C9A400")), 1), rect, 4, 4);
        ctx.DrawText(ft, new Point(16, 12));
    }
}
