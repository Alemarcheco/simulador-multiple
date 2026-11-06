using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Simulador.Banco.Generic;
using Simulador.Banco.Scenarios;
using Simulador.Core.CoSim;

namespace Simulador.Banco.Ui;

/// <summary>
/// Vista animada 2D del montaje: lee la última instantánea del banco y dibuja cada equipo con su estado. Los giros se
/// muestran en "estroboscopio" (2 vueltas por segundo a velocidad nominal) para que se vean.
/// </summary>
public sealed class PlantCanvas : Control
{
    private const double W = 1000, H = 580;
    private readonly Dictionary<string, double> _angle = [];
    private double _flowPhase;
    private Scenario? _scenario;
    private SimFrame? _frame;

    public PlantCanvas() { ClipToBounds = true; }

    public Scenario? Scenario
    {
        get => _scenario;
        set { _scenario = value; _angle.Clear(); _frame = null; InvalidateVisual(); }
    }

    /// <summary>Avanza la animación con el tiempo real transcurrido y la nueva instantánea.</summary>
    public void Update(SimFrame frame, double wallSeconds)
    {
        _frame = frame;
        _flowPhase += wallSeconds * 30;
        foreach (var (id, rel) in RotatingParts(frame))
            _angle[id] = (_angle.GetValueOrDefault(id) + rel * 2 * 2 * Math.PI * wallSeconds) % (2 * Math.PI * 1000);
        InvalidateVisual();
    }

    private double A(string id) => _angle.GetValueOrDefault(id);

    private IEnumerable<(string, double)> RotatingParts(SimFrame f)
    {
        switch (_scenario)
        {
            case TankFillScenario t:
                yield return ("M1", V(f, "M1", GenericKeys.Speed) / t.M1.SyncSpeed);
                break;
            case TurboGeneratorScenario g:
                yield return ("T1", V(f, "T1", GenericKeys.Speed) / g.T1.RatedSpeed);
                break;
            case TurboCompressorScenario c:
                yield return ("TC", V(f, "TC-T", GenericKeys.Speed) / c.Turbine.RatedSpeed);
                break;
        }
    }

    private static double V(SimFrame? f, string comp, string key) =>
        f?[comp] is { } s && s.Values.TryGetValue(key, out var v) && v is IConvertible c ? c.ToDouble(null) : 0;

    private static bool B(SimFrame? f, string comp, string key) =>
        f?[comp] is { } s && s.Values.TryGetValue(key, out var v) && v is true;

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (_scenario is null) return;
        double k = Math.Min(Bounds.Width / W, Bounds.Height / H);
        double ox = (Bounds.Width - W * k) / 2, oy = (Bounds.Height - H * k) / 2;
        using var _ = ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy));
        var p = new Paint(ctx);
        switch (_scenario)
        {
            case TankFillScenario t: DrawTankFill(p, t, _frame); break;
            case TurboGeneratorScenario g: DrawTurboGenerator(p, g, _frame); break;
            case TurboCompressorScenario c: DrawTurboCompressor(p, c, _frame); break;
        }
    }

    // ---------------- Llenado de tanque ----------------

    private void DrawTankFill(Paint p, TankFillScenario s, SimFrame? f)
    {
        bool gridLive = V(f, "RED", GenericKeys.Voltage) > 1;
        bool closed = B(f, "KM1", GenericKeys.Closed);
        bool coil = B(f, "KM1", GenericKeys.CoilOn);
        bool tripped = B(f, "KM1", GenericKeys.Tripped);
        double speed = V(f, "M1", GenericKeys.Speed);
        double current = V(f, "M1", GenericKeys.Current);
        double q = Math.Max(0, V(f, "agua", "P1.flow"));
        double qOut = Math.Max(0, V(f, "agua", "T3.flow"));
        double level = V(f, "agua", "TK1.levelRatio");
        double levelM = V(f, "agua", "TK1.level");
        double opening = V(f, "agua", "V1.opening");
        bool run = f?["PLC1"]?.Values.TryGetValue("plc.state", out var st) == true && (string)st == "Run";
        double q0 = V(f, "PLC1", "PLC1.%QX0.0"), q1 = V(f, "PLC1", "PLC1.%QX0.1");
        double i1 = V(f, "PLC1", "PLC1.%IX0.1"), ai = V(f, "PLC1", "PLC1.%IW64");

        // PLC y regleta X1
        p.Plc(new Rect(30, 40, 180, 150), "PLC1 · Genérico IEC", run,
            [("%QX0.0 bomba", q0 > 0.5), ("%QX0.1 alarma", q1 > 0.5), ("%IX0.1 térmico", i1 > 15)], $"%IW64 = {ai:0.00} V ({ai * 10:0} %)");
        p.TerminalBox(250, 60, "X1", 4);
        p.Signal(q0 > 0.5, new Point(210, 82), Paint.Terminal(250, 60, 1));
        p.Signal(i1 > 15, new Point(210, 100), Paint.Terminal(250, 60, 2));
        p.Signal(true, new Point(210, 165), new Point(230, 165), Paint.Terminal(250, 60, 4));
        // X1:1 → bobina KM1, X1:2 ← 97-98
        p.Signal(q0 > 0.5, Paint.Terminal(250, 60, 1), new Point(330, 73), new Point(330, 172), new Point(410, 172));
        p.Signal(i1 > 15, Paint.Terminal(250, 60, 2), new Point(320, 89), new Point(320, 194), new Point(410, 194));

        // Red, KM1 y motor
        p.Text("RED 400 V 50 Hz", 400, 14, 12, gridLive ? Paint.Live : Paint.Quiet, true);
        for (int i = 0; i < 3; i++) p.Cable(gridLive, new Point(432 + i * 26, 34), new Point(432 + i * 26, 116));
        p.Contactor(410, 108, closed, coil, tripped, gridLive, "KM1");
        for (int i = 0; i < 3; i++) p.Cable(closed && gridLive, new Point(432 + i * 26, 216), new Point(432 + i * 26, 262));
        p.Motor(458, 300, A("M1"), closed && gridLive, "M1");
        p.Text($"{current:0.0} A · {speed * 60 / (2 * Math.PI):0} rpm", 458, 354, 11, Paint.Quiet, false, true);

        // Eje y bomba
        p.Shaft(504, 300, 576, A("M1"), speed > 1);
        p.Pump(610, 300, A("M1"), speed > 5, "");
        p.Text("P1", 652, 330, 12, Paint.Ink, true);
        p.Text($"{q * 1000:0.0} L/s", 652, 346, 11, Paint.Quiet);

        // Cisterna, aspiración e impulsión
        var cis = new Rect(560, 440, 190, 90);
        p.Box(cis, Paint.White, Paint.Steel, 2, 6);
        p.Ctx.DrawRectangle(Paint.Water, null, new Rect(cis.X + 3, cis.Y + 30, cis.Width - 6, cis.Height - 33), 3, 3);
        p.Text("CIS · cisterna", cis.Center.X, cis.Bottom + 4, 11, Paint.Quiet, false, true);
        p.Pipe(_flowPhase * Math.Min(1, q / 0.004), q > 1e-4, Paint.Water, new Point(610, 500), new Point(610, 334));
        p.Pipe(_flowPhase * Math.Min(1, q / 0.004), q > 1e-4, Paint.Water, new Point(644, 300), new Point(720, 300), new Point(720, 90), new Point(800, 90), new Point(800, 116));

        // Tanque elevado con patas
        var tk = new Rect(780, 116, 130, 190);
        p.Line(792, 306, 782, 420, Paint.Steel, 4);
        p.Line(898, 306, 908, 420, Paint.Steel, 4);
        p.Tank(tk, level, $"TK1 · {levelM:0.00} m", 0.2, 0.8);
        p.Text("cota +4 m", 760, 424, 11, Paint.Quiet, false, true);

        // Transmisor de nivel LT1 y lazo hasta el PLC
        p.Box(new Rect(926, 150, 54, 34), Paint.Panel, Paint.Quiet, 1.25, 4);
        p.Text("LT1", 953, 158, 11, Paint.Ink, true, true);
        p.Line(910, 167, 926, 167, Paint.Quiet, 1.5);
        p.Signal(true, new Point(953, 150), new Point(953, 40), new Point(300, 40), new Point(300, 130), new Point(267, 130), Paint.Terminal(250, 60, 4));
        p.Text("4 hilos 0-10 V", 620, 24, 11, Paint.Quiet);

        // Consumo: T3 + V1 hacia desagüe
        p.Pipe(_flowPhase * Math.Min(1, qOut / 0.004), qOut > 1e-4, Paint.Water, new Point(880, 306), new Point(880, 470), new Point(950, 470), new Point(950, 520));
        p.Valve(880, 446, opening, "V1");
        p.Text("consumo", 950, 524, 11, Paint.Quiet, false, true);
        p.Text($"{qOut * 1000:0.0} L/s", 958, 480, 11, Paint.Quiet);
    }

    // ---------------- Turbogenerador ----------------

    private void DrawTurboGenerator(Paint p, TurboGeneratorScenario s, SimFrame? f)
    {
        double adm = V(f, "T1", GenericKeys.Admission);
        bool trip = B(f, "T1", GenericKeys.Tripped);
        double w = V(f, "T1", GenericKeys.Speed);
        double hz = V(f, "G1", GenericKeys.Frequency);
        double v = V(f, "G1", GenericKeys.Voltage);
        double kw = V(f, "G1", GenericKeys.Power) / 1000;
        double i = V(f, "G1", GenericKeys.Current);
        double gov = V(f, "GOV", GenericKeys.Output);

        // Caldera / vapor y válvula de admisión
        p.Box(new Rect(30, 230, 110, 90), Paint.Panel, Paint.Steam, 2, 8);
        p.Text("Vapor", 85, 260, 13, Paint.Steam, true, true);
        p.Pipe(_flowPhase * adm, adm > 0.01, Paint.Steam, new Point(140, 275), new Point(220, 275));
        p.Valve(180, 275, adm, "");
        p.Text($"admisión {adm:P0}", 180, 300, 11, Paint.Quiet, false, true);
        p.Turbine(220, 205, A("T1"), adm, trip, "turbina de vapor", "T1");
        p.Shaft(370, 275, 452, A("T1"), w > 1);
        p.Generator(500, 275, A("T1"), v > 1, "G1");

        // Regulador de velocidad
        p.Box(new Rect(220, 50, 230, 70), Paint.Panel, Paint.Quiet, 1.25, 6);
        p.Text("GOV · regulador PI", 335, 58, 12, Paint.Ink, true, true);
        p.Text($"consigna 3000 rpm · salida {gov:0} %", 335, 82, 11, Paint.Quiet, false, true);
        p.Signal(true, new Point(500, 227), new Point(500, 85), new Point(450, 85));
        p.Text("velocidad", 506, 140, 11, Paint.Quiet);
        p.Signal(true, new Point(220, 85), new Point(180, 85), new Point(180, 247));
        p.Text("mando admisión", 110, 140, 11, Paint.Quiet);

        // Barras y cargas
        bool live = v > 1;
        p.Cable(live, new Point(548, 275), new Point(640, 275));
        p.Line(640, 170, 640, 380, live ? Paint.Live : Paint.Steel, 6);
        p.Text("Barras 400 V", 640, 150, 11, Paint.Quiet, false, true);
        int lamps = (int)Math.Round(s.Load.PowerKw / 50);
        for (int n = 0; n < 10; n++)
        {
            double y = 182 + n * 21;
            bool connected = n < lamps;
            p.Cable(live && connected, new Point(640, y), new Point(700, y));
            p.Lamp(714, y, connected ? v / 400 : 0);
        }
        p.Text($"CARGA {s.Load.PowerKw:0} kW", 714, 380, 12, Paint.Ink, true, true);

        p.Gauge(860, 190, 62, hz, 45, 55, "frecuencia", $"{hz:0.00} Hz", 49.5, 50.5);
        p.Gauge(860, 380, 62, kw, 0, 600, "potencia", $"{kw:0} kW");
        p.Text($"{v:0} V · {i:0} A", 500, 340, 11, Paint.Quiet, false, true);
        p.Text($"{w * 60 / (2 * Math.PI):0} rpm", 410, 300, 11, Paint.Quiet, false, true);
    }

    // ---------------- Turbocompresor ----------------

    private void DrawTurboCompressor(Paint p, TurboCompressorScenario s, SimFrame? f)
    {
        double adm = V(f, "TC-T", GenericKeys.Admission);
        bool trip = B(f, "TC-T", GenericKeys.Tripped);
        double w = V(f, "TC-T", GenericKeys.Speed);
        double q = V(f, "TC-C", GenericKeys.Flow);
        double pr = V(f, "D1", GenericKeys.Pressure) / 1e5;
        bool relief = B(f, "D1", GenericKeys.Relief);
        bool run = f?["PLC2"]?.Values.TryGetValue("plc.state", out var st) == true && (string)st == "Run";
        double ai = V(f, "PLC2", "PLC2.%IW64"), ao = V(f, "PLC2", "PLC2.%QW64");

        p.Plc(new Rect(30, 30, 200, 130), "PLC2 · Genérico IEC", run,
            [("%QX0.0 en marcha", V(f, "PLC2", "PLC2.%QX0.0") > 0.5), ("%QX0.1 presión alta", V(f, "PLC2", "PLC2.%QX0.1") > 0.5)],
            $"%IW64 {ai:0.00} V · %QW64 {ao:0.00} V");
        p.TerminalBox(270, 50, "X3", 4);
        p.Signal(true, new Point(230, Paint.Terminal(270, 50, 2).Y), Paint.Terminal(270, 50, 2));
        p.Signal(true, new Point(230, Paint.Terminal(270, 50, 3).Y), Paint.Terminal(270, 50, 3));

        // Combustible y turbina de gas
        p.Box(new Rect(30, 260, 100, 70), Paint.Panel, Paint.Steam, 2, 8);
        p.Text("Gas", 80, 284, 13, Paint.Steam, true, true);
        p.Pipe(_flowPhase * adm, adm > 0.01, Paint.Steam, new Point(130, 295), new Point(220, 295));
        p.Valve(175, 295, adm, "Y1");
        p.Signal(true, Paint.Terminal(270, 50, 3), new Point(325, Paint.Terminal(270, 50, 3).Y), new Point(325, 200), new Point(175, 200), new Point(175, 267));
        p.Turbine(220, 225, A("TC"), adm, trip, "turbina de gas", "TC-T");
        p.Shaft(370, 295, 470, A("TC"), w > 1);
        p.Compressor(510, 295, A("TC"), w > 5, "TC-C");
        p.Text($"{w * 60 / (2 * Math.PI):0} rpm", 420, 318, 11, Paint.Quiet, false, true);

        // Aire hacia el depósito
        p.Text("aire", 500, 220, 11, Paint.Quiet, false, true);
        p.Pipe(_flowPhase * Math.Min(1, q / 0.2), q > 1e-3, Paint.Air, new Point(550, 280), new Point(700, 280));
        var d1 = new Rect(700, 180, 120, 260);
        p.Box(d1, Paint.Panel, relief ? Paint.Live : Paint.Steel, 2, 40);
        var fill = new SolidColorBrush(Color.FromArgb((byte)(30 + 200 * Math.Clamp(pr / 10, 0, 1)), 74, 144, 217));
        p.Ctx.DrawRectangle(fill, null, d1.Deflate(4), 36, 36);
        p.Text("D1 · 4 m³", d1.Center.X, d1.Y - 18, 12, Paint.Ink, true, true);
        if (relief) p.Text("válvula de seguridad abierta", d1.Center.X, d1.Bottom + 4, 11, Paint.Live, true, true);
        p.Pipe(_flowPhase, s.Receiver.Demand > 0.001 && pr > 0.05, Paint.Air, new Point(820, 400), new Point(960, 400));
        p.Text($"consumo {s.Receiver.Demand:0.00} m³/s", 890, 410, 11, Paint.Quiet, false, true);

        p.Gauge(900, 230, 58, pr, 0, 10, "presión D1", $"{pr:0.00} bar", 6.5, 7.5);
        p.Box(new Rect(840, 320, 50, 30), Paint.Panel, Paint.Quiet, 1.25, 4);
        p.Text("PT1", 865, 326, 11, Paint.Ink, true, true);
        p.Line(820, 335, 840, 335, Paint.Quiet, 1.5);
        p.Signal(true, new Point(865, 350), new Point(865, 520), new Point(345, 520), new Point(345, Paint.Terminal(270, 50, 2).Y), Paint.Terminal(270, 50, 2));
        p.Text($"{q:0.00} m³/s", 625, 296, 11, Paint.Quiet, false, true);
    }
}
