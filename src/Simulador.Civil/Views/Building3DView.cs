using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Simulador.Civil.Model;
using Simulador.Civil.Rendering;

namespace Simulador.Civil.Views;

/// <summary>
/// Vista 3D del edificio con rasterizado por software y búfer de profundidad (sin OpenGL): las caras se tapan bien
/// desde cualquier ángulo y el encuadre no cambia al girar. Izquierdo = girar, derecho o central = desplazar, rueda = zoom.
/// </summary>
public sealed class Building3DView : Control
{
    private readonly BuildingDocument _doc;
    private readonly OrbitCamera _camera = new();
    private readonly SoftwareRenderer _renderer = new();
    private BuildingMesh? _mesh;
    private WriteableBitmap? _bitmap;
    private Point? _drag;
    private bool _dragPan;

    public Building3DView(BuildingDocument doc)
    {
        _doc = doc;
        ClipToBounds = true;
        doc.Changed += (_, _) => Invalidate();
    }

    private string? _upTo;
    private bool _showRoof = true;

    /// <summary>Muestra los niveles hasta este (incluido); null = todos.</summary>
    public string? UpToLevelId { get => _upTo; set { _upTo = value; Invalidate(); } }
    public bool ShowRoof { get => _showRoof; set { _showRoof = value; Invalidate(); } }

    /// <summary>La malla se reconstruye solo cuando cambia el edificio o el filtro; al girar se reutiliza.</summary>
    private void Invalidate()
    {
        _mesh = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = (int)Math.Ceiling(Bounds.Width * scaling), h = (int)Math.Ceiling(Bounds.Height * scaling);
        if (w < 2 || h < 2) return;

        _mesh ??= BuildingMesh.Build(_doc, UpToLevelId, ShowRoof);

        // Suavizado de bordes por supermuestreo 2× mientras el lienzo no sea enorme.
        int ss = w * h <= 1_500_000 ? 2 : 1;
        _renderer.Resize(w * ss, h * ss);
        _renderer.Clear(0xFFE0EAF2);
        if (_mesh.Faces.Count > 0 || _mesh.Lines.Count > 0)
        {
            var cam = new OrbitCamera
            {
                Yaw = _camera.Yaw, Pitch = _camera.Pitch, Zoom = _camera.Zoom,
                PanX = _camera.PanX * scaling * ss, PanY = _camera.PanY * scaling * ss,
            };
            _renderer.Render(_mesh, cam.CreateProjector(_mesh.Bounds, w * ss, h * ss), edges: true);
        }
        var pixels = ss == 1 ? _renderer.Pixels : SoftwareRenderer.Downsample(_renderer.Pixels, w * ss, h * ss, ss, out _, out _);

        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using (var fb = _bitmap.Lock())
        {
            if (fb.RowBytes == w * 4) Marshal.Copy(pixels, 0, fb.Address, w * h);
            else
                for (int y = 0; y < h; y++) Marshal.Copy(pixels, y * w, fb.Address + y * fb.RowBytes, w);
        }
        ctx.DrawImage(_bitmap, new Rect(0, 0, w, h), new Rect(Bounds.Size));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _drag = e.GetPosition(this);
        _dragPan = props.IsMiddleButtonPressed || props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        if (_dragPan)
        {
            _camera.PanX += p.X - d.X;
            _camera.PanY += p.Y - d.Y;
        }
        else
        {
            _camera.Yaw -= (p.X - d.X) * 0.01;
            _camera.Pitch = Math.Clamp(_camera.Pitch + (p.Y - d.Y) * 0.01, 0.05, 1.5);
        }
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
        _camera.Zoom = Math.Clamp(_camera.Zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.2, 20);
        InvalidateVisual();
        e.Handled = true;
    }

    public void ResetView()
    {
        _camera.Reset();
        Invalidate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
