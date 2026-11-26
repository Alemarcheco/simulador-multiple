using System.Numerics;
using Simulador.Core.Rendering;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Rendering;

/// <summary>Qué mostrar en la vista 3D.</summary>
public sealed record PanelSceneOptions(double DoorAngleDeg = 110, bool ShowEnclosure = true, bool ShowWires = true, bool ShowDoor = true);

/// <summary>
/// Modelo 3D sencillo del tablero, en metros con Z hacia arriba: el frente de la envolvente está en Y = 0 mirando
/// hacia −Y y el fondo en Y = fondo. Cajas para cada aparato con su frente, carriles, canaletas, placa, puerta con
/// sus mandos (abierta el ángulo que se pida) y los hilos por las canaletas.
/// </summary>
public sealed class PanelScene
{
    public List<Triangle> Triangles { get; } = [];
    public List<(Vector3 A, Vector3 B, uint Color, float Width)> Lines { get; } = [];
    public List<(Vector3 Position, string Text)> Labels { get; } = [];
    public Vector3 Min { get; private set; }
    public Vector3 Max { get; private set; }

    private const uint DefaultEnclosureColor = 0xFFD5D8D4, PlinthColor = 0xFF2E3236, PlateColor = 0xFFDCCFA6, RailColor = 0xFFB9C0C7, DuctColor = 0xFF9EA4AA, DuctCover = 0xFFB4BAC0;

    /// <summary>Sistema local (u a lo ancho, v hacia fuera, w hacia arriba) para poner cajas giradas (puerta).</summary>
    private readonly record struct Basis(Vector3 Origin, Vector3 U, Vector3 V, Vector3 W)
    {
        public Vector3 At(double u, double v, double w) => Origin + U * (float)u + V * (float)v + W * (float)w;
    }

    public static PanelScene Build(PanelDocument doc, IReadOnlyList<WireRoute>? routes, PanelSceneOptions options)
    {
        var s = new PanelScene();
        s.Compose(doc, routes, options);
        return s;
    }

    /// <summary>Color de la chapa: el del modelo de catálogo (RAL 7035, ANSI 61…) o el gris de serie.</summary>
    private uint EnclosureColor = DefaultEnclosureColor;

    private void Compose(PanelDocument doc, IReadOnlyList<WireRoute>? routes, PanelSceneOptions o)
    {
        var e = doc.Enclosure;
        EnclosureColor = e.Color ?? DefaultEnclosureColor;
        double W = e.Width / 1000, H = e.Height / 1000, D = e.Depth / 1000;
        const double t = 0.0015;
        // Mundo: X = ancho, Y = fondo (0 = frente), Z = alto. "Frente" local: v hacia −Y.
        var front = new Basis(Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

        // Envolvente: espalda (salvo con doble frente, que lleva puerta detrás), laterales, techo y suelo si se pide.
        if (!e.DoubleFront) Box(new(0, (float)(D - t), 0), new((float)W, (float)D, (float)H), EnclosureColor);
        if (o.ShowEnclosure)
        {
            Box(new(0, 0, 0), new((float)t, (float)D, (float)H), EnclosureColor);
            Box(new((float)(W - t), 0, 0), new((float)W, (float)D, (float)H), EnclosureColor);
            Box(new(0, 0, (float)(H - t)), new((float)W, (float)D, (float)H), EnclosureColor);
            Box(new(0, 0, 0), new((float)W, (float)D, (float)t), EnclosureColor);
        }

        // Zócalo de 100 mm (armarios de pie), algo metido hacia dentro.
        if (e.Plinth) Box(new(0.01f, 0.01f, -0.1f), new((float)W - 0.01f, (float)D - 0.01f, 0), PlinthColor);

        Side(doc, routes, o, rear: false);
        if (e.DoubleFront) Side(doc, routes, o, rear: true);

        // Caja envolvente para encuadrar.
        var min = new Vector3(0, 0, e.Plinth ? -0.1f : 0);
        var max = new Vector3((float)W, (float)D, (float)H);
        if (o.ShowDoor && o.DoorAngleDeg > 5)
        {
            var ang = o.DoorAngleDeg * Math.PI / 180;
            min = Vector3.Min(min, new Vector3((float)(W * Math.Cos(ang)), (float)(-W * Math.Sin(ang)), 0));
            if (e.DoubleFront) max = Vector3.Max(max, new Vector3((float)W, (float)(D + W * Math.Sin(ang)), (float)H));
        }
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Una cara: placa con carriles, canaletas, aparatos e hilos, y su puerta. La delantera mira hacia −Y; la trasera
    /// (doble frente) está espalda con espalda en el centro del fondo, mira hacia +Y y se ve en espejo desde delante.
    /// </summary>
    private void Side(PanelDocument doc, IReadOnlyList<WireRoute>? routes, PanelSceneOptions o, bool rear)
    {
        var e = doc.Enclosure;
        double W = e.Width / 1000, H = e.Height / 1000, D = e.Depth / 1000, mx = e.PlateLeft / 1000, mz = e.PlateTop / 1000;
        double pw = e.PlateWidth / 1000, ph = e.PlateHeight / 1000;
        double plateY = e.DoubleFront ? (rear ? D / 2 + 0.002 : D / 2 - 0.002) : D - 0.02;
        if (rear) Box(new((float)mx, (float)(plateY - 0.002), (float)(H - mz - ph)), new((float)(mx + pw), (float)plateY, (float)(H - mz)), PlateColor);
        else Box(new((float)mx, (float)plateY, (float)(H - mz - ph)), new((float)(mx + pw), (float)(plateY + 0.002), (float)(H - mz)), PlateColor);

        Vector3 P(double xmm, double ymm, double depthFromPlate) => rear
            ? new((float)(W - mx - xmm / 1000), (float)(plateY + depthFromPlate), (float)(H - mz - ymm / 1000))
            : new((float)(mx + xmm / 1000), (float)(plateY - depthFromPlate), (float)(H - mz - ymm / 1000));

        foreach (var r in doc.RailsOf(rear))
        {
            var a = P(r.X, r.Y + DinRail.RailHeight, 0.0075);
            var b = P(r.X + r.Length, r.Y, 0);
            Box(Vector3.Min(a, b), Vector3.Max(a, b), RailColor);
        }
        var ducts = doc.DuctsOf(rear).ToList();
        foreach (var k in ducts)
        {
            var dd = k.Depth / 1000;
            var a = P(k.X + 1, k.Y + k.H - 1, dd);
            var b = P(k.X + k.W - 1, k.Y + 1, 0);
            Box(Vector3.Min(a, b), Vector3.Max(a, b), DuctColor);
            // Tapa algo más clara.
            var c1 = P(k.X, k.Y + k.H, dd + 0.002);
            var c2 = P(k.X + k.W, k.Y, dd);
            Box(Vector3.Min(c1, c2), Vector3.Max(c1, c2), DuctCover);
        }

        foreach (var d in doc.PlateOf(rear)) PlateDevice(d, P);

        // Hilos: de cada borne baja/sube a la canaleta, que recorren a media profundidad.
        if (o.ShowWires && routes is not null)
        {
            var ductDepth = ducts.Count > 0 ? ducts.Average(k => k.Depth) / 1000 * 0.5 : 0.03;
            foreach (var r in routes)
            {
                var points = r.PointsOn(rear);
                if (points.Count < 2) continue;
                var color = WireColor(r.Wire.Color);
                var pts = points.Select((p, i) => P(p.X, p.Y, i == 0 || i == points.Count - 1 ? TerminalDepth(doc, p, rear) : ductDepth)).ToList();
                for (var i = 0; i + 1 < pts.Count; i++) Lines.Add((pts[i], pts[i + 1], color, 1.6f));
            }
        }

        // Puerta: la delantera con bisagra a la izquierda; la trasera, a la izquierda vista desde detrás.
        if (!o.ShowDoor) return;
        var ang = Math.Clamp(o.DoorAngleDeg, 0, 180) * Math.PI / 180;
        float c = (float)Math.Cos(ang), sn = (float)Math.Sin(ang);
        // Cada hoja con su base: At(x, …) recibe la X de la puerta cerrada (0 = bisagra izquierda vista de frente).
        var hinged = rear
            ? new Basis(new Vector3((float)W, (float)D, 0), new Vector3(-c, sn, 0), new Vector3(sn, c, 0), Vector3.UnitZ)
            : new Basis(Vector3.Zero, new Vector3(c, -sn, 0), new Vector3(-sn, -c, 0), Vector3.UnitZ);
        var leaves = new List<(Basis B, double U0, double U1, bool HandleRight)>();
        if (e.Doors > 1)
        {
            // Dos hojas: la segunda con bisagra al otro lado, gira al revés.
            var second = rear
                ? new Basis(new Vector3((float)W * c, (float)(D + W * sn), 0), new Vector3(-c, -sn, 0), new Vector3(-sn, c, 0), Vector3.UnitZ)
                : new Basis(new Vector3((float)(W - W * c), (float)(-W * sn), 0), new Vector3(c, sn, 0), new Vector3(sn, -c, 0), Vector3.UnitZ);
            leaves.Add((hinged, 0, W / 2, true));
            leaves.Add((second, W / 2, W, false));
        }
        else leaves.Add((hinged, 0, W, true));
        foreach (var (door, u0, u1, handleRight) in leaves)
        {
            BoxIn(door, u0 + 0.001, u1 - 0.001, 0, 0.02, 0, H, EnclosureColor);
            // Junta de goma alrededor.
            BoxIn(door, u0 + 0.02, u1 - 0.02, -0.004, 0, 0.02, 0.03, 0xFF2B2F33);
            BoxIn(door, u0 + 0.02, u1 - 0.02, -0.004, 0, H - 0.03, H - 0.02, 0xFF2B2F33);
            BoxIn(door, u0 + 0.02, u0 + 0.03, -0.004, 0, 0.02, H - 0.02, 0xFF2B2F33);
            BoxIn(door, u1 - 0.03, u1 - 0.02, -0.004, 0, 0.02, H - 0.02, 0xFF2B2F33);
            // Maneta junto al cierre.
            var hx = handleRight ? u1 - 0.06 : u0 + 0.035;
            BoxIn(door, hx, hx + 0.025, 0.02, 0.045, H / 2 - 0.06, H / 2 + 0.06, 0xFF2B2F33);
        }
        // Bolsillo para planos en la primera hoja.
        var (first, f0, f1, _) = leaves[0];
        BoxIn(first, f0 + (f1 - f0) * 0.25, f0 + (f1 - f0) * 0.75, -0.012, 0, 0.08, 0.08 + Math.Min(0.3, H * 0.3), 0xFF5C6670);
        foreach (var d in doc.DoorOf(rear))
        {
            var cx = (d.X + d.Width / 2) / 1000;
            DoorDevice(leaves.Count > 1 && cx >= W / 2 ? leaves[1].B : leaves[0].B, d, H);
        }
        if (!rear && o.ShowWires && routes is not null && routes.Any(r => r.FromDoor || r.ToDoor))
        {
            // Manguera de la puerta: de la canaleta izquierda a la bisagra.
            var z = H / 2;
            Lines.Add((new Vector3((float)(mx + 0.02), (float)(plateY - 0.03), (float)z), new Vector3(0.01f, 0.01f, (float)z), 0xFF505860, 3f));
        }
    }

    private static double TerminalDepth(PanelDocument doc, (double X, double Y) p, bool rear)
    {
        foreach (var d in doc.PlateOf(rear))
            if (p.X >= d.X - 0.5 && p.X <= d.X + d.Width + 0.5 && (Math.Abs(p.Y - d.Y) < 0.5 || Math.Abs(p.Y - d.Y - d.Height) < 0.5))
                return Math.Min(d.Depth * 0.55, 60) / 1000;
        return 0.03;
    }

    public static uint WireColor(string color) => color switch
    {
        "Negro" => 0xFF1E1E1E,
        "Azul claro" => 0xFF5DA9E9,
        "Azul oscuro" => 0xFF1E3A8A,
        "Verde-amarillo" => 0xFF7DBB2E,
        "Marrón" => 0xFF7A4A21,
        "Gris" => 0xFF8A8F94,
        "Blanco" => 0xFFF2F2F2,
        _ => 0xFFD0312D,
    };

    private void PlateDevice(PanelDevice d, Func<double, double, double, Vector3> P)
    {
        var model = DeviceLibrary.Get(d.Kind);
        var rail = d.OnRail ? 0.0075 : 0;
        var depth = d.Depth / 1000;
        double x0 = d.X, x1 = d.X + d.Width, y0 = d.Y, y1 = d.Y + d.Height;
        void B(double ax, double ay, double bx, double by, double z0, double z1, uint c)
        {
            var a = P(ax, by, z1);
            var b = P(bx, ay, z0);
            Box(Vector3.Min(a, b), Vector3.Max(a, b), c);
        }

        // Modelo 3D de la librería de fabricante (paquete .edz), ajustado a la medida del aparato.
        if (ManufacturerCatalog.Find(d.Reference) is { Mesh: { } mesh } item)
        {
            double sx = d.Width / Math.Max(1e-6, item.Width), sz = d.Height / Math.Max(1e-6, item.Height);
            Vector3 M(Vector3 v) => P(d.X + v.X * sx, d.Y + d.Height - v.Z * sz, rail + v.Y / 1000);
            foreach (var (a, b, c, color) in mesh.Triangles) Triangles.Add(new Triangle(M(a), M(b), M(c), color));
            Labels.Add((P(d.X + d.Width / 2, d.Y + d.Height / 2, rail + mesh.Max.Y / 1000 + 0.015), d.Tag));
            return;
        }

        switch (d.Kind)
        {
            case "magnetotermico" or "diferencial" or "fusible" or "telerruptor" or "aparato":
            {
                // Cuerpo escalonado de aparato modular: base, cuello y frente con maneta.
                var h = d.Height;
                B(x0 + 0.3, y0, x1 - 0.3, y1, rail, rail + depth * 0.62, model.Body);
                B(x0 + 0.3, y0 + h * 0.27, x1 - 0.3, y1 - h * 0.27, rail, rail + depth, model.Body);
                var poles = Math.Max(1, (int)Math.Round(d.Width / DeviceLibrary.Module));
                for (var i = 0; i < poles; i++)
                {
                    var px = x0 + (i + 0.5) * d.Width / poles;
                    B(px - 4, y0 + h * 0.36, px + 4, y0 + h * 0.5, rail + depth, rail + depth + 0.008, model.Front);
                }
                if (d.Kind == "diferencial") B(x1 - 9, y0 + h * 0.56, x1 - 3, y0 + h * 0.62, rail + depth, rail + depth + 0.003, 0xFFE6B800);
                break;
            }
            case "regleta":
            {
                var n = d.Terminals.Count / 2;
                B(x0, y0, x0 + 9, y1, rail, rail + depth * 0.7, 0xFF5A6068);
                B(x1 - 9, y0, x1, y1, rail, rail + depth * 0.7, 0xFF5A6068);
                for (var i = 0; i < n; i++)
                {
                    var sx = x0 + 9 + i * DeviceLibrary.TerminalPitch;
                    var c = d.Terminals[2 * i].Label is "N" || d.Terminals[2 * i].Label.StartsWith("XN") ? 0xFF4F8FD6
                        : d.Terminals[2 * i].Label == "PE" ? 0xFF7DBB2E : 0xFFBFC4C9;
                    B(sx + 0.2, y0, sx + DeviceLibrary.TerminalPitch - 0.2, y1, rail, rail + depth, c);
                }
                B(x1 - 11, y0, x1 - 9, y1, rail, rail + depth, 0xFF8C9298);
                break;
            }
            case "contactor" or "rele_termico" or "guardamotor":
                B(x0 + 0.5, y0, x1 - 0.5, y1, rail, rail + depth * 0.8, model.Body);
                B(x0 + 3, y0 + d.Height * 0.15, x1 - 3, y1 - d.Height * 0.15, rail, rail + depth, model.Body);
                B(x0 + 8, y0 + d.Height * 0.35, x1 - 8, y1 - d.Height * 0.35, rail + depth, rail + depth + 0.003, model.Front);
                if (d.Kind == "guardamotor") B(x0 + d.Width / 2 - 6, y0 + d.Height * 0.2, x0 + d.Width / 2 + 6, y0 + d.Height * 0.32, rail + depth, rail + depth + 0.012, 0xFF2B2F33);
                break;
            case "barra_pe":
                B(x0, y0, x1, y1, 0, depth, model.Body);
                B(x0 + 5, y0 + 4, x1 - 5, y1 - 4, depth, depth + 0.002, model.Front);
                break;
            default:
                B(x0 + 0.5, y0, x1 - 0.5, y1, rail, rail + depth, model.Body);
                B(x0 + 5, y0 + 8, x1 - 5, y0 + Math.Min(40, d.Height * 0.35), rail + depth, rail + depth + 0.002, model.Front);
                if (d.Kind is "plc" or "mcu")
                    for (var i = 0; i < 8; i++)
                        B(x0 + 10 + i * 6, y1 - 20, x0 + 13 + i * 6, y1 - 17, rail + depth, rail + depth + 0.002, 0xFF39C24A);
                break;
        }
        Labels.Add((P(d.X + d.Width / 2, d.Y + d.Height / 2, rail + depth + 0.015), d.Tag));
    }

    private void DoorDevice(Basis door, PanelDevice d, double H)
    {
        var model = DeviceLibrary.Get(d.Kind);
        double cx = (d.X + d.Width / 2) / 1000, cz = H - (d.Y + d.Height / 2) / 1000;
        double r = d.Width / 2000;
        if (ManufacturerCatalog.Find(d.Reference) is { Mesh: { } mesh } item)
        {
            // En la puerta: Y = 0 en la cara exterior (lo positivo sobresale, lo negativo queda dentro).
            double sx = d.Width / Math.Max(1e-6, item.Width), sz = d.Height / Math.Max(1e-6, item.Height);
            Vector3 M(Vector3 v) => door.At((d.X + v.X * sx) / 1000, 0.02 + v.Y / 1000, H - (d.Y + d.Height - v.Z * sz) / 1000);
            foreach (var (a, b, c, color) in mesh.Triangles) Triangles.Add(new Triangle(M(a), M(b), M(c), color));
            Labels.Add((door.At(cx, 0.03 + Math.Max(0, mesh.Max.Y) / 1000, cz - d.Height / 2000 - 0.012), d.Tag));
            return;
        }
        switch (d.Kind)
        {
            case "controlador":
            {
                // Equipo de panel: marco que sobresale, pantalla y teclado.
                double hw = d.Width / 2000, hh = d.Height / 2000;
                BoxIn(door, cx - hw, cx + hw, 0.02, 0.035, cz - hh, cz + hh, model.Body);
                BoxIn(door, cx - hw * 0.8, cx + hw * 0.8, 0.035, 0.036, cz + hh * 0.02, cz + hh * 0.8, 0xFF9DB7A6);
                BoxIn(door, cx - hw * 0.7, cx + hw * 0.7, 0.035, 0.038, cz - hh * 0.8, cz - hh * 0.2, model.Front);
                // Por dentro, el cuerpo hacia la placa.
                BoxIn(door, cx - hw * 0.9, cx + hw * 0.9, -d.Depth / 1000, 0, cz - hh * 0.9, cz + hh * 0.9, 0xFF3A4652);
                break;
            }
            case "instrumento":
                BoxIn(door, cx - r, cx + r, 0.02, 0.03, cz - r, cz + r, model.Body);
                BoxIn(door, cx - r * 0.85, cx + r * 0.85, 0.03, 0.031, cz - r * 0.85, cz + r * 0.85, model.Front);
                break;
            case "seta":
                Cylinder(door, cx, cz, 0.016, 0.02, 0.035, 0xFF2B2F33);
                Cylinder(door, cx, cz, r, 0.035, 0.05, model.Front);
                BoxIn(door, cx - r * 1.3, cx + r * 1.3, 0.02, 0.021, cz - r * 1.3, cz + r * 1.3, model.Body);
                break;
            case "selector":
                Cylinder(door, cx, cz, 0.015, 0.02, 0.032, 0xFF2B2F33);
                BoxIn(door, cx - 0.004, cx + 0.004, 0.032, 0.045, cz - 0.012, cz + 0.012, 0xFF15181B);
                break;
            default:
            {
                var c = model.Front;
                if (d.Kind == "piloto") c = PilotColor(d.Description);
                else if (d.Kind == "pulsador") c = d.Description.Contains("rojo") ? 0xFFD32F2F : 0xFF2E9E4D;
                Cylinder(door, cx, cz, 0.015, 0.02, 0.026, 0xFF2B2F33);
                Cylinder(door, cx, cz, 0.0115, 0.026, d.Kind == "piloto" ? 0.034 : 0.03, c);
                break;
            }
        }
        // Lo que va por detrás (bloque de contactos).
        BoxIn(door, cx - 0.013, cx + 0.013, -d.Depth / 1000, 0, cz - 0.013, cz + 0.013, 0xFF6A7077);
        Labels.Add((door.At(cx, 0.06, cz - r - 0.012), d.Tag));
    }

    public static uint PilotColor(string description) =>
        description.Contains("rojo") ? 0xFFE53935 : description.Contains("ámbar") ? 0xFFFFB300
        : description.Contains("azul") ? 0xFF1E88E5 : description.Contains("blanco") ? 0xFFF5F5F5 : 0xFF43A047;

    // ------------------------------------------------------------------ primitivas

    private void Box(Vector3 a, Vector3 b, uint c)
    {
        var basis = new Basis(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        BoxIn(basis, a.X, b.X, a.Y, b.Y, a.Z, b.Z, c);
    }

    private void BoxIn(Basis f, double u0, double u1, double v0, double v1, double w0, double w1, uint c)
    {
        Span<Vector3> p =
        [
            f.At(u0, v0, w0), f.At(u1, v0, w0), f.At(u1, v1, w0), f.At(u0, v1, w0),
            f.At(u0, v0, w1), f.At(u1, v0, w1), f.At(u1, v1, w1), f.At(u0, v1, w1),
        ];
        Quad(p[0], p[1], p[5], p[4], c);
        Quad(p[1], p[2], p[6], p[5], c);
        Quad(p[2], p[3], p[7], p[6], c);
        Quad(p[3], p[0], p[4], p[7], c);
        Quad(p[4], p[5], p[6], p[7], c);
        Quad(p[3], p[2], p[1], p[0], c);
    }

    private void Cylinder(Basis f, double cu, double cw, double r, double v0, double v1, uint c, int seg = 14)
    {
        for (var i = 0; i < seg; i++)
        {
            double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
            var p0 = f.At(cu + r * Math.Cos(a0), v0, cw + r * Math.Sin(a0));
            var p1 = f.At(cu + r * Math.Cos(a1), v0, cw + r * Math.Sin(a1));
            var q0 = f.At(cu + r * Math.Cos(a0), v1, cw + r * Math.Sin(a0));
            var q1 = f.At(cu + r * Math.Cos(a1), v1, cw + r * Math.Sin(a1));
            Quad(p0, p1, q1, q0, c);
            Triangles.Add(new Triangle(f.At(cu, v1, cw), q0, q1, c));
        }
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, uint color)
    {
        Triangles.Add(new Triangle(a, b, c, color));
        Triangles.Add(new Triangle(a, c, d, color));
    }

}
