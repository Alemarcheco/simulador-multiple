using System.Numerics;
using Simulador.Core.Rendering;
using Simulador.Hidraulica.View3D;
using static Simulador.Hidraulica.View3D.SceneBuilder;

namespace Simulador.Hidraulica.Neumatica.View3D;

/// <summary>
/// Maqueta 3D del circuito neumático sobre un tablero de prácticas: la planta sigue el esquema
/// (1 px = 2,5 mm) y los cilindros sacan y meten el vástago con la simulación.
/// </summary>
public static class PneumaticSceneBuilder
{
    public const float Scale = 0.0025f;
    public const float Magnify = 4f;

    private const uint Plate = 0xFF7D8894, PlateSlot = 0xFF66707B, Alu = 0xFFE6EAEE, AluDark = 0xFF8C959F, Chrome = 0xFFE4E8EC;
    private const uint ValveBody = 0xFF3B4650, CoilOff = 0xFF24292F, CoilOn = 0xFFF08C2E, Blue = 0xFF1F6FEB, TubeIdle = 0xFF5A6B7D;
    private const uint Red = 0xFFCF222E, Green = 0xFF2DA44E, White = 0xFFF6F8FA, Black = 0xFF30363D, Yellow = 0xFFF2C744;

    public static Scene Build(PneumaticCircuit circuit, PneumaticSimulation? sim, string? selectedId = null)
    {
        var s = new Scene();
        BuildPlate(s, circuit);
        for (int i = 0; i < circuit.Tubes.Count; i++) AddTube(s, circuit, circuit.Tubes[i], sim?.TubePressure(i) ?? 0, sim is not null);
        foreach (var c in circuit.Components) AddComponent(s, circuit, c, sim, c.Id == selectedId);
        // Maqueta ampliada ×4: un tablero de un metro se ve tan grande como una instalación hidráulica.
        var big = new Scene();
        foreach (var t in s.Triangles) big.Add(new Triangle(t.A * Magnify, t.B * Magnify, t.C * Magnify, t.Color));
        foreach (var l in s.Labels) big.Labels.Add(l with { Position = l.Position * Magnify });
        return big;
    }

    private static Vector3 World(PneumaticComponent c, double lx, double ly, float z)
    {
        var (rx, ry) = PneumaticComponent.Rotate(lx, ly, c.Rotation);
        return new Vector3((float)(c.X + rx) * Scale, -(float)(c.Y + ry) * Scale, z);
    }

    private static Vector3 Plan(double x, double y, float z) => new((float)x * Scale, -(float)y * Scale, z);

    private static void BuildPlate(Scene s, PneumaticCircuit circuit)
    {
        double x0 = 0, x1 = 800, y0 = 0, y1 = 600;
        if (circuit.Components.Count > 0)
        {
            var boxes = circuit.Components.Select(c =>
            {
                var r = Ui.PneumaticCanvas.LocalBounds(c);
                var pts = new[] { r.TopLeft, r.BottomRight, r.TopRight, r.BottomLeft }.Select(p => PneumaticComponent.Rotate(p.X, p.Y, c.Rotation)).ToList();
                return (X0: c.X + pts.Min(p => p.Item1), X1: c.X + pts.Max(p => p.Item1), Y0: c.Y + pts.Min(p => p.Item2), Y1: c.Y + pts.Max(p => p.Item2));
            }).ToList();
            x0 = boxes.Min(b => b.X0) - 30; x1 = boxes.Max(b => b.X1) + 30;
            y0 = boxes.Min(b => b.Y0) - 30; y1 = boxes.Max(b => b.Y1) + 30;
        }
        var a = Plan(x0, y0, 0);
        var b = Plan(x1, y1, 0);
        var center = (a + b) / 2;
        float w = Math.Abs(b.X - a.X), d = Math.Abs(b.Y - a.Y);
        Box(s, center - new Vector3(0, 0, 0.012f), new Vector3(w, d, 0.024f), Plate);
        // Ranuras del perfil de aluminio cada 5 cm.
        for (float y = Math.Min(a.Y, b.Y) + 0.05f; y < Math.Max(a.Y, b.Y); y += 0.05f)
            Box(s, new Vector3(center.X, y, 0.0005f), new Vector3(w, 0.006f, 0.001f), PlateSlot);
    }

    private static void AddTube(Scene s, PneumaticCircuit circuit, Tube t, double pressure, bool running)
    {
        if (circuit.Find(t.FromComponent) is not { } a || circuit.Find(t.ToComponent) is not { } b || !a.HasPort(t.FromPort) || !b.HasPort(t.ToPort)) return;
        var pts = Ui.PneumaticCanvas.TubePolyline(t, a, b);
        uint color = running && pressure > 0.3e5 ? Blue : TubeIdle;
        float r = Math.Max((float)t.InnerDiameter * 0.8f, 0.0028f);
        for (int i = 1; i < pts.Count; i++)
        {
            var p = Plan(pts[i - 1].X, pts[i - 1].Y, 0.01f);
            var q = Plan(pts[i].X, pts[i].Y, 0.01f);
            if (Vector3.Distance(p, q) < 1e-4f) continue;
            Cylinder(s, p, q, r, color, 8, caps: false);
            if (i < pts.Count - 1) Sphere(s, q, r, color, 4, 8);
        }
    }

    private static void AddComponent(Scene s, PneumaticCircuit circuit, PneumaticComponent c, PneumaticSimulation? sim, bool selected)
    {
        string label = c.Id;
        switch (c)
        {
            case Cylinder cy:
            {
                float r = Math.Max((float)cy.Bore / 2 + 0.012f, 0.03f);
                float z = r + 0.012f;
                double ratio = sim?.PositionRatio(cy.Id) ?? cy.InitialPosition;
                double xp = -60 + Math.Clamp(ratio, 0, 1) * Cylinder.RodTravel;
                var back = World(cy, -70, 0, z);
                var front = World(cy, 70, 0, z);
                Cylinder(s, back, front, r, selected ? 0xFFF0B060 : Alu, 20, caps: true);
                Cylinder(s, back, World(cy, -62, 0, z), r * 1.12f, AluDark, 20, caps: true);
                Cylinder(s, World(cy, 62, 0, z), front, r * 1.12f, AluDark, 20, caps: true);
                Cylinder(s, World(cy, 70, 0, z), World(cy, xp + 150, 0, z), Math.Max((float)cy.RodDiameter / 2 + 0.002f, 0.008f), Chrome, 12, caps: true);
                Box(s, World(cy, xp + 154, 0, z), new Vector3(0.022f, 0.022f, 0.022f), AluDark);
                foreach (var lx in new[] { -60.0, 60.0 })
                    Box(s, World(cy, lx, 0, z / 2), new Vector3(0.03f, r * 2.2f, z), AluDark);
                foreach (var (name, lx) in new[] { (cy.SensorRetracted, -50.0), (cy.SensorExtended, 50.0) })
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    bool on = sim?.SensorActive(name) == true;
                    Box(s, World(cy, lx, 0, z + r + 0.004f), new Vector3(0.02f, 0.008f, 0.008f), on ? Yellow : Black);
                }
                label += sim is null ? $"  Ø{cy.Bore * 1000:0}×{cy.Stroke * 1000:0}" : $"  {sim.Position(cy.Id) * 1000:0} mm";
                s.Labels.Add(new(World(cy, 0, 0, z + r + 0.03f), label, selected));
                return;
            }
            case DirectionalValve v:
            {
                bool on = sim?.IsActuated(v.Id) ?? (v.IsBistable && v.InitiallyActuated);
                float z = 0.03f;
                Box(s, World(v, 0, 0, z), RotatedSize(v, 0.17f, 0.07f, 0.05f), selected ? 0xFF6B5A40 : ValveBody);
                // Indicador de la corredera.
                Box(s, World(v, on ? -20 : 20, 0, z + 0.027f), RotatedSize(v, 0.08f, 0.02f, 0.004f), on ? Blue : AluDark);
                var left = World(v, -44, 0, z);
                switch (v.Actuation)
                {
                    case ValveActuation.PushButton:
                    {
                        bool pressed = sim?.IsPressed(v.Id) == true;
                        var tip = World(v, pressed ? -48 : -56, 0, z);
                        Cylinder(s, left, tip, 0.008f, AluDark, 10, caps: true);
                        Cylinder(s, tip, World(v, pressed ? -52 : -60, 0, z), 0.018f, Red, 16, caps: true);
                        break;
                    }
                    case ValveActuation.Lever:
                        Cylinder(s, left, World(v, -48, 0, z), 0.01f, AluDark, 10, caps: true);
                        Cylinder(s, World(v, -48, 0, z), World(v, on ? -40 : -60, 0, z + 0.07f), 0.005f, Black, 8, caps: true);
                        Sphere(s, World(v, on ? -40 : -60, 0, z + 0.07f), 0.01f, Red, 6, 10);
                        break;
                    case ValveActuation.Solenoid or ValveActuation.DoubleSolenoid:
                    {
                        bool e14 = sim?.IsCoilEnergized(PneumaticSimulation.CoilKey(v, true)) == true;
                        Box(s, World(v, -52, 0, z + 0.005f), RotatedSize(v, 0.035f, 0.05f, 0.06f), e14 ? CoilOn : CoilOff);
                        if (v.Actuation == ValveActuation.DoubleSolenoid)
                        {
                            bool e12 = sim?.IsCoilEnergized(PneumaticSimulation.CoilKey(v, false)) == true;
                            Box(s, World(v, 52, 0, z + 0.005f), RotatedSize(v, 0.035f, 0.05f, 0.06f), e12 ? CoilOn : CoilOff);
                        }
                        break;
                    }
                    case ValveActuation.Roller:
                        Cylinder(s, left, World(v, -54, 0, z), 0.006f, AluDark, 8, caps: true);
                        Cylinder(s, World(v, -60, -6, z), World(v, -60, 6, z), 0.012f, on ? Yellow : Black, 14, caps: true);
                        break;
                    case ValveActuation.Pilot or ValveActuation.DoublePilot:
                        Cylinder(s, left, World(v, -52, 0, z), 0.02f, AluDark, 14, caps: true);
                        if (v.Actuation == ValveActuation.DoublePilot) Cylinder(s, World(v, 44, 0, z), World(v, 52, 0, z), 0.02f, AluDark, 14, caps: true);
                        break;
                }
                if (v.HasSpring) Cylinder(s, World(v, 40, 0, z), World(v, 50, 0, z), 0.016f, AluDark, 12, caps: true);
                break;
            }
            case FlowControl f:
                Cylinder(s, World(f, 0, 24, 0.016f), World(f, 0, -24, 0.016f), 0.012f, Alu, 12, caps: true);
                Cylinder(s, World(f, 0, 0, 0.02f), World(f, 0, 0, 0.05f), 0.006f, AluDark, 8, caps: true);
                Cylinder(s, World(f, 0, 0, 0.05f), World(f, 0, 0, 0.06f), 0.013f, Blue, 14, caps: true);
                label += $"  {f.Opening * 100:0} %";
                break;
            case CheckValve k:
                Cylinder(s, World(k, 0, 20, 0.014f), World(k, 0, -20, 0.014f), 0.011f, Alu, 12, caps: true);
                break;
            case LogicValve l:
                Box(s, World(l, 0, 0, 0.02f), RotatedSize(l, 0.13f, 0.04f, 0.04f), l.Kind == LogicKind.And ? 0xFF8250DF : 0xFF0E8A8A);
                break;
            case AirSupply a:
                Cylinder(s, World(a, 0, 0, 0.0f), World(a, 0, 0, 0.06f), 0.025f, Alu, 18, caps: true);
                Cylinder(s, World(a, 0, 0, 0.06f), World(a, 0, 0, 0.075f), 0.03f, Blue, 18, caps: true);
                label += $"  {a.Pressure / 1e5:0.#} bar";
                break;
            case Compressor k:
            {
                bool run = sim?.IsCompressorRunning(k.Id) ?? false;
                Box(s, World(k, 0, 0, 0.05f), new Vector3(0.12f, 0.1f, 0.1f), 0xFF2C5D9E);
                Cylinder(s, World(k, -40, 0, 0.05f), World(k, -20, 0, 0.05f), 0.04f, 0xFF2C5D9E, 16, caps: true);
                Sphere(s, World(k, 0, 0, 0.11f), 0.012f, run ? Green : Red, 6, 10);
                label += run ? "  en marcha" : "";
                break;
            }
            case AirReceiver r:
            {
                float rad = (float)Math.Clamp(Math.Cbrt(r.Volume / (Math.PI * 2.5)), 0.05, 0.3);
                float h = (float)(r.Volume / (Math.PI * rad * rad));
                var basePt = World(r, 0, 0, 0);
                Cylinder(s, basePt, basePt + new Vector3(0, 0, h), rad, 0xFFB8C2CC, 24, caps: true);
                Sphere(s, basePt + new Vector3(0, 0, h), rad * 0.98f, 0xFFB8C2CC, 6, 16);
                if (sim is not null) label += $"  {sim.PressureAt(r.Id, "1") / 1e5:0.00} bar";
                s.Labels.Add(new(basePt + new Vector3(0, 0, h + rad + 0.03f), label, selected));
                return;
            }
            case Gauge g:
            {
                var center = World(g, 0, 0, 0.04f);
                Cylinder(s, center + new Vector3(0, 0.006f, 0), center - new Vector3(0, 0.006f, 0), 0.03f, Black, 20, caps: true);
                Disk(s, center - new Vector3(0, 0.0065f, 0), 0.026f, White, 20);
                double p = sim?.PressureAt(g.Id, "1") ?? 0;
                double ang = Math.PI * (1.25 - 1.5 * Math.Clamp(p / 10e5, 0, 1));
                var tip = center + new Vector3((float)Math.Cos(ang) * 0.022f, -0.008f, (float)Math.Sin(ang) * 0.022f);
                Cylinder(s, center - new Vector3(0, 0.008f, 0), tip, 0.0015f, Red, 6, caps: false);
                Cylinder(s, World(g, 0, 22, 0.01f), center - new Vector3(0, 0, 0.03f), 0.004f, AluDark, 8, caps: false);
                if (sim is not null) label += $"  {p / 1e5:0.00} bar";
                break;
            }
        }
        var top = LabelHeight(c);
        s.Labels.Add(new(World(c, 0, 0, top), label, selected));
    }

    private static float LabelHeight(PneumaticComponent c) => c switch
    {
        DirectionalValve => 0.11f,
        Compressor => 0.16f,
        Gauge => 0.1f,
        _ => 0.09f,
    };

    /// <summary>Tamaño de una caja cuyo largo sigue el eje X local del componente girado.</summary>
    private static Vector3 RotatedSize(PneumaticComponent c, float lx, float ly, float lz) =>
        c.Rotation % 180 == 0 ? new Vector3(lx, ly, lz) : new Vector3(ly, lx, lz);
}
