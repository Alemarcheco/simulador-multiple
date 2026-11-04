using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Simulador.Cad3D.Geometry;
using static Avalonia.OpenGL.GlConsts;

namespace Simulador.Cad3D.Rendering;

/// <summary>
/// Visor 3D por GPU (OpenGL / OpenGL ES vía ANGLE en Windows): profundidad real, miles de triángulos sin
/// esfuerzo y giro fluido. Si el equipo no tiene OpenGL, <see cref="ViewportHost"/> usa el visor por software.
/// </summary>
public sealed unsafe class GlViewport3D : OpenGlControlBase
{
    private readonly OrbitCamera _camera;
    private readonly object _lock = new();
    private float[] _solid = Array.Empty<float>(), _overlay = Array.Empty<float>(), _lines;
    private bool _dirty = true;
    private int _program, _vao, _vboSolid, _vboOverlay, _vboLines;
    private int _aPos, _aNormal, _aColor, _uMvp, _uParams;
    private int _solidCount, _overlayCount, _linesCount;
    private BlendFuncDelegate? _blendFunc;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BlendFuncDelegate(int sfactor, int dfactor);

    private const int GL_LEQUAL = 0x0203, GL_SRC_ALPHA = 0x0302, GL_ONE_MINUS_SRC_ALPHA = 0x0303, GL_BLEND = 0x0BE2, GL_LINES = 0x0001;

    private const int Stride = 10; // posición (3) + normal (3) + color (4)

    /// <summary>true cuando OpenGL arrancó bien; false si falló.</summary>
    public bool? GlReady { get; private set; }
    public string Renderer { get; private set; } = "";

    public event Action<string>? Failed;

    public GlViewport3D(OrbitCamera camera)
    {
        _camera = camera;
        _lines = BuildGridAndAxes();
        IsHitTestVisible = false;
    }

    public void SetItems(IReadOnlyList<RenderItem> items)
    {
        var solid = new List<float>();
        var overlay = new List<float>();
        foreach (var it in items)
        {
            var target = it.Overlay ? overlay : solid;
            float r = it.Color.R / 255f, g = it.Color.G / 255f, b = it.Color.B / 255f, a = it.Color.A / 255f;
            foreach (var (p0, p1, p2) in it.Mesh.Triangles())
            {
                var n = (p1 - p0).Cross(p2 - p0).Normalized();
                foreach (var p in new[] { p0, p1, p2 })
                {
                    target.Add((float)p.X); target.Add((float)p.Y); target.Add((float)p.Z);
                    target.Add((float)n.X); target.Add((float)n.Y); target.Add((float)n.Z);
                    target.Add(r); target.Add(g); target.Add(b); target.Add(a);
                }
            }
        }
        lock (_lock)
        {
            _solid = solid.ToArray();
            _overlay = overlay.ToArray();
            _dirty = true;
        }
        RequestNextFrameRendering();
    }

    private static float[] BuildGridAndAxes()
    {
        var v = new List<float>();
        void L(Vec3 a, Vec3 b, float r, float g, float bl, float al)
        {
            foreach (var p in new[] { a, b })
            {
                v.Add((float)p.X); v.Add((float)p.Y); v.Add((float)p.Z);
                v.Add(0); v.Add(0); v.Add(1);
                v.Add(r); v.Add(g); v.Add(bl); v.Add(al);
            }
        }
        const int half = 100, step = 10;
        for (int i = -half; i <= half; i += step)
        {
            float al = i % 50 == 0 ? 0.55f : 0.3f;
            L(new Vec3(i, -half, 0), new Vec3(i, half, 0), 1, 1, 1, al);
            L(new Vec3(-half, i, 0), new Vec3(half, i, 0), 1, 1, 1, al);
        }
        L(Vec3.Zero, new Vec3(30, 0, 0.01), 0.85f, 0.3f, 0.3f, 1);
        L(Vec3.Zero, new Vec3(0, 30, 0.01), 0.3f, 0.8f, 0.3f, 1);
        L(Vec3.Zero, new Vec3(0, 0, 30), 0.25f, 0.55f, 1, 1);
        return v.ToArray();
    }

    private string Shader(bool fragment, string body)
    {
        int version = GlVersion.Type == GlProfileType.OpenGL
            ? (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 150 : 120)
            : 100;
        var head = $"#version {version}\n";
        if (GlVersion.Type == GlProfileType.OpenGLES) head += "precision mediump float;\n";
        if (version >= 150)
        {
            body = body.Replace("attribute", "in");
            body = fragment
                ? body.Replace("varying", "in").Replace("//OUT", "out vec4 outColor;").Replace("gl_FragColor", "outColor")
                : body.Replace("varying", "out");
        }
        return head + body;
    }

    private const string VertexSrc = @"
attribute vec3 aPos;
attribute vec3 aNormal;
attribute vec4 aColor;
uniform mat4 uMvp;
uniform mat4 uParams;
varying vec4 vColor;
void main()
{
    gl_Position = uMvp * vec4(aPos, 1.0);
    vec4 p = uParams[0];
    float s = p.w > 0.5 ? 0.35 + 0.65 * abs(dot(normalize(aNormal), p.xyz)) : 1.0;
    vColor = vec4(aColor.rgb * s, aColor.a);
}";

    private const string FragmentSrc = @"
varying vec4 vColor;
//OUT
void main()
{
    gl_FragColor = vColor;
}";

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            int vs = gl.CreateShader(GL_VERTEX_SHADER);
            if (gl.CompileShaderAndGetError(vs, Shader(false, VertexSrc)) is { } e1) throw new InvalidOperationException("Vértices: " + e1);
            int fs = gl.CreateShader(GL_FRAGMENT_SHADER);
            if (gl.CompileShaderAndGetError(fs, Shader(true, FragmentSrc)) is { } e2) throw new InvalidOperationException("Fragmentos: " + e2);
            _program = gl.CreateProgram();
            gl.AttachShader(_program, vs);
            gl.AttachShader(_program, fs);
            gl.BindAttribLocationString(_program, 0, "aPos");
            gl.BindAttribLocationString(_program, 1, "aNormal");
            gl.BindAttribLocationString(_program, 2, "aColor");
            if (gl.LinkProgramAndGetError(_program) is { } e3) throw new InvalidOperationException("Enlace: " + e3);
            _aPos = 0; _aNormal = 1; _aColor = 2;
            _uMvp = gl.GetUniformLocationString(_program, "uMvp");
            _uParams = gl.GetUniformLocationString(_program, "uParams");
            _vboSolid = gl.GenBuffer();
            _vboOverlay = gl.GenBuffer();
            _vboLines = gl.GenBuffer();
            if (gl.IsGenVertexArraysAvailable) _vao = gl.GenVertexArray();
            var bf = gl.GetProcAddress("glBlendFunc");
            if (bf != IntPtr.Zero) _blendFunc = Marshal.GetDelegateForFunctionPointer<BlendFuncDelegate>(bf);
            Upload(gl, _vboLines, _lines);
            _linesCount = _lines.Length / Stride;
            Renderer = $"OpenGL ({gl.GetString(GL_RENDERER)})";
            GlReady = true;
        }
        catch (Exception ex)
        {
            GlReady = false;
            Dispatcher.UIThread.Post(() => Failed?.Invoke(ex.Message));
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_program != 0) gl.DeleteProgram(_program);
        foreach (var b in new[] { _vboSolid, _vboOverlay, _vboLines }) if (b != 0) gl.DeleteBuffer(b);
        if (_vao != 0 && gl.IsDeleteVertexArraysAvailable) gl.DeleteVertexArray(_vao);
        _program = _vboSolid = _vboOverlay = _vboLines = _vao = 0;
    }

    protected override void OnOpenGlLost()
    {
        GlReady = false;
        Dispatcher.UIThread.Post(() => Failed?.Invoke("se perdió el contexto OpenGL"));
    }

    private static void Upload(GlInterface gl, int vbo, float[] data)
    {
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        fixed (float* p = data.Length > 0 ? data : new float[1])
            gl.BufferData(GL_ARRAY_BUFFER, new IntPtr(Math.Max(1, data.Length) * sizeof(float)), new IntPtr(p), GL_STATIC_DRAW);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (GlReady != true) return;
        lock (_lock)
        {
            if (_dirty)
            {
                Upload(gl, _vboSolid, _solid);
                Upload(gl, _vboOverlay, _overlay);
                _solidCount = _solid.Length / Stride;
                _overlayCount = _overlay.Length / Stride;
                _dirty = false;
            }
        }

        double scale = VisualRoot?.RenderScaling ?? 1;
        int w = Math.Max(1, (int)(Bounds.Width * scale)), h = Math.Max(1, (int)(Bounds.Height * scale));
        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0x23 / 255f, 0x27 / 255f, 0x2E / 255f, 1);
        gl.ClearDepth(1);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        gl.Enable(GL_DEPTH_TEST);
        gl.DepthFunc(GL_LEQUAL);
        gl.UseProgram(_program);
        if (_vao != 0) gl.BindVertexArray(_vao);

        var (eye, right, up, forward) = _camera.Basis();
        var mvp = ViewProjection(eye, right, up, forward, w, h, _camera.Distance);
        fixed (float* m = mvp) gl.UniformMatrix4fv(_uMvp, 1, false, m);
        var light = (-forward + up * 0.5 + right * 0.3).Normalized();

        SetParams(gl, light, lit: true);
        Draw(gl, _vboSolid, _solidCount, GL_TRIANGLES);

        _blendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        gl.Enable(GL_BLEND);
        SetParams(gl, light, lit: false);
        Draw(gl, _vboLines, _linesCount, GL_LINES);
        gl.DepthMask(0);
        SetParams(gl, light, lit: true);
        Draw(gl, _vboOverlay, _overlayCount, GL_TRIANGLES);
        gl.DepthMask(1);
        gl.Disable(GL_BLEND);
    }

    private void SetParams(GlInterface gl, Vec3 light, bool lit)
    {
        var p = new float[16];
        p[0] = (float)light.X; p[1] = (float)light.Y; p[2] = (float)light.Z; p[3] = lit ? 1 : 0;
        fixed (float* m = p) gl.UniformMatrix4fv(_uParams, 1, false, m);
    }

    private void Draw(GlInterface gl, int vbo, int count, int mode)
    {
        if (count == 0) return;
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        int stride = Stride * sizeof(float);
        gl.VertexAttribPointer(_aPos, 3, GL_FLOAT, 0, stride, IntPtr.Zero);
        gl.VertexAttribPointer(_aNormal, 3, GL_FLOAT, 0, stride, new IntPtr(3 * sizeof(float)));
        gl.VertexAttribPointer(_aColor, 4, GL_FLOAT, 0, stride, new IntPtr(6 * sizeof(float)));
        gl.EnableVertexAttribArray(_aPos);
        gl.EnableVertexAttribArray(_aNormal);
        gl.EnableVertexAttribArray(_aColor);
        gl.DrawArrays(mode, 0, new IntPtr(count));
    }

    /// <summary>Matriz vista-proyección en orden de columnas (lo que espera OpenGL), con la misma cámara que el visor por software.</summary>
    internal static float[] ViewProjection(Vec3 eye, Vec3 right, Vec3 up, Vec3 forward, double w, double h, double distance)
    {
        double focal = Math.Min(w, h) * OrbitCamera.FocalFactor;
        double sx = 2 * focal / w, sy = 2 * focal / h;
        double near = Math.Max(0.5, distance * 0.005), far = distance * 20 + 2000;
        double a = (far + near) / (far - near), b = -2 * far * near / (far - near);
        // Filas de la matriz (fila mayor): clip = P · V · p.
        double[,] m =
        {
            { sx * right.X, sx * right.Y, sx * right.Z, -sx * right.Dot(eye) },
            { sy * up.X, sy * up.Y, sy * up.Z, -sy * up.Dot(eye) },
            { a * forward.X, a * forward.Y, a * forward.Z, -a * forward.Dot(eye) + b },
            { forward.X, forward.Y, forward.Z, -forward.Dot(eye) },
        };
        var col = new float[16];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                col[c * 4 + r] = (float)m[r, c];
        return col;
    }
}
