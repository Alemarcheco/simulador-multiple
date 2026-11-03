using System.Numerics;
using Simulador.Core.Rendering;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.View3D;

/// <summary>Texto anclado a un punto 3D (identificadores y lecturas).</summary>
public readonly record struct Label3D(Vector3 Position, string Text, bool Highlight = false);

public sealed class Scene
{
    public List<Triangle> Triangles { get; } = [];
    public List<Label3D> Labels { get; } = [];
    public Vector3 Min { get; set; } = new(float.MaxValue);
    public Vector3 Max { get; set; } = new(float.MinValue);

    public void Add(Triangle t)
    {
        Triangles.Add(t);
        Min = Vector3.Min(Min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
        Max = Vector3.Max(Max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
    }
}

/// <summary>
/// Convierte el diseño (y, si la hay, la simulación en curso) en una escena 3D a escala real, en metros:
/// X e Y salen de la posición en el esquema (3 cuadros = 2 m) y Z es la cota de cada elemento.
/// Los tanques se ven con el agua a su nivel, las tuberías llevan bandas que avanzan con el caudal y
/// los rodetes de las bombas giran.
/// </summary>
public static class SceneBuilder
{
    public const float MetersPerPixel = 1f / 30f;

    // Colores (ARGB)
    private const uint Steel = 0xFF9AA4AE, SteelDark = 0xFF6B7580, Concrete = 0xFFB9B2A6, GroundA = 0xFFDCE3D3, GroundB = 0xFFD2DAC8;
    private const uint WaterDeep = 0xFF2F7FC9, WaterLight = 0xFF63A9E6, Glass = 0x55B8D4E8, MotorBlue = 0xFF2C5D9E, PumpRed = 0xFFC2462F;
    private const uint Green = 0xFF2DA44E, Red = 0xFFCF222E, Orange = 0xFFE8890C, Yellow = 0xFFF2C744, Black = 0xFF30363D;

    public static Scene Build(HydraulicNetwork net, HydraulicSimulation? sim, double animationSeconds, string? selectedId = null)
    {
        var scene = new Scene();
        if (net.Nodes.Count == 0) return scene;
        float ground = (float)Math.Min(0, net.Nodes.Min(n => n.Elevation)) - 0.01f;

        foreach (var link in net.Links) AddLink(scene, net, sim, link, ground, animationSeconds, link.Id == selectedId);
        foreach (var node in net.Nodes) AddNode(scene, net, sim, node, ground, node.Id == selectedId);
        foreach (var s in net.Sensors) AddSensor(scene, net, sim, s);
        AddGround(scene, ground);
        return scene;
    }

    // ------------------------------------------------------------------ Posiciones

    public static Vector3 PlanPosition(double x, double y, double z) => new((float)x * MetersPerPixel, -(float)y * MetersPerPixel, (float)z);

    /// <summary>Punto donde se conecta una tubería al nudo (fondo del tanque, lámina del depósito, centro del nudo).</summary>
    private static Vector3 Port(HydraulicNode n) => n switch
    {
        Tank t => PlanPosition(t.X, t.Y, t.Elevation),
        Reservoir r => PlanPosition(r.X, r.Y, r.Elevation + Math.Min(r.Level, 0.5) * 0.5),
        _ => PlanPosition(n.X, n.Y, n.Elevation),
    };

    // ------------------------------------------------------------------ Nudos

    private static void AddNode(Scene s, HydraulicNetwork net, HydraulicSimulation? sim, HydraulicNode node, float ground, bool selected)
    {
        var p = PlanPosition(node.X, node.Y, node.Elevation);
        switch (node)
        {
            case Tank t:
            {
                float r = (float)Math.Max(t.Diameter / 2, 0.6), h = (float)Math.Max(t.MaxLevel, 0.2);
                double level = sim is null ? t.InitialLevel : t.Level;
                float lv = (float)Math.Clamp(level, 0, t.MaxLevel);
                if (lv > 0.005f) Cylinder(s, p, p + new Vector3(0, 0, lv), r * 0.97f, WaterDeep, 32, caps: true);
                Cylinder(s, p, p + new Vector3(0, 0, h), r, selected ? 0x66F08C2E : Glass, 32, caps: false);
                Ring(s, p + new Vector3(0, 0, h), r, 0.05f, SteelDark, 32);
                Ring(s, p, r, 0.05f, SteelDark, 32);
                Disk(s, p - new Vector3(0, 0, 0.02f), r, SteelDark, 32);
                // Patas y plataforma si el tanque está elevado.
                if (p.Z - ground > 0.3f)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        double a = Math.PI / 4 + k * Math.PI / 2;
                        var off = new Vector3((float)Math.Cos(a) * r * 0.8f, (float)Math.Sin(a) * r * 0.8f, 0);
                        Box(s, new Vector3(p.X + off.X, p.Y + off.Y, (ground + p.Z) / 2), new Vector3(0.12f, 0.12f, p.Z - ground), SteelDark);
                    }
                    Box(s, p - new Vector3(0, 0, 0.08f), new Vector3(r * 2.1f, r * 2.1f, 0.1f), Steel);
                }
                string text = $"{t.Id}  {level:0.00} m";
                if (sim is not null && sim.Overflow[sim.NodeIndex(t.Id)] > 1e-7) text += "  ¡rebosa!";
                s.Labels.Add(new(p + new Vector3(0, 0, h + 0.4f), text, selected));
                break;
            }
            case Reservoir res:
            {
                float w = 3.4f, d = 1.8f, wall = 0.15f;
                float depth = (float)Math.Max(res.Level + 0.3, 0.8);
                var b = p;
                Box(s, b - new Vector3(0, 0, 0.1f), new Vector3(w, d, 0.2f), Concrete);
                Box(s, b + new Vector3(0, d / 2, depth / 2), new Vector3(w, wall, depth), Concrete);
                Box(s, b + new Vector3(0, -d / 2, depth / 2), new Vector3(w, wall, depth), Concrete);
                Box(s, b + new Vector3(w / 2, 0, depth / 2), new Vector3(wall, d, depth), Concrete);
                Box(s, b + new Vector3(-w / 2, 0, depth / 2), new Vector3(wall, d, depth), Concrete);
                float lv = (float)Math.Max(res.Level, 0.05);
                Quad(s, b + new Vector3(-w / 2 + wall, -d / 2 + wall, lv), b + new Vector3(w / 2 - wall, -d / 2 + wall, lv),
                    b + new Vector3(w / 2 - wall, d / 2 - wall, lv), b + new Vector3(-w / 2 + wall, d / 2 - wall, lv), WaterLight);
                s.Labels.Add(new(b + new Vector3(0, 0, depth + 0.4f), $"{res.Id}  H = {res.Elevation + res.Level:0.#} m", selected));
                break;
            }
            case Junction j:
            {
                float r = 0.09f;
                foreach (var l in net.Links.Where(l => l.From == j.Id || l.To == j.Id))
                    r = Math.Max(r, PipeRadius(l) * 1.35f);
                Sphere(s, p, r, selected ? Orange : Steel);
                if (p.Z - ground > 0.4f) // soporte
                    Box(s, new Vector3(p.X, p.Y, (ground + p.Z - r) / 2), new Vector3(0.08f, 0.08f, p.Z - r - ground), SteelDark);
                if (j.Demand > 0)
                {
                    // Grifo de consumo: bajante corta con el chorro.
                    Cylinder(s, p, p - new Vector3(0, 0, 0.35f), r * 0.6f, Steel, 12, caps: true);
                    bool flowing = sim is null || j.Demand > 0;
                    if (flowing) Cylinder(s, p - new Vector3(0, 0, 0.35f), p - new Vector3(0, 0, 0.75f), r * 0.35f, WaterLight, 10, caps: true);
                }
                string text = j.Id;
                if (sim is not null) text += $"  {sim.PressureAt(sim.NodeIndex(j.Id)) / 1e5:0.00} bar";
                if (j.Demand > 0) text += $"  ↓{j.Demand * 1000:0.#} L/s";
                if (j.Demand > 0 || selected) s.Labels.Add(new(p + new Vector3(0, 0, r + 0.25f), text, selected));
                break;
            }
        }
    }

    // ------------------------------------------------------------------ Enlaces

    private static float PipeRadius(HydraulicLink l)
    {
        double d = l switch { Pipe p => p.Diameter, Valve v => v.Diameter, _ => 0.05 };
        return (float)Math.Clamp(d * 2, 0.1, 0.35);
    }

    /// <summary>Recorrido de la tubería: tramos horizontales a la cota de salida y bajante o subida vertical al llegar.</summary>
    public static List<Vector3> PipePath(HydraulicNetwork net, HydraulicLink link)
    {
        var a = net.FindNode(link.From)!;
        var b = net.FindNode(link.To)!;
        var pa = Port(a);
        var pb = Port(b);
        var pts = new List<Vector3> { pa };
        foreach (var v in link.Vertices) pts.Add(PlanPosition(v.X, v.Y, pa.Z));
        var above = new Vector3(pb.X, pb.Y, pa.Z);
        if (Vector3.Distance(above, pts[^1]) > 1e-3f) pts.Add(above);
        if (Math.Abs(pb.Z - pa.Z) > 1e-3f) pts.Add(pb);
        return pts;
    }

    private static void AddLink(Scene s, HydraulicNetwork net, HydraulicSimulation? sim, HydraulicLink link, float ground,
        double t, bool selected)
    {
        if (net.FindNode(link.From) is null || net.FindNode(link.To) is null) return;
        var path = PipePath(net, link);
        float r = PipeRadius(link);
        double q = 0, vel = 0;
        if (sim is not null)
        {
            try { q = sim.FlowIn(link.Id); vel = sim.VelocityIn(link.Id); } catch (KeyNotFoundException) { }
        }
        bool flowing = Math.Abs(q) > 1e-7;

        // Pieza en el centro del tramo (bomba, válvula) y su sentido.
        var (mid, dir) = Middle(path);
        float gap = link switch { Pump => 0.95f, Valve => 0.45f, _ => 0 };

        double phase = flowing ? Math.Sign(q) * t * Math.Clamp(Math.Abs(vel), 0.3, 2.5) : 0;
        uint body = selected ? Orange : sim is null ? Steel : flowing ? WaterDeep : Steel;
        uint band = selected ? Orange : sim is null ? Steel : flowing ? WaterLight : Steel;
        float walked = 0;
        for (int i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            float len = Vector3.Distance(a, b);
            if (len < 1e-4f) continue;
            if (gap > 0)
            {
                // Corta el tubo donde va la bomba o la válvula.
                var cut = CutAround(a, b, mid, gap);
                foreach (var (ca, cb) in cut) Banded(s, ca, cb, r, body, band, walked + Vector3.Distance(a, ca), phase);
            }
            else Banded(s, a, b, r, body, band, walked, phase);
            walked += len;
            if (i < path.Count - 1) Sphere(s, b, r * 1.25f, Steel); // codo
        }

        switch (link)
        {
            case Pump p: AddPump(s, p, mid, dir, ground, sim, t, selected); break;
            case Valve v: AddValve(s, v, mid, dir, r, sim, selected); break;
        }

        string text = link.Id;
        if (sim is not null) text += $"  {q * 1000:0.00} L/s";
        if (link is Valve || (link is Pipe && selected))
            s.Labels.Add(new(mid + new Vector3(0, 0, r + 0.6f), text, selected));
    }

    private static (Vector3 Mid, Vector3 Dir) Middle(List<Vector3> path)
    {
        float total = 0;
        for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
        float half = total / 2;
        for (int i = 1; i < path.Count; i++)
        {
            float seg = Vector3.Distance(path[i - 1], path[i]);
            if (half <= seg && seg > 0)
            {
                var d = Vector3.Normalize(path[i] - path[i - 1]);
                return (path[i - 1] + d * half, d);
            }
            half -= seg;
        }
        return (path[0], Vector3.UnitX);
    }

    private static IEnumerable<(Vector3, Vector3)> CutAround(Vector3 a, Vector3 b, Vector3 c, float gap)
    {
        var d = b - a;
        float len = d.Length();
        var u = d / len;
        float tc = Vector3.Dot(c - a, u);
        bool onSegment = Vector3.Distance(a + u * tc, c) < 1e-3f && tc > -gap && tc < len + gap;
        if (!onSegment) { yield return (a, b); yield break; }
        if (tc - gap > 0) yield return (a, a + u * (tc - gap));
        if (tc + gap < len) yield return (a + u * (tc + gap), b);
    }

    /// <summary>Tubo con bandas de 0,5 m que se desplazan con <paramref name="phase"/> (sentido y velocidad del caudal).</summary>
    private static void Banded(Scene s, Vector3 a, Vector3 b, float r, uint body, uint band, float startDistance, double phase)
    {
        float len = Vector3.Distance(a, b);
        if (body == band) { Cylinder(s, a, b, r, body, 12, caps: false); return; }
        const float pitch = 0.5f;
        var u = (b - a) / len;
        float pos = 0;
        while (pos < len)
        {
            double world = startDistance + pos - phase;
            double inBand = ((world % pitch) + pitch) % pitch;
            float step = (float)Math.Min(len - pos, inBand < pitch / 2 ? pitch / 2 - inBand : pitch - inBand);
            step = Math.Max(step, 0.01f);
            Cylinder(s, a + u * pos, a + u * Math.Min(pos + step, len), r, inBand < pitch / 2 ? band : body, 12, caps: false);
            pos += step;
        }
    }

    private static void AddPump(Scene s, Pump p, Vector3 c, Vector3 dir, float ground, HydraulicSimulation? sim, double t, bool selected)
    {
        const float K = 1.7f;
        var h = new Vector3(dir.X, dir.Y, 0);
        h = h.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(h);
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, h));
        bool on = sim is null ? p.InitiallyOn : p.RelativeSpeed > 0.01;

        // Bancada hasta el suelo.
        Box(s, new Vector3(c.X, c.Y, (ground + c.Z - 0.3f * K) / 2), new Vector3(1.4f * K, 0.8f * K, Math.Max(c.Z - 0.3f * K - ground, 0.1f * K)), Concrete);
        // Voluta (eje transversal al tubo), bridas y motor detrás.
        Cylinder(s, c - side * 0.13f * K, c + side * 0.13f * K, 0.32f * K, selected ? Orange : PumpRed, 24, caps: true);
        Cylinder(s, c - h * 0.55f * K, c - h * 0.3f * K, 0.09f * K, SteelDark, 12, caps: true);
        Cylinder(s, c + h * 0.3f * K, c + h * 0.55f * K, 0.09f * K, SteelDark, 12, caps: true);
        Cylinder(s, c + side * 0.13f * K, c + side * 0.95f * K, 0.24f * K, MotorBlue, 24, caps: true);
        // Ventilador del motor que gira con la bomba (aspas visibles en la tapa trasera).
        double angle = sim is null ? 0 : t * p.RelativeSpeed * 4;
        var fanCenter = c + side * 0.97f * K;
        var e1 = Vector3.UnitZ;
        var e2 = Vector3.Normalize(Vector3.Cross(side, e1));
        for (int k = 0; k < 4; k++)
        {
            double a = angle + k * Math.PI / 2;
            var dirBlade = e1 * (float)Math.Cos(a) + e2 * (float)Math.Sin(a);
            var perp = e1 * (float)-Math.Sin(a) + e2 * (float)Math.Cos(a);
            Quad(s, fanCenter + perp * 0.03f * K, fanCenter + dirBlade * 0.21f * K + perp * 0.05f * K,
                fanCenter + dirBlade * 0.21f * K - perp * 0.05f * K, fanCenter - perp * 0.03f * K, Black);
        }
        // Piloto de marcha.
        Sphere(s, c + side * 0.5f * K + new Vector3(0, 0, 0.28f * K), 0.06f * K, on ? Green : Red);
        string text = $"{p.Id}  {(on ? "en marcha" : "parada")}";
        if (sim is not null && on) text += $"  {sim.PumpHead(p.Id):0.#} m";
        s.Labels.Add(new(c + new Vector3(0, 0, 0.6f * K), text, selected));
    }

    private static void AddValve(Scene s, Valve v, Vector3 c, Vector3 dir, float r, HydraulicSimulation? sim, bool selected)
    {
        const float K = 1.6f;
        double open = sim is null ? (v.Kind == ValveKind.Check ? 1 : v.InitialOpening) : v.Opening;
        uint color = selected ? Orange : open > 0.99 ? Green : open < 0.01 ? Red : Orange;
        float br = Math.Max(r * 1.6f * K, 0.12f * K);
        Cylinder(s, c - dir * 0.28f * K, c - dir * 0.22f * K, br * 1.1f * K, SteelDark, 16, caps: true);
        Cylinder(s, c + dir * 0.22f * K, c + dir * 0.28f * K, br * 1.1f * K, SteelDark, 16, caps: true);
        Sphere(s, c, br, color);
        var up = Vector3.UnitZ;
        switch (v.Kind)
        {
            case ValveKind.Manual:
                Cylinder(s, c, c + up * 0.4f * K, 0.025f * K, SteelDark, 8, caps: true);
                Ring(s, c + up * 0.4f * K, 0.16f * K, 0.025f * K, Red, 20);
                break;
            case ValveKind.Control:
                Cylinder(s, c, c + up * (0.35f * K + 0.1f * K * (float)open), 0.03f * K, SteelDark, 8, caps: true);
                Cylinder(s, c + up * 0.45f * K, c + up * 0.6f * K, 0.2f * K, Steel, 20, caps: true);
                break;
            case ValveKind.Solenoid:
                Box(s, c + up * (br + 0.12f * K), new Vector3(0.18f * K, 0.18f * K, 0.22f * K), open > 0.5 ? Green : Black);
                break;
            case ValveKind.Check:
                Cylinder(s, c + up * br, c + up * (br + 0.08f * K), 0.08f * K, SteelDark, 12, caps: true);
                break;
        }
    }

    private static void AddSensor(Scene s, HydraulicNetwork net, HydraulicSimulation? sim, Sensor sensor)
    {
        Vector3 at;
        if (net.FindNode(sensor.Target) is { } n)
            at = n is Tank t ? PlanPosition(t.X, t.Y, t.Elevation + t.MaxLevel) + new Vector3((float)t.Diameter / 2 * 0.7f, 0, 0) : Port(n);
        else if (net.FindLink(sensor.Target) is { } l && net.FindNode(l.From) is not null && net.FindNode(l.To) is not null)
            at = Middle(PipePath(net, l)).Mid + new Vector3(0.35f, 0, 0);
        else return;

        var head = at + new Vector3(0, 0, 0.9f);
        Cylinder(s, at, head, 0.04f, SteelDark, 8, caps: true);
        Box(s, head + new Vector3(0, 0, 0.08f), new Vector3(0.34f, 0.2f, 0.26f), Yellow);
        string text = sensor.Id;
        if (sim is not null)
        {
            try
            {
                double v = sim.SensorValue(sensor);
                text += sensor.Kind switch
                {
                    SensorKind.Pressure => $": {v / 1e5:0.00} bar",
                    SensorKind.Level => $": {v:0.00} m",
                    _ => $": {v * 1000:0.00} L/s",
                };
            }
            catch (KeyNotFoundException) { }
        }
        s.Labels.Add(new(head + new Vector3(0, 0, 0.3f), text));
    }

    private static void AddGround(Scene s, float z)
    {
        var min = s.Min;
        var max = s.Max;
        if (min.X > max.X) return;
        const float tile = 5;
        float x0 = MathF.Floor((min.X - 4) / tile) * tile, x1 = MathF.Ceiling((max.X + 4) / tile) * tile;
        float y0 = MathF.Floor((min.Y - 4) / tile) * tile, y1 = MathF.Ceiling((max.Y + 4) / tile) * tile;
        for (float x = x0; x < x1; x += tile)
            for (float y = y0; y < y1; y += tile)
            {
                bool even = ((int)MathF.Round(x / tile) + (int)MathF.Round(y / tile)) % 2 == 0;
                Quad(s, new(x, y, z), new(x + tile, y, z), new(x + tile, y + tile, z), new(x, y + tile, z), even ? GroundA : GroundB);
            }
    }

    // ------------------------------------------------------------------ Primitivas

    public static void Quad(Scene s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, uint color)
    {
        s.Add(new(a, b, c, color));
        s.Add(new(a, c, d, color));
    }

    public static void Box(Scene s, Vector3 center, Vector3 size, uint color)
    {
        var h = size / 2;
        Vector3 P(int x, int y, int z) => center + new Vector3(x * h.X, y * h.Y, z * h.Z);
        Quad(s, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), color);
        Quad(s, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color);
        Quad(s, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color);
        Quad(s, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), color);
        Quad(s, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), color);
        Quad(s, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), color);
    }

    private static (Vector3 U, Vector3 V) Basis(Vector3 axis)
    {
        var helper = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, helper));
        var v = Vector3.Cross(axis, u);
        return (u, v);
    }

    public static void Cylinder(Scene s, Vector3 a, Vector3 b, float r, uint color, int segments, bool caps)
    {
        var axis = b - a;
        if (axis.LengthSquared() < 1e-10f) return;
        var (u, v) = Basis(Vector3.Normalize(axis));
        for (int i = 0; i < segments; i++)
        {
            double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
            var o0 = (u * (float)Math.Cos(a0) + v * (float)Math.Sin(a0)) * r;
            var o1 = (u * (float)Math.Cos(a1) + v * (float)Math.Sin(a1)) * r;
            Quad(s, a + o0, a + o1, b + o1, b + o0, color);
            if (caps)
            {
                s.Add(new(a, a + o1, a + o0, color));
                s.Add(new(b, b + o0, b + o1, color));
            }
        }
    }

    public static void Disk(Scene s, Vector3 c, float r, uint color, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
            s.Add(new(c, c + new Vector3((float)Math.Cos(a0) * r, (float)Math.Sin(a0) * r, 0),
                c + new Vector3((float)Math.Cos(a1) * r, (float)Math.Sin(a1) * r, 0), color));
        }
    }

    /// <summary>Aro horizontal (borde de tanque, volante de válvula).</summary>
    public static void Ring(Scene s, Vector3 c, float r, float thickness, uint color, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
            var p0 = c + new Vector3((float)Math.Cos(a0) * r, (float)Math.Sin(a0) * r, 0);
            var p1 = c + new Vector3((float)Math.Cos(a1) * r, (float)Math.Sin(a1) * r, 0);
            Cylinder(s, p0, p1, thickness, color, 6, caps: false);
        }
    }

    public static void Sphere(Scene s, Vector3 c, float r, uint color, int rings = 8, int segments = 12)
    {
        for (int i = 0; i < rings; i++)
        {
            double t0 = Math.PI * i / rings - Math.PI / 2, t1 = Math.PI * (i + 1) / rings - Math.PI / 2;
            for (int j = 0; j < segments; j++)
            {
                double p0 = 2 * Math.PI * j / segments, p1 = 2 * Math.PI * (j + 1) / segments;
                Vector3 P(double t, double p) => c + new Vector3((float)(Math.Cos(t) * Math.Cos(p)), (float)(Math.Cos(t) * Math.Sin(p)), (float)Math.Sin(t)) * r;
                Quad(s, P(t0, p0), P(t0, p1), P(t1, p1), P(t1, p0), color);
            }
        }
    }
}
