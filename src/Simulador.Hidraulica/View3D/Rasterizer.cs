using System.Numerics;

namespace Simulador.Hidraulica.View3D;

/// <summary>Cámara orbital: gira alrededor de <see cref="Target"/>. Ángulos en grados, distancias en metros.</summary>
public sealed class OrbitCamera
{
    public Vector3 Target { get; set; }
    public double Yaw { get; set; } = -60;
    public double Pitch { get; set; } = 28;
    public double Distance { get; set; } = 60;
    /// <summary>Campo de visión vertical (grados).</summary>
    public double FieldOfView { get; set; } = 45;

    public (Vector3 Eye, Vector3 Right, Vector3 Up, Vector3 Forward) Frame()
    {
        double yaw = Yaw * Math.PI / 180, pitch = Pitch * Math.PI / 180;
        var dir = new Vector3((float)(Math.Cos(pitch) * Math.Cos(yaw)), (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch));
        var eye = Target + dir * (float)Distance;
        var forward = Vector3.Normalize(-dir);
        var right = Vector3.Cross(forward, Vector3.UnitZ);
        right = right.LengthSquared() < 1e-9f ? Vector3.UnitX : Vector3.Normalize(right);
        var up = Vector3.Cross(right, forward);
        return (eye, right, up, forward);
    }

    public void Orbit(double dxDeg, double dyDeg)
    {
        Yaw -= dxDeg;
        Pitch = Math.Clamp(Pitch + dyDeg, -5, 89);
    }

    public void Pan(double dxPixels, double dyPixels, int viewportHeight)
    {
        var (_, right, up, _) = Frame();
        double metersPerPixel = 2 * Distance * Math.Tan(FieldOfView * Math.PI / 360) / Math.Max(viewportHeight, 1);
        Target += (-right * (float)dxPixels + up * (float)dyPixels) * (float)metersPerPixel;
    }

    public void Zoom(double wheel) => Distance = Math.Clamp(Distance * Math.Pow(0.88, wheel), 2, 2000);

    public void Fit(Vector3 min, Vector3 max)
    {
        if (min.X > max.X) return;
        Target = (min + max) / 2;
        float size = (max - min).Length();
        Distance = Math.Max(5, size / (2 * Math.Tan(FieldOfView * Math.PI / 360)) * 0.8);
    }
}

/// <summary>
/// Rasterizador por software con búfer de profundidad: cada píxel muestra la superficie más cercana, así que la
/// imagen no se deforma ni parpadea al girar (no depende de ordenar triángulos). Los triángulos semitransparentes
/// se pintan después, mezclados y sin escribir profundidad.
/// </summary>
public sealed class Rasterizer
{
    private const float Near = 0.1f;

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Píxeles BGRA premultiplicados (formato de Avalonia).</summary>
    public uint[] Pixels { get; private set; } = [];
    /// <summary>Profundidad de vista (m) de cada píxel; +∞ si no hay nada.</summary>
    public float[] Depth { get; private set; } = [];

    private Vector3 _eye, _right, _up, _forward, _light;
    private float _focal, _cx, _cy;

    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
        Depth = new float[width * height];
    }

    public void Render(Scene scene, OrbitCamera camera, uint skyTop = 0xFFE9F1F8, uint skyBottom = 0xFFC9D8E6)
    {
        (_eye, _right, _up, _forward) = camera.Frame();
        _focal = (float)(Height / 2.0 / Math.Tan(camera.FieldOfView * Math.PI / 360));
        _cx = Width / 2f;
        _cy = Height / 2f;
        _light = Vector3.Normalize(new Vector3(0.4f, -0.6f, 1f));

        for (int y = 0; y < Height; y++)
        {
            uint c = LerpColor(skyTop, skyBottom, y / (float)Math.Max(Height - 1, 1));
            Array.Fill(Pixels, c, y * Width, Width);
        }
        Array.Fill(Depth, float.PositiveInfinity);

        var transparent = new List<(float Depth, Triangle T)>();
        foreach (var t in scene.Triangles)
        {
            if (t.Color >> 24 == 0xFF) Draw(t, writeDepth: true);
            else transparent.Add((Vector3.Dot((t.A + t.B + t.C) / 3 - _eye, _forward), t));
        }
        transparent.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach (var (_, t) in transparent) Draw(t, writeDepth: false);
    }

    /// <summary>Proyecta un punto a píxeles; null si queda detrás de la cámara.</summary>
    public (float X, float Y, float Depth)? Project(Vector3 p)
    {
        var d = p - _eye;
        float z = Vector3.Dot(d, _forward);
        if (z < Near) return null;
        return (_cx + _focal * Vector3.Dot(d, _right) / z, _cy - _focal * Vector3.Dot(d, _up) / z, z);
    }

    /// <summary>true si el punto se ve (no lo tapa ninguna superficie opaca).</summary>
    public bool IsVisible(float x, float y, float depth)
    {
        int ix = (int)x, iy = (int)y;
        if (ix < 0 || iy < 0 || ix >= Width || iy >= Height) return false;
        return depth <= Depth[iy * Width + ix] * 1.02f + 0.05f;
    }

    private void Draw(Triangle t, bool writeDepth)
    {
        var n = Vector3.Cross(t.B - t.A, t.C - t.A);
        float len = n.Length();
        if (len < 1e-12f) return;
        n /= len;
        // Iluminación a dos caras (las mallas no tienen que estar orientadas).
        float shade = 0.45f + 0.55f * MathF.Abs(Vector3.Dot(n, _light));
        uint color = Shade(t.Color, shade);

        // Pasa a coordenadas de cámara y recorta contra el plano cercano.
        Span<Vector3> v = stackalloc Vector3[4];
        int count = ClipNear(ToView(t.A), ToView(t.B), ToView(t.C), v);
        if (count < 3) return;
        RasterTriangle(v[0], v[1], v[2], color, writeDepth);
        if (count == 4) RasterTriangle(v[0], v[2], v[3], color, writeDepth);
    }

    private Vector3 ToView(Vector3 p)
    {
        var d = p - _eye;
        return new Vector3(Vector3.Dot(d, _right), Vector3.Dot(d, _up), Vector3.Dot(d, _forward));
    }

    private static int ClipNear(Vector3 a, Vector3 b, Vector3 c, Span<Vector3> output)
    {
        Span<Vector3> input = [a, b, c];
        int n = 0;
        for (int i = 0; i < 3; i++)
        {
            var p = input[i];
            var q = input[(i + 1) % 3];
            bool pin = p.Z >= Near, qin = q.Z >= Near;
            if (pin) output[n++] = p;
            if (pin != qin)
            {
                float k = (Near - p.Z) / (q.Z - p.Z);
                output[n++] = p + (q - p) * k;
            }
        }
        return n;
    }

    private void RasterTriangle(Vector3 a, Vector3 b, Vector3 c, uint color, bool writeDepth)
    {
        // Proyección: x, y en píxeles; se interpola 1/z, que es lineal en pantalla.
        float ax = _cx + _focal * a.X / a.Z, ay = _cy - _focal * a.Y / a.Z, aw = 1 / a.Z;
        float bx = _cx + _focal * b.X / b.Z, by = _cy - _focal * b.Y / b.Z, bw = 1 / b.Z;
        float cx = _cx + _focal * c.X / c.Z, cy = _cy - _focal * c.Y / c.Z, cw = 1 / c.Z;

        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (MathF.Abs(area) < 1e-6f) return;
        float invArea = 1 / area;

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        int maxX = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        int maxY = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        if (minX > maxX || minY > maxY) return;

        uint alpha = color >> 24;
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * Width;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * invArea;
                float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * invArea;
                float w2 = 1 - w0 - w1;
                if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                float depth = 1 / (w0 * aw + w1 * bw + w2 * cw);
                int i = row + x;
                if (depth >= Depth[i]) continue;
                if (writeDepth)
                {
                    Depth[i] = depth;
                    Pixels[i] = color;
                }
                else Pixels[i] = Blend(Pixels[i], color, alpha);
            }
        }
    }

    // ------------------------------------------------------------------ Color (ARGB de entrada, BGRA premultiplicado de salida)

    private static uint Shade(uint argb, float k)
    {
        uint a = argb >> 24;
        uint r = (uint)Math.Min(255, ((argb >> 16) & 0xFF) * k);
        uint g = (uint)Math.Min(255, ((argb >> 8) & 0xFF) * k);
        uint b = (uint)Math.Min(255, (argb & 0xFF) * k);
        return (a << 24) | (r << 16) | (g << 8) | b; // en memoria little-endian = B, G, R, A
    }

    private static uint Blend(uint dst, uint src, uint alpha)
    {
        uint inv = 255 - alpha;
        uint r = (((src >> 16) & 0xFF) * alpha + ((dst >> 16) & 0xFF) * inv) / 255;
        uint g = (((src >> 8) & 0xFF) * alpha + ((dst >> 8) & 0xFF) * inv) / 255;
        uint b = ((src & 0xFF) * alpha + (dst & 0xFF) * inv) / 255;
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    private static uint LerpColor(uint a, uint b, float t)
    {
        uint L(int shift) => (uint)(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | (L(16) << 16) | (L(8) << 8) | L(0);
    }
}
