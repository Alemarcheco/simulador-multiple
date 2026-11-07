using System.Numerics;

namespace Simulador.Core.Rendering;

/// <summary>
/// Rasterizador por software con búfer de profundidad, común a todas las vistas 3D (sin OpenGL). Cada píxel muestra
/// la superficie más cercana, así que la imagen no se deforma ni parpadea al girar (no depende de ordenar triángulos).
/// Los triángulos semitransparentes se pintan al final, de atrás hacia delante, mezclados y sin escribir profundidad.
/// <para>
/// Uso: <see cref="Resize"/>, <see cref="Begin"/> (cámara y fondo), <see cref="DrawTriangle"/>,
/// <see cref="DrawPolygon"/> y <see cref="DrawLine"/> las veces que haga falta, y <see cref="End"/>, que deja la imagen
/// en <see cref="Pixels"/>. <see cref="Render"/> hace todo de una vez para una lista de triángulos.
/// </para>
/// </summary>
public class ZBufferRasterizer
{
    private const float Near = 0.1f;

    /// <summary>Tamaño de la imagen final, en píxeles.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// Supermuestreo para suavizar bordes: se dibuja a N× y se promedia al terminar. 1 = desactivado (lo más rápido,
    /// para vistas que se redibujan en cada fotograma). Se aplica en el siguiente <see cref="Resize"/>.
    /// </summary>
    public int Supersampling { get; set; } = 1;

    /// <summary>Luz mínima de una cara de espaldas a la luz (0–1). El resto depende del ángulo con la luz.</summary>
    public float Ambient { get; set; } = 0.45f;

    /// <summary>Dirección hacia la luz (se normaliza).</summary>
    public Vector3 LightDirection { get; set; } = new(0.4f, -0.6f, 1f);

    /// <summary>
    /// Luz por píxel (sombras): si se fija antes de <see cref="Begin"/>, en <see cref="End"/> se llama con el punto del
    /// mundo que se ve en cada píxel opaco y la normal de su cara (hacia la cámara), y su color se multiplica por el
    /// valor devuelto (1 = igual, 0 = negro). Se aplica antes de los
    /// semitransparentes y de reducir el supermuestreo.
    /// </summary>
    public Func<Vector3, Vector3, float>? PixelLight { get; set; }

    /// <summary>Imagen final en BGRA (formato Bgra8888 de Avalonia; opaca, así que vale como premultiplicada).</summary>
    public uint[] Pixels { get; private set; } = [];

    /// <summary>Profundidad de vista (m) de cada píxel del búfer de trabajo (Width·N × Height·N); +∞ si no hay nada.</summary>
    public float[] Depth { get; private set; } = [];

    private uint[] _work = [];
    private int _ss = 1, _w, _h;
    private Vector3 _eye, _right, _up, _forward, _light;
    private float _scale, _cx, _cy;
    private bool _ortho;
    private readonly List<(float Depth, Triangle T)> _transparent = [];
    /// <summary>Normal de la cara de cada píxel (solo con <see cref="PixelLight"/>).</summary>
    private Vector3[]? _normals;

    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        int ss = Math.Clamp(Supersampling, 1, 4);
        if (width == Width && height == Height && ss == _ss && Pixels.Length > 0) return;
        Width = width;
        Height = height;
        _ss = ss;
        _w = width * ss;
        _h = height * ss;
        _work = new uint[_w * _h];
        Depth = new float[_w * _h];
        Pixels = ss == 1 ? _work : new uint[width * height];
    }

    /// <summary>Prepara un fotograma: fija la cámara y pinta el fondo en degradado vertical (colores ARGB).</summary>
    public void Begin(OrbitCamera camera, uint skyTop = 0xFFE9F1F8, uint skyBottom = 0xFFC9D8E6)
    {
        if (_work.Length == 0) Resize(Width, Height);
        (_eye, _right, _up, _forward) = camera.Frame();
        _ortho = camera.Orthographic;
        _scale = (float)camera.PixelsPerMeter(Width, Height) * _ss;
        if (!_ortho) _scale *= (float)camera.Distance; // focal en píxeles
        _cx = _w / 2f;
        _cy = _h / 2f;
        _light = Vector3.Normalize(LightDirection);
        _transparent.Clear();

        for (int y = 0; y < _h; y++)
        {
            uint c = skyTop == skyBottom ? skyTop | 0xFF000000 : LerpColor(skyTop, skyBottom, y / (float)Math.Max(_h - 1, 1));
            Array.Fill(_work, c, y * _w, _w);
        }
        Array.Fill(Depth, float.PositiveInfinity);
        if (PixelLight is null) _normals = null;
        else if (_normals is null || _normals.Length != _work.Length) _normals = new Vector3[_work.Length];
    }

    /// <summary>Dibuja los semitransparentes pendientes y, con supermuestreo, reduce la imagen a <see cref="Pixels"/>.</summary>
    public void End()
    {
        if (PixelLight is { } light && _normals is not null) ApplyPixelLight(light, _normals);
        _transparent.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach (var (_, t) in _transparent) Fill(t, writeDepth: false);
        _transparent.Clear();
        if (_ss > 1) Downsample();
    }

    /// <summary>Fotograma completo con una lista de triángulos.</summary>
    public void Render(IEnumerable<Triangle> triangles, OrbitCamera camera, uint skyTop = 0xFFE9F1F8, uint skyBottom = 0xFFC9D8E6)
    {
        Begin(camera, skyTop, skyBottom);
        foreach (var t in triangles) DrawTriangle(t);
        End();
    }

    /// <summary>Triángulo sombreado. Si es semitransparente se guarda para pintarlo en <see cref="End"/>.</summary>
    public void DrawTriangle(Triangle t)
    {
        if (t.Color >> 24 == 0xFF) Fill(t, writeDepth: true);
        else _transparent.Add((Vector3.Dot((t.A + t.B + t.C) / 3 - _eye, _forward), t));
    }

    /// <summary>
    /// Polígono plano y convexo. Con <paramref name="cullBackFaces"/> se omite si su cara (según el orden de los
    /// vértices, regla de la mano derecha) no mira a la cámara. <paramref name="edgeColor"/> dibuja su contorno.
    /// </summary>
    public void DrawPolygon(ReadOnlySpan<Vector3> pts, uint color, bool cullBackFaces = false, uint? edgeColor = null)
    {
        if (pts.Length < 3) return;
        if (cullBackFaces)
        {
            var n = Newell(pts);
            var toEye = _ortho ? -_forward : _eye - pts[0];
            if (Vector3.Dot(n, toEye) <= 1e-9f) return;
        }
        for (int i = 1; i + 1 < pts.Length; i++) DrawTriangle(new Triangle(pts[0], pts[i], pts[i + 1], color));
        if (edgeColor is { } ec)
            for (int i = 0; i < pts.Length; i++) DrawLine(pts[i], pts[(i + 1) % pts.Length], ec, depthTest: true);
    }

    /// <summary>
    /// Segmento de <paramref name="width"/> píxeles de la imagen final. Con <paramref name="depthTest"/> lo tapan las
    /// superficies que tiene delante (con un pequeño margen para que gane a su propia cara); sin él se ve siempre.
    /// </summary>
    public void DrawLine(Vector3 a, Vector3 b, uint color, bool depthTest = true, float width = 1)
    {
        var va = ToView(a);
        var vb = ToView(b);
        if (!_ortho)
        {
            if (va.Z < Near && vb.Z < Near) return;
            if (va.Z < Near) va = vb + (va - vb) * ((vb.Z - Near) / (vb.Z - va.Z));
            else if (vb.Z < Near) vb = va + (vb - va) * ((va.Z - Near) / (va.Z - vb.Z));
        }
        var (ax, ay, aw) = Screen(va);
        var (bx, by, bw) = Screen(vb);
        float dx = bx - ax, dy = by - ay;
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)));
        if (steps > 40000) return; // casi todo fuera de la vista
        int thick = Math.Max(1, (int)MathF.Round(width * _ss));
        int half = thick / 2;
        uint c = color | 0xFF000000;
        for (int s = 0; s <= steps; s++)
        {
            float t = steps == 0 ? 0 : (float)s / steps;
            int x0 = (int)(ax + dx * t) - half, y0 = (int)(ay + dy * t) - half;
            float w = aw + (bw - aw) * t;
            float z = _ortho ? w : 1 / w;
            float bias = 0.02f + 0.002f * MathF.Abs(z);
            for (int oy = 0; oy < thick; oy++)
            for (int ox = 0; ox < thick; ox++)
            {
                int x = x0 + ox, y = y0 + oy;
                if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) continue;
                int i = y * _w + x;
                if (depthTest && z - bias > Depth[i]) continue;
                _work[i] = c;
            }
        }
    }

    private void ApplyPixelLight(Func<Vector3, Vector3, float> light, Vector3[] normals)
    {
        for (int y = 0; y < _h; y++)
        {
            float py = y + 0.5f;
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                float z = Depth[i];
                if (float.IsPositiveInfinity(z)) continue;
                float px = x + 0.5f;
                float vx = (px - _cx) / _scale, vy = (_cy - py) / _scale;
                if (!_ortho) { vx *= z; vy *= z; }
                float k = light(_eye + _right * vx + _up * vy + _forward * z, normals[i]);
                if (k < 0.999f) _work[i] = Shade(_work[i], k);
            }
        }
    }

    /// <summary>
    /// Profundidad de vista (m) de la superficie más cercana en el punto (x, y) de la imagen final; +∞ si no hay nada
    /// o queda fuera. Con <see cref="Project"/> sirve de mapa de sombras visto desde el sol.
    /// </summary>
    public float DepthAt(float x, float y)
    {
        int ix = (int)(x * _ss), iy = (int)(y * _ss);
        if (x < 0 || y < 0 || ix >= _w || iy >= _h) return float.PositiveInfinity;
        return Depth[iy * _w + ix];
    }

    /// <summary>Proyecta un punto a píxeles de la imagen final; null si queda detrás de la cámara.</summary>
    public (float X, float Y, float Depth)? Project(Vector3 p)
    {
        var v = ToView(p);
        if (!_ortho && v.Z < Near) return null;
        var (x, y, _) = Screen(v);
        return (x / _ss, y / _ss, v.Z);
    }

    /// <summary>true si el punto (en píxeles de la imagen final) se ve: no lo tapa ninguna superficie opaca.</summary>
    public bool IsVisible(float x, float y, float depth)
    {
        int ix = (int)(x * _ss), iy = (int)(y * _ss);
        if (x < 0 || y < 0 || ix >= _w || iy >= _h) return false;
        float d = Depth[iy * _w + ix];
        return depth <= d + MathF.Abs(d) * 0.02f + 0.05f;
    }

    // ------------------------------------------------------------------ Triángulos

    private void Fill(Triangle t, bool writeDepth)
    {
        var n = Vector3.Cross(t.B - t.A, t.C - t.A);
        float len = n.Length();
        if (len < 1e-12f) return;
        n /= len;
        // Iluminación a dos caras (las mallas no tienen que estar orientadas).
        float shade = Ambient + (1 - Ambient) * MathF.Abs(Vector3.Dot(n, _light));
        uint color = Shade(t.Color, shade);

        Span<Vector3> v = stackalloc Vector3[4];
        int count = _ortho ? Copy(ToView(t.A), ToView(t.B), ToView(t.C), v) : ClipNear(ToView(t.A), ToView(t.B), ToView(t.C), v);
        if (count < 3) return;
        // Normal hacia el lado que se ve (para la luz por píxel).
        var toEye = _ortho ? -_forward : _eye - t.A;
        if (Vector3.Dot(n, toEye) < 0) n = -n;
        RasterTriangle(v[0], v[1], v[2], color, writeDepth, n);
        if (count == 4) RasterTriangle(v[0], v[2], v[3], color, writeDepth, n);
    }

    private Vector3 ToView(Vector3 p)
    {
        var d = p - _eye;
        return new Vector3(Vector3.Dot(d, _right), Vector3.Dot(d, _up), Vector3.Dot(d, _forward));
    }

    /// <summary>
    /// Punto de vista → píxel del búfer de trabajo. W es lo que se interpola linealmente en pantalla:
    /// 1/z en perspectiva, z en ortográfica.
    /// </summary>
    private (float X, float Y, float W) Screen(Vector3 v) => _ortho
        ? (_cx + _scale * v.X, _cy - _scale * v.Y, v.Z)
        : (_cx + _scale * v.X / v.Z, _cy - _scale * v.Y / v.Z, 1 / v.Z);

    private static int Copy(Vector3 a, Vector3 b, Vector3 c, Span<Vector3> output)
    {
        output[0] = a; output[1] = b; output[2] = c;
        return 3;
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

    private void RasterTriangle(Vector3 a, Vector3 b, Vector3 c, uint color, bool writeDepth, Vector3 normal)
    {
        var (ax, ay, aw) = Screen(a);
        var (bx, by, bw) = Screen(b);
        var (cx, cy, cw) = Screen(c);

        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (MathF.Abs(area) < 1e-6f) return;
        float invArea = 1 / area;

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        int maxX = Math.Min(_w - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        int maxY = Math.Min(_h - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        if (minX > maxX || minY > maxY) return;

        bool ortho = _ortho;
        var normals = _normals;
        uint alpha = color >> 24;
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * _w;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * invArea;
                float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * invArea;
                float w2 = 1 - w0 - w1;
                if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                float w = w0 * aw + w1 * bw + w2 * cw;
                float depth = ortho ? w : 1 / w;
                int i = row + x;
                if (depth >= Depth[i]) continue;
                if (writeDepth)
                {
                    Depth[i] = depth;
                    _work[i] = color;
                    if (normals is not null) normals[i] = normal;
                }
                else _work[i] = Blend(_work[i], color, alpha);
            }
        }
    }

    private void Downsample()
    {
        int n = _ss * _ss;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            uint r = 0, g = 0, b = 0;
            for (int sy = 0; sy < _ss; sy++)
            {
                int row = (y * _ss + sy) * _w + x * _ss;
                for (int sx = 0; sx < _ss; sx++)
                {
                    uint p = _work[row + sx];
                    r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF;
                }
            }
            Pixels[y * Width + x] = 0xFF000000 | (r / (uint)n << 16) | (g / (uint)n << 8) | (b / (uint)n);
        }
    }

    private static Vector3 Newell(ReadOnlySpan<Vector3> pts)
    {
        var n = Vector3.Zero;
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return n;
    }

    // ------------------------------------------------------------------ Color (ARGB de entrada, BGRA de salida)

    /// <summary>Oscurece o aclara un color ARGB conservando su alfa.</summary>
    public static uint Shade(uint argb, float k)
    {
        uint a = argb >> 24;
        uint r = (uint)Math.Clamp(((argb >> 16) & 0xFF) * k, 0, 255);
        uint g = (uint)Math.Clamp(((argb >> 8) & 0xFF) * k, 0, 255);
        uint b = (uint)Math.Clamp((argb & 0xFF) * k, 0, 255);
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
