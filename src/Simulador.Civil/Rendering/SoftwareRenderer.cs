using Simulador.Core.Building;

namespace Simulador.Civil.Rendering;

/// <summary>
/// Cámara orbital ortográfica. El encuadre se calcula con la esfera que envuelve el edificio, que no cambia al girar:
/// así la escala y el centro se mantienen fijos mientras se rota (antes se recalculaban en cada fotograma con la
/// silueta proyectada y el modelo "respiraba" y se deformaba al girar).
/// </summary>
public sealed class OrbitCamera
{
    public double Yaw { get; set; } = -0.6;
    public double Pitch { get; set; } = 0.6;
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public void Reset()
    {
        Yaw = -0.6; Pitch = 0.6; Zoom = 1; PanX = PanY = 0;
    }

    /// <summary>Proyector para un lienzo de <paramref name="width"/>×<paramref name="height"/> píxeles.</summary>
    public Projector CreateProjector((Point3 Min, Point3 Max) bounds, int width, int height)
    {
        var c = new Point3((bounds.Min.X + bounds.Max.X) / 2, (bounds.Min.Y + bounds.Max.Y) / 2, (bounds.Min.Z + bounds.Max.Z) / 2);
        double radius = Math.Max(0.5, bounds.Min.DistanceTo(bounds.Max) / 2);
        double scale = Math.Min(width, height) * 0.92 / (2 * radius) * Zoom;
        return new Projector(Yaw, Pitch, c, scale, width / 2.0 + PanX, height / 2.0 + PanY);
    }
}

/// <summary>Proyección ortográfica: X/Y en píxeles (Y hacia abajo) y profundidad (mayor = más lejos).</summary>
public readonly struct Projector
{
    private readonly double _cy, _sy, _cp, _sp, _scale, _ox, _oy;
    private readonly Point3 _c;

    public Projector(double yaw, double pitch, Point3 center, double scale, double originX, double originY)
    {
        _cy = Math.Cos(yaw); _sy = Math.Sin(yaw); _cp = Math.Cos(pitch); _sp = Math.Sin(pitch);
        _c = center; _scale = scale; _ox = originX; _oy = originY;
    }

    /// <summary>Vector unitario desde la escena hacia la cámara.</summary>
    public (double X, double Y, double Z) ToCamera => (-_cp * _sy, -_cp * _cy, _sp);

    public (double X, double Y, double Depth) Project(Point3 p)
    {
        double x = p.X - _c.X, y = p.Y - _c.Y, z = p.Z - _c.Z;
        double xr = x * _cy - y * _sy;
        double yr = x * _sy + y * _cy;
        return (_ox + xr * _scale, _oy - (z * _cp + yr * _sp) * _scale, yr * _cp - z * _sp);
    }
}

/// <summary>
/// Rasterizador por software con búfer de profundidad (z-buffer): cada píxel guarda la cara más cercana, así las
/// superficies se tapan correctamente desde cualquier ángulo, sin los errores del orden de pintor.
/// Escribe en un búfer BGRA (enteros 0xAARRGGBB) listo para copiar a un mapa de bits.
/// </summary>
public sealed class SoftwareRenderer
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int[] Pixels { get; private set; } = [];
    private float[] _depth = [];

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width; Height = height;
        Pixels = new int[width * height];
        _depth = new float[width * height];
    }

    public void Clear(uint argb)
    {
        Array.Fill(Pixels, unchecked((int)argb));
        Array.Fill(_depth, float.PositiveInfinity);
    }

    /// <summary>Dibuja la malla completa: caras sombreadas, aristas y rutas.</summary>
    public void Render(BuildingMesh mesh, Projector proj, bool edges = true)
    {
        var cam = proj.ToCamera;
        var light = Normalize((0.4, -0.6, 0.8));
        var ps = new (double X, double Y, double Depth)[16];

        foreach (var f in mesh.Faces)
        {
            var n = Normal(f.Pts);
            if (n.X * cam.X + n.Y * cam.Y + n.Z * cam.Z <= 1e-9) continue; // de espaldas a la cámara
            double shade = 0.62 + 0.38 * Math.Max(0, n.X * light.X + n.Y * light.Y + n.Z * light.Z);
            uint col = Shade(f.Argb, shade);
            if (ps.Length < f.Pts.Length) ps = new (double, double, double)[f.Pts.Length];
            for (int i = 0; i < f.Pts.Length; i++) ps[i] = proj.Project(f.Pts[i]);
            for (int i = 1; i + 1 < f.Pts.Length; i++) FillTriangle(ps[0], ps[i], ps[i + 1], col);
            if (edges)
            {
                uint ec = Shade(f.Argb, shade * 0.62);
                for (int i = 0; i < f.Pts.Length; i++) DrawLine(ps[i], ps[(i + 1) % f.Pts.Length], ec, depthTest: true, width: 1);
            }
        }
        foreach (var l in mesh.Lines)
            DrawLine(proj.Project(l.A), proj.Project(l.B), l.Argb, depthTest: false, width: 2);
    }

    private void FillTriangle((double X, double Y, double Depth) a, (double X, double Y, double Depth) b, (double X, double Y, double Depth) c, uint argb)
    {
        double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(area) < 1e-12) return;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        int maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        int maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        if (minX > maxX || minY > maxY) return;
        double inv = 1 / area;
        int color = unchecked((int)argb);
        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            int row = y * Width;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * inv;
                double w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * inv;
                double w2 = 1 - w0 - w1;
                if (w0 < -1e-9 || w1 < -1e-9 || w2 < -1e-9) continue;
                float z = (float)(w0 * a.Depth + w1 * b.Depth + w2 * c.Depth);
                int i = row + x;
                if (z < _depth[i])
                {
                    _depth[i] = z;
                    Pixels[i] = color;
                }
            }
        }
    }

    private void DrawLine((double X, double Y, double Depth) a, (double X, double Y, double Depth) b, uint argb, bool depthTest, int width)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (steps > 20000) return; // fuera de la vista
        int color = unchecked((int)argb);
        const float bias = 0.02f; // las aristas ganan a su propia cara
        for (int s = 0; s <= steps; s++)
        {
            double t = steps == 0 ? 0 : (double)s / steps;
            int x0 = (int)(a.X + dx * t), y0 = (int)(a.Y + dy * t);
            float z = (float)(a.Depth + (b.Depth - a.Depth) * t);
            for (int ox = 0; ox < width; ox++)
            for (int oy = 0; oy < width; oy++)
            {
                int x = x0 + ox, y = y0 + oy;
                if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) continue;
                int i = y * Width + x;
                if (depthTest && z - bias > _depth[i]) continue;
                Pixels[i] = color;
            }
        }
    }

    private static uint Shade(uint argb, double k)
    {
        uint r = (uint)Math.Clamp(((argb >> 16) & 0xFF) * k, 0, 255);
        uint g = (uint)Math.Clamp(((argb >> 8) & 0xFF) * k, 0, 255);
        uint b = (uint)Math.Clamp((argb & 0xFF) * k, 0, 255);
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    private static (double X, double Y, double Z) Normal(Point3[] pts)
    {
        double x = 0, y = 0, z = 0; // método de Newell
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        return Normalize((x, y, z));
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l < 1e-12 ? (0, 0, 0) : (v.X / l, v.Y / l, v.Z / l);
    }

    /// <summary>Reduce un búfer renderizado a <paramref name="factor"/>× promediando bloques (suavizado de bordes).</summary>
    public static int[] Downsample(int[] src, int width, int height, int factor, out int outW, out int outH)
    {
        outW = width / factor;
        outH = height / factor;
        var dst = new int[outW * outH];
        int n = factor * factor;
        for (int y = 0; y < outH; y++)
        for (int x = 0; x < outW; x++)
        {
            int r = 0, g = 0, b = 0;
            for (int sy = 0; sy < factor; sy++)
            for (int sx = 0; sx < factor; sx++)
            {
                int p = src[(y * factor + sy) * width + x * factor + sx];
                r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF;
            }
            dst[y * outW + x] = unchecked((int)0xFF000000) | (r / n << 16) | (g / n << 8) | (b / n);
        }
        return dst;
    }
}
