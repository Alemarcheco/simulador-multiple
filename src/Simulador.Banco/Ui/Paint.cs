using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Simulador.Banco.Ui;

/// <summary>Dibujo 2D de los equipos del banco de pruebas (símbolos sencillos y animados).</summary>
internal sealed class Paint(DrawingContext ctx)
{
    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly Typeface Bold = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);

    public static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1F2328"));
    public static readonly IBrush Quiet = new SolidColorBrush(Color.Parse("#57606A"));
    public static readonly IBrush Steel = new SolidColorBrush(Color.Parse("#8C959F"));
    public static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#F6F8FA"));
    public static readonly IBrush Water = new SolidColorBrush(Color.Parse("#4A90D9"));
    public static readonly IBrush WaterLight = new SolidColorBrush(Color.Parse("#CFE3F7"));
    public static readonly IBrush Air = new SolidColorBrush(Color.Parse("#B7C7D6"));
    public static readonly IBrush Live = new SolidColorBrush(Color.Parse("#D1242F"));
    public static readonly IBrush On = new SolidColorBrush(Color.Parse("#2DA44E"));
    public static readonly IBrush Off = new SolidColorBrush(Color.Parse("#D0D7DE"));
    public static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#BF8700"));
    public static readonly IBrush Steam = new SolidColorBrush(Color.Parse("#E8590C"));
    public static readonly IBrush White = Brushes.White;

    public DrawingContext Ctx => ctx;

    public void Text(string s, double x, double y, double size = 12, IBrush? brush = null, bool bold = false, bool center = false)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Font, size, brush ?? Ink);
        ctx.DrawText(ft, new Point(center ? x - ft.Width / 2 : x, y));
    }

    public void Box(Rect r, IBrush? fill = null, IBrush? stroke = null, double thick = 1.25, double radius = 6) =>
        ctx.DrawRectangle(fill, new Pen(stroke ?? Steel, thick), r, radius, radius);

    public void Line(double x1, double y1, double x2, double y2, IBrush brush, double thick = 2, double[]? dash = null, double offset = 0) =>
        ctx.DrawLine(new Pen(brush, thick, dash is null ? null : new DashStyle(dash, offset)), new Point(x1, y1), new Point(x2, y2));

    public void Poly(IBrush brush, double thick, double[]? dash, double offset, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(brush, thick, dash is null ? null : new DashStyle(dash, offset)) { LineJoin = PenLineJoin.Round }, g);
    }

    public void Shape(IBrush? fill, IBrush? stroke, double thick, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        ctx.DrawGeometry(fill, stroke is null ? null : new Pen(stroke, thick), g);
    }

    public void Circle(double cx, double cy, double r, IBrush? fill, IBrush? stroke = null, double thick = 1.5) =>
        ctx.DrawEllipse(fill, stroke is null ? null : new Pen(stroke, thick), new Point(cx, cy), r, r);

    /// <summary>Tubería: un tubo gris y, si hay caudal, guiones de agua que avanzan con él.</summary>
    public void Pipe(double flowPhase, bool flowing, IBrush fluid, params Point[] pts)
    {
        Poly(Steel, 9, null, 0, pts);
        Poly(flowing ? WaterLight : Panel, 6, null, 0, pts);
        if (flowing) Poly(fluid, 3, [2, 2], -flowPhase, pts);
    }

    /// <summary>Cable de potencia: rojo con tensión, gris sin ella.</summary>
    public void Cable(bool live, params Point[] pts) => Poly(live ? Live : Steel, live ? 3 : 2, null, 0, pts);

    /// <summary>Hilo de señal entre bornes: discontinuo, verde activo.</summary>
    public void Signal(bool active, params Point[] pts) => Poly(active ? On : Quiet, 1.5, [4, 3], 0, pts);

    public void Led(double cx, double cy, bool on, string label)
    {
        Circle(cx, cy, 5, on ? On : Off, Quiet, 1);
        Text(label, cx + 9, cy - 8, 11, Quiet);
    }

    /// <summary>Rotor con aspas (motor, bomba, turbina, compresor). <paramref name="angle"/> en radianes.</summary>
    public void Rotor(double cx, double cy, double r, double angle, int blades, IBrush brush)
    {
        for (int i = 0; i < blades; i++)
        {
            double a = angle + i * 2 * Math.PI / blades;
            var tip = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var side = new Point(cx + r * 0.55 * Math.Cos(a + 0.6), cy + r * 0.55 * Math.Sin(a + 0.6));
            Shape(brush, null, 0, new Point(cx, cy), side, tip);
        }
        Circle(cx, cy, r * 0.18, Ink);
    }

    public void Motor(double cx, double cy, double angle, bool energized, string tag)
    {
        Box(new Rect(cx - 46, cy - 34, 92, 68), Panel, energized ? Live : Steel, 2, 10);
        for (int i = -3; i <= 3; i++) Line(cx + i * 11, cy - 34, cx + i * 11, cy - 42, Steel, 2);
        Circle(cx, cy, 24, White, Steel, 1.5);
        Rotor(cx, cy, 20, angle, 4, energized ? Live : Steel);
        Text(tag, cx, cy + 38, 12, Ink, true, true);
    }

    public void Pump(double cx, double cy, double angle, bool running, string tag)
    {
        Circle(cx, cy, 34, Panel, running ? Water : Steel, 2);
        Rotor(cx, cy, 26, angle, 6, running ? Water : Steel);
        if (tag.Length > 0) Text(tag, cx, cy + 38, 12, Ink, true, true);
    }

    /// <summary>Tanque vertical con nivel; <paramref name="ratio"/> 0–1.</summary>
    public void Tank(Rect r, double ratio, string tag, double low = -1, double high = -1)
    {
        Box(r, White, Steel, 2, 8);
        double h = (r.Height - 6) * Math.Clamp(ratio, 0, 1);
        ctx.DrawRectangle(Water, null, new Rect(r.X + 3, r.Bottom - 3 - h, r.Width - 6, h), 4, 4);
        if (low >= 0) Mark(r, low, "L");
        if (high >= 0) Mark(r, high, "H");
        Text(tag, r.Center.X, r.Y - 18, 12, Ink, true, true);
        Text($"{ratio:P0}", r.Center.X, r.Bottom - 22, 13, White, true, true);
    }

    private void Mark(Rect r, double f, string s)
    {
        double y = r.Bottom - 3 - (r.Height - 6) * f;
        Line(r.Right - 12, y, r.Right + 6, y, Warn, 2);
        Text(s, r.Right + 8, y - 8, 11, Warn, true);
    }

    /// <summary>Contactor: tres polos que bajan al cerrar y una bobina. Térmico debajo.</summary>
    public void Contactor(double x, double y, bool closed, bool coil, bool tripped, bool live, string tag)
    {
        Box(new Rect(x, y, 96, 108), Panel, coil ? On : Steel, coil ? 2 : 1.25);
        for (int i = 0; i < 3; i++)
        {
            double px = x + 22 + i * 26;
            Line(px, y + 8, px, y + 24, live ? Live : Steel, 2);
            if (closed) Line(px, y + 24, px, y + 46, live ? Live : Steel, 2.5);
            else Line(px, y + 24, px + 10, y + 42, Steel, 2.5);
            Line(px, y + 46, px, y + 58, closed && live ? Live : Steel, 2);
        }
        Line(x + 14, y + 34, x + 82, y + 34, Quiet, 1, [3, 3]);
        Box(new Rect(x + 30, y + 64, 36, 16), coil ? On : White, Quiet, 1, 2);
        Text("A1", x + 8, y + 64, 10, Quiet);
        Box(new Rect(x + 8, y + 86, 80, 16), tripped ? Live : White, Quiet, 1, 2);
        Text(tripped ? "TÉRMICO ✕" : "térmico", x + 48, y + 87, 10, tripped ? White : Quiet, tripped, true);
        Text(tag, x + 48, y - 18, 12, Ink, true, true);
    }

    public void Plc(Rect r, string tag, bool run, (string Label, bool On)[] leds, string analog)
    {
        Box(r, Panel, run ? On : Warn, 2, 6);
        Text(tag, r.X + 10, r.Y + 6, 12, Ink, true);
        Text(run ? "RUN" : "STOP", r.Right - 40, r.Y + 6, 11, run ? On : Warn, true);
        for (int i = 0; i < leds.Length; i++) Led(r.X + 16, r.Y + 34 + i * 18, leds[i].On, leds[i].Label);
        Text(analog, r.X + 10, r.Bottom - 20, 11, Quiet);
    }

    /// <summary>Caja de bornes con regleta numerada.</summary>
    public void TerminalBox(double x, double y, string tag, int terminals)
    {
        Box(new Rect(x, y, 34, 14 + terminals * 16), White, Quiet, 1.25, 3);
        Text(tag, x + 17, y - 16, 11, Quiet, true, true);
        for (int i = 0; i < terminals; i++)
        {
            Box(new Rect(x + 5, y + 7 + i * 16, 24, 12), Panel, Quiet, 0.75, 2);
            Text($"{i + 1}", x + 17, y + 6 + i * 16, 9.5, Quiet, false, true);
        }
    }

    public static Point Terminal(double x, double y, int index) => new(x + 17, y + 13 + (index - 1) * 16);

    /// <summary>Válvula (dos triángulos) con su apertura.</summary>
    public void Valve(double cx, double cy, double opening, string tag)
    {
        var fill = opening > 0.02 ? Water : Off;
        Shape(fill, Quiet, 1.25, new Point(cx - 14, cy - 10), new Point(cx, cy), new Point(cx - 14, cy + 10));
        Shape(fill, Quiet, 1.25, new Point(cx + 14, cy - 10), new Point(cx, cy), new Point(cx + 14, cy + 10));
        Line(cx, cy, cx, cy - 18, Quiet, 1.5);
        Box(new Rect(cx - 9, cy - 28, 18, 10), White, Quiet, 1, 2);
        Text(tag.Length > 0 ? $"{tag} {opening:P0}" : "", cx + 18, cy + 2, 11, Ink);
    }

    /// <summary>Indicador de aguja (frecuencímetro, manómetro).</summary>
    public void Gauge(double cx, double cy, double r, double value, double min, double max, string label, string text, double? okLow = null, double? okHigh = null)
    {
        Circle(cx, cy, r, White, Quiet, 1.5);
        double Ang(double v) => Math.PI * (0.75 + 1.5 * Math.Clamp((v - min) / (max - min), 0, 1));
        if (okLow is { } lo && okHigh is { } hi)
            for (double v = lo; v <= hi; v += (max - min) / 60)
                Line(cx + (r - 6) * Math.Cos(Ang(v)), cy + (r - 6) * Math.Sin(Ang(v)), cx + (r - 2) * Math.Cos(Ang(v)), cy + (r - 2) * Math.Sin(Ang(v)), On, 3);
        for (int i = 0; i <= 10; i++)
        {
            double a = Ang(min + (max - min) * i / 10);
            Line(cx + (r - 10) * Math.Cos(a), cy + (r - 10) * Math.Sin(a), cx + (r - 4) * Math.Cos(a), cy + (r - 4) * Math.Sin(a), Quiet, 1);
        }
        double av = Ang(value);
        Line(cx, cy, cx + (r - 12) * Math.Cos(av), cy + (r - 12) * Math.Sin(av), Live, 2.5);
        Circle(cx, cy, 3, Ink);
        Text(text, cx, cy + r * 0.3, 12, Ink, true, true);
        Text(label, cx, cy + r + 4, 11, Quiet, false, true);
    }

    /// <summary>Turbina: trapecio que se ensancha con rotor animado.</summary>
    public void Turbine(double x, double y, double angle, double admission, bool tripped, string kind, string tag)
    {
        var stroke = tripped ? Live : Steel;
        Shape(Panel, stroke, 2, new Point(x, y + 30), new Point(x + 150, y), new Point(x + 150, y + 140), new Point(x, y + 110));
        for (int i = 0; i < 4; i++)
        {
            double cx = x + 30 + i * 34, h = 30 + i * 12;
            Line(cx, y + 70 - h, cx, y + 70 + h, admission > 0.01 ? Steam : Steel, 2, [6, 3], -angle * 8);
        }
        Rotor(x + 75, y + 70, 16, angle, 5, admission > 0.01 ? Steam : Steel);
        Text(tag, x + 75, y - 20, 12, Ink, true, true);
        Text($"{kind} · admisión {admission:P0}" + (tripped ? " · DISPARO" : ""), x + 75, y + 146, 11, tripped ? Live : Quiet, tripped, true);
    }

    public void Generator(double cx, double cy, double angle, bool excited, string tag)
    {
        Circle(cx, cy, 48, Panel, excited ? Live : Steel, 2);
        Rotor(cx, cy, 36, angle, 2, excited ? Live : Steel);
        Text("G", cx, cy - 10, 18, Ink, true, true);
        Text(tag, cx, cy + 52, 12, Ink, true, true);
    }

    public void Compressor(double cx, double cy, double angle, bool running, string tag)
    {
        Shape(Panel, running ? Water : Steel, 2, new Point(cx - 40, cy - 50), new Point(cx + 40, cy - 25), new Point(cx + 40, cy + 25), new Point(cx - 40, cy + 50));
        Rotor(cx, cy, 22, angle, 7, running ? Water : Steel);
        Text(tag, cx, cy + 54, 12, Ink, true, true);
    }

    /// <summary>Lámpara de una carga: más brillo con más tensión.</summary>
    public void Lamp(double cx, double cy, double brightness)
    {
        var glow = new SolidColorBrush(Color.FromArgb((byte)(40 + 200 * Math.Clamp(brightness, 0, 1)), 255, 196, 0));
        Circle(cx, cy, 9, glow, Quiet, 1.25);
        Line(cx - 5, cy - 5, cx + 5, cy + 5, Quiet, 1);
        Line(cx + 5, cy - 5, cx - 5, cy + 5, Quiet, 1);
    }

    public void Shaft(double x1, double y, double x2, double angle, bool turning)
    {
        Line(x1, y, x2, y, Steel, 8);
        if (turning) Line(x1, y, x2, y, Ink, 2, [3, 9], -angle * 10);
    }
}
