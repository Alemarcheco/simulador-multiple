using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Simulador.Civil.Model;
using Simulador.Civil.Rendering;
using Simulador.Core.Rendering;

namespace Simulador.Civil.Views;

/// <summary>
/// Vista 3D del edificio con rasterizado por software y búfer de profundidad (sin OpenGL): las caras se tapan bien
/// desde cualquier ángulo y el encuadre no cambia al girar. Izquierdo = girar, derecho o central = desplazar, rueda = zoom.
/// </summary>
public sealed class Building3DView : Control
{
    private readonly BuildingDocument _doc;
    private readonly OrbitCamera _camera = BuildingRenderer.CreateCamera();
    private readonly ZBufferRasterizer _renderer = BuildingRenderer.CreateRasterizer();
    private BuildingMesh? _mesh;
    /// <summary>Caja del último encuadre automático; null = encuadrar en el próximo dibujo.</summary>
    private (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)? _fitted;
    /// <summary>true cuando el usuario ha desplazado o acercado: los cambios del edificio ya no reencuadran.</summary>
    private bool _userMoved;
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
        var bounds = BuildingRenderer.Bounds(_mesh);
        // Encuadre con la esfera envolvente (fijo al girar). Se rehace si el edificio cambia de tamaño, salvo que el
        // usuario haya movido la vista a mano.
        if (_fitted is null || (!_userMoved && _fitted.Value != bounds))
        {
            _camera.Fit(bounds.Min, bounds.Max);
            _fitted = bounds;
        }

        // Suavizado de bordes por supermuestreo 2× mientras el lienzo no sea enorme.
        _renderer.Supersampling = w * h <= 1_500_000 ? 2 : 1;
        _renderer.Resize(w, h);
        BuildingRenderer.Render(_renderer, _mesh, _camera, edges: true);
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
            _camera.Pan(p.X - d.X, p.Y - d.Y, (int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
            _userMoved = true;
        }
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
        _camera.Zoom(e.Delta.Y > 0 ? 1.1 : -1.1);
        _userMoved = true;
        InvalidateVisual();
        e.Handled = true;
    }

    public void ResetView()
    {
        var fresh = BuildingRenderer.CreateCamera();
        _camera.Yaw = fresh.Yaw;
        _camera.Pitch = fresh.Pitch;
        _userMoved = false;
        _fitted = null;
        Invalidate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
