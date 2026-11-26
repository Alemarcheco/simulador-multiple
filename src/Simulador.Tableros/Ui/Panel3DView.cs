using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Simulador.Core.Rendering;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Ui;

/// <summary>
/// Vista 3D del tablero con el rasterizador común (sin OpenGL). Izquierdo = girar, derecho o central = desplazar,
/// rueda = acercar. Las designaciones se escriben encima de cada aparato visible.
/// </summary>
internal sealed class Panel3DView : Control
{
    private readonly OrbitCamera _camera = new() { Yaw = -62, Pitch = 18, FieldOfView = 35, MinPitch = -60, MaxPitch = 89 };
    private readonly ZBufferRasterizer _renderer = new() { Ambient = 0.5f };
    private PanelDocument _doc = new();
    private IReadOnlyList<WireRoute> _routes = [];
    private PanelScene? _scene;
    private WriteableBitmap? _bitmap;
    private Point? _drag;
    private bool _pan, _fitted;

    public PanelSceneOptions Options { get; private set; } = new();

    public Panel3DView()
    {
        ClipToBounds = true;
    }

    public void Load(PanelDocument doc, IReadOnlyList<WireRoute> routes)
    {
        if (!ReferenceEquals(doc, _doc)) _fitted = false;
        _doc = doc;
        _routes = routes;
        _scene = null;
        InvalidateVisual();
    }

    public void SetOptions(PanelSceneOptions options)
    {
        Options = options;
        _scene = null;
        InvalidateVisual();
    }

    public void ResetView()
    {
        _camera.Yaw = -62;
        _camera.Pitch = 18;
        _fitted = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = (int)Math.Ceiling(Bounds.Width * scaling), h = (int)Math.Ceiling(Bounds.Height * scaling);
        if (w < 2 || h < 2) return;
        _scene ??= PanelScene.Build(_doc, _routes, Options);
        if (!_fitted)
        {
            // Encuadre propio: OrbitCamera.Fit no baja de 5 m en perspectiva y un tablero mide un metro.
            _camera.Target = (_scene.Min + _scene.Max) / 2;
            var size = (_scene.Max - _scene.Min).Length();
            _camera.Distance = size / (2 * Math.Tan(_camera.FieldOfView * Math.PI / 360)) * 0.8;
            _fitted = true;
        }
        _renderer.Supersampling = w * h <= 1_500_000 ? 2 : 1;
        _renderer.Resize(w, h);
        _renderer.Begin(_camera, 0xFFEEF2F6, 0xFFC9D3DD);
        foreach (var t in _scene.Triangles) _renderer.DrawTriangle(t);
        foreach (var (a, b, c, width) in _scene.Lines) _renderer.DrawLine(a, b, c, depthTest: true, width: width * (float)scaling);
        _renderer.End();
        Blit(ctx, w, h, scaling);

        // Designaciones encima de lo visible.
        foreach (var (pos, text) in _scene.Labels)
        {
            if (_renderer.Project(pos) is not { } p) continue;
            // Margen corto: en un armario de doble frente, lo de la placa trasera queda a pocos centímetros detrás.
            if (!_renderer.IsVisible(p.X, p.Y, p.Depth, 0.03f)) continue;
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 11, Brushes.White);
            var at = new Point(p.X / scaling - ft.Width / 2, p.Y / scaling - ft.Height / 2);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 20, 30, 45)), new Rect(at.X - 3, at.Y - 1, ft.Width + 6, ft.Height + 2), 3);
            ctx.DrawText(ft, at);
        }
    }

    private void Blit(DrawingContext ctx, int w, int h, double scaling)
    {
        var pixels = _renderer.Pixels;
        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using (var fb = _bitmap.Lock())
        {
            var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
            for (int y = 0; y < h; y++)
                unsafe
                {
                    bytes.Slice(y * w * 4, w * 4).CopyTo(new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, w * 4));
                }
        }
        ctx.DrawImage(_bitmap, new Rect(0, 0, w, h), new Rect(Bounds.Size));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _drag = e.GetPosition(this);
        _pan = props.IsMiddleButtonPressed || props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        if (_pan) _camera.Pan(p.X - d.X, p.Y - d.Y, (int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        else _camera.Orbit((p.X - d.X) * 0.5, (p.Y - d.Y) * 0.5);
        _drag = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Distance = Math.Clamp(_camera.Distance * (e.Delta.Y > 0 ? 0.88 : 1 / 0.88), 0.2, 50);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
