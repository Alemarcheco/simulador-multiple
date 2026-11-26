using System.Numerics;
using Simulador.Banco.Mesa.Ui;
using Simulador.Core.Rendering;
using Simulador.Hidraulica.View3D;

namespace Simulador.Banco.Mesa.View3D;

/// <summary>Lo que la vista 3D necesita del estado en marcha (todo opcional: sin simulación se ve la mesa quieta).</summary>
public sealed class BenchLive
{
    public Func<string, PartLook?>? LookOf { get; init; }
    public Func<string, IssueLevel?>? IssueOf { get; init; }
    public Func<BenchWire, bool>? WireActive { get; init; }
    /// <summary>Ángulo girado (rad) de cada pieza con eje (motor, turbina, bomba…).</summary>
    public Func<string, double>? AngleOf { get; init; }
    /// <summary>Tiempo de animación (s) para lo que avanza por los cables.</summary>
    public double Phase { get; init; }
    public string? SelectedId { get; init; }
    public bool Running { get; init; }
}

/// <summary>Escena de la mesa más lo que hace falta para elegir con el ratón.</summary>
public sealed class BenchScene
{
    public Scene Scene { get; } = new();
    /// <summary>Caja de cada pieza (para elegirla con el ratón y poner su etiqueta encima).</summary>
    public Dictionary<string, (Vector3 Min, Vector3 Max)> Parts { get; } = [];
    /// <summary>Posición 3D de cada borne: (pieza, borne) → punto.</summary>
    public Dictionary<(string Part, string Port), Vector3> Terminals { get; } = [];
    /// <summary>Caja de lo que hay sobre la mesa (sin el tablero), para encuadrar.</summary>
    public Vector3 Min { get; set; } = new(float.MaxValue);
    public Vector3 Max { get; set; } = new(float.MinValue);
}

/// <summary>
/// Convierte el montaje de la mesa en una escena 3D: un tablero de trabajo con cada pieza sobre su placa (la misma
/// huella que en la vista de bloques), sus bornes de color en el borde y cables con caída que se apoyan en la mesa.
/// Las piezas son modelos sencillos y reconocibles (caja de pulsador, motor con aletas, tanque transparente…) que se
/// animan con lo que publica la simulación: ejes que giran, nivel de agua, pilotos y LED encendidos.
/// Unidades: metros de escena; 100 px de la vista de bloques = 1 m. Z arriba; la parte de abajo del esquema mira
/// hacia la cámara (−Y).
/// </summary>
public static class BenchScene3D
{
    public const float Scale = 0.01f;
    public const float PlateTop = 0.04f;
    private const float TerminalZ = PlateTop + 0.06f;

    // Colores ARGB
    private const uint Mat = 0xFF3E5A4F, MatB = 0xFF39544A, Wood = 0xFF9C7A54, WoodDark = 0xFF7A5C3C;
    private const uint Plate = 0xFFCDD3D8, PlateSel = 0xFF9EC3F2, PlateErr = 0xFFE89A9A, PlateWarn = 0xFFEBCB7A;
    private const uint Enclosure = 0xFF5A6370, EnclosureLight = 0xFFD5D9DE, Black = 0xFF262A30, Steel = 0xFFA7B0B9, SteelDark = 0xFF6F7883;
    private const uint Copper = 0xFFC8793F, White = 0xFFF2F2F2, Screen = 0xFF1C2B24, ScreenOn = 0xFF2E4F3C;
    private const uint MotorBlue = 0xFF2F5E9E, GenGreen = 0xFF2E7D5B, TurbineOrange = 0xFFC9822E, PumpRed = 0xFFB8412F;
    private const uint Pcb = 0xFF17695A, RelayBlue = 0xFF2A62C9, Yellow = 0xFFF2C230, Water = 0xFF2F7FC9, Glass = 0x50B8D4E8;
    private const uint LedOff = 0xFF4B4F55, LedGreen = 0xFF3BE068, LedRed = 0xFFFF4A3D, LedAmber = 0xFFFFB52E;

    public static BenchScene Build(BenchProject project, BenchLive live)
    {
        var bs = new BenchScene();
        var s = bs.Scene;

        // Bornes activos (para los LED de E/S): un borne está activo si algún cable suyo lo está.
        var activePorts = new HashSet<(string, string)>();
        if (live.Running && live.WireActive is { } wa)
            foreach (var w in project.Wires)
                if (wa(w)) { activePorts.Add((w.FromPart, w.FromPort)); activePorts.Add((w.ToPart, w.ToPort)); }

        foreach (var part in project.Parts)
        {
            var type = BenchCatalog.Get(part.Type);
            var lay = BenchCanvas.Layout(part);
            var box = lay.Box;
            float x0 = (float)box.X * Scale, x1 = (float)box.Right * Scale;
            float y0 = -(float)box.Bottom * Scale, y1 = -(float)box.Y * Scale;
            var look = live.Running ? live.LookOf?.Invoke(part.Id) : null;
            var issue = live.IssueOf?.Invoke(part.Id);
            bool sel = part.Id == live.SelectedId;

            int before = s.Triangles.Count;
            // Placa base (la huella de la pieza), con el color del aviso o de la selección.
            uint plate = issue == IssueLevel.Error ? PlateErr : issue == IssueLevel.Warning ? PlateWarn : sel ? PlateSel : Plate;
            SceneBuilder.Box(s, new((x0 + x1) / 2, (y0 + y1) / 2, PlateTop / 2), new(x1 - x0, y1 - y0, PlateTop), plate);
            uint stripe = Argb(BenchCanvas.CategoryColor(type?.Category ?? ""));
            SceneBuilder.Box(s, new((x0 + x1) / 2, y1 - 0.03f, PlateTop + 0.004f), new(x1 - x0 - 0.04f, 0.04f, 0.008f), stripe);

            var kit = new Kit(s, new Vector3((x0 + x1) / 2, (y0 + y1) / 2 + 0.04f, PlateTop), x1 - x0 - 0.2f, y1 - y0 - 0.3f);
            double angle = live.AngleOf?.Invoke(part.Id) ?? 0;
            bool PortOn(string port) => activePorts.Contains((part.Id, port));
            Model(kit, part, type, look, angle, PortOn, live.Phase);

            foreach (var slot in lay.Ports)
            {
                var p = new Vector3((float)slot.Pos.X * Scale, -(float)slot.Pos.Y * Scale, TerminalZ);
                bs.Terminals[(part.Id, slot.Def.Name)] = p;
                uint c = Argb(BenchCanvas.PortColor(slot.Def));
                SceneBuilder.Box(s, p - new Vector3(0, 0, 0.03f), new(0.09f, 0.09f, 0.08f), Shade(c, 0.85f));
                SceneBuilder.Cylinder(s, p + new Vector3(0, 0, 0.01f), p + new Vector3(0, 0, 0.05f), 0.025f, Steel, 8, true);
            }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = before; i < s.Triangles.Count; i++)
            {
                var t = s.Triangles[i];
                min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
                max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
            }
            bs.Parts[part.Id] = (min, max);
            bs.Min = Vector3.Min(bs.Min, min);
            bs.Max = Vector3.Max(bs.Max, max);

            string text = part.Id + (look is { Lines.Count: > 0 } && look.Lines[0].Length > 0 ? " · " + look.Lines[0] : "");
            s.Labels.Add(new Label3D(new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, max.Z + 0.12f), text, sel || look?.State is PartState.Fault));
        }

        foreach (var w in project.Wires) AddWire(bs, project, w, live);
        AddTable(bs);
        return bs;
    }

    // ---------------------------------------------------------------- cables

    private static void AddWire(BenchScene bs, BenchProject project, BenchWire w, BenchLive live)
    {
        if (!bs.Terminals.TryGetValue((w.FromPart, w.FromPort), out var a) || !bs.Terminals.TryGetValue((w.ToPart, w.ToPort), out var b)) return;
        var da = BenchRules.PortOf(project, w.FromPart, w.FromPort);
        var db = BenchRules.PortOf(project, w.ToPart, w.ToPort);
        if (da is null || db is null) return;
        var def = da.Kind == PortKind.Probe ? da : db.Kind == PortKind.Probe ? db : da.Kind == PortKind.Tap ? db : da;
        bool active = live.Running && live.WireActive?.Invoke(w) == true;
        float r = def.Kind switch { PortKind.Pipe or PortKind.Tap => 0.045f, PortKind.Power => 0.022f, PortKind.Probe => 0.01f, _ => 0.013f };
        uint color = def.Kind is PortKind.Pipe or PortKind.Tap ? (active ? 0xFF8FA9BF : 0xFF9AA4AE) : Argb(BenchCanvas.PortColor(def));
        if (live.Running && !active && def.Kind is not (PortKind.Pipe or PortKind.Tap or PortKind.Probe)) color = Shade(color, 0.6f);

        var pts = WirePath(a, Out(da.EffectiveSide), b, Out(db.EffectiveSide), def.Kind is PortKind.Pipe or PortKind.Tap);
        for (int i = 1; i < pts.Count; i++) SceneBuilder.Cylinder(bs.Scene, pts[i - 1], pts[i], r, color, 6, caps: false);
        foreach (var p in new[] { pts[0], pts[^1] }) SceneBuilder.Sphere(bs.Scene, p, r * 1.15f, color, 4, 6);

        if (active && def.Kind != PortKind.Probe)
        {
            // Lo que circula: cuentas claras que avanzan por el cable (agua azul por las tuberías).
            bool reverse = da.Kind == PortKind.SignalIn;
            uint bead = def.Kind is PortKind.Pipe or PortKind.Tap ? 0xFF4FA3F0 : Shade(Argb(BenchCanvas.PortColor(def)), 1.6f);
            double len = 0;
            for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
            int n = Math.Max(1, (int)(len / 0.35));
            for (int k = 0; k < n; k++)
            {
                double t = ((k + live.Phase * 0.8) / n) % 1.0;
                if (reverse) t = 1 - t;
                SceneBuilder.Sphere(bs.Scene, Along(pts, t), r * 1.6f, bead, 4, 6);
            }
        }
    }

    private static Vector3 Out(PortSide side) => side switch
    {
        PortSide.Left => -Vector3.UnitX, PortSide.Right => Vector3.UnitX, PortSide.Top => Vector3.UnitY, _ => -Vector3.UnitY,
    };

    /// <summary>Curva del cable: sale del borne hacia fuera, cae hasta apoyarse en la mesa y sube al otro borne.</summary>
    public static List<Vector3> WirePath(Vector3 a, Vector3 outA, Vector3 b, Vector3 outB, bool stiff = false)
    {
        float d = Math.Clamp(Vector3.Distance(a, b) * 0.4f, 0.3f, 1.2f);
        Vector3 c1 = a + outA * d, c2 = b + outB * d;
        var pts = new List<Vector3>();
        const int N = 18;
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N, u = 1 - t;
            var p = u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * b;
            float sag = MathF.Sin(MathF.PI * t);
            float floor = stiff ? 0.07f : 0.018f;
            p.Z = a.Z * u + b.Z * t;
            p.Z += (floor - p.Z) * MathF.Min(1, sag * 1.6f);
            pts.Add(p);
        }
        return pts;
    }

    private static Vector3 Along(List<Vector3> pts, double t)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        double target = total * Math.Clamp(t, 0, 1), acc = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = Vector3.Distance(pts[i - 1], pts[i]);
            if (acc + seg >= target && seg > 0) return Vector3.Lerp(pts[i - 1], pts[i], (float)((target - acc) / seg));
            acc += seg;
        }
        return pts[^1];
    }

    // ---------------------------------------------------------------- mesa

    private static void AddTable(BenchScene bs)
    {
        var s = bs.Scene;
        Vector3 min = bs.Min, max = bs.Max;
        if (min.X > max.X) { min = new(-3, -2, 0); max = new(3, 2, 0); }
        float x0 = MathF.Floor(min.X - 1.2f), x1 = MathF.Ceiling(max.X + 1.2f), y0 = MathF.Floor(min.Y - 1.2f), y1 = MathF.Ceiling(max.Y + 1.2f);
        // Tapete de trabajo con cuadrícula de 0,5 m.
        const float tile = 0.5f;
        for (float x = x0; x < x1 - 1e-3f; x += tile)
            for (float y = y0; y < y1 - 1e-3f; y += tile)
            {
                bool even = ((int)MathF.Round((x - x0) / tile) + (int)MathF.Round((y - y0) / tile)) % 2 == 0;
                SceneBuilder.Quad(s, new(x, y, 0), new(x + tile, y, 0), new(x + tile, y + tile, 0), new(x, y + tile, 0), even ? Mat : MatB);
            }
        // Tablero de madera debajo, un poco más grande.
        const float rim = 0.25f, thick = 0.12f;
        SceneBuilder.Box(s, new((x0 + x1) / 2, (y0 + y1) / 2, -thick / 2 - 0.002f), new(x1 - x0 + 2 * rim, y1 - y0 + 2 * rim, thick), Wood);
        SceneBuilder.Box(s, new((x0 + x1) / 2, (y0 + y1) / 2, -thick - 0.04f), new(x1 - x0 + 2 * rim - 0.1f, y1 - y0 + 2 * rim - 0.1f, 0.08f), WoodDark);
    }

    // ---------------------------------------------------------------- modelos

    private static void Model(Kit k, BenchPart part, PartType? type, PartLook? look, double angle, Func<string, bool> portOn, double phase)
    {
        var st = look?.State ?? PartState.Idle;
        bool on = st == PartState.On;
        string first = look?.Lines.FirstOrDefault() ?? "";
        switch (part.Type)
        {
            case "pulsador":
            {
                k.Enclosure(0.8f, 0.6f, 0.3f, Enclosure);
                bool pressed = first.StartsWith("pulsado");
                uint cap = part.Text("contacto", "NA") == "NC" ? 0xFFD13A31 : 0xFF2EA34F;
                k.Cyl(new(0, -0.05f, 0.3f), new(0, -0.05f, 0.34f), 0.2f, SteelDark);
                k.Cyl(new(0, -0.05f, 0.3f), new(0, -0.05f, pressed ? 0.36f : 0.44f), 0.15f, cap);
                k.Led(new(0.28f, -0.2f, 0.3f), on ? LedGreen : LedOff);
                break;
            }
            case "selector":
            {
                k.Enclosure(0.8f, 0.6f, 0.3f, Enclosure);
                k.Cyl(new(0, -0.05f, 0.3f), new(0, -0.05f, 0.38f), 0.18f, Black);
                bool pos1 = first.StartsWith("posición 1");
                float a = pos1 ? 0.7f : -0.7f;
                var dir = new Vector3(MathF.Sin(a), MathF.Cos(a), 0);
                k.Bar(new Vector3(0, -0.05f, 0.42f) - dir * 0.17f, new Vector3(0, -0.05f, 0.42f) + dir * 0.17f, 0.05f, Black);
                k.Led(new(0.28f, -0.2f, 0.3f), on ? LedGreen : LedOff);
                break;
            }
            case "piloto":
            {
                k.Enclosure(0.8f, 0.6f, 0.3f, Enclosure);
                uint c = LampColor(part.Text("color", "verde"));
                k.Cyl(new(0, -0.05f, 0.3f), new(0, -0.05f, 0.36f), 0.2f, SteelDark);
                k.Ball(new(0, -0.05f, 0.4f), 0.16f, on ? c : Shade(c, 0.35f));
                if (on) k.Ball(new(0, -0.05f, 0.4f), 0.26f, (c & 0x00FFFFFF) | 0x50000000);
                break;
            }
            case "consigna":
            {
                k.Enclosure(0.8f, 0.6f, 0.3f, Enclosure);
                k.Cyl(new(0, -0.05f, 0.3f), new(0, -0.05f, 0.42f), 0.17f, Black);
                k.Bar(new(0, -0.05f, 0.43f), new(0.1f, -0.15f, 0.43f), 0.025f, White);
                break;
            }
            case "transmisor":
            case "pi":
                k.DinModule(0.6f, part.Type == "pi" ? 0xFFE6E2D6 : EnclosureLight, on, st);
                break;
            case "plc":
                Plc(k, part, type, st, portOn);
                break;
            case "arduino-io":
                Arduino(k, part, type, st, portOn);
                break;
            case "red":
            {
                k.BoxB(0, 0.1f, 0, 0.9f, 0.6f, 1.1f, 0xFF8D969F);
                k.BoxB(0, -0.205f, 0.3f, 0.7f, 0.01f, 0.65f, 0xFF7E868F);
                k.Triangle(new(-0.16f, -0.215f, 0.72f), new(0.16f, -0.215f, 0.72f), new(0, -0.215f, 0.98f), Yellow);
                for (int i = -1; i <= 1; i++)
                {
                    k.Cyl(new(i * 0.25f, 0.1f, 1.1f), new(i * 0.25f, 0.1f, 1.3f), 0.06f, White);
                    k.Cyl(new(i * 0.25f, 0.1f, 1.3f), new(i * 0.25f, 0.1f, 1.34f), 0.09f, White);
                }
                k.Led(new(0.32f, -0.21f, 0.98f), on ? LedGreen : LedOff);
                break;
            }
            case "barras":
            {
                float len = k.W * 0.9f;
                for (int i = -1; i <= 1; i++)
                {
                    foreach (float x in new[] { -len / 2 + 0.1f, len / 2 - 0.1f })
                        k.Cyl(new(x, i * 0.22f, 0), new(x, i * 0.22f, 0.25f), 0.05f, 0xFFB33A2E);
                    k.BoxB(0, i * 0.22f, 0.25f, len, 0.08f, 0.05f, on ? Shade(Copper, 1.15f) : Copper);
                }
                break;
            }
            case "contactor":
            {
                k.BoxB(0, 0, 0, 0.6f, 0.55f, 0.75f, EnclosureLight);
                k.BoxB(0, -0.01f, 0.75f, 0.5f, 0.45f, 0.06f, 0xFFBFC4CA);
                bool tripped = st == PartState.Fault || first.Contains("TÉRMICO");
                bool closed = first.StartsWith("cerrado");
                k.BoxB(0, -0.28f, 0.42f, 0.22f, 0.01f, 0.16f, tripped ? LedRed : closed ? 0xFF2E7D32 : 0xFF9AA1A8);
                k.BoxB(0, -0.06f, -0.0f, 0.62f, 0.45f, 0.26f, 0xFF4E5662); // relé térmico
                k.Cyl(new(0.18f, -0.285f, 0.12f), new(0.18f, -0.3f, 0.12f), 0.05f, tripped ? LedRed : 0xFF3B76C4);
                break;
            }
            case "motor":
                RotatingMachine(k, 0.33f, 1.0f, MotorBlue, angle, fins: true);
                break;
            case "generador":
                RotatingMachine(k, 0.42f, 1.1f, GenGreen, angle, fins: false);
                k.Led(new(-0.5f, -0.44f, 0.05f), on ? LedGreen : LedOff);
                break;
            case "carga":
            {
                k.BoxB(0, 0, 0, 0.9f, 0.6f, 0.55f, 0xFF6C737C);
                uint hot = on ? 0xFFFF7A2E : 0xFF3C3F44;
                for (int i = -3; i <= 3; i++) k.BoxB(i * 0.11f, -0.305f, 0.1f, 0.05f, 0.01f, 0.36f, hot);
                k.BoxB(0, 0, 0.55f, 0.95f, 0.65f, 0.04f, 0xFF59606A);
                break;
            }
            case "turbina":
                Turbine(k, angle, st);
                break;
            case "compresor":
                Compressor(k, angle);
                break;
            case "deposito-aire":
            {
                float r = 0.26f, len = Math.Min(k.W * 0.75f, 1.2f);
                k.BoxB(-len / 3, 0, 0, 0.1f, 0.4f, 0.15f, SteelDark);
                k.BoxB(len / 3, 0, 0, 0.1f, 0.4f, 0.15f, SteelDark);
                k.Cyl(new(-len / 2, 0, 0.15f + r), new(len / 2, 0, 0.15f + r), r, 0xFFB83B32, 18, false);
                k.Ball(new(-len / 2, 0, 0.15f + r), r, 0xFFB83B32);
                k.Ball(new(len / 2, 0, 0.15f + r), r, 0xFFB83B32);
                Gauge(k, new(0, 0, 0.15f + 2 * r), look?.Fill ?? 0);
                break;
            }
            case "multimetro":
            {
                k.BoxB(0, 0.05f, 0, 0.6f, 0.15f, 0.85f, Yellow);
                k.BoxB(0, -0.03f, 0.08f, 0.48f, 0.02f, 0.74f, Black);
                k.BoxB(0, -0.045f, 0.55f, 0.38f, 0.01f, 0.2f, 0xFFB9C7A8);
                k.Cyl(new(0, -0.04f, 0.3f), new(0, -0.07f, 0.3f), 0.12f, 0xFF3A3F45);
                k.Bar(new(0, -0.07f, 0.3f), new(0.07f, -0.07f, 0.38f), 0.02f, White);
                break;
            }
            case "osciloscopio":
            {
                k.BoxB(0, 0.05f, 0, 1.2f, 0.55f, 0.72f, 0xFFDADDE1);
                k.BoxB(-0.15f, -0.225f, 0.12f, 0.75f, 0.01f, 0.5f, Screen);
                // Traza que corre por la pantalla.
                const int n = 24;
                for (int i = 0; i < n; i++)
                {
                    float x0 = -0.5f + 0.7f * i / n, x1 = -0.5f + 0.7f * (i + 1) / n;
                    float z0 = 0.37f + 0.15f * MathF.Sin((float)(x0 * 18 - phase * 3)), z1 = 0.37f + 0.15f * MathF.Sin((float)(x1 * 18 - phase * 3));
                    k.Bar(new(x0, -0.235f, z0), new(x1, -0.235f, z1), 0.008f, LedGreen);
                }
                for (int i = 0; i < 4; i++) k.Cyl(new(0.38f, -0.22f, 0.18f + i * 0.13f), new(0.38f, -0.26f, 0.18f + i * 0.13f), 0.04f, Black);
                break;
            }
            case "analizador":
            {
                k.BoxB(0, 0, 0, 0.7f, 0.5f, 0.2f, Black);
                for (int i = 0; i < 8; i++)
                    k.Led(new(-0.28f + i * 0.08f, -0.255f, 0.1f), portOn($"D{i + 1}") ? LedGreen : LedOff);
                break;
            }
            default:
                if (part.Type.StartsWith("h-")) Hydraulic(k, part.Type[2..], look, angle);
                else k.BoxB(0, 0, 0, 0.6f, 0.5f, 0.4f, EnclosureLight);
                break;
        }
    }

    private static void Plc(Kit k, BenchPart part, PartType? type, PartState st, Func<string, bool> portOn)
    {
        float w = k.W * 0.88f;
        k.BoxB(0, 0.25f, 0, w + 0.1f, 0.08f, 0.9f, Steel); // placa de montaje
        k.BoxB(0, 0.05f, 0.12f, w, 0.32f, 0.7f, 0xFF3D434B);
        k.BoxB(-w / 2 + 0.25f, -0.115f, 0.3f, 0.36f, 0.01f, 0.4f, 0xFF59616B);
        k.Led(new(-w / 2 + 0.15f, -0.125f, 0.62f), st == PartState.On ? LedGreen : LedOff);
        k.Led(new(-w / 2 + 0.3f, -0.125f, 0.62f), st == PartState.Fault ? LedRed : LedOff);
        // Fila de LED por cada borne de E/S, como en un PLC compacto.
        var ports = type?.PortsFor(part) ?? [];
        var ins = ports.Where(p => p.Kind == PortKind.SignalIn).ToList();
        var outs = ports.Where(p => p.Kind == PortKind.SignalOut).ToList();
        float x = -w / 2 + 0.55f, step = Math.Min(0.09f, (w - 0.7f) / Math.Max(1, ins.Count + outs.Count + 1));
        foreach (var p in ins) { k.Led(new(x, -0.125f, 0.7f), portOn(p.Name) ? (p.Analog ? LedAmber : LedGreen) : LedOff, 0.025f); x += step; }
        x += step;
        foreach (var p in outs) { k.Led(new(x, -0.125f, 0.7f), portOn(p.Name) ? (p.Analog ? LedAmber : LedGreen) : LedOff, 0.025f); x += step; }
        // Regletas de bornes arriba y abajo.
        k.BoxB(0, 0.0f, 0.78f, w - 0.1f, 0.2f, 0.06f, 0xFF2B3036);
        k.BoxB(0, 0.0f, 0.06f, w - 0.1f, 0.2f, 0.06f, 0xFF2B3036);
    }

    private static void Arduino(Kit k, BenchPart part, PartType? type, PartState st, Func<string, bool> portOn)
    {
        float w = k.W * 0.95f, d = k.D * 0.9f;
        // Placa de E/S de 24 V (azul) con relés, y la placa del microcontrolador encima, a la izquierda.
        k.BoxB(0, 0, 0.06f, w, d, 0.03f, 0xFF1D3F75);
        for (int i = 0; i < 4; i++) foreach (float sx in new[] { -1f, 1f }) foreach (float sy in new[] { -1f, 1f })
            k.Cyl(new(sx * (w / 2 - 0.06f), sy * (d / 2 - 0.06f), 0), new(sx * (w / 2 - 0.06f), sy * (d / 2 - 0.06f), 0.06f), 0.025f, Steel, 6, true);
        var ports = type?.PortsFor(part) ?? [];
        var outs = ports.Where(p => p.Kind == PortKind.SignalOut).ToList();
        var ins = ports.Where(p => p.Kind == PortKind.SignalIn).ToList();
        float rx = -w / 2 + 0.55f, rstep = (w - 0.65f) / Math.Max(1, outs.Count);
        for (int i = 0; i < outs.Count; i++)
        {
            float x = rx + rstep * (i + 0.5f);
            k.BoxB(x, d / 2 - 0.2f, 0.09f, rstep * 0.8f, 0.22f, 0.18f, RelayBlue);
            k.Led(new(x, d / 2 - 0.33f, 0.1f), portOn(outs[i].Name) ? LedRed : LedOff, 0.02f);
        }
        float ix = -w / 2 + 0.55f, istep = (w - 0.65f) / Math.Max(1, ins.Count);
        for (int i = 0; i < ins.Count; i++)
            k.Led(new(ix + istep * (i + 0.5f), -d / 2 + 0.12f, 0.1f), portOn(ins[i].Name) ? LedGreen : LedOff, 0.02f);
        // Microcontrolador.
        float bx = -w / 2 + 0.3f;
        k.BoxB(bx, -0.02f, 0.09f, 0.5f, 0.38f, 0.025f, Pcb);
        k.BoxB(bx, -0.02f, 0.115f, 0.18f, 0.07f, 0.025f, Black);
        k.BoxB(bx - 0.2f, 0.08f, 0.115f, 0.1f, 0.1f, 0.06f, Steel);
        k.BoxB(bx, 0.15f, 0.115f, 0.4f, 0.03f, 0.04f, Black);
        k.BoxB(bx, -0.19f, 0.115f, 0.4f, 0.03f, 0.04f, Black);
        k.Led(new(bx + 0.18f, -0.12f, 0.12f), st == PartState.On ? LedGreen : st == PartState.Fault ? LedRed : LedOff, 0.018f);
    }

    /// <summary>Motor o generador horizontal: carcasa con aletas, patas, caja de bornes y eje que gira.</summary>
    private static void RotatingMachine(Kit k, float r, float len, uint color, double angle, bool fins)
    {
        len = Math.Min(len, k.W * 0.6f);
        float zc = 0.12f + r, xa = -len / 2, xb = len / 2;
        k.BoxB(0, 0, 0, len * 0.8f, r * 1.7f, 0.12f, Shade(color, 0.7f));
        k.Cyl(new(xa, 0, zc), new(xb, 0, zc), r, color, 20, true);
        if (fins)
            for (int i = 1; i < 8; i++)
            {
                float x = xa + len * i / 8;
                k.Cyl(new(x - 0.015f, 0, zc), new(x + 0.015f, 0, zc), r * 1.07f, Shade(color, 0.85f), 20, true);
            }
        k.Cyl(new(xa - 0.12f, 0, zc), new(xa, 0, zc), r * 0.92f, 0xFF4A5460, 20, true); // tapa del ventilador
        k.BoxB(0, 0, zc + r * 0.85f, 0.3f, 0.26f, 0.16f, Shade(color, 0.9f));
        k.Cyl(new(xb, 0, zc), new(xb + 0.3f, 0, zc), 0.045f, Steel, 10, true);
        Rotor(k, new(xb + 0.24f, 0, zc), 0.14f, angle, 0xFFE0A21B, 3);
    }

    /// <summary>Acoplamiento con radios que giran alrededor del eje X (se ve claramente si la máquina gira).</summary>
    private static void Rotor(Kit k, Vector3 c, float radius, double angle, uint color, int spokes)
    {
        k.Cyl(c - new Vector3(0.03f, 0, 0), c + new Vector3(0.03f, 0, 0), radius * 0.45f, SteelDark, 12, true);
        for (int i = 0; i < spokes; i++)
        {
            double a = angle + 2 * Math.PI * i / spokes;
            var tip = c + new Vector3(0, (float)Math.Cos(a) * radius, (float)Math.Sin(a) * radius);
            k.Bar(c, tip, 0.03f, color);
        }
    }

    private static void Turbine(Kit k, double angle, PartState st)
    {
        float len = Math.Min(1.3f, k.W * 0.8f);
        float xa = -len / 2;
        k.BoxB(0, 0, 0, len, 0.7f, 0.12f, 0xFF59606A);
        float[] radii = [0.22f, 0.27f, 0.32f, 0.37f, 0.42f];
        float zc = 0.12f + 0.42f;
        for (int i = 0; i < radii.Length; i++)
        {
            float x0 = xa + len * 0.75f * i / radii.Length, x1 = xa + len * 0.75f * (i + 1) / radii.Length;
            k.Cyl(new(x0, 0, zc), new(x1, 0, zc), radii[i], i % 2 == 0 ? TurbineOrange : Shade(TurbineOrange, 0.88f), 20, true);
        }
        float xe = xa + len * 0.75f;
        k.Cyl(new(xa + 0.15f, 0, zc + 0.2f), new(xa + 0.15f, 0, zc + 0.55f), 0.09f, Steel, 12, true); // entrada de vapor/gas
        k.Cyl(new(xe, 0, zc), new(len / 2, 0, zc), 0.05f, Steel, 10, true);
        Rotor(k, new(len / 2 - 0.1f, 0, zc), 0.18f, angle, 0xFFE0A21B, 4);
        k.Led(new(xa + 0.1f, -0.36f, 0.06f), st == PartState.On ? LedGreen : st == PartState.Fault ? LedRed : LedOff);
    }

    private static void Compressor(Kit k, double angle)
    {
        k.BoxB(0, 0, 0, 0.9f, 0.6f, 0.35f, 0xFF3F6E8C);
        k.BoxB(-0.1f, 0, 0.35f, 0.3f, 0.3f, 0.35f, 0xFF4B4F55);
        for (int i = 0; i < 4; i++) k.BoxB(-0.1f, 0, 0.42f + i * 0.08f, 0.38f, 0.38f, 0.02f, 0xFF5E636A);
        // Volante vertical (eje Y) que gira.
        var c = new Vector3(0.3f, -0.34f, 0.45f);
        k.Cyl(c, c + new Vector3(0, -0.05f, 0), 0.22f, 0xFF2B2F34, 20, true);
        for (int i = 0; i < 4; i++)
        {
            double a = angle + Math.PI * i / 2;
            k.Bar(c + new Vector3(0, -0.055f, 0), c + new Vector3((float)Math.Cos(a) * 0.2f, -0.055f, (float)Math.Sin(a) * 0.2f), 0.025f, 0xFFE0A21B);
        }
    }

    private static void Gauge(Kit k, Vector3 at, double fill)
    {
        k.Cyl(at, at + new Vector3(0, 0, 0.08f), 0.02f, Steel, 6, true);
        var c = at + new Vector3(0, 0, 0.18f);
        k.Cyl(c, c + new Vector3(0, -0.04f, 0), 0.1f, White, 16, true);
        double a = Math.PI * 1.25 - Math.Clamp(fill, 0, 1) * Math.PI * 1.5;
        k.Bar(c + new Vector3(0, -0.045f, 0), c + new Vector3((float)Math.Cos(a) * 0.08f, -0.045f, (float)Math.Sin(a) * 0.08f), 0.01f, LedRed);
    }

    private static void Hydraulic(Kit k, string kind, PartLook? look, double angle)
    {
        bool on = look?.State == PartState.On;
        switch (kind)
        {
            case "tanque":
            {
                float r = Math.Min(0.42f, k.W * 0.28f), h = 1.2f;
                k.Ring(new(0, 0, 0.02f), r, 0.03f, SteelDark);
                double fill = Math.Clamp(look?.Fill ?? 0.4, 0, 1);
                if (fill > 0.005) k.Cyl(new(0, 0, 0.02f), new(0, 0, 0.02f + (h - 0.04f) * (float)fill), r - 0.02f, Water, 24, true);
                k.Cyl(new(0, 0, 0), new(0, 0, h), r, Glass, 24, false);
                k.Ring(new(0, 0, h), r, 0.03f, SteelDark);
                for (int i = 0; i < 4; i++)
                {
                    double a = Math.PI / 4 + i * Math.PI / 2;
                    var p = new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0);
                    k.Bar(p, p + new Vector3(0, 0, h), 0.02f, SteelDark);
                }
                break;
            }
            case "deposito":
            {
                float w = Math.Min(1.4f, k.W * 0.85f), d = 0.8f, h = 0.5f;
                k.BoxB(0, 0, 0, w, d, 0.06f, 0xFFB9B2A6);
                k.BoxB(0, d / 2 - 0.04f, 0.06f, w, 0.08f, h, 0xFFB9B2A6);
                k.BoxB(0, -d / 2 + 0.04f, 0.06f, w, 0.08f, h * 0.9f, 0xFFC4BDB1);
                k.BoxB(-w / 2 + 0.04f, 0, 0.06f, 0.08f, d, h, 0xFFB0A99D);
                k.BoxB(w / 2 - 0.04f, 0, 0.06f, 0.08f, d, h, 0xFFB0A99D);
                k.BoxB(0, 0, 0.06f, w - 0.16f, d - 0.16f, h * 0.6f, Water);
                break;
            }
            case "tuberia":
            {
                float len = k.W * 0.95f, z = 0.2f;
                k.Cyl(new(-len / 2, 0, z), new(len / 2, 0, z), 0.07f, on ? 0xFF8FA9BF : 0xFF9AA4AE, 14, true);
                foreach (float x in new[] { -len / 2, len / 2 })
                    k.Cyl(new(x - 0.02f, 0, z), new(x + 0.02f, 0, z), 0.11f, SteelDark, 14, true);
                foreach (float x in new[] { -len / 3, len / 3 }) k.BoxB(x, 0, 0, 0.06f, 0.18f, z - 0.06f, SteelDark);
                break;
            }
            case "bomba":
            {
                k.BoxB(0, 0, 0, 0.9f, 0.5f, 0.12f, 0xFF3E434A);
                var c = new Vector3(-0.15f, 0, 0.42f);
                k.Cyl(c + new Vector3(0, 0.12f, 0), c - new Vector3(0, 0.12f, 0), 0.3f, PumpRed, 22, true);
                k.Cyl(c + new Vector3(0, 0, 0.28f), c + new Vector3(0, 0, 0.45f), 0.08f, PumpRed, 12, true); // impulsión
                k.Cyl(c, c + new Vector3(-0.45f, 0, 0), 0.08f, PumpRed, 12, true);                         // aspiración
                k.Cyl(new(0.05f, 0, 0.42f), new(0.4f, 0, 0.42f), 0.05f, Steel, 10, true);
                // Rodete que se ve girar en la tapa.
                for (int i = 0; i < 5; i++)
                {
                    double a = angle + 2 * Math.PI * i / 5;
                    k.Bar(c - new Vector3(0, 0.125f, 0), c + new Vector3((float)Math.Cos(a) * 0.24f, -0.125f, (float)Math.Sin(a) * 0.24f), 0.025f, 0xFFF0C24A);
                }
                Rotor(k, new(0.33f, 0, 0.42f), 0.12f, angle, 0xFFE0A21B, 3);
                break;
            }
            case "valvula":
            case "valvula-control":
            case "electrovalvula":
            case "antirretorno":
            {
                float len = Math.Min(0.9f, k.W * 0.7f), z = 0.2f;
                k.Cyl(new(-len / 2, 0, z), new(len / 2, 0, z), 0.08f, 0xFF7C8792, 14, true);
                foreach (float x in new[] { -len / 2, len / 2 }) k.Cyl(new(x - 0.02f, 0, z), new(x + 0.02f, 0, z), 0.12f, SteelDark, 14, true);
                k.Ball(new(0, 0, z), 0.15f, 0xFF7C8792);
                double open = look?.Lines.FirstOrDefault() is { } l && l.StartsWith("abierta") && double.TryParse(new string(l.Where(char.IsDigit).ToArray()), out var pct) ? pct / 100 : 0;
                if (kind == "valvula")
                {
                    k.Cyl(new(0, 0, z), new(0, 0, z + 0.35f), 0.025f, Steel, 8, true);
                    k.Ring(new(0, 0, z + 0.35f), 0.16f, 0.025f, 0xFFC0392B);
                }
                else if (kind == "valvula-control")
                {
                    k.Cyl(new(0, 0, z), new(0, 0, z + 0.3f + 0.08f * (float)open), 0.03f, Steel, 8, true);
                    k.Cyl(new(0, 0, z + 0.38f), new(0, 0, z + 0.5f), 0.22f, 0xFF3B6FB6, 20, true);
                    k.Cyl(new(0, 0, z + 0.5f), new(0, 0, z + 0.53f), 0.24f, 0xFF2D5A96, 20, true);
                }
                else if (kind == "electrovalvula")
                {
                    k.BoxB(0, 0, z + 0.1f, 0.22f, 0.22f, 0.25f, open > 0.01 ? 0xFF2D7D46 : 0xFF33383F);
                    k.Led(new(0, -0.115f, z + 0.28f), open > 0.01 ? LedGreen : LedOff, 0.025f);
                }
                else
                {
                    k.Triangle(new(-0.12f, -0.155f, z - 0.07f), new(-0.12f, -0.155f, z + 0.07f), new(0.1f, -0.155f, z), White);
                }
                break;
            }
            case "sensor-presion":
            case "sensor-nivel":
            case "caudalimetro":
            {
                k.Cyl(new(0, 0, 0), new(0, 0, 0.3f), 0.035f, Steel, 8, true);
                k.Cyl(new(0, 0, 0.3f), new(0, 0, 0.5f), 0.14f, kind == "caudalimetro" ? 0xFF2E7D8C : 0xFF3B6FB6, 18, true);
                k.BoxB(0, -0.13f, 0.34f, 0.16f, 0.02f, 0.12f, ScreenOn);
                break;
            }
            case "nudo":
            {
                k.Ball(new(0, 0, 0.2f), 0.11f, 0xFF9AA4AE);
                k.Cyl(new(0, 0, 0), new(0, 0, 0.12f), 0.03f, SteelDark, 8, true);
                break;
            }
            default:
                k.BoxB(0, 0, 0, 0.5f, 0.4f, 0.3f, 0xFF9AA4AE);
                break;
        }
    }

    // ---------------------------------------------------------------- utilidades

    public static uint Argb(Avalonia.Media.Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static uint Shade(uint argb, float k) => ZBufferRasterizer.Shade(argb, k);

    private static uint LampColor(string name) => name switch
    {
        "rojo" => 0xFFE53935, "ámbar" => 0xFFFFB300, "azul" => 0xFF1E88E5, "blanco" => 0xFFFAFAFA, _ => 0xFF43A047,
    };

    /// <summary>Ayuda para dibujar una pieza en coordenadas locales: origen en el centro de su placa, sobre ella.</summary>
    private sealed class Kit(Scene s, Vector3 origin, float width, float depth)
    {
        /// <summary>
        /// Los modelos se dibujan a esta escala sobre su placa: así llenan la huella del bloque y se reconocen bien
        /// desde lejos. <see cref="W"/> y <see cref="D"/> ya van en unidades del modelo.
        /// </summary>
        private const float K = 1.55f;
        public float W { get; } = Math.Max(width / K, 0.6f);
        public float D { get; } = Math.Max(depth / K, 0.5f);

        private Vector3 P(Vector3 v) => origin + v * K;

        /// <summary>Caja apoyada en z0 (no centrada en Z).</summary>
        public void BoxB(float x, float y, float z0, float sx, float sy, float sz, uint c) =>
            SceneBuilder.Box(s, P(new Vector3(x, y, z0 + sz / 2)), new Vector3(sx, sy, sz) * K, c);

        public void Cyl(Vector3 a, Vector3 b, float r, uint c, int seg = 16, bool caps = true) =>
            SceneBuilder.Cylinder(s, P(a), P(b), r * K, c, seg, caps);

        public void Ball(Vector3 c, float r, uint col) => SceneBuilder.Sphere(s, P(c), r * K, col, 6, 10);

        public void Bar(Vector3 a, Vector3 b, float r, uint c) => SceneBuilder.Cylinder(s, P(a), P(b), r * K, c, 6, true);

        public void Ring(Vector3 c, float r, float t, uint col) => SceneBuilder.Ring(s, P(c), r * K, t * K, col, 20);

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, uint col) => s.Add(new Triangle(P(a), P(b), P(c), col));

        public void Led(Vector3 at, uint c, float r = 0.03f) => Ball(at, r, c);

        /// <summary>Caja de mando de panel (pulsadores, pilotos).</summary>
        public void Enclosure(float w, float d, float h, uint c)
        {
            BoxB(0, 0, 0, w, d, h, c);
            BoxB(0, 0, h - 0.01f, w - 0.06f, d - 0.06f, 0.02f, Shade(c, 1.15f));
        }

        /// <summary>Módulo de carril DIN (convertidor, regulador) con pantalla y LED.</summary>
        public void DinModule(float w, uint c, bool on, PartState st)
        {
            BoxB(0, 0.2f, 0, Math.Min(W, w + 0.4f), 0.12f, 0.07f, Steel);
            BoxB(0, 0, 0.0f, w, 0.55f, 0.75f, c);
            BoxB(0, -0.06f, 0.75f, w * 0.7f, 0.4f, 0.12f, Shade(c, 0.92f));
            BoxB(0, -0.28f, 0.42f, w * 0.7f, 0.01f, 0.18f, on ? ScreenOn : Screen);
            Led(new(-w * 0.28f, -0.285f, 0.2f), st == PartState.On ? LedGreen : st == PartState.Fault ? LedRed : st == PartState.Warning ? LedAmber : LedOff, 0.025f);
        }
    }
}
