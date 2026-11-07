using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Editor;

/// <summary>Dibuja los símbolos IEC 60617 en coordenadas locales (unidades de rejilla × <see cref="G"/>).</summary>
internal static class SymbolPainter
{
    public const double G = 16;

    public static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1B1F24"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#5F6B7A"));
    public static readonly IBrush Live = new SolidColorBrush(Color.Parse("#D32F2F"));
    public static readonly IBrush Energized = new SolidColorBrush(Color.Parse("#7CC47F"));
    public static readonly IBrush Fault = new SolidColorBrush(Color.Parse("#E53935"));
    public static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#FFFFFF"));
    public static readonly IBrush Select = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly IBrush Tint = new SolidColorBrush(Color.Parse("#F3F6FA"));

    public static readonly IPen Line = new Pen(Ink, 1.6);
    private static readonly IPen Thin = new Pen(Ink, 1);
    private static readonly IPen Dashed = new Pen(Muted, 1, new DashStyle([3, 3], 0));
    private static readonly IPen FaultPen = new Pen(Fault, 2);

    private static readonly Typeface Face = new(FontFamily.Default);

    private static Point P(double x, double y) => new(x * G, y * G);

    public static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    private static void Label(DrawingContext dc, string s, double x, double y, double size = 10, IBrush? brush = null, bool center = false)
    {
        var t = Text(s, size, brush ?? Ink);
        var px = x * G - (center ? t.Width / 2 : 0);
        dc.DrawText(t, new Point(px, y * G - t.Height / 2));
    }

    private static void L(DrawingContext dc, double x0, double y0, double x1, double y1, IPen? pen = null) =>
        dc.DrawLine(pen ?? Line, P(x0, y0), P(x1, y1));

    /// <summary>Hilo de un elemento de dos bornes en vertical: patillas de 0 a 1,2 y de 2,8 a 4.</summary>
    private static void Leads(DrawingContext dc, double x = 0, double top = 1.2, double bottom = 2.8)
    {
        L(dc, x, 0, x, top);
        L(dc, x, bottom, x, 4);
    }

    /// <summary>Cuchilla de contacto entre (x,2,8) y el borne superior.</summary>
    private static void Blade(DrawingContext dc, double x, bool closed, bool nc)
    {
        L(dc, x, 0, x, 1.2);
        L(dc, x, 2.8, x, 4);
        if (nc)
        {
            // Contacto NC: tope a la derecha; la cuchilla apoya en él cuando está cerrado.
            L(dc, x, 1.2, x + 0.75, 1.2);
            if (closed) L(dc, x, 2.8, x + 0.75, 1.05);
            else L(dc, x, 2.8, x + 0.15, 1.35);
        }
        else
        {
            if (closed) L(dc, x, 2.8, x + 0.05, 1.2);
            else L(dc, x, 2.8, x - 0.8, 1.35);
        }
    }

    private static void Actuator(DrawingContext dc, Behavior b, bool pressed)
    {
        var y = 2.0;
        var off = pressed ? 0.25 : 0;
        L(dc, -0.4, y, -1.4 + off, y, Dashed);
        switch (b)
        {
            case Behavior.PushNO or Behavior.PushNC:
                L(dc, -1.4 + off, y - 0.5, -1.4 + off, y + 0.5);
                L(dc, -1.4 + off, y - 0.5, -1.65 + off, y - 0.5);
                L(dc, -1.4 + off, y + 0.5, -1.65 + off, y + 0.5);
                break;
            case Behavior.EmergencyStop:
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(P(-1.4 + off, y - 0.7), false);
                    c.ArcTo(P(-1.4 + off, y + 0.7), new Size(0.7 * G, 0.7 * G), 0, false, SweepDirection.CounterClockwise);
                }
                dc.DrawGeometry(Fault, Line, g);
                break;
            }
            case Behavior.Switch:
                L(dc, -1.4, y - 0.5, -1.4, y + 0.5);
                L(dc, -1.4, y - 0.5, -1.15, y - 0.75);
                L(dc, -1.4, y + 0.5, -1.65, y + 0.75);
                break;
            case Behavior.LimitNO or Behavior.LimitNC:
                L(dc, -1.4 + off, y, -1.9 + off, y - 0.5);
                L(dc, -1.4 + off, y, -1.9 + off, y + 0.5);
                L(dc, -1.9 + off, y - 0.5, -1.9 + off, y + 0.5);
                break;
        }
    }

    private static void TimedMark(DrawingContext dc, bool onDelay)
    {
        // Semicírculo ("paracaídas") sobre la cuchilla: retardo.
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(-1.2, 1.6), false);
            c.ArcTo(P(-1.2, 2.4), new Size(0.4 * G, 0.4 * G), 0, false, onDelay ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
        }
        dc.DrawGeometry(null, Thin, g);
        L(dc, -1.2, 2, -0.4, 2, Dashed);
    }

    public static void Draw(DrawingContext dc, PlacedElement e, ElementType t, ElementStatus? st)
    {
        var sim = st is not null;
        var paths = st?.Paths ?? [];
        bool Path(int i, bool rest) => sim && paths.Length > i ? paths[i] : rest;
        if (t.Graphic is { } imported)
        {
            DrawGraphic(dc, imported, st);
            // Los símbolos importados no saben dibujar su estado: un piloto al lado dice si el contacto está cerrado.
            if (sim && paths.Length > 0 && t.Behavior != Behavior.Graphic)
                dc.DrawEllipse(paths[0] ? Energized : Paper, Thin, P(imported.X1 + 0.5, imported.Y0 + 0.5), 4, 4);
            return;
        }

        switch (t.Behavior)
        {
            case Behavior.Source3Ph or Behavior.SourceAc1 or Behavior.SourceDc:
            {
                var b = Layout.LocalBounds(t);
                dc.DrawRectangle(Tint, Line, new Rect(P(b.X0, 0), P(b.X1, 1.3)));
                foreach (var term in t.Terminals) L(dc, term.X, 1.3, term.X, 2);
                var v = e.Param(t, "V");
                var text = t.Behavior switch
                {
                    Behavior.Source3Ph => $"3~ {v:0} V {e.Param(t, "f"):0} Hz",
                    Behavior.SourceAc1 => $"1~ {v:0} V {e.Param(t, "f"):0} Hz",
                    _ => $"= {v:0.#} V",
                };
                Label(dc, text, b.X0 + 0.3, 0.65, 11);
                break;
            }
            case Behavior.PushNO or Behavior.Switch or Behavior.LimitNO or Behavior.SensorNO or Behavior.ContactNO or Behavior.TimedContactNO:
                Blade(dc, 0, Path(0, false), false);
                if (t.IsManual && t.Behavior != Behavior.SensorNO) Actuator(dc, t.Behavior, st?.Position > 0);
                if (t.Behavior == Behavior.TimedContactNO) TimedMark(dc, true);
                if (t.Behavior == Behavior.SensorNO) Label(dc, e.Text("Rótulo", "◇"), -1.9, 2, 9, Muted);
                break;
            case Behavior.PushNC or Behavior.EmergencyStop or Behavior.LimitNC or Behavior.SensorNC or Behavior.ContactNC or Behavior.TimedContactNC:
                Blade(dc, 0, Path(0, true), true);
                if (t.IsManual && t.Behavior != Behavior.SensorNC) Actuator(dc, t.Behavior, st?.Position > 0);
                if (t.Behavior == Behavior.TimedContactNC) TimedMark(dc, true);
                if (t.Behavior == Behavior.SensorNC) Label(dc, e.Text("Rótulo", "◇"), -1.9, 2, 9, Muted);
                break;
            case Behavior.TwoWay: // 1 (0,0), 2 (2,0), C (0,4)
            {
                L(dc, 0, 4, 0, 2.8);
                L(dc, 0, 0, 0, 1.2);
                L(dc, 2, 0, 2, 1.2);
                var pos1 = Path(0, true);
                if (pos1) L(dc, 0, 2.8, 0.05, 1.2); else L(dc, 0, 2.8, 2, 1.2);
                break;
            }
            case Behavior.Selector3: // C (0,0), 1 (0,4), 2 (2,4)
            {
                L(dc, 0, 0, 0, 1.2);
                L(dc, 0, 2.8, 0, 4);
                L(dc, 2, 2.8, 2, 4);
                if (Path(0, false)) L(dc, 0, 1.2, 0, 2.8);
                else if (Path(1, false)) L(dc, 0, 1.2, 2, 2.8);
                else L(dc, 0, 1.2, 0.9, 2.2);
                Label(dc, sim ? (st!.Position switch { 1 => "I", 2 => "II", _ => "0" }) : "I-0-II", -1.4, 2, 9, Muted);
                break;
            }
            case Behavior.Intermediate:
            {
                L(dc, 0, 0, 0, 1.2); L(dc, 2, 0, 2, 1.2); L(dc, 0, 2.8, 0, 4); L(dc, 2, 2.8, 2, 4);
                if (Path(0, true)) { L(dc, 0, 1.2, 0, 2.8); L(dc, 2, 1.2, 2, 2.8); }
                else { L(dc, 0, 1.2, 2, 2.8); L(dc, 2, 1.2, 0, 2.8); }
                break;
            }
            case Behavior.MainPoles or Behavior.Breaker or Behavior.MotorBreaker or Behavior.Rcd or Behavior.Fuse or Behavior.ThermalRelay:
                DrawPoles(dc, e, t, st);
                break;
            case Behavior.CoilContactor or Behavior.CoilRelay or Behavior.CoilTimerOn or Behavior.CoilTimerOff or Behavior.CoilFlasher or Behavior.CoilImpulse or Behavior.Solenoid:
            {
                Leads(dc);
                var on = t.Behavior == Behavior.Solenoid ? st?.SolenoidOpen == true : st?.CoilOn == true;
                dc.DrawRectangle(on ? Energized : Paper, Line, new Rect(P(-0.9, 1.2), P(0.9, 2.8)));
                var mark = t.Behavior switch
                {
                    Behavior.CoilTimerOn => "TON",
                    Behavior.CoilTimerOff => "TOF",
                    Behavior.CoilFlasher => "⇵",
                    Behavior.CoilImpulse => "⊓",
                    Behavior.Solenoid => "YV",
                    _ => "",
                };
                if (t.Behavior == Behavior.CoilTimerOn || t.Behavior == Behavior.CoilTimerOff)
                {
                    // Recuadro lateral de retardo (IEC): relleno a un lado.
                    dc.DrawRectangle(Ink, null, new Rect(P(-0.9, 1.2), P(-0.55, 2.8)));
                }
                if (mark.Length > 0) Label(dc, mark, 0.1, 2, 8.5, Ink, center: true);
                break;
            }
            case Behavior.Lamp or Behavior.Pilot:
            {
                Leads(dc, 0, 1.1, 2.9);
                var br = st?.Brightness ?? 0;
                var baseColor = t.Behavior == Behavior.Pilot
                    ? e.Param(t, "color") switch { 0 => Color.Parse("#FF3B30"), 1 => Color.Parse("#34C759"), 2 => Color.Parse("#FFB300"), 3 => Color.Parse("#2F80ED"), _ => Color.Parse("#FAFAFA") }
                    : Color.Parse("#FFD54F");
                var fill = br > 0.05
                    ? new SolidColorBrush(baseColor, Math.Clamp(0.25 + 0.75 * br, 0, 1))
                    : t.Behavior == Behavior.Pilot ? new SolidColorBrush(baseColor, 0.18) : Paper;
                if (br > 0.5) dc.DrawEllipse(new SolidColorBrush(baseColor, 0.25 * Math.Min(br, 1)), null, P(0, 2), 1.6 * G, 1.6 * G);
                dc.DrawEllipse(fill, st?.Tripped == true ? FaultPen : Line, P(0, 2), 0.9 * G, 0.9 * G);
                L(dc, -0.64, 1.36, 0.64, 2.64); L(dc, -0.64, 2.64, 0.64, 1.36);
                break;
            }
            case Behavior.Bell:
            {
                Leads(dc, 0, 1.4, 2.6);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(P(-0.9, 2.6), true);
                    c.ArcTo(P(0.9, 2.6), new Size(0.9 * G, 0.9 * G), 0, false, SweepDirection.Clockwise);
                    c.LineTo(P(-0.9, 2.6));
                }
                dc.DrawGeometry((st?.Voltage ?? 0) > 0.7 * e.Param(t, "V") ? Energized : Paper, Line, g);
                L(dc, -0.9, 2.6, 0, 2.6);
                break;
            }
            case Behavior.Heater:
                Leads(dc);
                dc.DrawRectangle((st?.Current ?? 0) > 0.01 ? new SolidColorBrush(Color.Parse("#FF8A65")) : Paper, Line, new Rect(P(-0.45, 1.2), P(0.45, 2.8)));
                break;
            case Behavior.Voltmeter or Behavior.Ammeter:
            {
                Leads(dc, 0, 1.1, 2.9);
                dc.DrawEllipse(Paper, Line, P(0, 2), 0.9 * G, 0.9 * G);
                Label(dc, t.Behavior == Behavior.Voltmeter ? "V" : "A", 0, 2, 11, Ink, center: true);
                if (sim)
                {
                    var value = t.Behavior == Behavior.Voltmeter ? $"{st!.Voltage:0.#} V" : FormatAmps(st!.Current);
                    var tx = Text(value, 11, Select, FontWeight.Bold);
                    dc.DrawRectangle(Paper, null, new Rect(P(1.1, 2.6), new Size(tx.Width + 4, tx.Height)));
                    dc.DrawText(tx, P(1.1, 2.6) + new Point(2, 0));
                }
                break;
            }
            case Behavior.Motor3 or Behavior.Motor6:
            {
                var six = t.Behavior == Behavior.Motor6;
                var cy = six ? 3 : 3.2;
                foreach (var term in t.Terminals.Where(x => x.Y == 0)) L(dc, term.X, 0, term.X, cy - Math.Sqrt(Math.Max(0, 2.89 - Math.Pow(term.X - 2, 2))));
                if (six) foreach (var term in t.Terminals.Where(x => x.Y > 0)) L(dc, term.X, cy + Math.Sqrt(Math.Max(0, 2.89 - Math.Pow(term.X - 2, 2))), term.X, 6);
                var running = Math.Abs(st?.Rpm ?? 0) > 5;
                dc.DrawEllipse(running ? new SolidColorBrush(Color.Parse("#DCEFFD")) : Paper, st?.Tripped == true ? FaultPen : Line, P(2, cy), 1.7 * G, 1.7 * G);
                Label(dc, "M", 2, cy - 0.35, 15, Ink, center: true);
                Label(dc, "3~", 2, cy + 0.65, 10, Ink, center: true);
                if (running)
                {
                    // Flecha que gira con el eje.
                    var angle = (st!.Rpm / 60.0) * 2 * Math.PI * (Environment.TickCount64 / 1000.0) * 0.02;
                    var dir = Math.Sign(st.Rpm);
                    var r = 2.15;
                    var a0 = angle * dir;
                    var p0 = P(2 + r * Math.Cos(a0), cy + r * Math.Sin(a0));
                    var p1 = P(2 + r * Math.Cos(a0 + dir * 0.9), cy + r * Math.Sin(a0 + dir * 0.9));
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(p0, false);
                        c.ArcTo(p1, new Size(r * G, r * G), 0, false, dir > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                    }
                    dc.DrawGeometry(null, new Pen(Select, 2.2), g);
                    dc.DrawEllipse(Select, null, p1, 3, 3);
                }
                break;
            }
            case Behavior.Junction:
                dc.DrawEllipse(Ink, null, P(0, 0), 3.5, 3.5);
                break;
            case Behavior.Motor1:
            {
                const double cx = 1, cy = 3.2;
                foreach (var term in t.Terminals) L(dc, term.X, 0, term.X, cy - Math.Sqrt(Math.Max(0, 2.89 - Math.Pow(term.X - cx, 2))));
                DrawMotorBody(dc, st, cx, cy, "1~");
                break;
            }
            case Behavior.Transformer1 or Behavior.Transformer3:
                DrawTransformer(dc, t, st);
                break;
            case Behavior.Vfd:
                DrawVfd(dc, st);
                break;
            case Behavior.Plc:
                DrawPlc(dc, t, st);
                break;
            case Behavior.Transmitter:
                DrawTransmitter(dc, st);
                break;
            case Behavior.Graphic:
            default:
                if (t.Graphic is { } graphic) DrawGraphic(dc, graphic, st);
                break;
        }
    }

    private static readonly IBrush Running = new SolidColorBrush(Color.Parse("#DCEFFD"));
    private static readonly IBrush LedInput = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush LedOutput = new SolidColorBrush(Color.Parse("#FFB300"));
    private static readonly IBrush LedOff = new SolidColorBrush(Color.Parse("#D5DBE3"));

    /// <summary>Círculo del motor con su rótulo y la flecha que gira con el eje.</summary>
    private static void DrawMotorBody(DrawingContext dc, ElementStatus? st, double cx, double cy, string kind)
    {
        var running = Math.Abs(st?.Rpm ?? 0) > 5;
        dc.DrawEllipse(running ? Running : Paper, st?.Tripped == true ? FaultPen : Line, P(cx, cy), 1.7 * G, 1.7 * G);
        Label(dc, "M", cx, cy - 0.35, 15, Ink, center: true);
        Label(dc, kind, cx, cy + 0.65, 10, Ink, center: true);
        if (!running) return;
        var angle = (st!.Rpm / 60.0) * 2 * Math.PI * (Environment.TickCount64 / 1000.0) * 0.02;
        var dir = Math.Sign(st.Rpm);
        const double r = 2.15;
        var a0 = angle * dir;
        var p0 = P(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        var p1 = P(cx + r * Math.Cos(a0 + dir * 0.9), cy + r * Math.Sin(a0 + dir * 0.9));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(p0, false);
            c.ArcTo(p1, new Size(r * G, r * G), 0, false, dir > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
        }
        dc.DrawGeometry(null, new Pen(Select, 2.2), g);
        dc.DrawEllipse(Select, null, p1, 3, 3);
    }

    /// <summary>Dos círculos entrelazados (IEC 60617-06): primario arriba, secundario abajo.</summary>
    private static void DrawTransformer(DrawingContext dc, ElementType t, ElementStatus? st)
    {
        var three = t.Behavior == Behavior.Transformer3;
        var cx = three ? 2.0 : 1.0;
        var r = three ? 1.35 : 1.05;
        var (y1, y2) = three ? (2.25, 3.75) : (2.35, 3.65);
        foreach (var term in t.Terminals)
        {
            if (term.Y == 0) L(dc, term.X, 0, term.X, y1 - Math.Sqrt(Math.Max(0, r * r - Math.Pow(term.X - cx, 2))) + 0.05);
            else if (term.X - cx <= r) L(dc, term.X, 6, term.X, y2 + Math.Sqrt(Math.Max(0, r * r - Math.Pow(term.X - cx, 2))) - 0.05);
            else
            {
                // Neutro del secundario (2N): sale del centro de la estrella hacia su borne.
                L(dc, term.X, 6, term.X, y2);
                L(dc, term.X, y2, cx + r, y2);
            }
        }
        var loaded = (st?.Current ?? 0) > 0.01;
        dc.DrawEllipse(loaded ? Running : Paper, Line, P(cx, y1), r * G, r * G);
        dc.DrawEllipse(loaded ? new SolidColorBrush(Color.Parse("#DCEFFD"), 0.6) : null, Line, P(cx, y2), r * G, r * G);
        if (three)
        {
            // Triángulo en el primario y estrella en el secundario.
            var tri = new StreamGeometry();
            using (var c = tri.Open())
            {
                c.BeginFigure(P(cx, y1 - 0.55), false);
                c.LineTo(P(cx + 0.5, y1 + 0.3)); c.LineTo(P(cx - 0.5, y1 + 0.3)); c.LineTo(P(cx, y1 - 0.55));
            }
            dc.DrawGeometry(null, Thin, tri);
            foreach (var a in new[] { -90.0, 30, 150 })
            {
                var rad = a * Math.PI / 180;
                L(dc, cx, y2 + 0.15, cx + 0.5 * Math.Cos(rad), y2 + 0.15 + 0.5 * Math.Sin(rad), Thin);
            }
        }
    }

    private static void DrawVfd(DrawingContext dc, ElementStatus? st)
    {
        foreach (var x in new[] { 0.0, 2, 4 }) { L(dc, x, 0, x, 1); L(dc, x, 7, x, 8); }
        foreach (var y in new[] { 2.0, 4, 6 }) L(dc, 6.6, y, 8, y);
        var fault = st?.Fault is not null;
        var on = Math.Abs(st?.Frequency ?? 0) > 0.05;
        dc.DrawRectangle(fault ? new SolidColorBrush(Color.Parse("#FFEBEE")) : on ? Running : Tint, fault ? FaultPen : Line, new Rect(P(-0.8, 1), P(6.6, 7)));
        // Símbolo de convertidor: diagonal con ~ arriba y ~ abajo.
        L(dc, -0.8, 7, 2.2, 1, Thin);
        Label(dc, "3~", 0.2, 2.1, 10, Ink, center: true);
        Label(dc, "f ~", 1.8, 5.8, 10, Ink, center: true);
        var display = st is null ? "VFD" : fault ? "ERR" : $"{Math.Abs(st.Frequency):0.0} Hz";
        var box = new Rect(P(2.7, 3.4), new Size(3.6 * G, 1.2 * G));
        dc.DrawRectangle(new SolidColorBrush(Color.Parse("#102030")), null, box, 3, 3);
        var lcd = new SolidColorBrush(fault ? Color.Parse("#FF6B6B") : Color.Parse("#7CF2A0"));
        var tlcd = Text(display, 11, lcd, FontWeight.Bold);
        dc.DrawText(tlcd, new Point(box.X + (box.Width - tlcd.Width) / 2, box.Y + (box.Height - tlcd.Height) / 2));
        if (st is not null && st.Frequency != 0)
            Label(dc, st.Frequency > 0 ? "↻" : "↺", 4.5, 2.4, 12, Select, center: true);
    }

    /// <summary>Transmisor de 0-10 V: caja con la barra de su valor (L+ y OUT arriba, M abajo).</summary>
    private static void DrawTransmitter(DrawingContext dc, ElementStatus? st)
    {
        L(dc, 0, 0, 0, 1); L(dc, 2, 0, 2, 1); L(dc, 1, 3, 1, 4);
        var powered = st?.Powered == true;
        dc.DrawRectangle(powered ? Running : Tint, Line, new Rect(P(-0.6, 1), P(2.6, 3)), 3, 3);
        Label(dc, "0-10V", 1, 1.55, 8, Ink, center: true);
        var pct = Math.Clamp(st?.Percent ?? 0, 0, 100) / 100;
        dc.DrawRectangle(LedOff, null, new Rect(P(-0.3, 2.3), P(2.3, 2.65)));
        if (st is not null) dc.DrawRectangle(powered ? LedInput : LedOff, null, new Rect(P(-0.3, 2.3), P(-0.3 + 2.6 * pct, 2.65)));
    }

    private static void DrawPlc(DrawingContext dc, ElementType t, ElementStatus? st)
    {
        var maxX = t.Terminals.Max(x => x.X);
        foreach (var term in t.Terminals) L(dc, term.X, term.Y == 0 ? 0 : 5, term.X, term.Y == 0 ? 1 : 6);
        var powered = st?.Powered == true;
        dc.DrawRectangle(Tint, Line, new Rect(P(-0.8, 1), P(maxX + 0.8, 5)));
        Label(dc, t.Id == ElementCatalog.McuTypeId ? "µC" : "PLC", maxX - 1.2, 3.0, 12, Ink, center: true);
        dc.DrawEllipse(st is null ? LedOff : powered ? LedInput : Fault, null, P(maxX - 2.6, 3.0), 3, 3);
        var inputs = st?.Inputs ?? [];
        var outputs = st?.Paths ?? [];
        var i = 0; var o = 0;
        foreach (var term in t.Terminals)
        {
            if (term.Id.StartsWith('I'))
            {
                var lit = i < inputs.Length && inputs[i];
                dc.DrawRectangle(lit ? LedInput : LedOff, null, new Rect(P(term.X - 0.25, 2.25), P(term.X + 0.25, 2.6)));
                i++;
            }
            else if (term.Id.StartsWith('Q'))
            {
                var lit = o < outputs.Length && outputs[o];
                dc.DrawRectangle(lit ? LedOutput : LedOff, null, new Rect(P(term.X - 0.25, 3.4), P(term.X + 0.25, 3.75)));
                o++;
            }
        }
    }

    /// <summary>Símbolo importado (QElectroTech…): sus primitivas vectoriales en unidades de rejilla.</summary>
    private static void DrawGraphic(DrawingContext dc, SymbolGraphic graphic, ElementStatus? st)
    {
        var active = st is not null && (st.CoilOn || st.Brightness > 0.3 || (st.Paths.Length > 0 && st.Paths[0] && st.Current > 0.001));
        foreach (var p in graphic.Primitives)
        {
            var pen = new Pen(Ink, Math.Max(0.8, 1.2 * p.Thickness), p.Dashed ? new DashStyle([3, 3], 0) : null);
            IBrush? fill = p.Filled ? Ink : null;
            switch (p.Kind)
            {
                case PrimitiveKind.Line when p.Points.Count >= 2:
                    dc.DrawLine(pen, P(p.Points[0].X, p.Points[0].Y), P(p.Points[1].X, p.Points[1].Y));
                    break;
                case PrimitiveKind.Polygon when p.Points.Count >= 2:
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(P(p.Points[0].X, p.Points[0].Y), p.Filled);
                        foreach (var q in p.Points.Skip(1)) c.LineTo(P(q.X, q.Y));
                        if (p.Closed) c.EndFigure(true);
                    }
                    dc.DrawGeometry(fill, pen, g);
                    break;
                }
                case PrimitiveKind.Rect:
                    dc.DrawRectangle(fill ?? (active ? Energized : null), pen, new Rect(P(p.X, p.Y), P(p.X + p.W, p.Y + p.H)));
                    break;
                case PrimitiveKind.Ellipse:
                    dc.DrawEllipse(fill ?? (active ? Energized : null), pen, P(p.X + p.W / 2, p.Y + p.H / 2), p.W / 2 * G, p.H / 2 * G);
                    break;
                case PrimitiveKind.Arc:
                {
                    // QElectroTech: ángulos en grados, 0 = a la derecha, positivos en sentido antihorario (y hacia arriba).
                    var cx = p.X + p.W / 2; var cy = p.Y + p.H / 2;
                    var a0 = p.Start * Math.PI / 180; var a1 = (p.Start + p.Angle) * Math.PI / 180;
                    var s0 = P(cx + p.W / 2 * Math.Cos(a0), cy - p.H / 2 * Math.Sin(a0));
                    var s1 = P(cx + p.W / 2 * Math.Cos(a1), cy - p.H / 2 * Math.Sin(a1));
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(s0, false);
                        c.ArcTo(s1, new Size(p.W / 2 * G, p.H / 2 * G), 0, Math.Abs(p.Angle) > 180,
                            p.Angle > 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                    }
                    dc.DrawGeometry(null, pen, g);
                    break;
                }
                case PrimitiveKind.Text when p.Text.Length > 0:
                {
                    var tx = Text(p.Text, Math.Clamp(p.Size, 5, 24), Ink);
                    dc.DrawText(tx, P(p.X, p.Y) - new Point(0, tx.Height * 0.8));
                    break;
                }
            }
        }
    }

    private static void DrawPoles(DrawingContext dc, PlacedElement e, ElementType t, ElementStatus? st)
    {
        var n = t.PoleCount;
        var sim = st is not null;
        var b = t.Behavior;
        var closed = b switch
        {
            Behavior.ThermalRelay => true,
            Behavior.Fuse => true,
            _ => sim ? st!.Paths.Length > 0 && st.Paths[0] : b != Behavior.MainPoles,
        };
        for (var k = 0; k < n; k++)
        {
            var x = 2.0 * k;
            switch (b)
            {
                case Behavior.Fuse:
                {
                    var blown = sim && st!.FuseBlown.Length > k && st.FuseBlown[k];
                    L(dc, x, 0, x, 1.1); L(dc, x, 2.9, x, 4);
                    dc.DrawRectangle(blown ? new SolidColorBrush(Color.Parse("#FFCDD2")) : Paper, blown ? FaultPen : Line, new Rect(P(x - 0.4, 1.1), P(x + 0.4, 2.9)));
                    if (!blown) L(dc, x, 1.1, x, 2.9, Thin);
                    break;
                }
                case Behavior.ThermalRelay:
                {
                    L(dc, x, 0, x, 1.4); L(dc, x, 2.6, x, 4);
                    // Elemento bimetálico (escalón).
                    L(dc, x, 1.4, x + 0.5, 1.4); L(dc, x + 0.5, 1.4, x + 0.5, 2.0); L(dc, x + 0.5, 2.0, x - 0.5, 2.0);
                    L(dc, x - 0.5, 2.0, x - 0.5, 2.6); L(dc, x - 0.5, 2.6, x, 2.6);
                    break;
                }
                default:
                {
                    L(dc, x, 0, x, 1.2);
                    L(dc, x, 2.8, x, 4);
                    if (closed) L(dc, x, 2.8, x + 0.05, 1.2); else L(dc, x, 2.8, x - 0.8, 1.35);
                    if (b == Behavior.MainPoles)
                    {
                        // Contacto de contactor: semicírculo en el borne fijo.
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                        {
                            c.BeginFigure(P(x, 1.2), false);
                            c.ArcTo(P(x + 0.5, 0.95), new Size(0.35 * G, 0.35 * G), 0, false, SweepDirection.Clockwise);
                        }
                        dc.DrawGeometry(null, Thin, g);
                    }
                    else
                    {
                        // Interruptor automático: aspa en el borne fijo.
                        L(dc, x - 0.25, 0.95, x + 0.25, 1.45, Thin);
                        L(dc, x - 0.25, 1.45, x + 0.25, 0.95, Thin);
                    }
                    break;
                }
            }
        }
        // Unión mecánica entre polos.
        if (n > 1 && b is not (Behavior.Fuse))
            L(dc, -0.4, 2.0, 2.0 * (n - 1) + 0.3, 2.0, Dashed);
        if (b is Behavior.Breaker or Behavior.MotorBreaker)
        {
            var w = 2.0 * (n - 1);
            dc.DrawRectangle(Paper, Thin, new Rect(P(w + 0.6, 1.5), P(w + 1.6, 2.5)));
            Label(dc, b == Behavior.MotorBreaker ? "I>" : "⊐", w + 1.1, 2.0, 9, Ink, center: true);
        }
        if (b == Behavior.Rcd)
        {
            var w = 2.0 * (n - 1);
            dc.DrawEllipse(null, Thin, P(w / 2, 3.2), (w / 2 + 0.7) * G, 0.35 * G);
            dc.DrawRectangle(Paper, Thin, new Rect(P(w + 0.6, 1.5), P(w + 1.6, 2.5)));
            Label(dc, "IΔ", w + 1.1, 2.0, 9, Ink, center: true);
        }
        if (b == Behavior.ThermalRelay && sim && st!.Tripped)
            dc.DrawRectangle(null, FaultPen, new Rect(P(-0.9, 1.1), P(2.0 * (n - 1) + 0.9, 2.9)));
    }

    public static string FormatAmps(double a) => a switch
    {
        >= 1000 => $"{a / 1000:0.0} kA",
        >= 10 => $"{a:0} A",
        >= 1 => $"{a:0.0} A",
        _ => $"{a * 1000:0} mA",
    };
}
